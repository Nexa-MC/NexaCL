using Nexa.Native.Network.Implementation;
using Nexa.Native.Network.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Network;

namespace Nexa.Native.Network;

internal sealed class NetworkServiceSession : IAsyncDisposable
{
    private sealed record RequestPolicy(NetworkPreferences Preferences, bool Redirects);
    private readonly ServiceEndpoint _endpoint;
    private readonly NetworkHttpStreamServer _http;
    private readonly NativeDownloadOwner _downloads;
    private readonly NativeUiAsyncResources<NetworkCertificatePool> _certificates = new(32);
    private readonly AsyncLocal<RequestPolicy?> _current = new();
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private NetworkPreferences _latest = new() { DnsOverHttps = false };
    private Implementation.NetworkHttpClientPool? _pool;
    private HttpClient? _redirecting, _direct;
    private Implementation.NetworkManualProbeService? _probe;

    internal NetworkServiceSession(NativeUiCallTable peer)
    {
        _endpoint = new(ExecuteAsync, _ => throw new InvalidDataException("Unexpected network publication."), Report, Rejected);
        _endpoint.Connect(peer); _http = new(SendAsync); _downloads = new(_endpoint, SendDownloadAsync);
    }
    internal NativeUiCallTable Table => _endpoint.Table;
    private static void Report(Exception error) => Console.Error.WriteLine("Native network operation failed: " + error.GetType().Name);
    private async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = NetworkProtocol.Read(bytes, out int operation);
        if (operation == NetworkProtocol.Initialize)
        {
            string directory = reader.String() ?? ""; bool mainland = reader.Boolean(); reader.End();
            if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("Network base directory must be absolute.");
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_pool is not null) throw new InvalidOperationException("Network session is already initialized.");
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
                _pool = new(() => _current.Value?.Preferences ?? Volatile.Read(ref _latest), mainland);
                _direct = _pool.CreateClient(allowAutoRedirect: false); _redirecting = _pool.CreateClient();
                _direct.Timeout = Timeout.InfiniteTimeSpan; _redirecting.Timeout = Timeout.InfiniteTimeSpan;
                _probe = new(() => _pool.CreateClient(allowAutoRedirect: false), mainland);
            }
            finally { _initialization.Release(); }
            return [];
        }
        if (_pool is null) throw new InvalidOperationException("Network session has not initialized.");
        if (operation == DownloadProtocol.Download) return await _downloads.ExecuteAsync(reader, token).ConfigureAwait(false);
        if (operation is NetworkProtocol.CreateCertificate or NetworkProtocol.ClaimCertificate or NetworkProtocol.ReleaseCertificate)
        {
            using var writer = new ServiceWireWriter();
            if (operation == NetworkProtocol.CreateCertificate)
            {
                byte[] pfx = reader.Buffer(); string? password = reader.String(); reader.End(); token.ThrowIfCancellationRequested();
                var certificate = new NetworkCertificatePool(pfx, password, _pool);
                try { writer.Int64(_certificates.Register(certificate)); }
                catch { await certificate.DisposeAsync().ConfigureAwait(false); throw; }
            }
            else
            {
                long handle = reader.Int64(); reader.End();
                if (operation == NetworkProtocol.ClaimCertificate) writer.Boolean(_certificates.Claim(handle));
                else await _certificates.ReleaseAsync(handle).ConfigureAwait(false);
            }
            return writer.Finish();
        }
        if (operation is NetworkProtocol.Trace or NetworkProtocol.Probe)
        {
            var policy = reader.NetworkPreferences() ?? throw new InvalidDataException("Missing network preferences.");
            policy.Validate(); reader.End(); Volatile.Write(ref _latest, policy); _current.Value = new(policy, false);
            try
            {
                using var writer = new ServiceWireWriter();
                if (operation == NetworkProtocol.Probe)
                    writer.NetworkManualProbeSnapshot(await _probe!.ProbeAsync(token).ConfigureAwait(false));
                else
                {
                    var trace = _pool.TraceSnapshot(); writer.Count(trace.Count);
                    foreach (var item in trace) writer.NetworkRequestTrace(item);
                }
                return writer.Finish();
            }
            finally { _current.Value = null; }
        }
        return await _http.ExecuteAsync(operation, reader, token).ConfigureAwait(false);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ReadOnlyMemory<byte> configuration, CancellationToken token)
    {
        using var reader = new ServiceWireReader(configuration);
        var policy = reader.NetworkPreferences() ?? throw new InvalidDataException("Missing committed network policy.");
        policy.Validate(); bool redirects = reader.Boolean(); bool probe = reader.Boolean(); long certificate = reader.Int64(); reader.End();
        if (probe) request.Options.Set(new HttpRequestOptionsKey<bool>("Nexa.Network.DiagnosticProbe"), true);
        _current.Value = new(policy, redirects); Volatile.Write(ref _latest, policy);
        NativeUiAsyncResources<NetworkCertificatePool>.Borrow? borrow = null;
        try
        {
            if (certificate != 0)
            {
                if (redirects) throw new InvalidDataException("Client certificates require redirects to be disabled.");
                borrow = _certificates.Acquire(certificate);
            }
            var client = borrow?.Resource.Client ?? (redirects ? _redirecting! : _direct!);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (borrow is not null) { NetworkCertificatePool.Retain(response, borrow); borrow = null; }
            return response;
        }
        finally { borrow?.Dispose(); _current.Value = null; }
    }
    private Task<HttpResponseMessage> SendDownloadAsync(HttpRequestMessage request, NativeDownloadSource source, bool redirects, CancellationToken token)
    {
        if (source.Certificate != 0 && source.CertificateContext != Table.Context)
            throw new InvalidDataException("Download certificate belongs to another network context.");
        using var writer = new ServiceWireWriter(); writer.NetworkPreferences(source.Preferences);
        writer.Boolean(redirects); writer.Boolean(false); writer.Int64(source.Certificate);
        return SendAsync(request, writer.Finish(), token);
    }
    private void Rejected(long id, ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> result)
    {
        using var reader = NetworkProtocol.Read(request, out int operation);
        if (operation is not (NetworkHttpWire.Create or NetworkProtocol.CreateCertificate)) return;
        using var response = new ServiceWireReader(result); long handle = response.Int64(); response.End();
        _ = ObserveRejectionAsync(handle, operation == NetworkProtocol.CreateCertificate);
    }
    private async Task ObserveRejectionAsync(long handle, bool certificate)
    { try { await (certificate ? _certificates.RejectProvisionalAsync(handle) : _http.RejectAsync(handle)).ConfigureAwait(false); } catch (Exception error) { Report(error); } }
    public async ValueTask DisposeAsync()
    {
        await _endpoint.StopAsync().ConfigureAwait(false);
        await _http.DisposeAsync().ConfigureAwait(false);
        await _downloads.DisposeAsync().ConfigureAwait(false);
        await _certificates.DisposeAsync().ConfigureAwait(false);
        _direct?.Dispose(); _redirecting?.Dispose(); _pool?.Dispose();
        if (_pool is not null) await _pool.WhenDrained.ConfigureAwait(false);
        await _endpoint.DisposeAsync().ConfigureAwait(false); _initialization.Dispose();
    }
}
