using System.Runtime.CompilerServices;
using Nexa.Native.Network.Services;

namespace Nexa.Native.Network.Client;

/// <summary>Local adapter; descriptions are encoded before crossing the native boundary.</summary>
public interface INativeDownloadConnectionDescriptor
{
    ValueTask<NativeDownloadSource> DescribeDownloadAsync(CancellationToken token = default);
}

public interface INativeDownloadWriterDescriptor
{
    string TemporaryExtension { get; }
}

public interface INativeDownloadPort
{
    Task<Nexa.Services.Downloads.DownloadTransferResult> DownloadAsync(NativeDownloadRequest request,
        Action<Nexa.Services.Downloads.DownloadProgress>? progress = null, CancellationToken token = default);
}

public static class NativeHttpClients
{
    private sealed record Description(Func<string, CancellationToken, ValueTask<NativeDownloadSource>> Capture);
    private static readonly ConditionalWeakTable<HttpClient, Description> Descriptions = new();
    private static readonly Lazy<NativeNetworkClient> Default = new(static () => new());
    public static HttpClient CreateDefault(bool allowAutoRedirect = true, Func<HttpMessageHandler, HttpMessageHandler>? wrap = null)
        => Default.Value.CreateClient(static () => new() { DnsOverHttps = false }, allowAutoRedirect, wrap);
    internal static void Register(HttpClient client, Func<string, CancellationToken, ValueTask<NativeDownloadSource>> capture)
        => Descriptions.Add(client, new(capture));
    public static HttpClient CreateDescribed(HttpMessageHandler handler,
        Func<string, CancellationToken, ValueTask<NativeDownloadSource>> capture)
    {
        ArgumentNullException.ThrowIfNull(handler); ArgumentNullException.ThrowIfNull(capture);
        HttpClient client = new(handler); Register(client, capture); return client;
    }
    public static async ValueTask<NativeDownloadSource> DescribeAsync(HttpClient client, string address, CancellationToken token = default)
    {
        if (!Descriptions.TryGetValue(client, out var description))
            throw new NotSupportedException("Native downloads require an explicitly configured native HTTP client.");
        var source = await description.Capture(address, token).ConfigureAwait(false);
        return source with
        {
            HeaderTimeout = client.Timeout,
            Headers = client.DefaultRequestHeaders.Select(header => new NativeHttpHeader(header.Key, header.Value.ToArray())).ToArray()
        };
    }
}
