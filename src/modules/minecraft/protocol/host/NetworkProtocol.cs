using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft.Http;

// The transfer implementation is shared; its Minecraft peer uses Minecraft admission.
internal static class NetworkProtocol
{
    internal static ServiceWireWriter Request(int operation) => MinecraftServiceProtocol.Request(operation);
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation) => MinecraftServiceProtocol.Read(bytes, out operation);
}
