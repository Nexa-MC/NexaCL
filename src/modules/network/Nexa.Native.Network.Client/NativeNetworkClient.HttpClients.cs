using Nexa.Native.Network.Services;
using Nexa.Services.Network;

namespace Nexa.Native.Network.Client;

public sealed partial class NativeNetworkClient
{
    public HttpClient CreateCertifiedClient(byte[] pfx, string? password, Func<NetworkPreferences> preferences)
    {
        Task<long> certificate = AdmitCertificate(pfx, password, preferences, CancellationToken.None);
        async Task<HttpMessageInvoker> PrepareAsync() => new(await CreateAsync().ConfigureAwait(false));
        async Task<HttpMessageHandler> CreateAsync() => CreateHandler(preferences, false, await certificate.ConfigureAwait(false));
        HttpClient client = new(new DeferredHandler(PrepareAsync()));
        NativeHttpClients.Register(client, async (address, token) => new(address, preferences(), false,
            Certificate: await certificate.WaitAsync(token).ConfigureAwait(false), CertificateContext: _context));
        return client;
    }
    private sealed class DeferredHandler(Task<HttpMessageInvoker> prepared) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            await (await prepared.WaitAsync(token).ConfigureAwait(false)).SendAsync(request, token).ConfigureAwait(false);
        protected override void Dispose(bool disposing)
        { if (disposing) _ = RetireAsync(); base.Dispose(disposing); }
        private async Task RetireAsync()
        { try { (await prepared.ConfigureAwait(false)).Dispose(); } catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { } }
    }
    public HttpClient CreateClient(Func<NetworkPreferences> preferences, bool allowAutoRedirect = true,
        Func<HttpMessageHandler, HttpMessageHandler>? wrap = null)
    {
        var handler = CreateHandler(preferences, allowAutoRedirect);
        HttpClient client;
        try { client = new(wrap is null ? handler : wrap(handler)); }
        catch { handler.Dispose(); throw; }
        NativeHttpClients.Register(client, (address, _) => ValueTask.FromResult(new NativeDownloadSource(address, preferences(), allowAutoRedirect)));
        return client;
    }
}
