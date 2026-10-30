using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Native.Crypto;

internal static unsafe class Exports
{
    private static readonly ConcurrentDictionary<nint, GpgVerification.Session> Sessions = new();
    private static long _next;

    [UnmanagedCallersOnly(EntryPoint = "nexa_crypto_create_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Create(byte* request, int length, nint* handle, byte* error, int capacity)
    {
        if (handle == null) return -1;
        *handle = 0;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(NativeEngineAbi.Input(request, length)));
            string key = NativeEngineAbi.ReadString(reader), fingerprint = NativeEngineAbi.ReadString(reader);
            int size = reader.ReadInt32();
            if (size < 0 || size > 1024 * 1024 || size != reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid GPG signature envelope length.");
            var session = new GpgVerification(key, fingerprint).Begin(reader.ReadBytes(size));
            nint id = checked((nint)Interlocked.Increment(ref _next));
            if (id <= 0 || !Sessions.TryAdd(id, session)) throw new InvalidOperationException("GPG session allocation failed.");
            *handle = id;
            return 0;
        }
        catch (Exception failure) { return NativeEngineAbi.Output(failure.Message, error, capacity, -1); }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_crypto_update_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Update(nint handle, byte* bytes, int length, byte* error, int capacity)
    {
        try
        {
            if (length < 0 || length > 128 * 1024 || (length > 0 && bytes == null)
                || !Sessions.TryGetValue(handle, out var session))
                throw new InvalidDataException("Invalid GPG verification session or chunk.");
            session.Update(new ReadOnlySpan<byte>(bytes, length));
            return 0;
        }
        catch (Exception failure) { return NativeEngineAbi.Output(failure.Message, error, capacity, -1); }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_crypto_finish_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Finish(nint handle, byte* error, int capacity)
    {
        try
        {
            if (!Sessions.TryGetValue(handle, out var session)) throw new InvalidDataException("Unknown GPG verification session.");
            session.Finish();
            return 0;
        }
        catch (Exception failure) { return NativeEngineAbi.Output(failure.Message, error, capacity, -1); }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_crypto_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(nint handle) => Sessions.TryRemove(handle, out _);
}
