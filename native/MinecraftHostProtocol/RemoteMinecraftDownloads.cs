using System.Collections.Concurrent;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Network.Client;
using Nexa.Native.Network.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Downloads;

namespace Nexa.Native.Minecraft;

internal sealed class RemoteMinecraftDownloads(ServiceEndpoint endpoint) : INativeDownloadPort
{
    private readonly ConcurrentDictionary<long, DownloadProgressSink> _progress = new();
    private long _next;
    internal async ValueTask<NativeDownloadSource> DescribeAsync(string address, CancellationToken token)
    {
        using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.DescribeDownload); writer.String(address);
        byte[] bytes = await endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var source = reader.NativeDownloadSource() ?? throw new InvalidDataException("Missing host download descriptor."); reader.End(); return source;
    }
    public async Task<DownloadTransferResult> DownloadAsync(NativeDownloadRequest request, Action<DownloadProgress>? progress = null, CancellationToken token = default)
    {
        long id = Interlocked.Increment(ref _next);
        DownloadProgressSink? sink = progress is null ? null : new(progress);
        if (sink is not null) _progress[id] = sink;
        try
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.Download); writer.Int64(id); writer.NativeDownloadRequest(request);
            byte[] bytes = await endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes);
            var result = reader.NativeDownloadOutcome() ?? throw new InvalidDataException("Missing host download result."); reader.End();
            return NativeDownloadResults.Decode(result);
        }
        finally { _progress.TryRemove(id, out _); if (sink is not null) await sink.DisposeAsync().ConfigureAwait(false); }
    }
    internal void ReceiveProgress(ServiceWireReader reader)
    {
        long id = reader.Int64(); var progress = reader.DownloadProgress(); reader.End();
        if (_progress.TryGetValue(id, out var report)) report.Post(progress);
    }
}
