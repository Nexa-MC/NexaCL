using Nexa.Native.Ui;

namespace Nexa.Native.Ui.Tests;

internal static partial class Program
{
    private static async Task AsyncResourceOwnership()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int disposals = 0;
        var resource = new AsyncResource(async () =>
        { Interlocked.Increment(ref disposals); started.TrySetResult(); await finish.Task.ConfigureAwait(false); });
        var registry = new NativeUiAsyncResources<AsyncResource>(1);
        long id = registry.Register(resource);
        True(registry.TryAcquire(id) is null); True(registry.Claim(id));
        var borrow = registry.Acquire(id);
        Task retired = registry.ReleaseAsync(id);
        True(ReferenceEquals(retired, registry.ReleaseAsync(id)));
        True(!retired.IsCompleted); Equal(0, disposals);
        True(registry.TryAcquire(id) is null); True(!registry.Claim(id));
        Throws<InvalidOperationException>(() => registry.Register(resource));
        Task stopped = registry.DisposeAsync().AsTask();
        borrow.Dispose(); borrow.Dispose();
        await started.Task.ConfigureAwait(false);
        Equal(1, disposals); True(!retired.IsCompleted); True(!stopped.IsCompleted);
        finish.TrySetResult(); await Task.WhenAll(retired, stopped).ConfigureAwait(false);
        Throws<ObjectDisposedException>(() => registry.Register(resource));

        int abandoned = 0;
        var provisionals = new NativeUiAsyncResources<AsyncResource>(2);
        long claimed = provisionals.Register(new(() => Task.CompletedTask));
        True(provisionals.Claim(claimed));
        await provisionals.RejectProvisionalAsync(claimed).ConfigureAwait(false);
        using (provisionals.Acquire(claimed)) { }
        long rejected = provisionals.Register(new(() => { abandoned++; return Task.CompletedTask; }));
        await provisionals.RejectProvisionalAsync(rejected).ConfigureAwait(false);
        Equal(1, abandoned); await provisionals.DisposeAsync().ConfigureAwait(false);

        var broken = new NativeUiAsyncResources<AsyncResource>(1, _ => throw new InvalidOperationException("logger"));
        long failed = broken.Register(new(() => Task.FromException(new IOException("dispose"))));
        try { await broken.RejectProvisionalAsync(failed).ConfigureAwait(false); throw new InvalidOperationException("Disposal failure was hidden."); }
        catch (IOException) { }
        try { await broken.DisposeAsync().ConfigureAwait(false); throw new InvalidOperationException("Shutdown failure was hidden."); }
        catch (IOException) { }
        Console.WriteLine("PASS: async resource retirement drains users and physical cleanup; shutdown retains cleanup failures");
    }

    private sealed class AsyncResource(Func<Task> dispose) : IAsyncDisposable
    { public ValueTask DisposeAsync() => new(dispose()); }
}
