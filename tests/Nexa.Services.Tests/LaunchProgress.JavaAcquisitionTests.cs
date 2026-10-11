using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{

    private static async ValueTask JavaAcquisitionWaitsForDecisionAndDenialFailsLaunch()
    {
        MinecraftProcessService? processes = null;
        (MinecraftLaunchCoordinator coordinator, FoundationHost host, RecordingNeverInstaller installer, string root) =
            ComposeAcquisitionCoordinator(new RecordingNeverInstaller(), processServiceCreated: service => processes = service);
        using CancellationTokenSource launchLifetime = new();
        Task<XsrResult>? launchTask = null;
        try
        {
            AssertTrue(host.SettingsPolicy.Set(new("java.auto-install", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
            AssertTrue(host.SettingsPolicy.Set(new("java.auto-install", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "false"), Path.Combine(root, "versions", "1.20.1"))).IsSuccess);
            launchTask = Task.Run(
                () => coordinator.StartAsync("1.20.1", accountIndex: 0, launchLifetime.Token).AsTask());
            XsrStateStore store = host.StateStore;
            AssertTrue(SpinWait.SpinUntil(
                () => store.ReadAppliedValue(store.Resolve(MinecraftLaunchProgressState.AcquirePendingKey)) is bool waiting && waiting,
                TimeSpan.FromSeconds(5)));
            AssertEqual(1, host.Work.Snapshot.QuietScopes);
            AssertTrue(coordinator.DecideJavaAcquisition(approve: false));
            XsrResult result = await launchTask;
            AssertFalse(result.IsSuccess);
            AssertEqual(MinecraftErrors.JavaUnavailableCode, result.Error!.Code);
            AssertEqual(0, installer.Calls);
            AssertEqual(0, host.Work.Snapshot.QuietScopes);
            AssertFalse(ReadProgressFlag(store, MinecraftLaunchProgressState.AcquirePendingKey));
        }
        finally
        {
            await DisposeLaunchProgressFixtureAsync(root, host, processes, launchLifetime, launchTask);
        }
    }

    private static async ValueTask JavaAcquisitionApprovalDownloadsAndLaunches()
    {
        RecordingStubInstaller installer = new();
        LongLivedProcessPort processPort = new();
        MinecraftProcessService? processes = null;
        (MinecraftLaunchCoordinator coordinator, FoundationHost host, _, string root) =
            ComposeAcquisitionCoordinator(installer, processPort: processPort, processServiceCreated: service => processes = service);
        using CancellationTokenSource launchLifetime = new();
        Task<XsrResult>? launchTask = null;
        try
        {
            launchTask = Task.Run(
                () => coordinator.StartAsync("1.20.1", accountIndex: 0, launchLifetime.Token).AsTask());
            XsrStateStore store = host.StateStore;
            AssertTrue(SpinWait.SpinUntil(
                () => store.ReadAppliedValue(store.Resolve(MinecraftLaunchProgressState.AcquirePendingKey)) is bool waiting && waiting,
                TimeSpan.FromSeconds(5)));
            AssertEqual("java-runtime-gamma", store.ReadAppliedValue(store.Resolve(MinecraftLaunchProgressState.AcquireComponentKey)));
            AssertTrue(coordinator.DecideJavaAcquisition(approve: true));
            XsrResult result = await launchTask;
            if (!result.IsSuccess)
            {
                Console.WriteLine("DIAG approve launch failed: " + result.Error?.Message);
            }

            AssertTrue(result.IsSuccess,
                "approval launch failed: " + result.Error?.Code.Value + " " + result.Error?.Message);
            AssertEqual(1, installer.Calls);
            AssertEqual(1, host.Work.Snapshot.QuietScopes);
            // The child remains alive through window detection. An immediately exiting fixture
            // races the production exited-before-window check, especially in Linux NativeAOT.
            AssertFalse(ReadProgressFlag(store, MinecraftLaunchProgressState.AcquirePendingKey));
        }
        finally
        {
            if (processPort.LastProcess is { } child)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            AssertTrue(SpinWait.SpinUntil(() => host.Work.Snapshot.QuietScopes == 0, TimeSpan.FromSeconds(5)));
            await DisposeLaunchProgressFixtureAsync(root, host, processes, launchLifetime, launchTask);
        }
    }

    /// <summary>Builds a locator offering one fake but selectable Java 17 runtime.</summary>
    private static InMemoryJavaLocator ComposeWorkingJavaLocator()
    {
        string javaHome = Path.Combine(
            Path.GetTempPath(), "nexa-test-java", Guid.NewGuid().ToString("N"));
        string javaExecutable = Path.Combine(javaHome, "bin",
            OperatingSystem.IsWindows() ? "java.exe" : "java");
        Directory.CreateDirectory(Path.GetDirectoryName(javaExecutable)!);
        File.WriteAllBytes(javaExecutable, [0x00]);
        return new InMemoryJavaLocator([new JavaRuntimeCandidate(new JavaInstallation(
            javaHome,
            javaExecutable,
            null,
            new Version(17, 0, 10),
            JavaBrand.EclipseTemurin,
            JavaArchitecture.X64,
            is64Bit: true,
            isJre: false))]);
    }

    /// <summary>
    /// Composes a launchable corpus with NO compatible Java installed, so the pipeline stops
    /// at the acquisition approval gate.
    /// </summary>
    private static (MinecraftLaunchCoordinator Coordinator, FoundationHost Host, T Installer, string Root)
        ComposeAcquisitionCoordinator<T>(T installer, IMinecraftProcessPort? processPort = null, IJavaRuntimeLocator? javaLocator = null, IMinecraftWindowProbe? windowProbe = null,
            Action<MinecraftProcessService>? processServiceCreated = null) where T : IJavaRuntimeInstaller
    {
        string root = CreateTempDirectory();
        string baseDirectory = CreateVersionDirectory(root, "1.20.1", new JsonObject
        {
            ["id"] = "1.20.1",
            ["type"] = "release",
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T00:00:00Z",
            ["javaVersion"] = new JsonObject
            {
                ["majorVersion"] = 17,
                ["component"] = "java-runtime-gamma",
            },
        });
        MinecraftInstanceMetadataStore metadataStore = new();
        metadataStore.SaveAsync(baseDirectory, new MinecraftInstanceMetadata()).GetAwaiter().GetResult();
        File.WriteAllBytes(Path.Combine(baseDirectory, "1.20.1.jar"), [0xCA, 0xFE]);

        SettingsSchema schema = LauncherDefaults.CreateSchema();
        FoundationHost host = FoundationComposer.Compose(
            new InMemorySettingsPort(),
            schema,
            new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
        AssertTrue(host.Accounts.AddProfile(new LaunchProfile
        {
            Username = "Player",
            Kind = LaunchProfileKind.Offline,
        }).IsSuccess);
        MinecraftProcessService processes = new(processPort ?? new ExitingProcessPort(), host.StateStore);
        processServiceCreated?.Invoke(processes);
        MinecraftLaunchCoordinator coordinator = new(
            root,
            Path.Combine(root, "runtime"),
            new MinecraftInstanceDiscovery(
                versionDiscovery: new MinecraftVersionDiscovery(),
                metadataStore: metadataStore),
            host.Accounts,
            host.Settings,
            new JavaSelectionService(javaLocator ?? new InMemoryJavaLocator([])),
            installer,
            new MinecraftLaunchExecutor(processes),
            new MinecraftLaunchPlatform(
                MinecraftLibraryOperatingSystem.Linux,
                "6.12",
                Is64BitArchitecture: true,
                IsArm64Architecture: false),
            progress: new MinecraftLaunchProgressPublisher(host.StateStore),
            windowProbe: windowProbe ?? new ImmediateWindowProbe(), settingsPolicy: host.SettingsPolicy)
        { WorkScheduler = host.Work };
        return (coordinator, host, installer, root);
    }

    private static async ValueTask JavaAutoInstallUsesSettingsWithoutApprovalAndPreservesExplicitRuntime()
    {
        MinecraftProcessService? processes = null;
        var (coordinator, host, installer, root) = ComposeAcquisitionCoordinator(new RecordingStubInstaller(),
            processServiceCreated: service => processes = service);
        try
        {
            AssertTrue(host.SettingsPolicy.Set(new("java.auto-install", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var prepared = await coordinator.PrepareAsync("1.20.1", 0, cancellationToken: timeout.Token);
            AssertTrue(prepared.IsSuccess); AssertEqual(1, installer.Calls);
            AssertFalse(ReadProgressFlag(host.StateStore, MinecraftLaunchProgressState.AcquirePendingKey));
            AssertTrue(host.SettingsPolicy.Set(new("java.runtime", SettingsLayer.Instance,
                new(SettingsOverrideMode.Custom, Path.Combine(root, "missing-java")), Path.Combine(root, "versions", "1.20.1"))).IsSuccess);
            var explicitRuntime = await coordinator.PrepareAsync("1.20.1", 0, cancellationToken: timeout.Token);
            AssertFalse(explicitRuntime.IsSuccess); AssertEqual(1, installer.Calls);
        }
        finally { await DisposeLaunchProgressFixtureAsync(root, host, processes); }
    }

    private sealed class RecordingNeverInstaller : IJavaRuntimeInstaller
    {
        public int Calls { get; private set; }

        public Task<string> InstallAsync(
            string requestedComponent,
            string runtimeRootDirectory,
            IProgress<JavaRuntimeInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("The declined acquisition must not download.");
        }
    }

    /// <summary>Fakes a runtime installation by creating an executable and returning its path.</summary>
    private sealed class RecordingStubInstaller : IJavaRuntimeInstaller
    {
        public int Calls { get; private set; }

        public Task<string> InstallAsync(
            string requestedComponent,
            string runtimeRootDirectory,
            IProgress<JavaRuntimeInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            string executable = Path.Combine(runtimeRootDirectory, requestedComponent,
                OperatingSystem.IsWindows() ? "java.exe" : "java");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, [0x00]);
            return Task.FromResult(executable);
        }
    }
}
