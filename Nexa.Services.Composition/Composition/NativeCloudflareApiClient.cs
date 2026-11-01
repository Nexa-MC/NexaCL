#if NEXA_NATIVE_SERVICES
using Nexa.Native.Network.Client;

namespace Nexa.Services.Composition;

/// <summary>API Shield client whose key and TLS pool are owned by the native network runtime.</summary>
public sealed class CloudflareApiClient : IDisposable, IAsyncDisposable
{
    private readonly NativeNetworkHttpClientPool? _owned;
    public HttpClient Client { get; }
    private CloudflareApiClient(byte[] pfx, string? password, NativeNetworkHttpClientPool? transport)
    {
        if (transport is null) transport = _owned = new(static () => new() { DnsOverHttps = false });
        Client = transport.CreateCertifiedClient(pfx, password); Client.Timeout = TimeSpan.FromSeconds(15);
    }
    public static CloudflareApiClient? TryCreate(Func<Stream?>? embeddedCertificate = null) => TryCreate(embeddedCertificate, null);
    public static CloudflareApiClient? TryCreate(Func<Stream?>? embeddedCertificate, NativeNetworkHttpClientPool? transport)
    {
        string? path = Environment.GetEnvironmentVariable("NEXA_API_CLIENT_CERT_PATH");
        string? password = Environment.GetEnvironmentVariable("NEXA_API_CLIENT_CERT_PASSWORD");
        if (OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(path))
        {
            path = Environment.GetEnvironmentVariable("NEXA_API_CLIENT_CERT_PATH", EnvironmentVariableTarget.User);
            password ??= Environment.GetEnvironmentVariable("NEXA_API_CLIENT_CERT_PASSWORD", EnvironmentVariableTarget.User);
        }
        byte[] pfx;
        if (!string.IsNullOrWhiteSpace(path)) pfx = File.ReadAllBytes(path);
        else
        {
            using var stream = embeddedCertificate?.Invoke(); if (stream is null) return null;
            using var bytes = new MemoryStream(); stream.CopyTo(bytes); pfx = bytes.ToArray();
        }
        return new(pfx, password, transport);
    }
    public void Dispose() { Client.Dispose(); _owned?.Dispose(); }
    public async ValueTask DisposeAsync()
    { Client.Dispose(); if (_owned is not null) await _owned.DisposeAsync().ConfigureAwait(false); }
}
#endif
