using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Memory;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private sealed class PrelaunchMemoryPort : IElevatedMemoryCleanupPort
    {
        public bool IsSupported => true;
        internal int Requests, OrdinaryCalls;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<MemoryCleanupResult> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SystemMemorySample Sample() => new(1000, 100);
        public IReadOnlyList<MemoryProcess> CaptureProcesses() { OrdinaryCalls++; return []; }
        public bool TryTrim(MemoryProcess process, out long bytes) { bytes = 0; throw new InvalidOperationException(); }
        public async Task<MemoryCleanupResult> CleanElevatedAsync(IReadOnlySet<int> protectedIds, CancellationToken token)
        {
            AssertTrue(protectedIds.Contains(123)); Interlocked.Increment(ref Requests); Entered.TrySetResult();
            return await Release.Task.WaitAsync(token);
        }
    }
    private static async ValueTask PrelaunchMemoryCleanupSharesOverlapAndScopesCancellation()
    {
        var port = new PrelaunchMemoryPort();
        await using var service = new MemoryOptimizationService(port, () => new(false, 80, false), () => new HashSet<int> { 123 }, MemoryStore());
        var first = service.CleanBeforeLaunchAsync().AsTask();
        await port.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancelled = new CancellationTokenSource();
        var second = service.CleanBeforeLaunchAsync(cancelled.Token).AsTask();
        var third = service.CleanBeforeLaunchAsync().AsTask();
        cancelled.Cancel(); await TrustRejectsAsync<OperationCanceledException>(() => second);
        AssertFalse(first.IsCompleted); AssertFalse(third.IsCompleted);
        port.Release.SetResult(new(0, 1, 0, 0, 0));
        AssertTrue((await first).IsSuccess); AssertTrue((await third).IsSuccess);
        AssertEqual(1, port.Requests); AssertEqual(0, port.OrdinaryCalls);
        AssertTrue((await service.CleanAsync()).IsSuccess); AssertEqual(1, port.Requests); AssertEqual(1, port.OrdinaryCalls);
    }
    private static async ValueTask PrelaunchMemoryCleanupIsOptInAndUacRejectionAllowsGame()
    {
        string root = CreateTempDirectory();
        try
        {
            using var host = FoundationComposer.Compose(new InMemorySettingsPort(), Nexa.Services.Settings.LauncherDefaults.CreateSchema(),
                new Nexa.Services.Accounts.LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
            var port = new DeepMemoryPortFixture { Reject = true };
            await using var runtime = new MemoryOptimizationRuntime(host, port);
            AssertTrue((await runtime.BeforeLaunchAsync(CancellationToken.None)).IsSuccess); AssertEqual(0, port.Requests);
            AssertTrue(host.Settings.SetRawValue(MemoryOptimizationContract.BeforeLaunchSetting, "true").IsSuccess);
            AssertTrue((await runtime.BeforeLaunchAsync(CancellationToken.None)).IsSuccess); AssertEqual(1, port.Requests);
            AssertEqual("false", host.Settings.GetRawValue(MemoryOptimizationContract.DeepSetting).Value);
            AssertEqual("elevation-cancelled", host.StateStore.Read<MemoryOptimizationSnapshot>(host.StateStore.Resolve(MemoryOptimizationContract.Snapshot)).Value!.Status);
        }
        finally { Directory.Delete(root, true); }
    }
    private static async ValueTask PrelaunchMemoryCancellationPreventsJvmCreation()
    {
        var port = new ConcurrentProcessPort(); MinecraftProcessService? processes = null;
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<XsrResult> BeforeStart(CancellationToken token) { entered.TrySetResult(); await release.Task.WaitAsync(token); return XsrResult.Success(); }
        var (coordinator, host, _, root) = ComposeAcquisitionCoordinator(new RecordingNeverInstaller(), processPort: port,
            javaLocator: ComposeWorkingJavaLocator(), processServiceCreated: value => processes = value, beforeProcessStart: BeforeStart);
        Task<XsrResult>? pending = null;
        try
        {
            pending = coordinator.StartAsync("1.20.1", 0).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(0, port.Children.Count);
            AssertTrue(coordinator.CancelActiveLaunch());
            AssertFalse((await pending).IsSuccess);
            AssertEqual(0, port.Children.Count);
        }
        finally { coordinator.Dispose(); await DisposeLaunchProgressFixtureAsync(root, host, processes, pendingLaunch: pending); }
    }
}
