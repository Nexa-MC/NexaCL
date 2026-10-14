using System.Buffers.Binary;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal static class AvaloniaUiThemeFontData
{
    internal static void Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 12 or > 16 * 1048576 || !(bytes[..4].SequenceEqual(new byte[] { 0, 1, 0, 0 }) || bytes[..4].SequenceEqual("OTTO"u8)))
            throw new InvalidDataException("Invalid TrueType/OpenType resource.");
        int tables = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..6]);
        if (tables is < 1 or > 256 || bytes.Length < 12 + tables * 16) throw new InvalidDataException("Font table directory exceeds its budget.");
        var names = new HashSet<uint>();
        for (int index = 0; index < tables; index++)
        {
            ReadOnlySpan<byte> record = bytes.Slice(12 + index * 16, 16);
            uint name = BinaryPrimitives.ReadUInt32BigEndian(record[..4]);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..12]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(record[12..16]);
            if (!names.Add(name) || offset > bytes.Length || length > bytes.Length - offset)
                throw new InvalidDataException("Font table identity or range is invalid.");
        }
    }
}
