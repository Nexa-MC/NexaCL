using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native;

namespace Nexa.Native.Updates;

internal static unsafe class Exports
{
    private static long _next;
    private static readonly ConcurrentDictionary<long, DecoderSession> Sessions = new();
    private static readonly ConcurrentDictionary<long, byte[]> Results = new();

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_create_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static long Create(int codec, nint callback, long context)
    {
        try
        {
            if (codec is not (1 or 2) || callback == 0) return 0;
            long id = Interlocked.Increment(ref _next);
            Sessions[id] = new DecoderSession(codec, () => ((delegate* unmanaged[Cdecl]<long, void>)callback)(context));
            return id;
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_feed_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static int Feed(long id, byte* input, int length)
    {
        try
        {
            if (length < 0 || length > DecoderSession.PacketSize || (length != 0 && input == null)) return -1;
            Sessions[id].Feed(new ReadOnlySpan<byte>(input, length));
            return 0;
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_read_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static int Read(long id, byte* output, int capacity, byte* error, int errorCapacity)
    {
        try
        {
            if (output == null || capacity <= 0 || capacity > DecoderSession.PacketSize) return -1;
            int status = Sessions[id].Poll(new Span<byte>(output, capacity), out string? failure);
            if (failure is not null) NativeEngineAbi.Output(failure, error, errorCapacity, -4);
            return status;
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_close_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static void Close(long id)
    {
        try { if (Sessions.TryRemove(id, out DecoderSession? session)) session.Stop(); }
        catch { /* No exception may cross a runtime boundary. */ }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_delta_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static long Delta(byte* delta, int deltaLength, byte* source, int sourceLength, int* size)
    {
        try
        {
            if (size == null) return 0;
            *size = 0;
            if (deltaLength < 0 || sourceLength < 0 || (deltaLength != 0 && delta == null) || (sourceLength != 0 && source == null)) return 0;
            byte[] result = VcdiffDecoder.Decode(new ReadOnlySpan<byte>(delta, deltaLength), new ReadOnlySpan<byte>(source, sourceLength));
            long id = Interlocked.Increment(ref _next);
            Results[id] = result;
            *size = result.Length;
            return id;
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_delta_copy_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static int Copy(long id, byte* output, int length)
    {
        try
        {
            if (!Results.TryGetValue(id, out byte[]? result) || length != result.Length || (length != 0 && output == null)) return -1;
            result.CopyTo(new Span<byte>(output, length));
            return 0;
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_updates_delta_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static void Release(long id) { Results.TryRemove(id, out _); }
}
