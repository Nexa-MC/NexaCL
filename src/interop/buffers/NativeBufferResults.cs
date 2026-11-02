using System.Collections.Concurrent;

namespace Nexa.Native;

// Each native runtime owns its own store. No pointer or managed object leaves it.
internal sealed unsafe class NativeBufferResults(int maximum)
{
    private readonly ConcurrentDictionary<long, ReadOnlyMemory<byte>> _results = new();
    private long _next;
    internal long Store(ReadOnlyMemory<byte> bytes, int* length)
    {
        if (length == null || bytes.Length > maximum) return 0;
        long id = Interlocked.Increment(ref _next);
        if (id <= 0 || !_results.TryAdd(id, bytes)) return 0;
        *length = bytes.Length;
        return id;
    }
    internal int Copy(long id, byte* destination, int capacity)
    {
        try
        {
            if (!_results.TryGetValue(id, out var bytes) || destination == null || capacity != bytes.Length) return -1;
            bytes.Span.CopyTo(new Span<byte>(destination, capacity));
            return 0;
        }
        catch { return -1; }
    }
    internal void Release(long id) => _results.TryRemove(id, out _);
}
