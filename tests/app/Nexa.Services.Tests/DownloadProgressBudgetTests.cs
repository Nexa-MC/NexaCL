using Nexa.Services.Downloads;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void FileBatchProgressRetainsSpeeds()
    {
        const int count = 8192;
        long speed = -1; int completed = -1; double fraction = -1;
        var progress = new FileBatchProgress(count, (value, done, totalSpeed) =>
        { fraction = value; completed = done; speed = totalSpeed; });
        for (int i = 0; i < count; i++)
            progress.Update(i, new(DownloadStage.Downloading, "memory://batch", 0, 100, 10));
        // Speed updates with no fraction change are throttled but still affect totals.
        progress.Update(1, new(DownloadStage.Downloading, "memory://batch", 0, 100, 30));
        progress.Update(2, new(DownloadStage.Downloading, "memory://batch", 0, 100, -1));
        progress.Complete(0);
        AssertEqual(count * 10L, speed); AssertEqual(1, completed);
        for (int i = 1; i < count; i++) progress.Complete(i);
        AssertEqual(0L, speed); AssertEqual(count, completed); AssertEqual(1d, fraction);
    }

    private static async ValueTask SingleStreamProgressIsBoundedAndKeepsFinalBytes()
    {
        string root = CreateTempDirectory();
        try
        {
            foreach (int milliseconds in new[] { 0, 5 })
            {
                var clock = new DownloadProgressClock();
                var builder = new XsrStateStoreBuilder();
                DownloadService.DeclareState(builder);
                var service = new DownloadService(builder.Build()) { ProgressClock = clock };
                var reports = new List<DownloadProgress>();
                var connection = new ClockedDownloadConnection(clock, milliseconds);
                string path = Path.Combine(root, milliseconds + ".bin");
                var result = await service.DownloadAsync(new()
                {
                    Sources = ["memory://progress"],
                    DestinationPath = path,
                    AllowResume = false,
                    ConnectionFactory = _ => connection
                }, reports.Add);
                AssertTrue(result.Success);
                AssertEqual(1024L * 4096, result.TotalBytes);
                AssertEqual(result.TotalBytes, new FileInfo(path).Length);
                AssertTrue(connection.Stopped);
                DownloadProgress[] moving = reports.Where(p => p.Stage == DownloadStage.Downloading).ToArray();
                AssertTrue(moving.Length >= 2 && moving.Length <= (milliseconds == 0 ? 2 : 53));
                AssertEqual(4096L, moving[0].DownloadedBytes);
                AssertEqual(result.TotalBytes, moving[^1].DownloadedBytes);
                for (int index = 1; index < moving.Length; index++)
                    AssertTrue(moving[index].DownloadedBytes > moving[index - 1].DownloadedBytes);
                AssertEqual(DownloadStage.Connecting, reports[0].Stage);
                AssertEqual(DownloadStage.Reading, reports[1].Stage);
                AssertEqual(DownloadStage.Committing, reports[^2].Stage);
                AssertEqual(DownloadStage.Completed, reports[^1].Stage);
                AssertEqual(result.TotalBytes, reports[^2].DownloadedBytes);
                AssertEqual(result.TotalBytes, reports[^1].DownloadedBytes);
                WaitForDrainedState(service);
                AssertEqual(0, service.StateStore.ReadCollection<DownloadTransferView>(service.StateStore.Resolve(DownloadService.TransfersKey)).Count);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class DownloadProgressClock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    private sealed class ClockedDownloadConnection(DownloadProgressClock clock, int milliseconds) : IDownloadConnection
    {
        private int _chunks;
        public bool Stopped;
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DownloadConnectionInfo(1024L * 4096, 0, 1024L * 4096 - 1, false));
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_chunks++ == 1024) return ValueTask.FromResult(0);
            clock.Milliseconds += milliseconds;
            buffer.Span[..4096].Fill((byte)_chunks);
            return ValueTask.FromResult(4096);
        }
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        { Stopped = true; return ValueTask.CompletedTask; }
    }
}
