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
    internal const int JavaInstall = 50, JavaStop = 51, JavaRecover = 52, JavaProgress = 103;
    internal const int JavaConfigure = 60, JavaManage = 61, JavaManualPreview = 62, JavaManualInstall = 63, JavaManualCancel = 64, JavaManualStatus = 65;
    internal const int JavaInspect = 66, JavaProperties = 67, JavaModules = 68;
    internal const int InstallConfigure = 70, InstallRun = 71, InstallStop = 72, InstallRecover = 73, InstallModpack = 74;
    internal const int InstallEditRead = 75;
    internal const int RecoveryRead = 80, RecoveryRestore = 81, RecoveryResume = 82;
    internal const int LaunchConfigure = 90, LaunchStart = 92, LaunchCancel = 93;
    internal const int LaunchJavaApprove = 94, LaunchJavaMajor = 95, LaunchJavaPath = 96, LaunchPreflightDecide = 97, LaunchSafe = 98;
    internal const int LaunchFinalize = 99, LaunchSafeRead = 104, LaunchDiagnostics = 105;
    internal const int LaunchState = 110;
    internal const int ObservationState = 120, SampleState = 121, ContextState = 122;
    internal const int Remediation = 130;
    internal const int CatalogConfigure = 140, CatalogRead = 141, CatalogPrefetch = 142, CatalogEligibility = 143, CatalogEditPlan = 144, CatalogDraft = 145;
    internal const int CatalogState = 146, DraftState = 147;
    internal const int CatalogDraftReplace = 148;

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
