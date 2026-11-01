using System.Collections.Concurrent;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Network.Client;
using Nexa.Native.Network.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private INativeDownloadPort? _downloads;
    private HttpClient? _downloadHttp;
    private ServiceStatePublisher? _hostPublications;
    private readonly SemaphoreSlim _downloadAdmission = new(64, 64);
    private readonly ConcurrentQueue<int> _downloadSlots = new(Enumerable.Range(0, 64));
    private long _downloadRevision;

    public void AttachDownloads(NativeNetworkClient network, HttpClient http)
        => AttachDownloads((INativeDownloadPort)network, http);
    public void AttachDownloads(INativeDownloadPort network, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(network); ArgumentNullException.ThrowIfNull(http);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_downloads is not null) throw new InvalidOperationException("Minecraft downloads are already attached.");
            _downloads = network; _downloadHttp = http; _hostPublications = new(_endpoint);
        }
    }
    private async Task<byte[]> ExecuteDownloadAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _);
        if (_downloads is null || _downloadHttp is null) throw new InvalidOperationException("Canonical Minecraft downloads are not attached.");
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftHostOperations.DescribeDownload)
        {
            string address = reader.String() ?? throw new InvalidDataException("Missing download address."); reader.End();
            writer.NativeDownloadSource(await NativeHttpClients.DescribeAsync(_downloadHttp, address, token).ConfigureAwait(false));
            return writer.Finish();
        }
        long call = reader.Int64(); var request = reader.NativeDownloadRequest() ?? throw new InvalidDataException("Missing download request."); reader.End();
        if (call <= 0) throw new InvalidDataException("Invalid native download call.");
        await _downloadAdmission.WaitAsync(token).ConfigureAwait(false);
        if (!_downloadSlots.TryDequeue(out int slot)) { _downloadAdmission.Release(); throw new InvalidOperationException("Download progress slot accounting failed."); }
        try
        {
            var result = await _downloads.DownloadAsync(request, progress =>
            {
                long revision = Interlocked.Increment(ref _downloadRevision);
                _hostPublications!.Post(slot, revision, () =>
                {
                    using var publication = MinecraftServiceProtocol.Request(MinecraftHostOperations.DownloadProgress);
                    publication.Int64(call); publication.DownloadProgress(progress); return publication.Finish();
                });
            }, token).ConfigureAwait(false);
            writer.NativeDownloadOutcome(NativeDownloadResults.Encode(result)); return writer.Finish();
        }
        finally
        {
            try { await _hostPublications!.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { _downloadSlots.Enqueue(slot); _downloadAdmission.Release(); }
        }
    }
}
