using System.Text;

namespace Nexa.Native;

internal static unsafe class NativeEngineAbi
{
    internal static byte[] Input(byte* input, int length)
    {
        if (length < 0 || length > 8 * 1024 * 1024 || (length > 0 && input == null))
            throw new InvalidDataException("Invalid native engine input.");
        return new ReadOnlySpan<byte>(input, length).ToArray();
    }

    internal static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 4 * 1024 * 1024 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid native engine string length.");
        return new UTF8Encoding(false, true).GetString(reader.ReadBytes(length));
    }

    internal static void End(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Trailing native engine input.");
    }

    internal static int Output(string message, byte* output, int capacity, int status)
    {
        if (output == null || capacity < 1) return -1;
        Span<byte> buffer = new(output, capacity);
        buffer.Clear();
        if (Encoding.UTF8.GetByteCount(message) >= capacity) return -1;
        Encoding.UTF8.GetBytes(message, buffer);
        return status;
    }
}
