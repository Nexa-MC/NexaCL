using Nexa.Services.Tasks;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask TaskRetryPreservesRecoveryPolicyAndRejectsStaleOwners()
    {
        using var service = NewTaskCenter(out _);
        TaskCompletionSource[] entered = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        TaskCompletionSource[] release = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        int attempts = 0;
        using var original = service.Begin(new("journal", "安装", ["发布"]), async (task, token) =>
        {
            int index = Interlocked.Increment(ref attempts) - 1;
            entered[index].SetResult(); await release[index].Task.WaitAsync(token);
            if (index == 0) task.Fail("需要继续恢复"); else task.Complete();
        });
        var originalToken = original.CancellationToken;
        service.SetRetryCancellation(original.TaskId, false, originalToken);
        AssertTrue(service.ReadEntries().Single().CanCancel);
        original.Fail("已保留发布记录"); original.Dispose();
        for (int index = 0; index < 2; index++)
        {
            AssertTrue(service.RequestRetry("journal"));
            await entered[index].Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertFalse(service.ReadEntries().Single().CanCancel);
            AssertEqual(TaskCenterCancelResult.NotCancelable, service.RequestCancel("journal"));
            service.SetRetryCancellation("journal", true, originalToken);
            release[index].SetResult();
            await WaitForRetryEntryAsync(service, "journal", entry => index == 0 ? entry.CanRetry : entry.State == TaskCenterEntryState.Finished);
        }
    }

    private static async Task WaitForRetryEntryAsync(TaskCenterService service, string id, Func<TaskCenterEntry, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!service.ReadEntries().Any(entry => entry.TaskId == id && predicate(entry)))
            await Task.Delay(10, timeout.Token);
    }

    private static async ValueTask TaskRetryWaitsForCleanupAndRejectsDuplicateClicks()
    {
        using var service = NewTaskCenter(out _);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var original = service.Begin(new("retry", "安装", ["下载", "发布"]), async (task, token) =>
        {
            Interlocked.Increment(ref calls);
            AssertFalse(token.IsCancellationRequested);
            AssertEqual("retry", task.TaskId);
            entered.SetResult();
            await release.Task.WaitAsync(token);
            task.Complete();
        });
        original.Report("下载", "中断", .6, 3, 5, 100);
        original.Fail("网络中断");
        AssertFalse(service.RequestRetry("retry")); // Worker still owns its scratch directory.
        original.Dispose();
        AssertTrue(service.ReadEntries().Single().CanRetry);
        AssertTrue(service.RequestRetry("retry"));
        AssertFalse(service.RequestRetry("retry"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var running = service.ReadEntries().Single();
        AssertEqual(0d, running.Progress); AssertEqual(0, running.CompletedFiles);
        AssertEqual<string?>(null, running.ErrorMessage);
        AssertEqual(TaskCenterEntryState.Waiting, running.Steps![1].State);
        original.Fail("迟到结果"); original.Dispose();
        AssertEqual(TaskCenterEntryState.Running, service.ReadEntries().Single().State);
        release.SetResult();
        await WaitForRetryEntryAsync(service, "retry", entry => entry.State == TaskCenterEntryState.Finished);
        AssertEqual(1, calls); AssertFalse(service.RequestRetry("retry"));
    }

    private static async ValueTask TaskRetryRepeatsFailuresAndHonorsCancellationAndDismissal()
    {
        using var service = NewTaskCenter(out _);
        int attempts = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var original = service.Begin(new("repeat", "下载", ["下载"]), async (task, token) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("仍然离线");
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        })) original.Fail("离线");
        AssertTrue(service.RequestRetry("repeat"));
        await WaitForRetryEntryAsync(service, "repeat", entry => entry.CanRetry);
        AssertEqual("仍然离线", service.ReadEntries().Single().ErrorMessage);
        AssertTrue(service.RequestRetry("repeat"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AssertEqual(TaskCenterCancelResult.Canceled, service.RequestCancel("repeat"));
        await WaitForRetryEntryAsync(service, "repeat", entry => entry.State == TaskCenterEntryState.Canceled);
        AssertFalse(service.RequestRetry("repeat"));
        AssertTrue(service.Dismiss("repeat")); AssertFalse(service.RequestRetry("repeat"));
        using (var abandoned = service.Begin(new("dismiss", "下载", ["下载"]), (_, _) => throw new InvalidOperationException("Must not run")))
            abandoned.Fail("离线");
        AssertTrue(service.Dismiss("dismiss")); AssertFalse(service.RequestRetry("dismiss"));
        using (var failed = service.Begin(new("shutdown", "下载", ["下载"]), (_, _) => Task.CompletedTask)) failed.Fail("离线");
        service.Dispose(); AssertFalse(service.RequestRetry("shutdown"));
    }
}
