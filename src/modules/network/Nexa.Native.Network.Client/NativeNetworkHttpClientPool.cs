using Nexa.Services.Network;

namespace Nexa.Native.Network.Client;

/// <summary>Local HTTP factories over one native network service lifetime.</summary>
public sealed class NativeNetworkHttpClientPool(Func<NetworkPreferences> capture, bool mainlandChina = false) : IDisposable, IAsyncDisposable
{
    public NativeNetworkClient Client { get; } = new(mainlandChina);
    private Task? _retirement;
    public HttpMessageHandler CreateHandler(bool allowAutoRedirect = true) => Client.CreateHandler(capture, allowAutoRedirect);
    public HttpClient CreateClient(bool allowAutoRedirect = true, Func<HttpMessageHandler, HttpMessageHandler>? wrap = null)
        => Client.CreateClient(capture, allowAutoRedirect, wrap);
    public Task<IReadOnlyList<NetworkRequestTrace>> TraceAsync(CancellationToken token = default) => Client.TraceAsync(capture(), token);
    public Task<NetworkManualProbeSnapshot> ProbeAsync(CancellationToken token = default) => Client.ProbeAsync(capture(), token);
    public HttpClient CreateCertifiedClient(byte[] pfx, string? password) => Client.CreateCertifiedClient(pfx, password, capture);
    public void Dispose() { _ = DisposeAsync().AsTask(); }
    public ValueTask DisposeAsync() { lock (Client) return new(_retirement ??= Client.DisposeAsync().AsTask()); }
}
