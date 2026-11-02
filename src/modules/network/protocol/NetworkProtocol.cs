using Nexa.Native.Services.Wire;

namespace Nexa.Native.Network.Services;

internal static class NetworkProtocol
{
    internal static string Identity => "nexa-network-service/1/" + ServiceWireSchema.Fingerprint;
    internal const int Initialize = 0, CreateCertificate = 10, ClaimCertificate = 11, ReleaseCertificate = 12;
    internal const int Trace = 20, Probe = 21;
    internal static ServiceWireWriter Request(int operation)
    { var writer = new ServiceWireWriter(); writer.Int32(1); writer.String(Identity); writer.Int32(operation); return writer; }
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation)
    {
        var reader = new ServiceWireReader(bytes);
        if (reader.Int32() != 1 || reader.String() != Identity) { reader.Dispose(); throw new InvalidDataException("Incompatible native network protocol."); }
        operation = reader.Int32(); return reader;
    }
}
