using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private readonly NativeUiResources<NativeInputCallback> _inputCallbacks = new(32, 0);
    private NativeWindowInputLease? _doubleClick;

    public Task<NativeWindowInputLease> StartInputPollingAsync(TimeSpan interval, Func<TimeSpan> poll,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poll); WindowInputProtocol.Interval(interval.Ticks);
        return CreateInputAsync(new(poll, null), interval, cancellationToken);
    }

    public async Task<NativeWindowInputLease> ArmPostNavigationDoubleClickAsync(Action action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = Interlocked.Exchange(ref _doubleClick, null);
        if (previous is not null) await previous.DisposeAsync().ConfigureAwait(false);
        var lease = await CreateInputAsync(new(null, action), default, cancellationToken).ConfigureAwait(false);
        previous = Interlocked.Exchange(ref _doubleClick, lease);
        if (previous is not null) await previous.DisposeAsync().ConfigureAwait(false);
        return lease;
    }

    private async Task<NativeWindowInputLease> CreateInputAsync(NativeInputCallback callback, TimeSpan interval, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0, this);
        long id = _inputCallbacks.Register(callback, 0);
        var lease = new NativeWindowInputLease(this, id, callback); callback.Lease = lease;
        if (!_inputCallbacks.Claim(id)) { await lease.DisposeAsync().ConfigureAwait(false); throw new ObjectDisposedException(nameof(NativeWindowSession)); }
        try
        {
            // Once admitted, finish this bounded UI transaction before honoring cancellation,
            // so rollback always knows whether the native resource was actually created.
            await _context.RunAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0, this);
                using var writer = WindowControlProtocol.Write((int)WindowCommand.InputLease);
                writer.Int32((int)callback.Operation); writer.Int64(id);
                if (callback.Operation == WindowInputOperation.Poll) writer.Int64(interval.Ticks);
                using var result = WindowControlProtocol.Read(_ui.Exchange(WindowControlProtocol.Prefix(
                    (byte)WindowUiCommand.InputLease, writer.Finish())), out int kind);
                if (kind != (int)WindowCommand.InputLease) throw new InvalidDataException("Invalid native input response.");
                callback.NativeHandle = result.Int64(); result.End();
                if (callback.NativeHandle <= 0) throw new InvalidDataException("Invalid native input handle.");
                return Task.CompletedTask;
            }, CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); return lease;
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private byte[] ReceiveInput(ReadOnlyMemory<byte> message)
    {
        using var reader = WindowControlProtocol.Read(message, out int kind, callback: true);
        if (kind != (int)WindowControlCallback.InputLease) throw new InvalidDataException("Invalid input callback envelope.");
        var operation = (WindowInputOperation)reader.Int32(); long id = reader.Int64(); reader.End();
        using var borrow = _inputCallbacks.Acquire(id);
        var callback = borrow.Resource;
        try
        {
            if (operation != callback.Operation) throw new InvalidDataException("Input callback operation mismatch.");
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.InputLease, callback: true);
            if (operation == WindowInputOperation.Poll) writer.Int64(WindowInputProtocol.Interval(callback.Poll!().Ticks).Ticks);
            else { callback.Click!(); callback.Lease!.Dispose(); }
            return writer.Finish();
        }
        catch { callback.Lease!.Dispose(); throw; }
    }

    internal async Task ReleaseInputAsync(long id, NativeInputCallback callback)
    {
        try
        {
            if (Volatile.Read(ref _uiRetired) == 0 && callback.NativeHandle > 0)
            {
                if (Volatile.Read(ref _nativeRetiring) != 0) await _uiStopped.Task.ConfigureAwait(false);
                else
                {
                    using var writer = WindowControlProtocol.Write((int)WindowCommand.ReleaseInputLease);
                    writer.Int64(callback.NativeHandle);
                    try { await _operations.InvokeAsync(writer.Finish()).ConfigureAwait(false); }
                    catch (Exception) when (Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0)
                    { if (Volatile.Read(ref _uiRetired) == 0) await _uiStopped.Task.ConfigureAwait(false); }
                }
            }
        }
        finally
        {
            await _inputCallbacks.ReleaseAsync(id).ConfigureAwait(false);
            Interlocked.CompareExchange(ref _doubleClick, null, callback.Lease);
        }
    }

    private async Task CancelDoubleClickAsync(CancellationToken token)
    {
        await PlatformEffectAsync(WindowPlatformCommand.CancelDoubleClick, null, token).ConfigureAwait(false);
        var previous = Interlocked.Exchange(ref _doubleClick, null);
        if (previous is not null) await previous.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class NativeInputCallback(Func<TimeSpan>? poll, Action? click) : IDisposable
{
    internal Func<TimeSpan>? Poll = poll;
    internal Action? Click = click;
    internal WindowInputOperation Operation { get; } = poll is null ? WindowInputOperation.DoubleClick : WindowInputOperation.Poll;
    internal long NativeHandle;
    internal NativeWindowInputLease? Lease;
    public void Dispose() { Poll = null; Click = null; }
}

/// <summary>Disposal requests retirement; async disposal also waits for all admitted callbacks.</summary>
public sealed class NativeWindowInputLease : IDisposable, IAsyncDisposable
{
    private readonly NativeWindowSession _owner;
    private readonly NativeInputCallback _callback;
    private readonly long _id;
    private TaskCompletionSource? _release;
    internal NativeWindowInputLease(NativeWindowSession owner, long id, NativeInputCallback callback)
    { _owner = owner; _id = id; _callback = callback; }
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
        try { await _owner.ReleaseInputAsync(_id, _callback).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
