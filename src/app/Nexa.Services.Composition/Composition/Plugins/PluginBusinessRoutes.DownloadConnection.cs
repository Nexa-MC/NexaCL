using System.Net;
using Nexa.Services.Downloads;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed class PluginDownloadConnection(HttpClient client, string address, long? expected) : IDownloadConnection
#if NEXA_NATIVE_SERVICES
        , Nexa.Native.Network.Client.INativeDownloadConnectionDescriptor
#endif
    {
        private HttpResponseMessage? _response;
        private Stream? _stream;
        private long _read;
#if NEXA_NATIVE_SERVICES
        public async ValueTask<Nexa.Native.Network.Services.NativeDownloadSource> DescribeDownloadAsync(CancellationToken token = default) =>
            (await Nexa.Native.Network.Client.NativeHttpClients.DescribeAsync(client, address, token).ConfigureAwait(false)) with
            { AllowAutoRedirect = false, MaximumLength = MaximumPluginDownloadBytes, ExactLength = expected };
#endif
        public async ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            if (beginOffset > 0) request.Headers.Range = new(beginOffset, null);
            _response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            _response.EnsureSuccessStatusCode();
            long size = _response.Content.Headers.ContentLength ?? -1, from = 0;
            if (_response.StatusCode == HttpStatusCode.PartialContent)
            {
                if (_response.Content.Headers.ContentRange is not { Unit: "bytes", From: { } start, To: { } end, Length: { } total }
                    || start != beginOffset || end != total - 1 || size >= 0 && size != total - start) throw new IOException("Invalid download range.");
                size = total; from = start;
            }
            if (size > MaximumPluginDownloadBytes || expected is { } declared && size >= 0 && size != declared) throw new IOException("Invalid download size.");
            _read = from; _stream = await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new(size, from, size < 0 ? -1 : size - 1, false);
        }
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await _stream!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); _read += count;
            if (_read > (expected ?? MaximumPluginDownloadBytes)) throw new IOException("Download exceeded its size limit.");
            return count;
        }
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        { _stream?.Dispose(); _response?.Dispose(); _stream = null; _response = null; return ValueTask.CompletedTask; }
    }
}
