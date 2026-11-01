using System.Buffers.Binary;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window.Wire;

internal enum WindowMediaOperation { Create = 0, Configure = 1, WarmUp = 2, ReadState = 3, Release = 4, Validate = 5 }
internal enum WindowMediaEvent { State = 0, Frame = 1 }
internal sealed record WindowCreatedMedia(long Handle, AvaloniaUiMediaState State);

internal static class WindowMediaResult
{
    private const int Magic = 0x31524d4e;
    internal static byte[] Encode(WindowCreatedMedia value)
    {
        if (value.Handle <= 0) throw new InvalidDataException("Invalid native media handle.");
        using var writer = new UiWireWriter(new Dictionary<string, Nexa.Core.Media.PngImage>());
        writer.Int32(Magic); writer.Int32(1); writer.Int64(value.Handle);
        writer.String(UiWireSchema.Fingerprint); writer.Buffer(WindowValueChannel.EncodeMediaState(value.State));
        return writer.Finish();
    }
    internal static long ProvisionalHandle(ReadOnlyMemory<byte> bytes) => bytes.Length >= 16
        && BinaryPrimitives.ReadInt32LittleEndian(bytes.Span) == Magic
        && BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[4..]) == 1
        ? Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[8..])) : 0;
    internal static WindowCreatedMedia Decode(byte[] bytes)
    {
        using var reader = new UiWireReader(bytes, new Dictionary<string, Nexa.Core.Media.PngImage>());
        if (reader.Int32() != Magic || reader.Int32() != 1) throw new InvalidDataException("Invalid native media descriptor.");
        long handle = reader.Int64();
        if (handle <= 0 || reader.String() != UiWireSchema.Fingerprint) throw new InvalidDataException("Incompatible native media descriptor.");
        var state = WindowValueChannel.DecodeMediaState(reader.Buffer()); reader.End(); return new(handle, state);
    }
}
