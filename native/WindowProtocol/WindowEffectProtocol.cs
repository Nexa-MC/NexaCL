using System.Buffers.Binary;

namespace Nexa.Native.Window.Wire;

internal enum WindowEffectOperation { CropScreenshot = 0, AcquireWorldLock = 1 }

internal static class WindowEffectResult
{
    private const int Magic = 0x3152454e;
    internal static byte[] Encode(long handle, int kind = 0)
    {
        if (handle <= 0) throw new InvalidDataException("Invalid native effect handle.");
        using var writer = new UiWireWriter(new Dictionary<string, Nexa.Core.Media.PngImage>());
        writer.Int32(Magic + kind); writer.Int32(1); writer.Int64(handle); writer.String(UiWireSchema.Fingerprint);
        return writer.Finish();
    }
    internal static long ProvisionalHandle(ReadOnlyMemory<byte> bytes, int kind = 0) => bytes.Length >= 16
        && BinaryPrimitives.ReadInt32LittleEndian(bytes.Span) == Magic + kind
        && BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[4..]) == 1
        ? Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[8..])) : 0;
    internal static long Decode(byte[] bytes, int kind = 0)
    {
        using var reader = new UiWireReader(bytes, new Dictionary<string, Nexa.Core.Media.PngImage>());
        if (reader.Int32() != Magic + kind || reader.Int32() != 1) throw new InvalidDataException("Invalid native effect descriptor.");
        long handle = reader.Int64();
        if (handle <= 0 || reader.String() != UiWireSchema.Fingerprint) throw new InvalidDataException("Incompatible native effect descriptor.");
        reader.End(); return handle;
    }
}
