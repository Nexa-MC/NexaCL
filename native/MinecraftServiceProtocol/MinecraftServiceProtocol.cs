using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft.Services;

internal static class MinecraftServiceProtocol
{
    internal static string Identity => "nexa-minecraft-service/1/error-envelope-1/" + ServiceWireSchema.Fingerprint;
    internal const int Initialize = 0, Versions = 10, Instances = 11, RefreshInstances = 12;
    internal const int Processes = 20, ProcessOutput = 21, CancelProcess = 22, CrashAnalyze = 23;
    internal const int StartProcess = 24;
    internal const int JavaInventory = 30, ProcessState = 100, FailureState = 101;
    internal const int TaskRetry = 40, TaskCancel = 41, TaskDismiss = 42, TaskClear = 43, TaskDismissFinished = 44, TaskState = 102;

    internal static ServiceWireWriter Request(int operation)
    {
        var writer = new ServiceWireWriter(); writer.Int32(1); writer.String(Identity); writer.Int32(operation); return writer;
    }
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation)
    {
        var reader = new ServiceWireReader(bytes);
        if (reader.Int32() != 1 || reader.String() != Identity)
        { reader.Dispose(); throw new InvalidDataException("Incompatible Minecraft service protocol."); }
        operation = reader.Int32(); return reader;
    }
}
