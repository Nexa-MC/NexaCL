#if NEXA_EXTERNAL_MODULES
using Nexa.Native.Interop;

namespace Nexa.Native.Minecraft;

internal static class NativeMinecraftClient
{
    private static readonly NativeResultClient Client = new("nexa-minecraft", "nexa_minecraft", MinecraftProtocol.InputLimit, MinecraftProtocol.OutputLimit);

    internal static T Call<T>(int operation, Action<BinaryWriter> write, Func<BinaryReader, T> read)
    {
        using var request = new MinecraftWireBuffer(MinecraftProtocol.InputLimit);
        using (var writer = new BinaryWriter(request, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(operation); write(writer);
        }
        if (request.Length > MinecraftProtocol.InputLimit) throw new InvalidDataException("Minecraft request exceeds the wire budget.");
        byte[] output = Client.Execute(request.GetBuffer().AsSpan(0, checked((int)request.Length)));
        using var reader = new BinaryReader(new MemoryStream(output));
        int status = reader.ReadInt32();
        if (status != 0)
        {
            string message = MinecraftProtocol.RequiredText(reader);
            string? parameter = MinecraftProtocol.Text(reader);
            MinecraftProtocol.End(reader);
            throw status switch
            {
                2 => new ArgumentNullException(parameter, message), 3 => new ArgumentException(message, parameter),
                4 => new FormatException(message), 5 => new OverflowException(message),
                6 => new NotSupportedException(message), 7 => new InvalidOperationException(message),
                _ => new InvalidDataException(message),
            };
        }
        T result = read(reader); MinecraftProtocol.End(reader); return result;
    }
}
#endif
