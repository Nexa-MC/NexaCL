using Nexa.Services.Downloads;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadRejectsChangedSegmentTotals()
    {
        string root = CreateTempDirectory();
        try
        {
            using var service = CreateDownloadService(minimumSegmentBytes: 16);
            byte[] bytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
            string destination = Path.Combine(root, "segmented.bin");
            var result = await service.DownloadAsync(new()
            {
                Sources = ["changed", "stable"],
                DestinationPath = destination,
                MaxParallelSegments = 4,
                ConnectionFactory = source => source == "changed" ? new ChangedTotalConnection(bytes) : new FakeSegmentedConnection(bytes),
            });
            AssertTrue(result.Success);
            AssertEqual("stable", result.SuccessfulSource);
            AssertEqual(1, result.Errors.Count);
            AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes));
            AssertEqual(0, Directory.GetFiles(root, "*.NexaSegment.*").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ChangedTotalConnection(byte[] bytes) : ISegmentedDownloadConnection
    {
        private readonly FakeSegmentedConnection _inner = new(bytes);
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default)
            => _inner.StartAsync(beginOffset, cancellationToken);
        public async ValueTask<DownloadConnectionInfo> StartSegmentAsync(long beginOffset, long endOffset, CancellationToken cancellationToken = default)
        {
            var info = await _inner.StartSegmentAsync(beginOffset, endOffset, cancellationToken);
            return endOffset == 0 ? info : info with { Length = info.Length + 1 };
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public ValueTask StopAsync(CancellationToken cancellationToken = default) => _inner.StopAsync(cancellationToken);
    }

    private static async ValueTask DownloadRejectsUnexpectedResumeOffsets()
    {
        string root = CreateTempDirectory();
        try
        {
            foreach (long offset in new long[] { -1, 1, 4 })
            {
                using var service = CreateDownloadService();
                string destination = Path.Combine(root, offset + ".bin");
                await File.WriteAllBytesAsync(destination, [42]);
                await File.WriteAllBytesAsync(destination + ".PCLDownloading", [1, 2]);
                var request = new DownloadRequest
                {
                    Sources = ["wrong-range"],
                    DestinationPath = destination,
                    ConnectionFactory = _ => new UnexpectedOffsetConnection(offset),
                };
                var rejected = await service.DownloadAsync(request);
                AssertFalse(rejected.Success);
                AssertTrue(rejected.Errors.Single().Failure is InvalidDataException);
                AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 42 }));
                AssertTrue((await File.ReadAllBytesAsync(destination + ".PCLDownloading")).SequenceEqual(new byte[] { 1, 2 }));
                var recovered = await service.DownloadAsync(request with
                {
                    Sources = ["wrong-range", "correct-range"],
                    ConnectionFactory = source => source == "wrong-range" ? new UnexpectedOffsetConnection(offset) : new FakeConnection(3, [3]),
                });
                AssertTrue(recovered.Success);
                AssertEqual("correct-range", recovered.SuccessfulSource);
                AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 1, 2, 3 }));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class UnexpectedOffsetConnection(long offset) : IDownloadConnection
    {
        private bool _read;
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DownloadConnectionInfo(-1, offset, -1, false));
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) return ValueTask.FromResult(0);
            _read = true; buffer.Span[0] = 99; return ValueTask.FromResult(1);
        }
        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
