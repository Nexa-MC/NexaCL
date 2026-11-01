using System.Net;
using System.Security.Cryptography.X509Certificates;
using Nexa.Native.Ui;

namespace Nexa.Native.Network;

// Imported key material and its specialized transports remain in the network runtime.
internal sealed class NetworkCertificatePool : IAsyncDisposable
{
    internal Implementation.NetworkHttpClientPool Pool { get; }
    internal HttpClient Client { get; }
    private readonly X509Certificate2 _certificate;

    internal NetworkCertificatePool(byte[] pfx, string? password, Implementation.NetworkHttpClientPool transport)
    {
        if (pfx.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("Invalid network certificate size.");
        X509KeyStorageFlags storage = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet
            : OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        _certificate = X509CertificateLoader.LoadPkcs12(pfx, password, storage);
        try
        {
            DateTime now = DateTime.UtcNow;
            if (!_certificate.HasPrivateKey || now < _certificate.NotBefore.ToUniversalTime() || now > _certificate.NotAfter.ToUniversalTime())
                throw new InvalidOperationException("Network client certificate is unavailable.");
            Pool = transport.CreateSpecialized(handler => handler.SslOptions.ClientCertificates = new X509CertificateCollection { _certificate });
            Client = Pool.CreateClient(allowAutoRedirect: false); Client.Timeout = Timeout.InfiniteTimeSpan;
        }
        catch { _certificate.Dispose(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); Pool.Dispose();
        try { await Pool.WhenDrained.ConfigureAwait(false); }
        finally { _certificate.Dispose(); }
    }
    internal static void Retain(HttpResponseMessage response, NativeUiAsyncResources<NetworkCertificatePool>.Borrow borrow)
    {
        var content = new CertificateContent(response.Content, borrow); content.CopyHeaders(); response.Content = content;
    }
    private sealed class CertificateContent(HttpContent inner, NativeUiAsyncResources<NetworkCertificatePool>.Borrow borrow) : HttpContent
    {
        private NativeUiAsyncResources<NetworkCertificatePool>.Borrow? _borrow = borrow;
        protected override bool TryComputeLength(out long length) { length = inner.Headers.ContentLength ?? 0; return inner.Headers.ContentLength.HasValue; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token) => inner.CopyToAsync(stream, token);
        protected override Task<Stream> CreateContentReadStreamAsync() => inner.ReadAsStreamAsync();
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => inner.ReadAsStreamAsync(token);
        internal void CopyHeaders() { foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value); }
        protected override void Dispose(bool disposing)
        {
            try { if (disposing) inner.Dispose(); }
            finally { if (disposing) Interlocked.Exchange(ref _borrow, null)?.Dispose(); base.Dispose(disposing); }
        }
    }
}
