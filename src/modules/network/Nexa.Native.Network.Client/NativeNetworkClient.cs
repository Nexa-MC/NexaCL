using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Host.Services;
using Nexa.Native.Network.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Network;

namespace Nexa.Native.Network.Client;

/// <summary>Async binary network admission and local streaming HTTP handlers.</summary>
public sealed partial class NativeNetworkClient : IAsyncDisposable, INativeDownloadPort
{
    private readonly ServiceEndpoint _endpoint;
    private readonly nint _close;
    private NativeHostServiceAdmission? _hostAdmission;
    private readonly long _context;
    private readonly Task _initialization;
    private readonly object _gate = new();
    private readonly List<NetworkHttpStreamHandler> _handlers = [];
    private readonly List<Task<long>> _certificates = [];
    private Task? _retirement;
    private bool _closed;
    private Exception? _handlerFailure;
    private readonly Action<Exception>? _failure;

    public NativeNetworkClient(bool mainlandChina = false, Action<Exception>? failure = null)
    {
        _failure = failure;
        _endpoint = new(ExecuteReverseAsync, ApplyPublication, Report, RejectReverse);
        try { (_context, _close) = Open(_endpoint); }
        catch { _ = _endpoint.DisposeAsync().AsTask(); throw; }
        _initialization = InitializeAsync(mainlandChina);
    }
    private void Report(Exception error) { try { _failure?.Invoke(error); } catch { } }
    private async Task InitializeAsync(bool mainland)
    {
        using var writer = NetworkProtocol.Request(NetworkProtocol.Initialize);
        writer.String(AppContext.BaseDirectory); writer.Boolean(mainland);
        byte[] bytes = await _endpoint.InvokeAsync(writer.Finish()).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    private async Task<byte[]> HttpAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await _initialization.WaitAsync(token).ConfigureAwait(false);
        return await _endpoint.InvokeAsync(bytes, token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<NetworkRequestTrace>> TraceAsync(NetworkPreferences preferences, CancellationToken token = default)
    {
        preferences.Validate();
        using var writer = NetworkProtocol.Request(NetworkProtocol.Trace); writer.NetworkPreferences(preferences);
        byte[] bytes = await HttpAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int count = reader.Count();
        if (count is < 0 or > 256) throw new InvalidDataException("Invalid native trace count.");
        NetworkRequestTrace[] trace = new NetworkRequestTrace[count];
        for (int index = 0; index < count; index++) trace[index] = reader.NetworkRequestTrace() ?? throw new InvalidDataException("Missing native trace item.");
        reader.End(); return Array.AsReadOnly(trace);
    }
    public async Task<NetworkManualProbeSnapshot> ProbeAsync(NetworkPreferences preferences, CancellationToken token = default)
    {
        preferences.Validate();
        using var writer = NetworkProtocol.Request(NetworkProtocol.Probe); writer.NetworkPreferences(preferences);
        byte[] bytes = await HttpAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var snapshot = reader.NetworkManualProbeSnapshot() ?? throw new InvalidDataException("Missing native probe result.");
        reader.End(); return snapshot;
    }
    /// <summary>Imports PFX identity in the network runtime; retained until this client retires.</summary>
    public async Task<HttpMessageHandler> CreateCertifiedHandlerAsync(byte[] pfx, string? password,
        Func<NetworkPreferences> preferences, CancellationToken token = default)
    {
        long certificate = await AdmitCertificate(pfx, password, preferences, token).ConfigureAwait(false);
        return CreateHandler(preferences, allowAutoRedirect: false, certificate);
    }
    private Task<long> AdmitCertificate(byte[] pfx, string? password, Func<NetworkPreferences> preferences, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(preferences); ArgumentNullException.ThrowIfNull(pfx);
        if (pfx.Length is < 1 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(pfx));
        Task<long> creation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _certificates.RemoveAll(task => task.IsFaulted || task.IsCanceled);
            if (_certificates.Count >= 32) throw new InvalidOperationException("Native client certificate budget exceeded.");
            creation = CreateCertificateAsync(pfx, password, token); _certificates.Add(creation);
        }
        return creation;
    }
    private async Task<long> CreateCertificateAsync(byte[] pfx, string? password, CancellationToken token)
    {
        using var writer = NetworkProtocol.Request(NetworkProtocol.CreateCertificate); writer.Buffer(pfx); writer.String(password);
        byte[] bytes = await HttpAsync(writer.Finish(), token).ConfigureAwait(false);
        long handle;
        using (var reader = new ServiceWireReader(bytes)) { handle = reader.Int64(); reader.End(); }
        try
        {
            using var claim = NetworkProtocol.Request(NetworkProtocol.ClaimCertificate); claim.Int64(handle);
            byte[] confirmed = await HttpAsync(claim.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(confirmed);
            if (!reader.Boolean()) throw new InvalidOperationException("Native certificate retired before admission.");
            reader.End(); return handle;
        }
        catch { await ReleaseCertificateAsync(handle).ConfigureAwait(false); throw; }
    }
    private async Task ReleaseCertificateAsync(long handle)
    {
        using var release = NetworkProtocol.Request(NetworkProtocol.ReleaseCertificate); release.Int64(handle);
        byte[] bytes = await HttpAsync(release.Finish(), CancellationToken.None).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public HttpMessageHandler CreateHandler(Func<NetworkPreferences> preferences, bool allowAutoRedirect = true)
        => CreateHandler(preferences, allowAutoRedirect, certificate: 0);
    private NetworkHttpStreamHandler CreateHandler(Func<NetworkPreferences> preferences, bool allowAutoRedirect, long certificate)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            for (int index = _handlers.Count - 1; index >= 0; index--)
            {
                Task retired = _handlers[index].Retirement;
                if (!retired.IsCompleted) continue;
                if (retired.IsFaulted) _handlerFailure ??= retired.Exception;
                _handlers.RemoveAt(index);
            }
            if (_handlers.Count >= 128) throw new InvalidOperationException("Native HTTP handler budget exceeded.");
            var handler = new NetworkHttpStreamHandler(HttpAsync, request =>
            {
                var policy = preferences(); policy.Validate();
                using var writer = new ServiceWireWriter(); writer.NetworkPreferences(policy); writer.Boolean(allowAutoRedirect);
                writer.Boolean(request.Options.TryGetValue(new HttpRequestOptionsKey<bool>("Nexa.Network.DiagnosticProbe"), out bool probe) && probe);
                writer.Int64(certificate);
                return writer.Finish();
            });
            _handlers.Add(handler); return handler;
        }
    }
    private unsafe (long Context, nint Close) Open(ServiceEndpoint endpoint)
    {
        if (NativeHostServiceAdmission.Required)
        {
            _hostAdmission = NativeHostServiceAdmission.Open(2, NetworkProtocol.Identity, endpoint.Table, () => DisposeAsync().AsTask());
            endpoint.Connect(_hostAdmission.Owner);
            return (_hostAdmission.Owner.Context, 0);
        }
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint image = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-network" + suffix));
        var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, "nexa_network_service_identity_v1");
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, int>)NativeLibrary.GetExport(image, "nexa_network_service_open_v1");
        nint close = NativeLibrary.GetExport(image, "nexa_network_service_close_v1");
        byte[] expected = Encoding.UTF8.GetBytes(NetworkProtocol.Identity), actual = new byte[expected.Length];
        if (identity(null, 0) != actual.Length) throw new InvalidDataException("Incompatible native network service.");
        fixed (byte* output = actual)
            if (identity(output, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Incompatible native network service.");
        var host = endpoint.Table; NativeUiCallTable peer = default;
        if (open(&host, &peer) != 0) throw new InvalidOperationException("Native network admission failed.");
        endpoint.Connect(peer); return (peer.Context, close);
    }
    private unsafe int Close() => _hostAdmission is { } admission ? admission.CloseOwner() : ((delegate* unmanaged[Cdecl]<long, int>)_close)(_context);
    public ValueTask DisposeAsync()
    { lock (_gate) { _closed = true; return new(_retirement ??= RetireAsync()); } }
    private async Task RetireAsync()
    {
        Exception? failed = _handlerFailure;
        try { await Task.WhenAll(_handlers.Select(handler => handler.DisposeAsync().AsTask())).ConfigureAwait(false); }
        catch (Exception error) { failed ??= error; }
        foreach (Task<long> creation in _certificates)
        {
            long handle;
            try { handle = await creation.ConfigureAwait(false); } catch { continue; }
            try { await ReleaseCertificateAsync(handle).ConfigureAwait(false); } catch (Exception error) { failed ??= error; }
        }
        int delay = 1;
        while (true)
        {
            int status = Close(); if (status == 1) break;
            if (status != 0) throw new IOException("Native network shutdown failed; callbacks remain retained.");
            await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32);
        }
        await DrainDownloadsAsync().ConfigureAwait(false);
        await _endpoint.DisposeAsync().ConfigureAwait(false);
        if (failed is not null) throw new IOException("Native HTTP handler cleanup failed.", failed);
        _hostAdmission?.Complete();
    }
}
