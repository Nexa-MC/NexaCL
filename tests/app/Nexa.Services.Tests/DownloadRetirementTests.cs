using Nexa.Services.Downloads;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadNewCallersWaitForRetirement()
    {
        string root = CreateTempDirectory();
        using var service = CreateDownloadService();
        using var caller = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connection = new RetiringDownloadConnection();
        int attempts = 0;
        DownloadRequest request = new()
        {
            Sources = ["memory://retirement"],
            DestinationPath = Path.Combine(root, "artifact"),
            ConnectionFactory = _ => Interlocked.Increment(ref attempts) == 1 ? connection : new FakeConnection(2, [4, 2]),
        };
        Task<DownloadTransferResult>? first = null, next = null;
        try
        {
            first = service.DownloadAsync(request, cancellationToken: caller.Token);
            await connection.Reading.Task.WaitAsync(deadline.Token);
            caller.Cancel();
            await connection.Stopping.Task.WaitAsync(deadline.Token);
            next = service.DownloadAsync(request, cancellationToken: deadline.Token);
            using var queuedCancellation = new CancellationTokenSource();
            var abandoned = service.DownloadAsync(request, cancellationToken: queuedCancellation.Token);
            queuedCancellation.Cancel();
            await TrustRejectsAsync<OperationCanceledException>(() => abandoned);
            AssertFalse(first.IsCompleted); AssertFalse(next.IsCompleted); AssertEqual(1, attempts);
            connection.Release.TrySetResult();
            bool canceled = false;
            try { await first; } catch (OperationCanceledException) { canceled = true; }
            AssertTrue(canceled);
            AssertTrue((await next).Success); AssertEqual(2, attempts);
            AssertTrue(File.ReadAllBytes(request.DestinationPath) is [4, 2]);
        }
        finally
        {
            caller.Cancel(); connection.Release.TrySetResult();
            foreach (var task in new[] { first, next })
                if (task is not null)
                    try { await task; } catch (OperationCanceledException) { }
            Directory.Delete(root, true);
        }
    }

    private static async ValueTask DownloadCancellationFailuresStillDrainWriters()
    {
        string root = CreateTempDirectory();
        using var service = CreateDownloadService();
        using var caller = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connection = new RetiringDownloadConnection { ThrowOnCancellation = true };
        Task<DownloadTransferResult>? pending = null;
        try
        {
            pending = service.DownloadAsync(new()
            {
                Sources = ["memory://cancellation-failure"],
                DestinationPath = Path.Combine(root, "artifact"),
                ConnectionFactory = _ => connection,
            }, cancellationToken: caller.Token);
            await connection.Reading.Task.WaitAsync(deadline.Token);
            caller.Cancel();
            await connection.Stopping.Task.WaitAsync(deadline.Token);
            await TrustRejectsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromMilliseconds(100)));
            connection.Release.TrySetResult();
            await TrustRejectsAsync<AggregateException>(() => pending);
            AssertFalse(File.Exists(Path.Combine(root, "artifact")));
        }
        finally
        {
            caller.Cancel(); connection.Release.TrySetResult();
            if (pending is not null)
                try { await pending; } catch (Exception error) when (error is OperationCanceledException or AggregateException) { }
            Directory.Delete(root, true);
        }
    }

    private sealed class RetiringDownloadConnection : IDownloadConnection
    {
        private CancellationTokenRegistration _fault;
        internal bool ThrowOnCancellation { get; init; }
        internal TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DownloadConnectionInfo(2, beginOffset, 1, false));
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _fault = ThrowOnCancellation
                ? cancellationToken.Register(static () => throw new InvalidOperationException("cancellation fixture")) : default;
            Reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        { Stopping.TrySetResult(); await Release.Task; _fault.Dispose(); }
    }
}
