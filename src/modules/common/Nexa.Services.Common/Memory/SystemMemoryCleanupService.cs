using System.Diagnostics;

namespace Nexa.Services.Memory;

public static class SystemMemoryCleanupService
{
    public static IReadOnlyList<SystemMemoryCleanupOperation> Operations { get; } = Array.AsReadOnly<SystemMemoryCleanupOperation>(
        [SystemMemoryCleanupOperation.WorkingSets, SystemMemoryCleanupOperation.FileCache, SystemMemoryCleanupOperation.VolumeCache, SystemMemoryCleanupOperation.ModifiedPages,
         SystemMemoryCleanupOperation.StandbyPages, SystemMemoryCleanupOperation.LowPriorityStandby, SystemMemoryCleanupOperation.RegistryCache, SystemMemoryCleanupOperation.CombinePages]);

    public static async Task<IReadOnlyList<SystemMemoryCleanupResult>> RunAsync(ISystemMemoryCleanupPort port,
        Func<bool> hasProtectedGames, CancellationToken token = default)
    {
        List<SystemMemoryCleanupResult> results = [];
        foreach (var operation in Operations)
        {
            token.ThrowIfCancellationRequested();
            if (!port.IsSupported) { results.Add(new(operation, SystemMemoryCleanupOutcome.Unsupported, unchecked((int)0xC00000BB), 0, 0)); continue; }
            if (hasProtectedGames()) { results.Add(new(operation, SystemMemoryCleanupOutcome.GameProtected, 0, 0, 0)); continue; }
            token.ThrowIfCancellationRequested();
            long started = Stopwatch.GetTimestamp(); var before = port.Sample();
            int status = port.Execute(operation);
            var after = port.Sample();
            var outcome = status >= 0 ? SystemMemoryCleanupOutcome.Succeeded : unchecked((uint)status) switch
            {
                0xC0000002 or 0xC0000003 or 0xC00000BB => SystemMemoryCleanupOutcome.Unsupported,
                0xC0000022 or 0xC0000061 => SystemMemoryCleanupOutcome.AccessDenied,
                _ => SystemMemoryCleanupOutcome.Failed
            };
            results.Add(new(operation, outcome, status, before.IsValid && after.IsValid ? after.AvailableBytes - before.AvailableBytes : 0,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            await Task.Yield();
        }
        return results.AsReadOnly();
    }
}
