using Nexa.Native.Ui;

namespace Nexa.Native.Ui.Tests;

internal static partial class Program
{
    private static void DispatcherOwnerLifetime()
    {
        NativeUiSynchronizationContext? context = null;
        int failures = 0, queued = 0;
        context = new(id => context!.Execute(id), _ => Interlocked.Increment(ref failures));
        context.OperationStarted(); // Composition owns this lifetime until controller teardown ends.
        Equal(17, context.ExecuteInline(() =>
        {
            True(ReferenceEquals(context, SynchronizationContext.Current));
            return context.ExecuteInline(() => 17);
        }));
        Throws<IOException>(() => context.ExecuteInline<int>(() => throw new IOException("local inline failure")));
        True(SynchronizationContext.Current is null);
        Throws<InvalidOperationException>(() => context.RunCleanupAsync(() => Task.CompletedTask));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task inline = Task.Run(() => context.ExecuteInline(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return 0;
        }));
        True(entered.Wait(TimeSpan.FromSeconds(10)));
        Task queuedWork = context.RunAsync(() => { Interlocked.Increment(ref queued); return Task.CompletedTask; });
        Equal(0, Volatile.Read(ref queued));
        release.Set(); Task.WhenAll(inline, queuedWork).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        context.ExecuteInline(() => 0); // All old dispatcher callbacks are now outside their scopes.
        context.UseCleanupDispatcher(id => context.Execute(id));
        Throws<ObjectDisposedException>(() => context.RunAsync(() => Task.CompletedTask));
        Throws<ObjectDisposedException>(() => context.ExecuteInline(() => 0));
        Task retired = context.DisposeAsync().AsTask(); True(!retired.IsCompleted);
        context.RunCleanupAsync(async () =>
        {
            True(ReferenceEquals(context, SynchronizationContext.Current));
            await Task.Yield();
            True(ReferenceEquals(context, SynchronizationContext.Current));
            Interlocked.Increment(ref queued);
        }).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        True(!retired.IsCompleted); context.OperationCompleted();
        retired.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Equal(2, queued); Equal(0, failures);
        Throws<ObjectDisposedException>(() => context.RunCleanupAsync(() => Task.CompletedTask));
        Console.WriteLine("PASS: owner lifetime admits ordered controller cleanup without reopening UI admission; inline callbacks serialize with continuations");
    }

    private static void DispatcherDeliveryAndCleanup()
    {
        NativeUiSynchronizationContext? context = null;
        int attempts = 0, executions = 0, failures = 0;
        context = new(id =>
        {
            if (Interlocked.Increment(ref attempts) <= 3) throw new NativeUiCallBusyException();
            context!.Execute(id);
            // A lost acknowledgement after local consumption cannot replay the delegate.
            throw new IOException("acknowledgement lost");
        }, _ => Interlocked.Increment(ref failures));
        context.RunAsync(() => { executions++; return Task.CompletedTask; })
            .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        context.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Equal(4, attempts); Equal(1, executions); Equal(0, failures);
        Throws<ObjectDisposedException>(() => context.Post(_ => { }, null));

        using var failedDelivery = new ManualResetEventSlim();
        long retained = 0;
        NativeUiSynchronizationContext? cleanup = null;
        cleanup = new(id =>
        {
            retained = id; throw new IOException("native dispatcher stopped");
        }, _ => { failedDelivery.Set(); throw new IOException("logger failed"); });
        Task operation = cleanup.RunAsync(async () =>
        {
            True(ReferenceEquals(cleanup, SynchronizationContext.Current));
            await Task.Yield();
            True(ReferenceEquals(cleanup, SynchronizationContext.Current));
            Interlocked.Increment(ref executions);
        });
        True(failedDelivery.Wait(TimeSpan.FromSeconds(10)));
        Task drain = cleanup.DisposeAsync().AsTask(); True(!drain.IsCompleted);
        // This fixture's original dispatcher never executes callbacks and is already stopped.
        cleanup.UseCleanupDispatcher(id => cleanup.Execute(id));
        operation.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        drain.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        True(!cleanup.Execute(retained)); Equal(2, executions);
        Throws<InvalidOperationException>(() => cleanup.UseCleanupDispatcher(_ => { }));
        Console.WriteLine("PASS: dispatcher backpressure, ambiguous delivery and ordered cleanup preserve exactly-once continuations");
    }

    private static void IndependentDispatcher(NativeUiSynchronizationContext context)
    {
        int callerThread = Environment.CurrentManagedThreadId, dispatcherThread = 0;
        void VerifyOwner()
        {
            True(ReferenceEquals(context, SynchronizationContext.Current));
            Equal(dispatcherThread, Environment.CurrentManagedThreadId);
        }
        context.RunAsync(async () =>
        {
            dispatcherThread = Environment.CurrentManagedThreadId;
            True(dispatcherThread != callerThread); VerifyOwner();
            await Task.Delay(10); VerifyOwner();
            await Task.Yield(); VerifyOwner();
        }).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        True(SynchronizationContext.Current is null);
        Throws<NotSupportedException>(() => context.Send(_ => { }, null));
        Throws<InvalidOperationException>(() => context.RunAsync(() => throw new InvalidOperationException("local callback failure"))
            .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            Throws<OperationCanceledException>(() => context.RunAsync(() => throw new InvalidOperationException("must not execute"), canceled.Token)
                .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
        }
        using (var canceled = new CancellationTokenSource())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task canceledOperation = context.RunAsync(async () =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, canceled.Token); }
                finally
                {
                    cleaning.TrySetResult();
                    await finishCleanup.Task;
                    VerifyOwner();
                }
            }, canceled.Token);
            entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            canceled.Cancel(); cleaning.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            True(!canceledOperation.IsCompleted); finishCleanup.TrySetResult();
            Throws<OperationCanceledException>(() => canceledOperation.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
        }

        var taskEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var voidEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var voidRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int cleaned = 0;
        Task active = context.RunAsync(async () =>
        {
            taskEntered.TrySetResult();
            try { await taskRelease.Task; VerifyOwner(); }
            finally { Interlocked.Increment(ref cleaned); }
        });
        context.Post(async _ =>
        {
            voidEntered.TrySetResult();
            try { await voidRelease.Task; VerifyOwner(); }
            finally { Interlocked.Increment(ref cleaned); }
        }, null);
        Task.WhenAll(taskEntered.Task, voidEntered.Task).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Task drain = context.DisposeAsync().AsTask(); True(!drain.IsCompleted);
        Throws<ObjectDisposedException>(() => context.RunAsync(() => Task.CompletedTask));
        taskRelease.TrySetResult(); active.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        True(!drain.IsCompleted); Equal(1, cleaned);
        voidRelease.TrySetResult(); drain.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Equal(2, cleaned); True(SynchronizationContext.Current is null);
        Console.WriteLine("PASS: independent native UI thread resumes Host awaits and drains task/async-void cleanup before context retirement");
    }
}
