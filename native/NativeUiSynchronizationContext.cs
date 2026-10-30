using System.Threading.Channels;

namespace Nexa.Native.Ui;

/// <summary>Owns Host delegates locally while the native dispatcher carries only numeric IDs.</summary>
internal sealed class NativeUiSynchronizationContext : SynchronizationContext, IAsyncDisposable
{
    private const int MaximumPending = 4096;
    private readonly object _gate = new(), _execution = new();
    private readonly Dictionary<long, (SendOrPostCallback Callback, object? State)> _pending = [];
    private readonly Channel<long> _outbound = Channel.CreateUnbounded<long>(new() { SingleReader = true });
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _dispatcherChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<Exception> _failure;
    private readonly Task _delivery;
    private Action<long> _post;
    private long _next;
    private int _operations, _active;
    private bool _closing, _cleanup;

    internal NativeUiSynchronizationContext(Action<long> post, Action<Exception> failure)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(failure);
        _post = post; _failure = failure;
        _delivery = Task.Run(DeliverAsync);
    }

    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_drained.Task.IsCompleted, this);
            if (_pending.Count >= MaximumPending)
                throw new InvalidOperationException("Native UI continuation budget exceeded.");
            long id = checked(++_next);
            _pending.Add(id, (callback, state));
            if (!_outbound.Writer.TryWrite(id)) throw new InvalidOperationException("Native UI delivery is retired.");
        }
    }

    // Synchronous cross-thread Send would deadlock the duplex UI call boundary.
    public override void Send(SendOrPostCallback callback, object? state) =>
        throw new NotSupportedException("Use asynchronous native UI dispatch.");

    internal Task RunAsync(Func<Task> operation, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _operations++;
        }
        try { Post(state => { _ = RunCoreAsync(operation, result, token); }, null); }
        catch (Exception error) { result.TrySetException(error); OperationCompleted(); }
        return result.Task;
    }

    private async Task RunCoreAsync(Func<Task> operation, TaskCompletionSource result, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await operation().ConfigureAwait(true);
            result.TrySetResult();
        }
        catch (OperationCanceledException error) { result.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { result.TrySetException(error); }
        finally { OperationCompleted(); }
    }

    public override void OperationStarted()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_drained.Task.IsCompleted, this);
            _operations++;
        }
    }

    public override void OperationCompleted()
    {
        lock (_gate)
        {
            if (_operations == 0) throw new InvalidOperationException("Unbalanced native UI operation completion.");
            _operations--; FinishIfDrained();
        }
    }

    // Called by a reverse native callback on the UI thread, or by the ordered cleanup dispatcher.
    internal bool Execute(long id)
    {
        lock (_execution)
        {
            (SendOrPostCallback Callback, object? State) callback;
            lock (_gate)
            {
                if (!_pending.Remove(id, out callback)) return false;
                _active++;
            }
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback.Callback(callback.State); }
            catch (Exception error) { Report(error); }
            finally
            {
                SetSynchronizationContext(previous);
                lock (_gate) { _active--; FinishIfDrained(); }
            }
            return true;
        }
    }

    // The owner must first stop/drain the native dispatcher and its reverse-call registration.
    // Task continuations then finish Host cleanup without accessing retired native resources.
    internal void UseCleanupDispatcher(Action<long> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        lock (_gate)
        {
            if (_cleanup) throw new InvalidOperationException("Native UI cleanup handover already occurred.");
            _cleanup = true; _closing = true; _post = post;
            var previous = _dispatcherChanged;
            _dispatcherChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (long id in _pending.Keys) _outbound.Writer.TryWrite(id);
            previous.TrySetResult();
            FinishIfDrained();
        }
    }

    private async Task DeliverAsync()
    {
        await foreach (long id in _outbound.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            int delay = 1;
            while (true)
            {
                Action<long> post; Task changed;
                lock (_gate)
                {
                    if (!_pending.ContainsKey(id)) break;
                    post = _post; changed = _dispatcherChanged.Task;
                }
                try { post(id); break; }
                catch (NativeUiCallBusyException)
                {
                    await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32);
                }
                catch (Exception error)
                {
                    lock (_gate) { if (!_pending.ContainsKey(id)) break; }
                    Report(error);
                    await Task.WhenAny(changed, _drained.Task).ConfigureAwait(false);
                }
            }
        }
    }

    private void Report(Exception error) { try { _failure(error); } catch { } }

    private void FinishIfDrained()
    {
        if (!_closing || _operations != 0 || _active != 0 || _pending.Count != 0) return;
        _outbound.Writer.TryComplete(); _drained.TrySetResult();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { _closing = true; FinishIfDrained(); }
        return new(Task.WhenAll(_drained.Task, _delivery));
    }
}
