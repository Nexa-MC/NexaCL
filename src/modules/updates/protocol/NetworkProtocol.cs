using Nexa.Native.Services.Wire;

namespace Nexa.Native.Network.Services;

internal static class NetworkProtocol
{
    internal static ServiceWireWriter Request(int operation) => Nexa.Native.Updates.Services.UpdatesServiceProtocol.Request(operation);
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation) => Nexa.Native.Updates.Services.UpdatesServiceProtocol.Read(bytes, out operation);
}
