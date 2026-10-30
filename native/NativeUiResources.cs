namespace Nexa.Native.Ui;

/// <summary>Session-local native resources; only monotonic numeric handles leave this runtime.</summary>
internal sealed class NativeUiResources<T>(int maximumCount, long maximumBytes, Action<Exception>? failure = null)
    : IAsyncDisposable where T : class, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Entry> _entries = [];
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _next, _bytes;
    private bool _closed;

    // Ownership transfers only on success. The producer disposes a rejected registration.
    internal long Register(T resource, long bytes)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_entries.Count >= maximumCount || bytes > maximumBytes - _bytes)
                throw new InvalidOperationException("Native UI resource budget exceeded.");
            long id = checked(++_next);
            _entries.Add(id, new(resource, bytes)); _bytes += bytes;
            return id;
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

    internal Borrow Acquire(long id)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!_entries.TryGetValue(id, out var entry) || entry.Retired || !entry.Claimed)
                throw new InvalidOperationException("Native UI resource handle is not available.");
            var borrow = new Borrow(this, id, entry.Resource);
            entry.Borrows++; return borrow;
        }
    }

    internal void Release(long id) => Retire(id, provisionalOnly: false);
    internal void RejectProvisional(long id) => Retire(id, provisionalOnly: true);

    private void Retire(long id, bool provisionalOnly)
    {
        Entry? dispose;
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry) || provisionalOnly && entry.Claimed) return;
            entry.Retired = true;
            dispose = BeginDisposal(entry);
        }
        if (dispose is not null) FinishDisposal(id, dispose);
    }

    private void Return(long id)
    {
        Entry? dispose;
        lock (_gate)
        {
            var entry = _entries[id];
            entry.Borrows--;
            dispose = BeginDisposal(entry);
        }
        if (dispose is not null) FinishDisposal(id, dispose);
    }

    private static Entry? BeginDisposal(Entry entry)
    {
        if (!entry.Retired || entry.Borrows != 0 || entry.Disposing) return null;
        entry.Disposing = true; return entry;
    }

    private void FinishDisposal(long id, Entry entry)
    {
        try { entry.Resource.Dispose(); }
        catch (Exception error) { try { failure?.Invoke(error); } catch { } }
        finally
        {
            lock (_gate)
            {
                _entries.Remove(id); _bytes -= entry.Bytes;
                if (_closed && _entries.Count == 0) _drained.TrySetResult();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        List<(long Id, Entry Entry)> dispose = [];
        lock (_gate)
        {
            _closed = true;
            foreach (var (id, entry) in _entries)
            {
                entry.Retired = true;
                if (BeginDisposal(entry) is { } ready) dispose.Add((id, ready));
            }
            if (_entries.Count == 0) _drained.TrySetResult();
        }
        foreach (var (id, entry) in dispose) FinishDisposal(id, entry);
        return new(_drained.Task);
    }

    private sealed class Entry(T resource, long bytes)
    {
        internal readonly T Resource = resource;
        internal readonly long Bytes = bytes;
        internal int Borrows;
        internal bool Claimed, Retired, Disposing;
    }

    internal sealed class Borrow(NativeUiResources<T> owner, long id, T resource) : IDisposable
    {
        private NativeUiResources<T>? _owner = owner;
        internal T Resource
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _owner) is null, this);
                return resource;
            }
        }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Return(id);
    }
}
