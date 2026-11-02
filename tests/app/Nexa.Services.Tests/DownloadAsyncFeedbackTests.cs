using Nexa.Services.Downloads;
using Nexa.Services.Scheduling;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadsAwaitAsynchronousDiskFeedback()
    {
        string directory = CreateTempDirectory();
        try
        {
            foreach (bool cancel in new[] { false, true })
            {
                var feedback = new AwaitedDiskFeedback();
                XsrStateStoreBuilder builder = new(); DownloadService.DeclareState(builder);
                using var service = new DownloadService(builder.Build()) { WorkScheduler = feedback };
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                string path = Path.Combine(directory, cancel ? "canceled.bin" : "committed.bin");
                Task<DownloadTransferResult> pending = service.DownloadAsync(new()
                {
                    Sources = ["fixture"],
                    DestinationPath = path,
                    ConnectionFactory = _ => new FakeConnection(2, [7, 8])
                }, cancellationToken: stop.Token);
                try
                {
                    await feedback.Observed.Task.WaitAsync(stop.Token);
                    AssertFalse(pending.IsCompleted); AssertFalse(File.Exists(path)); AssertEqual(1, feedback.Active);
                    if (cancel) stop.Cancel(); else feedback.Release.TrySetResult();
                    if (cancel)
                    {
                        bool canceled = false;
                        try { await pending; } catch (OperationCanceledException) { canceled = true; }
                        AssertTrue(canceled); AssertFalse(File.Exists(path));
                    }
                    else
                    {
                        AssertTrue((await pending).Success); AssertTrue(File.ReadAllBytes(path) is [7, 8]);
                        AssertEqual(2, feedback.Bytes); AssertTrue(feedback.Elapsed >= TimeSpan.Zero);
                    }
                    AssertEqual(0, feedback.Active);
                }
                finally { feedback.Release.TrySetResult(); try { await pending; } catch (OperationCanceledException) { } }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private sealed class AwaitedDiskFeedback : IWorkScheduler, IAsyncWorkResourceFeedback
    {
        internal TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Active, Bytes;
        internal TimeSpan Elapsed;
        public WorkPriority CurrentPriority => WorkPriority.Interactive;
        public IDisposable UsePriority(WorkPriority priority) => new FeedbackLease(() => { });
        public IWorkQuietLease EnterQuiet() => throw new NotSupportedException();
        public ValueTask<IDisposable> AcquireAsync(WorkPriority priority, WorkResource resource, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Interlocked.Increment(ref Active);
            return ValueTask.FromResult<IDisposable>(new FeedbackLease(() => Interlocked.Decrement(ref Active)));
        }
        public async ValueTask ObserveDiskWriteAsync(int bytes, TimeSpan elapsed, CancellationToken token = default)
        { Bytes = bytes; Elapsed = elapsed; Observed.TrySetResult(); await Release.Task.WaitAsync(token); }
        private sealed class FeedbackLease(Action release) : IDisposable
        {
            private Action? _release = release;
            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}
