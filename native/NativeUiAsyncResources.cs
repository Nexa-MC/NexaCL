namespace Nexa.Native.Ui;

/// <summary>Bounded native resources whose physical retirement requires asynchronous cleanup.</summary>
internal sealed class NativeUiAsyncResources<T>(int maximumCount, Action<Exception>? failure = null)
    : IAsyncDisposable where T : class, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Entry> _entries = [];
    private long _next;
    private bool _closed;
    private Exception? _disposalFailure;
    private TaskCompletionSource? _shutdown;

    internal long Register(T resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_entries.Count >= maximumCount) throw new InvalidOperationException("Native async resource budget exceeded.");
            long id = checked(++_next); _entries.Add(id, new(resource)); return id;
        }
    }

    internal bool Claim(long id)
    {
        lock (_gate)
        {
            if (_closed || !_entries.TryGetValue(id, out var entry) || entry.Retired) return false;
            entry.Claimed = true; return true;
        }
    }

    internal Borrow Acquire(long id) => TryAcquire(id)
        ?? throw new InvalidOperationException("Native async resource handle is not available.");

    internal Borrow? TryAcquire(long id)
    {
        lock (_gate)
        {
            if (_closed || !_entries.TryGetValue(id, out var entry) || entry.Retired || !entry.Claimed) return null;
            var borrow = new Borrow(this, id, entry.Resource); entry.Borrows++; return borrow;
        }
    }

    internal Task ReleaseAsync(long id) => Retire(id, provisionalOnly: false);
    internal Task RejectProvisionalAsync(long id) => Retire(id, provisionalOnly: true);

    private Task Retire(long id, bool provisionalOnly)
    {
        Entry? dispose;
        Task drained;
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry) || provisionalOnly && entry.Claimed) return Task.CompletedTask;
            entry.Retired = true; drained = entry.Drained.Task; dispose = BeginDisposal(entry);
        }
        if (dispose is not null) _ = FinishDisposalAsync(id, dispose);
        return drained;
    }

    private void Return(long id)
    {
        Entry? dispose;
        lock (_gate)
        {
            var entry = _entries[id]; entry.Borrows--; dispose = BeginDisposal(entry);
        }
        if (dispose is not null) _ = FinishDisposalAsync(id, dispose);
    }

    private static Entry? BeginDisposal(Entry entry)
    {
        if (!entry.Retired || entry.Borrows != 0 || entry.Disposing) return null;
        entry.Disposing = true; return entry;
    }

    private async Task FinishDisposalAsync(long id, Entry entry)
    {
        Exception? failed = null;
        try { await entry.Resource.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error)
        {
            failed = error;
            try { failure?.Invoke(error); } catch { }
        }
        finally
        {
            lock (_gate)
            {
                _entries.Remove(id);
                if (failed is null) entry.Drained.TrySetResult();
                else { _disposalFailure ??= failed; entry.Drained.TrySetException(failed); _ = entry.Drained.Task.Exception; }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        List<(long Id, Entry Entry)> dispose = [];
        Task[] pending;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_shutdown is not null) return new(_shutdown.Task);
            _closed = true; completion = _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var (id, entry) in _entries)
            {
                entry.Retired = true;
                if (BeginDisposal(entry) is { } ready) dispose.Add((id, ready));
            }
            pending = _entries.Values.Select(entry => entry.Drained.Task).ToArray();
        }
        foreach (var (id, entry) in dispose) _ = FinishDisposalAsync(id, entry);
        _ = FinishShutdownAsync(pending, completion); return new(completion.Task);
    }

    private async Task FinishShutdownAsync(Task[] pending, TaskCompletionSource completion)
    {
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
            Exception? failed;
            lock (_gate) failed = _disposalFailure;
            if (failed is null) completion.TrySetResult(); else completion.TrySetException(failed);
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private sealed class Entry(T resource)
    {
        internal readonly T Resource = resource;
        internal readonly TaskCompletionSource Drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Borrows;
        internal bool Claimed, Retired, Disposing;
    }

    internal sealed class Borrow(NativeUiAsyncResources<T> owner, long id, T resource) : IDisposable
    {
        private NativeUiAsyncResources<T>? _owner = owner;
        internal T Resource
        {
            get { ObjectDisposedException.ThrowIf(Volatile.Read(ref _owner) is null, this); return resource; }
        }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Return(id);
    }
}
