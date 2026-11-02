using System.Security.Cryptography;
using Nexa.Services.Downloads;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadSessionCacheRejectsBadMirrorAndTriesOfficialSource()
    {
        string root = CreateTempDirectory(); using var service = CreateDownloadService();
        byte[] data = [1, 2, 3]; List<long> offsets = [];
        try
        {
            var result = await service.DownloadAsync(new()
            {
                Sources = ["mirror", "official"],
                DestinationPath = Path.Combine(root, "artifact"),
                CacheIdentity = new(Convert.ToHexString(SHA256.HashData(data)), data.Length),
                ConnectionFactory = source => new RetryBytesConnection(source == "mirror" ? [9, 9, 9] : data, offsets),
            });
            AssertTrue(result.Success); AssertEqual("official", result.SuccessfulSource);
            AssertEqual(1, result.Errors.Count); AssertTrue(offsets.SequenceEqual([0L, 0L]));
            AssertTrue((await File.ReadAllBytesAsync(result.DestinationPath)).SequenceEqual(data));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask DownloadSessionCacheDefersShutdownUntilWritersRelease()
    {
        string root = CreateTempDirectory(); using var service = CreateDownloadService();
        byte[] data = [1, 2, 3]; List<long> offsets = [];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new DownloadRequest
        {
            Sources = ["fixture"],
            DestinationPath = Path.Combine(root, "artifact"),
            CacheIdentity = new(Convert.ToHexString(SHA256.HashData(data)), data.Length),
            ConnectionFactory = _ => new NetworkGatedPackConnection(new RetryBytesConnection(data, offsets),
                async token => { entered.SetResult(); await release.Task.WaitAsync(token); }, () => { }),
        };
        try
        {
            Task<DownloadTransferResult> download = service.DownloadAsync(request);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            string cache = service.SessionCacheDirectory!;
            using var cancel = new CancellationTokenSource();
            var queued = service.DownloadAsync(request with { DestinationPath = Path.Combine(root, "queued") }, cancellationToken: cancel.Token);
            cancel.Cancel(); await TrustRejectsAsync<OperationCanceledException>(() => queued);
            service.Dispose(); AssertTrue(Directory.Exists(cache));
            await TrustRejectsAsync<ObjectDisposedException>(() => service.DownloadAsync(request));
            release.SetResult(); AssertTrue((await download).Success);
            AssertFalse(Directory.Exists(cache)); AssertEqual(1, offsets.Count);
        }
        finally { release.TrySetResult(); Directory.Delete(root, true); }
    }

    private sealed class RetryBytesConnection(byte[] bytes, List<long> offsets, int available = int.MaxValue) : IDownloadConnection
    {
        private int _position;
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default)
        {
            offsets.Add(beginOffset); _position = (int)beginOffset;
            return ValueTask.FromResult(new DownloadConnectionInfo(bytes.Length, beginOffset, bytes.Length - 1, false));
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, Math.Max(0, Math.Min(bytes.Length, available) - _position));
            bytes.AsMemory(_position, count).CopyTo(buffer); _position += count;
            return ValueTask.FromResult(count);
        }
        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private static async ValueTask DownloadSessionCacheResumesAcrossScratchDirectoriesAndCloses()
    {
        string root = CreateTempDirectory();
        using var service = CreateDownloadService();
        byte[] data = [1, 2, 3, 4, 5, 6];
        List<long> offsets = [];
        var request = new DownloadRequest
        {
            Sources = ["fixture"],
            DestinationPath = Path.Combine(root, "first", "artifact"),
            CacheIdentity = new(Convert.ToHexString(SHA256.HashData(data)), data.Length),
            ConnectionFactory = _ => new RetryBytesConnection(data, offsets, 3),
        };
        try
        {
            AssertFalse((await service.DownloadAsync(request)).Success);
            string cache = service.SessionCacheDirectory!;
            AssertTrue(Directory.Exists(cache));
            AssertEqual(3L, new FileInfo(Directory.GetFiles(cache).Single()).Length);
            // Original transaction cleanup cannot touch the cache; retry has a new destination.
            if (Directory.Exists(Path.Combine(root, "first"))) Directory.Delete(Path.Combine(root, "first"), true);
            var retry = request with { DestinationPath = Path.Combine(root, "second", "artifact"), ConnectionFactory = _ => new RetryBytesConnection(data, offsets) };
            List<bool> completedDestinations = [];
            AssertTrue((await service.DownloadAsync(retry, update =>
            {
                if (update.Stage == DownloadStage.Completed) completedDestinations.Add(File.Exists(retry.DestinationPath));
            })).Success);
            AssertTrue(completedDestinations is [true]);
            AssertTrue(offsets.SequenceEqual([0L, 3L]));
            AssertTrue((await File.ReadAllBytesAsync(retry.DestinationPath)).SequenceEqual(data));
            File.Delete(retry.DestinationPath);
            var cached = retry with { DestinationPath = Path.Combine(root, "third", "artifact"), ConnectionFactory = _ => throw new IOException("Offline") };
            AssertTrue((await service.DownloadAsync(cached)).Success); AssertEqual(2, offsets.Count);
            service.Dispose(); AssertFalse(Directory.Exists(cache));
            await TrustRejectsAsync<ObjectDisposedException>(() => service.DownloadAsync(cached));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask DownloadSessionCacheRejectsCorruptionAndCoalescesArtifacts()
    {
        string root = CreateTempDirectory();
        using var service = CreateDownloadService();
        byte[] data = [1, 2, 3, 4]; List<long> offsets = [];
        var request = new DownloadRequest
        {
            Sources = ["fixture"],
            DestinationPath = Path.Combine(root, "one"),
            CacheIdentity = new(Convert.ToHexString(SHA256.HashData(data)), data.Length),
            ConnectionFactory = _ => new RetryBytesConnection(data, offsets),
        };
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(index => service.DownloadAsync(request with { DestinationPath = Path.Combine(root, "file" + index) })));
            AssertTrue(results.All(result => result.Success)); AssertEqual(1, offsets.Count);
            string cacheFile = Directory.GetFiles(service.SessionCacheDirectory!).Single();
            await File.WriteAllBytesAsync(cacheFile, [9, 9, 9, 9]);
            AssertTrue((await service.DownloadAsync(request)).Success); AssertEqual(2, offsets.Count);
            AssertTrue((await File.ReadAllBytesAsync(request.DestinationPath)).SequenceEqual(data));
            // An upstream body with the right length and wrong digest must never become a hit.
            File.Delete(cacheFile);
            var corrupt = request with { ConnectionFactory = _ => new RetryBytesConnection([9, 9, 9, 9], offsets) };
            AssertFalse((await service.DownloadAsync(corrupt)).Success);
            AssertFalse(File.Exists(cacheFile)); AssertFalse(File.Exists(cacheFile + ".PCLDownloading"));
            AssertTrue((await service.DownloadAsync(request)).Success); AssertEqual(4, offsets.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
