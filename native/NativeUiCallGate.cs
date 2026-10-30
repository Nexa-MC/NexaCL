using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Native.Ui;

// This file is compiled once into each owning runtime. It never exchanges CLR references.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeUiCallTable
{
    internal int Size;
    internal int Version;
    internal long Context;
    internal delegate* unmanaged[Cdecl]<long, byte*, int, int*, long> Execute;
    internal delegate* unmanaged[Cdecl]<long, long, byte*, int, int> Copy;
    internal delegate* unmanaged[Cdecl]<long, long, void> Release;
}

internal sealed class NativeUiCallBusyException : IOException
{
    public NativeUiCallBusyException() : base("Native UI call capacity is temporarily exhausted.") { }
    public NativeUiCallBusyException(string message) : base(message) { }
    public NativeUiCallBusyException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed unsafe class NativeUiCallClient
{
    private readonly NativeUiCallTable _table;
    internal NativeUiCallClient(NativeUiCallTable table)
    {
        if (table.Size != sizeof(NativeUiCallTable) || table.Version != 1 || table.Context <= 0
            || table.Execute == null || table.Copy == null || table.Release == null)
            throw new InvalidDataException("Unsupported native UI call table.");
        _table = table;
    }

    internal byte[] Exchange(ReadOnlyMemory<byte> request)
    {
        if (request.Length > NativeUiCallRegistration.MaximumBytes) throw new InvalidDataException("Native UI request exceeds its budget.");
        int length = 0;
        long result;
        fixed (byte* input = request.Span) result = _table.Execute(_table.Context, input, request.Length, &length);
        if (result == 0 && length == -2) throw new NativeUiCallBusyException();
        if (result <= 0) throw new InvalidOperationException("Native UI endpoint is retired or cannot admit this call.");
        try
        {
            if (length is < 1 or > NativeUiCallRegistration.MaximumBytes + 1)
                throw new InvalidDataException("Invalid native UI response length.");
            byte[] output = new byte[length];
            fixed (byte* buffer = output)
                if (_table.Copy(_table.Context, result, buffer, length) != 0)
                    throw new InvalidDataException("Native UI result is unavailable.");
            if (output[0] != 0) throw new InvalidOperationException("The native UI endpoint failed to execute the operation.");
            return output.AsSpan(1).ToArray();
        }
        finally { _table.Release(_table.Context, result); }
    }
}

internal sealed unsafe class NativeUiCallRegistration : IAsyncDisposable
{
    internal const int MaximumBytes = 72 * 1024 * 1024;
    private const int MaximumResults = 128;
    private const int MaximumActiveCalls = 16;
    private const long MaximumRetainedBytes = 128L * 1024 * 1024;
    private static readonly ConcurrentDictionary<long, NativeUiCallRegistration> Registrations = new();
    private static long _nextContext;
    private readonly object _gate = new();
    private readonly Func<ReadOnlyMemory<byte>, byte[]> _endpoint;
    private readonly Action<Exception>? _failure;
    private readonly SynchronizationContext? _callbackContext;
    private readonly Dictionary<long, byte[]> _results = [];
    private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _context;
    private long _nextResult, _retainedBytes, _activeRequestBytes;
    private int _active;
    private bool _closing;

    internal NativeUiCallRegistration(Func<ReadOnlyMemory<byte>, byte[]> endpoint, Action<Exception>? failure = null, SynchronizationContext? callbackContext = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoint = endpoint; _failure = failure; _callbackContext = callbackContext;
        _context = Interlocked.Increment(ref _nextContext);
        if (_context <= 0 || !Registrations.TryAdd(_context, this))
            throw new InvalidOperationException("Native UI context space is exhausted.");
    }

    internal NativeUiCallTable Table => new()
    {
        Size = sizeof(NativeUiCallTable),
        Version = 1,
        Context = _context,
        Execute = &Execute,
        Copy = &Copy,
        Release = &Release,
    };

    // Call this before disposing owner resources; do not await it inside an endpoint invocation.
    public ValueTask DisposeAsync()
    {
        lock (_gate) { _closing = true; CompleteRetirement(); }
        return new(_retired.Task);
    }

    private void CompleteRetirement()
    {
        if (!_closing || _active != 0 || _results.Count != 0) return;
        Registrations.TryRemove(_context, out _);
        _retired.TrySetResult();
    }

    private bool Enter(int requestBytes, out bool busy)
    {
        lock (_gate)
        {
            busy = false;
            if (_closing) return false;
            if (_active >= MaximumActiveCalls || _active + _results.Count >= MaximumResults
                || requestBytes > MaximumRetainedBytes - _activeRequestBytes)
            { busy = true; return false; }
            _active++; _activeRequestBytes += requestBytes;
            return true;
        }
    }
    private void Leave(int requestBytes)
    {
        lock (_gate) { _active--; _activeRequestBytes -= requestBytes; CompleteRetirement(); }
    }

    private byte[] Invoke(ReadOnlyMemory<byte> request)
    {
        if (_callbackContext is null) return _endpoint(request);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_callbackContext);
        try { return _endpoint(request); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private long Store(byte[] bytes, int* length)
    {
        lock (_gate)
        {
            if (_results.Count >= MaximumResults || bytes.Length > MaximumRetainedBytes - _retainedBytes) return 0;
            long id = ++_nextResult;
            if (id <= 0) return 0;
            _results.Add(id, bytes); _retainedBytes += bytes.Length;
            *length = bytes.Length;
            return id;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Execute(long context, byte* input, int length, int* outputLength)
    {
        if (outputLength == null) return 0;
        *outputLength = 0;
        if (length is < 0 or > MaximumBytes || input == null && length != 0) return 0;
        if (!Registrations.TryGetValue(context, out var owner)) return 0;
        if (!owner.Enter(length, out bool busy)) { if (busy) *outputLength = -2; return 0; }
        try
        {
            byte[] result;
            try
            {
                byte[] body = owner.Invoke(new ReadOnlySpan<byte>(input, length).ToArray());
                if (body is null || body.Length > MaximumBytes) throw new InvalidDataException("Native UI response exceeds its budget.");
                result = new byte[body.Length + 1];
                body.CopyTo(result, 1);
            }
            catch (Exception error)
            {
                // Diagnostics remain local. Even a logging failure must not unwind across native code.
                try { owner._failure?.Invoke(error); } catch { }
                result = [1];
            }
            return owner.Store(result, outputLength);
        }
        catch { return 0; }
        finally { owner.Leave(length); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Copy(long context, long result, byte* output, int capacity)
    {
        try
        {
            if (output == null || !Registrations.TryGetValue(context, out var owner)) return -1;
            // Hold this lock through the short copy so retirement cannot finish while it is in progress.
            lock (owner._gate)
            {
                if (!owner._results.TryGetValue(result, out byte[]? bytes) || capacity != bytes.Length) return -1;
                bytes.CopyTo(new Span<byte>(output, capacity));
                return 0;
            }
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(long context, long result)
    {
        try
        {
            if (!Registrations.TryGetValue(context, out var owner)) return;
            lock (owner._gate)
            {
                if (owner._results.Remove(result, out byte[]? bytes)) owner._retainedBytes -= bytes.Length;
                owner.CompleteRetirement();
            }
        }
        catch { }
    }
}
