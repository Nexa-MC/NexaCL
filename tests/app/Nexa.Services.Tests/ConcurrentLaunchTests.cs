using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask ConcurrentLaunchPromptsAndCancellationAreScoped()
    {
        MinecraftProcessService? processes = null;
        var (coordinator, host, _, root) = ComposeAcquisitionCoordinator(new RecordingNeverInstaller(), processServiceCreated: value => processes = value);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<XsrResult>? first = null, second = null;
        try
        {
            await CreateSecondConcurrentInstance(root);
            first = coordinator.StartAsync("1.20.1", 0, lifetime.Token).AsTask();
            second = coordinator.StartAsync("other", 0, lifetime.Token).AsTask();
            MinecraftLaunchProgressSnapshot Snapshot() => (MinecraftLaunchProgressSnapshot)host.StateStore.ReadAppliedValue(host.StateStore.Resolve(MinecraftLaunchProgressState.SnapshotKey))!;
            AssertTrue(SpinWait.SpinUntil(() => Snapshot().Attempts.Count(item => item.AcquirePending) == 2, TimeSpan.FromSeconds(5)));
            var attempts = Snapshot().Attempts;
            var a = attempts.Single(item => item.InstanceId == "1.20.1"); var b = attempts.Single(item => item.InstanceId == "other");
            AssertFalse(a.Id == b.Id); AssertFalse(first.IsCompleted); AssertFalse(second.IsCompleted);
            AssertFalse((await coordinator.StartAsync("other", 0, lifetime.Token)).IsSuccess);
            AssertTrue(coordinator.CancelActiveLaunch(a.Id));
            AssertFalse((await first).IsSuccess);
            AssertFalse(second.IsCompleted);
            AssertTrue(Snapshot().Attempts.Single(item => item.Id == b.Id).AcquirePending);
            AssertFalse(coordinator.CancelActiveLaunch(a.Id));
            AssertTrue(coordinator.DecideJavaAcquisition(false, b.Id));
            AssertFalse((await second).IsSuccess);
            AssertFalse(Snapshot().Attempts.Any(item => item.Active));
        }
        finally
        {
            lifetime.Cancel(); coordinator.Dispose();
            if (second is not null) await second;
            await DisposeLaunchProgressFixtureAsync(root, host, processes, lifetime, first);
        }
    }

    private static async Task CreateSecondConcurrentInstance(string root)
    {
        string path = CreateVersionDirectory(root, "other", new JsonObject
        {
            ["id"] = "other",
            ["type"] = "release",
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T00:00:00Z",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 17, ["component"] = "java-runtime-gamma" }
        });
        await new MinecraftInstanceMetadataStore().SaveAsync(path, new MinecraftInstanceMetadata { InstanceIsolation = true });
        await File.WriteAllBytesAsync(Path.Combine(path, "other.jar"), [0xCA, 0xFE]);
    }

    private sealed class ConcurrentWindowProbe : IMinecraftWindowProbe
    {
        private int _entered;
        internal readonly TaskCompletionSource Both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<MinecraftWindowProbeResult> ProbeAsync(int processId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _entered) == 2) Both.TrySetResult();
            await Both.Task.WaitAsync(cancellationToken);
            return MinecraftWindowProbeResult.Visible;
        }
    }
    private sealed class ConcurrentProcessPort : IMinecraftProcessPort
    {
        public bool UsesPrivateArgumentTransport => true;
        internal readonly System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process> Children = [];
        public async ValueTask<System.Diagnostics.Process> StartAsync(System.Diagnostics.ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            var process = await new LongLivedProcessPort().StartAsync(startInfo, cancellationToken);
            Children.Add(process); return process;
        }
    }
    private static async ValueTask ConcurrentInstancesReachWindowConfirmationTogether()
    {
        MinecraftProcessService? processes = null; var probe = new ConcurrentWindowProbe(); var port = new ConcurrentProcessPort();
        var (coordinator, host, _, root) = ComposeAcquisitionCoordinator(new RecordingNeverInstaller(), processPort: port,
            javaLocator: ComposeWorkingJavaLocator(), windowProbe: probe, processServiceCreated: value => processes = value);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<XsrResult>? first = null, second = null;
        try
        {
            await CreateSecondConcurrentInstance(root);
            first = coordinator.StartAsync("1.20.1", 0, lifetime.Token).AsTask();
            second = coordinator.StartAsync("other", 0, lifetime.Token).AsTask();
            await probe.Both.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertTrue((await first).IsSuccess); AssertTrue((await second).IsSuccess);
            var sessions = processes!.ListSessions(); AssertEqual(2, sessions.Count);
            AssertEqual(2, sessions.Select(item => item.SessionId).Distinct().Count());
            AssertTrue(sessions.All(item => item.State == MinecraftProcessState.Running));
        }
        finally
        {
            lifetime.Cancel(); coordinator.Dispose();
            if (second is not null) await second;
            foreach (var child in port.Children)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await DisposeLaunchProgressFixtureAsync(root, host, processes, lifetime, first);
        }
    }

    private static async ValueTask ConcurrentIdentityRefreshSharesWorkAndKeepsOtherWaiter()
    {
        string root = CreateTempDirectory();
        try
        {
            var accounts = CreateAccountService(new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
            var profile = SampleProfile("Alice") with { Kind = LaunchProfileKind.Microsoft, RefreshToken = "refresh" };
            AssertTrue(accounts.AddProfile(profile).IsSuccess);
            var auth = new PausedMicrosoftRefresh(); var resolver = new AccountLaunchIdentityResolver(accounts, auth, "client-id");
            using var cancelled = new CancellationTokenSource();
            var first = resolver.ResolveAsync(0, profile, cancelled.Token).AsTask();
            await auth.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = resolver.ResolveAsync(0, profile).AsTask();
            cancelled.Cancel();
            await TrustRejectsAsync<OperationCanceledException>(() => first);
            AssertFalse(second.IsCompleted);
            auth.Completion.SetResult(new("Alice", profile.Uuid, "new-access", "new-refresh", null, true));
            AssertTrue((await second).IsSuccess);
            AssertEqual("new-access", accounts.GetProfile(0).Value.AccessToken);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ConcurrentPreflightPromptsQueueWithoutReplacement()
    {
        XsrStateStoreBuilder builder = new(); LaunchPreflightGate.DeclareState(builder); var store = builder.Build();
        var issue = new CapabilityPreflightIssue("MEM_HEAP_LAUNCH_LOW", PreflightSeverity.Warning, "memory", PreflightCertainty.Estimated, false, ["estimate.heap.launch"], canBypass: true);
        CapabilityPreflightReport report = new([issue], [issue], PreflightSeverity.Warning);
        LaunchPreflightGate gate = new(store, (_, _, _, _) => Task.FromResult(report));
        MinecraftLaunchPlan plan = new("java", "root", [], [], [], new(MinecraftModLoaderKind.Vanilla, null, null, []));
        var first = gate.CheckAsync("root", "a", plan, CancellationToken.None);
        var a = (LaunchPreflightPrompt)store.ReadAppliedValue(store.Resolve(LaunchPreflightGate.StateKey))!;
        var second = gate.CheckAsync("root", "b", plan, CancellationToken.None);
        AssertEqual(a.Attempt, ((LaunchPreflightPrompt)store.ReadAppliedValue(store.Resolve(LaunchPreflightGate.StateKey))!).Attempt);
        AssertTrue(gate.Decide(new(a.Attempt, true))); AssertTrue(await first);
        var b = (LaunchPreflightPrompt)store.ReadAppliedValue(store.Resolve(LaunchPreflightGate.StateKey))!;
        AssertFalse(a.Attempt == b.Attempt); AssertFalse(second.IsCompleted);
        AssertFalse(gate.Decide(new(a.Attempt, false))); AssertTrue(gate.Decide(new(b.Attempt, false))); AssertFalse(await second);
    }
}
