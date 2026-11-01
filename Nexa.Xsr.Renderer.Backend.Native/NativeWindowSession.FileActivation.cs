using Nexa.Native.Window.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private Action<IReadOnlyList<string>>? _fileActivation;

    public async Task<NativeWindowFileActivationLease> SubscribeFileActivationsAsync(Action<IReadOnlyList<string>> activate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activate); cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _fileActivation, activate, null) is not null)
            throw new InvalidOperationException("Native file activation already has an owner.");
        var lease = new NativeWindowFileActivationLease(this, activate);
        try
        {
            using var writer = WindowControlProtocol.Write((int)WindowCommand.FileActivation); writer.Boolean(true);
            await _operations.InvokeAsync(writer.Finish(), CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); return lease;
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal async Task ReleaseFileActivationAsync(Action<IReadOnlyList<string>> activate)
    {
        try
        {
            if (Volatile.Read(ref _uiRetired) != 0) return;
            if (Volatile.Read(ref _nativeRetiring) != 0) { await _uiStopped.Task.ConfigureAwait(false); return; }
            using var writer = WindowControlProtocol.Write((int)WindowCommand.FileActivation); writer.Boolean(false);
            try { await _operations.InvokeAsync(writer.Finish()).ConfigureAwait(false); }
            catch (Exception) when (Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0)
            { await _uiStopped.Task.ConfigureAwait(false); }
        }
        finally { Interlocked.CompareExchange(ref _fileActivation, null, activate); }
    }
}

/// <summary>Owns the native document subscription, including queued activation drainage.</summary>
public sealed class NativeWindowFileActivationLease : IDisposable, IAsyncDisposable
{
    private readonly NativeWindowSession _owner;
    private readonly Action<IReadOnlyList<string>> _activate;
    private TaskCompletionSource? _release;
    internal NativeWindowFileActivationLease(NativeWindowSession owner, Action<IReadOnlyList<string>> activate)
    { _owner = owner; _activate = activate; }
    public void Dispose() => _ = BeginRelease();
    public ValueTask DisposeAsync() => new(BeginRelease());
    private Task BeginRelease()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.CompareExchange(ref _release, completion, null);
        if (previous is not null) return previous.Task;
        _ = ReleaseAsync(completion); return completion.Task;
    }
    private async Task ReleaseAsync(TaskCompletionSource completion)
    {
        try { await _owner.ReleaseFileActivationAsync(_activate).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
