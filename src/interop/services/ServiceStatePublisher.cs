namespace Nexa.Native.Services;

// A bounded set of latest snapshots. Service locks only capture immutable local values;
// encoding, transport backpressure and receiver callbacks run on this worker.
internal sealed class ServiceStatePublisher : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (long Version, Func<byte[]> Encode)> _pending = [];
    private readonly Dictionary<int, long> _sourceRevisions = [];
    private readonly List<(long Version, TaskCompletionSource Source)> _barriers = [];
    private readonly SemaphoreSlim _ready = new(0, 1);
    private readonly ServiceEndpoint _endpoint;
    private readonly Task _pump;
    private long _posted, _applied;
    private bool _closed;
    private Exception? _failure;

    internal ServiceStatePublisher(ServiceEndpoint endpoint) { _endpoint = endpoint; _pump = Task.Run(PumpAsync); }
    internal void Post(int slot, long revision, Func<byte[]> encode)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            ThrowFailure();
            if (slot is < 0 or >= 128) throw new InvalidDataException("Invalid service state slot.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
            if (_sourceRevisions.TryGetValue(slot, out long previous) && revision <= previous) return;
            _sourceRevisions[slot] = revision;
            _pending[slot] = (checked(++_posted), encode);
            Signal();
        }
    }
    internal Task FlushAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            ThrowFailure();
            if (_applied >= _posted) return Task.CompletedTask;
            TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_barriers.Count >= 256) throw new InvalidOperationException("Service state barrier budget exceeded.");
            _barriers.Add((_posted, source)); return source.Task.WaitAsync(token);
        }
    }
    private void Signal() { if (_ready.CurrentCount == 0) _ready.Release(); }
    private void ThrowFailure() { if (_failure is not null) throw new IOException("Service state publication failed.", _failure); }
    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                await _ready.WaitAsync().ConfigureAwait(false);
                (long Version, Func<byte[]> Encode)[] batch;
                lock (_gate)
                {
                    if (_closed && _pending.Count == 0) return;
                    batch = [.. _pending.Values]; _pending.Clear();
                }
                long applied = 0;
                foreach (var item in batch)
                {
                    byte[] bytes = item.Encode();
                    int delay = 1;
                    while (true)
                    {
                        try { _endpoint.Publish(bytes); break; }
                        catch (Nexa.Native.Ui.NativeUiCallBusyException)
                        { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
                    }
                    applied = Math.Max(applied, item.Version);
                }
                lock (_gate)
                {
                    _applied = Math.Max(_applied, applied);
                    for (int index = _barriers.Count - 1; index >= 0; index--)
                    {
                        if (_barriers[index].Version > _applied) continue;
                        _barriers[index].Source.TrySetResult(); _barriers.RemoveAt(index);
                    }
                    if (_closed && _pending.Count == 0) return;
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _failure = error; _pending.Clear();
                foreach (var barrier in _barriers)
                { barrier.Source.TrySetException(error); _ = barrier.Source.Task.Exception; }
                _barriers.Clear();
            }
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_gate) { _closed = true; Signal(); }
        try { await _pump.ConfigureAwait(false); }
        finally { _ready.Dispose(); }
    }
}
