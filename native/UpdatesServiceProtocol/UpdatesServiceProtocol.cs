using Nexa.Native.Services.Wire;

namespace Nexa.Native.Updates.Services;

internal static class UpdatesServiceProtocol
{
    internal static string Identity => "nexa-updates-service/1/error-envelope-1/" + ServiceWireSchema.Fingerprint;
    internal const int Initialize = 0, ConfigureSidecars = 10, ActiveSidecars = 11, CheckSidecars = 12, InstallSidecar = 13;
    internal const int ConfigureDiscovery = 20, CheckUpdate = 21, StartRollouts = 22, RefreshRollouts = 23;
    internal const int ConfigureControl = 30, ReadControl = 31, InstallControl = 32, RollbackControl = 33;
    internal const int ConfigureCommunity = 50, StartCommunity = 51, AcknowledgeCommunity = 52;
    internal const int SidecarState = 100, RolloutState = 101, Telemetry = 102;
    internal static ServiceWireWriter Request(int operation)
    { var writer = new ServiceWireWriter(); writer.Int32(1); writer.Int32(operation); return writer; }
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation)
    { var reader = new ServiceWireReader(bytes); if (reader.Int32() != 1) { reader.Dispose(); throw new InvalidDataException("Unsupported Updates protocol."); } operation = reader.Int32(); return reader; }
}
