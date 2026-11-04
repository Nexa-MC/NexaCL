using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Memory;

public readonly record struct SystemMemorySample(long TotalBytes, long AvailableBytes)
{
    public bool IsValid => TotalBytes > 0 && AvailableBytes >= 0 && AvailableBytes <= TotalBytes;
    public double UsedPercent => IsValid ? 100d * (TotalBytes - AvailableBytes) / TotalBytes : 0;
}

public sealed record MemoryProcess(int Id, int ParentId, string Name, long CreationTime, bool IsProtected);
public sealed record MemoryCleanupResult(int Cleaned, int Skipped, int Failed, long WorkingSetReductionBytes,
    long AvailableChangeBytes)
{
    public IReadOnlyList<SystemMemoryCleanupResult> SystemOperations { get; init; } = [];
    public long ElapsedMilliseconds { get; init; }
}
public enum SystemMemoryCleanupOperation { WorkingSets, FileCache, ModifiedPages, StandbyPages, LowPriorityStandby, RegistryCache, CombinePages, VolumeCache }
public enum SystemMemoryCleanupOutcome { Succeeded, GameProtected, Unsupported, AccessDenied, Failed }
public sealed record SystemMemoryCleanupResult(SystemMemoryCleanupOperation Operation, SystemMemoryCleanupOutcome Outcome,
    int NativeStatus, long AvailableChangeBytes, long ElapsedMilliseconds);
public interface ISystemMemoryCleanupPort
{
    bool IsSupported { get; }
    SystemMemorySample Sample();
    int Execute(SystemMemoryCleanupOperation operation);
}

/// <summary>Platform owns native handles and measurements; the service owns selection and scheduling.</summary>
public interface IProcessMemoryPort
{
    bool IsSupported { get; }
    SystemMemorySample Sample();
    IReadOnlyList<MemoryProcess> CaptureProcesses();
    bool TryTrim(MemoryProcess process, out long workingSetReductionBytes);
}

public interface IElevatedMemoryCleanupPort : IProcessMemoryPort
{
    Task<MemoryCleanupResult> CleanElevatedAsync(IReadOnlySet<int> protectedIds, CancellationToken token);
}

public sealed record MemoryOptimizationOptions(bool Automatic, int ThresholdPercent, bool Deep = false);
public sealed record MemoryOptimizationSnapshot(bool Supported, SystemMemorySample Memory, bool Busy,
    string Status, MemoryCleanupResult? LastResult = null);
public sealed record MemoryCleanupCommand;

public static class MemoryOptimizationContract
{
    public const string AutomaticSetting = "SystemMemoryAutoClean";
    public const string ThresholdSetting = "SystemMemoryCleanThreshold";
    public const string DeepSetting = "SystemMemoryDeepClean";
    public const string BeforeLaunchSetting = "SystemMemoryDeepCleanBeforeLaunch";
    public static readonly XsrSemanticId Snapshot = XsrSemanticId.Parse("memory.optimization.snapshot");
    public static readonly XsrSemanticId Clean = XsrSemanticId.Parse("memory.optimization.clean");
    public static void DeclareState(XsrStateStoreBuilder builder) =>
        builder.Cell<MemoryOptimizationSnapshot>(Snapshot, "Nexa.Services.Memory");
}
