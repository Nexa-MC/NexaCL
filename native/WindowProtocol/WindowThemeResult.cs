using System.Buffers.Binary;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window.Wire;

internal sealed record WindowPreparedTheme(long Handle, bool NativePrepared, long DecodedBitmapBytes,
    AvaloniaUiThemeStartupSnapshot? Startup);

internal static class WindowThemeResult
{
    private const int Magic = 0x3152544e;
    internal static byte[] Encode(WindowPreparedTheme value)
    {
        if (value.Handle <= 0 || value.DecodedBitmapBytes < 0) throw new InvalidDataException("Invalid prepared theme result.");
        using var writer = new UiWireWriter(new Dictionary<string, Nexa.Core.Media.PngImage>());
        writer.Int32(Magic); writer.Int32(1); writer.Int64(value.Handle);
        writer.String(UiWireSchema.Fingerprint); writer.Boolean(value.NativePrepared);
        writer.Int64(value.DecodedBitmapBytes); writer.AvaloniaUiThemeStartupSnapshot(value.Startup);
        return writer.Finish();
    }
    internal static long ProvisionalHandle(ReadOnlyMemory<byte> bytes) => bytes.Length >= 16
        && BinaryPrimitives.ReadInt32LittleEndian(bytes.Span) == Magic
        && BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[4..]) == 1
        ? Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[8..])) : 0;

    internal static WindowPreparedTheme Decode(byte[] bytes)
    {
        using var reader = new UiWireReader(bytes, new Dictionary<string, Nexa.Core.Media.PngImage>());
        if (reader.Int32() != Magic || reader.Int32() != 1) throw new InvalidDataException("Invalid prepared theme result.");
        long handle = reader.Int64();
        if (handle <= 0 || reader.String() != UiWireSchema.Fingerprint) throw new InvalidDataException("Incompatible prepared theme result.");
        bool prepared = reader.Boolean(); long decoded = reader.Int64();
        var startup = reader.AvaloniaUiThemeStartupSnapshot(); reader.End();
        if (decoded < 0) throw new InvalidDataException("Invalid prepared theme byte count.");
        return new(handle, prepared, decoded, startup);
    }
}
