using Nexa.Platform;
using Nexa.Services.Memory;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static XsrStateStore MemoryStore()
    {
        XsrStateStoreBuilder builder = new();
        MemoryOptimizationContract.DeclareState(builder);
        return builder.Build();
    }

    private static async Task MemoryThresholdCooldownAndManualCleanup()
    {
        var port = new MemoryPortFixture();
        var clock = new MemoryClock();
        var options = new MemoryOptimizationOptions(false, 80);
        var store = MemoryStore();
        await using var service = new MemoryOptimizationService(port, () => options, () => new HashSet<int>(), store, clock);
        await service.ObserveAsync(); AssertEqual(0, port.Trimmed.Count);
        options = new(true, 80);
        // Time spent cleaning must not consume the cooldown after completion.
        port.OnTrim = _ => { clock.Now += TimeSpan.FromMinutes(1); return true; };
        port.Memory = new(1000, 200); // Equal to the threshold is not "exceeds".
        await service.ObserveAsync(); AssertEqual(0, port.Trimmed.Count);
        port.Memory = new(1000, 100);
        await service.ObserveAsync(); AssertEqual(1, port.Trimmed.Count);
        await service.ObserveAsync(); AssertEqual(1, port.Trimmed.Count);
        clock.Now += TimeSpan.FromSeconds(119);
        await service.ObserveAsync(); AssertEqual(1, port.Trimmed.Count);
        clock.Now += TimeSpan.FromSeconds(1);
        await service.ObserveAsync(); AssertEqual(2, port.Trimmed.Count);
        options = new(false, 80); port.Memory = new(1000, 900);
        AssertTrue((await service.CleanAsync()).IsSuccess); AssertEqual(3, port.Trimmed.Count);
        port.Memory = default;
        AssertFalse((await service.CleanAsync()).IsSuccess); AssertEqual(3, port.Trimmed.Count);
    }

    private static async Task MemoryCleanupProtectsGamesAndLateRoots()
    {
        var port = new MemoryPortFixture
        {
            Processes = [new(1, 0, "system", 1, true), new(10, 0, "javaw.exe", 2, false),
                new(11, 10, "game-helper.exe", 3, false), new(12, 11, "nested.exe", 4, false),
                new(20, 0, "Minecraft.Windows.exe", 5, false), new(30, 0, "other-game.exe", 6, false),
                new(31, 30, "other-child.exe", 7, false), new(40, 0, "app.exe", 8, false),
                new(50, 0, "late-game.exe", 9, false), new(51, 50, "late-child.exe", 10, false),
                new(60, 0, "denied.exe", 11, false)]
        };
        HashSet<int> games = [30];
        port.OnTrim = process => { if (process.Id == 40) games.Add(50); return process.Id != 60; };
        var store = MemoryStore();
        await using var service = new MemoryOptimizationService(port, () => new(false, 80), () => games, store);
        AssertTrue((await service.CleanAsync()).IsSuccess);
        AssertTrue(port.Trimmed.SequenceEqual([40, 60]));
        var result = store.Read<MemoryOptimizationSnapshot>(store.Resolve(MemoryOptimizationContract.Snapshot)).Value!.LastResult!;
        AssertEqual(1, result.Cleaned); AssertEqual(9, result.Skipped); AssertEqual(1, result.Failed);
        AssertEqual(4096L, result.WorkingSetReductionBytes);
    }

    private static async Task MemoryCleanupIsExclusiveAndCancelsOnShutdown()
    {
        var port = new MemoryPortFixture { Processes = Enumerable.Range(10, 50).Select(id => new MemoryProcess(id, 0, "app.exe", id, false)).ToArray() };
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        port.OnTrim = _ => { entered.TrySetResult(); released.Task.GetAwaiter().GetResult(); return true; };
        var store = MemoryStore();
        var service = new MemoryOptimizationService(port, () => new(false, 80), () => new HashSet<int>(), store);
        var running = service.CleanAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertFalse((await service.CleanAsync()).IsSuccess);
        var disposal = service.DisposeAsync().AsTask();
        AssertFalse(disposal.IsCompleted);
        released.TrySetResult();
        try { await running; throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { }
        await disposal;
        AssertEqual(1, port.Trimmed.Count);
        AssertFalse(store.Read<MemoryOptimizationSnapshot>(store.Resolve(MemoryOptimizationContract.Snapshot)).Value!.Busy);
        var unsupported = new MemoryPortFixture { IsSupported = false };
        await using var unavailable = new MemoryOptimizationService(unsupported, () => new(true, 80), () => new HashSet<int>(), store);
        await unavailable.ObserveAsync(); AssertFalse((await unavailable.CleanAsync()).IsSuccess);
        AssertEqual(0, unsupported.Trimmed.Count);
    }

    private static async Task MemoryDeepCleanupKeepsProtectionAndReportsUacRejection()
    {
        var port = new DeepMemoryPortFixture();
        var store = MemoryStore();
        await using var service = new MemoryOptimizationService(port, () => new(true, 80, true), () => new HashSet<int> { 123, 456 }, store);
        AssertTrue((await service.CleanAsync()).IsSuccess);
        AssertTrue(port.Protected!.SetEquals([123, 456]));
        AssertEqual(0, port.OrdinaryCalls);
        port.Reject = true;
        AssertFalse((await service.CleanAsync()).IsSuccess);
        var snapshot = store.Read<MemoryOptimizationSnapshot>(store.Resolve(MemoryOptimizationContract.Snapshot)).Value!;
        AssertEqual("elevation-cancelled", snapshot.Status);
        AssertFalse(snapshot.Busy);
        AssertEqual(0, port.OrdinaryCalls);
        await service.ObserveAsync(); // Rejection still establishes the automatic cooldown.
        AssertEqual(2, port.Requests);
    }

    private sealed class DeepMemoryPortFixture : IElevatedMemoryCleanupPort
    {
        public bool IsSupported => true;
        public bool Reject;
        public int Requests, OrdinaryCalls;
        public HashSet<int>? Protected;
        public SystemMemorySample Sample() => new(1000, 100);
        public IReadOnlyList<MemoryProcess> CaptureProcesses() { OrdinaryCalls++; return []; }
        public bool TryTrim(MemoryProcess process, out long workingSetReductionBytes) { OrdinaryCalls++; workingSetReductionBytes = 0; return false; }
        public Task<MemoryCleanupResult> CleanElevatedAsync(IReadOnlySet<int> protectedIds, CancellationToken token)
        {
            Requests++; Protected = protectedIds.ToHashSet();
            if (Reject) throw new System.ComponentModel.Win32Exception(1223);
            return Task.FromResult(new MemoryCleanupResult(2, 3, 4, 1024, -512));
        }
    }

    private sealed class MemoryClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryPortFixture : IProcessMemoryPort
    {
        public bool IsSupported { get; set; } = true;
        public SystemMemorySample Memory = new(1000, 100);
        public IReadOnlyList<MemoryProcess> Processes = [new(10, 0, "app.exe", 1, false)];
        public List<int> Trimmed = [];
        public Func<MemoryProcess, bool>? OnTrim;
        public SystemMemorySample Sample() => Memory;
        public IReadOnlyList<MemoryProcess> CaptureProcesses() => Processes;
        public bool TryTrim(MemoryProcess process, out long bytes)
        { Trimmed.Add(process.Id); bytes = 4096; return OnTrim?.Invoke(process) ?? true; }
    }
}
