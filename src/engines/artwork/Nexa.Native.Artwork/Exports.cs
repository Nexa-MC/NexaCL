using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexa.Native.Artwork;

internal static unsafe class Exports
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly NativeBufferResults Results = new(MaximumBytes + 1);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [UnmanagedCallersOnly(EntryPoint = "nexa_artwork_execute_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static long Execute(byte* input, int length, int* outputLength)
    {
        if (outputLength == null) return 0;
        *outputLength = 0;
        try
        {
            if (input == null || length is < 1 or > 256) return 0;
            using Stream? source = ArtworkCatalog.Open(Utf8.GetString(new ReadOnlySpan<byte>(input, length)));
            if (source is null) return Results.Store(new byte[] { 0 }, outputLength);
            if (source.Length > MaximumBytes) return 0;
            byte[] output = new byte[checked((int)source.Length + 1)];
            output[0] = 1;
            source.ReadExactly(output.AsSpan(1));
            return Results.Store(output, outputLength);
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_artwork_copy_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Copy(long id, byte* output, int capacity) => Results.Copy(id, output, capacity);
    [UnmanagedCallersOnly(EntryPoint = "nexa_artwork_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(long id) => Results.Release(id);
}
