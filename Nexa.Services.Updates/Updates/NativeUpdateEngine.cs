#if NEXA_EXTERNAL_MODULES
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Nexa.Native.Interop;

namespace Nexa.Services.Updates;

internal static unsafe class NativeUpdateEngine
{
    private static readonly nint Library = NativeEngineClient.Load("nexa-updates");
    internal static readonly delegate* unmanaged[Cdecl]<int, nint, long, long> Create = (delegate* unmanaged[Cdecl]<int, nint, long, long>)Export("create");
    internal static readonly delegate* unmanaged[Cdecl]<long, byte*, int, int> Feed = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)Export("feed");
    internal static readonly delegate* unmanaged[Cdecl]<long, byte*, int, byte*, int, int> Read = (delegate* unmanaged[Cdecl]<long, byte*, int, byte*, int, int>)Export("read");
    internal static readonly delegate* unmanaged[Cdecl]<long, void> Close = (delegate* unmanaged[Cdecl]<long, void>)Export("close");
    private static readonly delegate* unmanaged[Cdecl]<byte*, int, byte*, int, int*, long> Delta = (delegate* unmanaged[Cdecl]<byte*, int, byte*, int, int*, long>)Export("delta");
    private static readonly delegate* unmanaged[Cdecl]<long, byte*, int, int> Copy = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)Export("delta_copy");
    private static readonly delegate* unmanaged[Cdecl]<long, void> Release = (delegate* unmanaged[Cdecl]<long, void>)Export("delta_release");
    private static nint Export(string name) => NativeLibrary.GetExport(Library, "nexa_updates_" + name + "_v1");

    internal static byte[] Decode(ReadOnlySpan<byte> delta, ReadOnlySpan<byte> source)
    {
        long id;
        int length;
        fixed (byte* d = delta, s = source) id = Delta(d, delta.Length, s, source.Length, &length);
        if (id == 0) throw new InvalidDataException("VCDIFF decoding failed.");
        try
        {
            byte[] result = new byte[length];
            fixed (byte* output = result)
                if (Copy(id, output, length) != 0) throw new InvalidDataException("VCDIFF result transfer failed.");
            return result;
        }
        finally { Release(id); }
    }
}

internal sealed class NativeBlockStream : Stream
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Wake callbacks and pending reads can outlive release; these timer-free managed primitives are reclaimed after the last user. No WaitHandle is allocated.")]
    private sealed class Signal
    {
        internal readonly SemaphoreSlim Ready = new(0, 1);
        internal readonly CancellationTokenSource Stop = new();
    }
    // Pending-read continuations can refer back to this stream. Do not root that
    // cycle from the callback registry; SafeHandle can retire abandoned sessions.
    private static readonly ConcurrentDictionary<long, WeakReference<Signal>> Signals = new();
    private static long _next;
    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly Signal _signal = new();
    private readonly SemaphoreSlim _reader = new(1, 1);
    private readonly SessionHandle _handle;
    private readonly byte[] _packet = new byte[65536];
    private ExceptionDispatchInfo? _inputFailure;

    internal unsafe NativeBlockStream(Stream input, int codec, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(input));
        _input = input;
        _leaveOpen = leaveOpen;
        long context = Interlocked.Increment(ref _next);
        Signals[context] = new WeakReference<Signal>(_signal);
        try
        {
            long id = NativeUpdateEngine.Create(codec, (nint)(delegate* unmanaged[Cdecl]<long, void>)&Wake, context);
            if (id == 0) throw new IOException("Cannot create update decoder.");
            _handle = new SessionHandle(id, context);
        }
        catch { Signals.TryRemove(context, out _); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Wake(long context)
    {
        try { if (Signals.TryGetValue(context, out var reference) && reference.TryGetTarget(out Signal? signal)) signal.Ready.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private sealed class SessionHandle : SafeHandle
    {
        internal readonly long Id;
        private readonly long _context;
        internal SessionHandle(long id, long context) : base(0, true)
        { Id = id; _context = context; SetHandle(1); }
        public override bool IsInvalid => handle == 0;
        internal unsafe void Abort() => NativeUpdateEngine.Close(Id);
        protected override unsafe bool ReleaseHandle()
        {
            if (Signals.TryRemove(_context, out var reference) && reference.TryGetTarget(out Signal? signal)) signal.Stop.Cancel();
            NativeUpdateEngine.Close(Id);
            return true;
        }
    }

    private unsafe int Poll(Memory<byte> output)
    {
        bool held = false;
        try
        {
            _handle.DangerousAddRef(ref held);
            Span<byte> error = stackalloc byte[2048];
            error.Clear();
            using var pinned = output.Pin();
            fixed (byte* e = error)
            {
                int status = NativeUpdateEngine.Read(_handle.Id, (byte*)pinned.Pointer, Math.Min(output.Length, 65536), e, error.Length);
                if (status == -4) throw new InvalidDataException(NativeEngineClient.Message(error));
                if (status == -1) throw new IOException("Update decoder session unavailable.");
                return status;
            }
        }
        finally { if (held) _handle.DangerousRelease(); }
    }

    private unsafe void Feed(int count)
    {
        bool held = false;
        try
        {
            _handle.DangerousAddRef(ref held);
            fixed (byte* p = _packet)
                if (NativeUpdateEngine.Feed(_handle.Id, p, count) != 0) throw new IOException("Update decoder input rejected.");
        }
        finally { if (held) _handle.DangerousRelease(); }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (buffer.IsEmpty) return 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _signal.Stop.Token);
        await _reader.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            _inputFailure?.Throw();
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                int status = Poll(buffer);
                if (status >= 0) return status;
                if (status == -2)
                {
                    int count;
                    try { count = await _input.ReadAsync(_packet, linked.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                    catch (Exception e)
                    {
                        _inputFailure = ExceptionDispatchInfo.Capture(e);
                        _handle.Abort();
                        throw;
                    }
                    // Do not discard successfully read bytes if cancellation races this call.
                    Feed(count);
                }
                else await _signal.Ready.WaitAsync(linked.Token).ConfigureAwait(false);
            }
        }
        finally { _reader.Release(); }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override bool CanRead => !_handle.IsClosed;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _handle.Dispose();
            if (!_leaveOpen) _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
#endif
