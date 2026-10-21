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

// XSR-712: the launch progress contract — the legacy stage-weight table, one coherent cell
// set per report, the pipeline narration from login through a launched game, and pipeline
// cancellation.
internal static partial class Program
{
    private static void LaunchStageWeightsMatchLegacyTable()
    {
        double total = MinecraftLaunchStages.Total;
        AssertEqual(44d, total);
        AssertEqual(0d, MinecraftLaunchStages.ProgressAt(0d));
        AssertEqual(1d, MinecraftLaunchStages.ProgressAt(total));
        AssertEqual(15d / 44d, MinecraftLaunchStages.ProgressAt(MinecraftLaunchStages.LoginWeight));
        AssertEqual(30d / 44d, MinecraftLaunchStages.ProgressAt(
            MinecraftLaunchStages.LoginWeight + MinecraftLaunchStages.CompleteFilesWeight));

        // Every stage boundary of the legacy table, in pipeline order: 36 → 38 → 39
        // → 40 → 42 → 44. Hooks use the original custom-command slot before game startup.
        const double afterLogin = MinecraftLaunchStages.LoginWeight;
        const double afterCompleteFiles = afterLogin + MinecraftLaunchStages.CompleteFilesWeight;
        const double afterJava = afterCompleteFiles + MinecraftLaunchStages.GetJavaWeight;
        const double afterArguments = afterJava + MinecraftLaunchStages.GetArgumentsWeight;
        const double afterExtract = afterArguments + MinecraftLaunchStages.ExtractNativesWeight;
        const double afterPreLaunch = afterExtract + MinecraftLaunchStages.PreLaunchWeight;
        const double afterCustomCommand = afterPreLaunch + MinecraftLaunchStages.CustomCommandWeight;
        const double afterStart = afterCustomCommand + MinecraftLaunchStages.StartProcessWeight;
        const double afterWait = afterStart + MinecraftLaunchStages.WaitWindowWeight;
        AssertEqual(36d, afterArguments);
        AssertEqual(39d, afterPreLaunch);
        AssertEqual(40d, afterCustomCommand);
        AssertEqual(42d, afterStart);
        AssertEqual(total, afterWait + MinecraftLaunchStages.EndWeight);
    }

    private static void ProgressPublisherWritesCoherentCells()
    {
        XsrStateStoreBuilder builder = new();
        MinecraftLaunchProgressState.DeclareState(builder);
        XsrStateStore store = builder.Build();
        MinecraftLaunchProgressPublisher publisher = new(store);

        publisher.Start();
        AssertTrue(ReadProgressFlag(store, MinecraftLaunchProgressState.ActiveKey));
        AssertEqual("get_java", ReadProgressText(store, MinecraftLaunchProgressState.StageKey));
        AssertEqual(0d, ReadProgressNumber(store, MinecraftLaunchProgressState.ProgressKey));

        publisher.Report(new MinecraftLaunchStageReport(
            "login", 0.5d, IsLaunched: false, Method: "offline", DownloadSpeed: "1.2 MB/s"));
        AssertEqual("login", ReadProgressText(store, MinecraftLaunchProgressState.StageKey));
        AssertEqual(0.5d, ReadProgressNumber(store, MinecraftLaunchProgressState.ProgressKey));
        AssertEqual("offline", ReadProgressText(store, MinecraftLaunchProgressState.MethodKey));
        AssertEqual("1.2 MB/s", ReadProgressText(store, MinecraftLaunchProgressState.SpeedKey));
        AssertFalse(ReadProgressFlag(store, MinecraftLaunchProgressState.LaunchedKey));

        publisher.Stop();
        AssertFalse(ReadProgressFlag(store, MinecraftLaunchProgressState.ActiveKey));
        AssertEqual(string.Empty, ReadProgressText(store, MinecraftLaunchProgressState.StageKey));
    }

    private static void CancelActiveLaunchWithoutLaunchReturnsFalse()
    {
        string root = CreateTempDirectory();
        try
        {
            using FoundationHost host = FoundationComposer.Compose(
                new InMemorySettingsPort(),
                LauncherDefaults.CreateSchema(),
                new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
            MinecraftLaunchCoordinator coordinator = new(
                root,
                Path.Combine(root, "runtime"),
                new MinecraftInstanceDiscovery(),
                host.Accounts,
                host.Settings,
                new JavaSelectionService(new InMemoryJavaLocator([])),
                new NeverJavaInstaller(),
                new MinecraftLaunchExecutor(new MinecraftProcessService()));
            AssertFalse(coordinator.CancelActiveLaunch());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async ValueTask LaunchPipelineNarratesStagesAndReachesLaunchedAsync()
    {
        string root = CreateTempDirectory();
        LongLivedProcessPort port = new();
        FoundationHost? host = null;
        MinecraftProcessService? processes = null;
        try
        {
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
            await metadataStore.SaveAsync(baseDirectory, new MinecraftInstanceMetadata());
            File.WriteAllBytes(Path.Combine(baseDirectory, "1.20.1.jar"), [0xCA, 0xFE]);

            SettingsSchema schema = LauncherDefaults.CreateSchema();
            host = FoundationComposer.Compose(
                new InMemorySettingsPort(),
                schema,
                new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
            AssertTrue(AddFixtureProfile(host.Accounts, new LaunchProfile
            {
                Username = "Player",
                Kind = LaunchProfileKind.Offline,
            }).IsSuccess);

            RecordingProgressPublisher progress = new(host.StateStore);
            string javaHome = Path.Combine(root, "test-java");
            string javaExecutable = Path.Combine(javaHome, "bin",
                OperatingSystem.IsWindows() ? "java.exe" : "java");
            Directory.CreateDirectory(Path.GetDirectoryName(javaExecutable)!);
            await File.WriteAllBytesAsync(javaExecutable, [0x00]);
            JavaRuntimeCandidate candidate = new(new JavaInstallation(
                javaHome,
                javaExecutable,
                null,
                new Version(17, 0, 10),
                JavaBrand.EclipseTemurin,
                JavaArchitecture.X64,
                is64Bit: true,
                isJre: false));
            processes = new(port, host.StateStore);
            MinecraftLaunchCoordinator coordinator = new(
                root,
                Path.Combine(root, "runtime"),
                new MinecraftInstanceDiscovery(
                    versionDiscovery: new MinecraftVersionDiscovery(),
                    metadataStore: metadataStore),
                host.Accounts,
                host.Settings,
                new JavaSelectionService(new InMemoryJavaLocator([candidate])),
                new NeverJavaInstaller(),
                new MinecraftLaunchExecutor(processes),
                new MinecraftLaunchPlatform(
                    MinecraftLibraryOperatingSystem.Linux,
                    "6.12",
                    Is64BitArchitecture: true,
                    IsArm64Architecture: false),
                progress: progress,
                windowProbe: new ImmediateWindowProbe());

            XsrResult result = await coordinator.StartServerAsync("1.20.1", 0, null, "play.example.org:25566");
            AssertTrue(port.LastStartInfo!.ArgumentList.Contains("--quickPlayMultiplayer"));
            AssertTrue(port.LastStartInfo.ArgumentList.Contains("play.example.org:25566"));
            AssertTrue(string.IsNullOrEmpty((await metadataStore.LoadAsync(baseDirectory)).ServerToEnter));
            if (!result.IsSuccess)
            {
                Console.WriteLine("DIAG launch failed: " + result.Error?.Code.Value + " " + result.Error?.Message);
            }

            AssertTrue(result.IsSuccess,
                "narration launch failed: " + result.Error?.Code.Value + " " + result.Error?.Message);


            // The narration reaches the launched state through the pipeline stage order; each
            // stage reports its entry, optional heartbeats, and completion, so assertions use
            // first-appearance order and monotonic progress instead of an exact report list.
            string[] expectedOrder =
            {
                "login", "complete_files", "get_java", "get_arguments",
                "extract_natives", "pre_launch", "custom_command", "start_process", "wait_window", "end",
            };
            string[] firstAppearance = progress.Stages.Distinct().ToArray();
            AssertTrue(expectedOrder.SequenceEqual(firstAppearance),
                "stage order: " + string.Join(",", progress.Stages));
            AssertTrue(progress.Progress.SequenceEqual(progress.Progress.OrderBy(value => value)),
                "progress not monotonic: " + string.Join(",", progress.Progress));

            // Each stage ENTERS exactly at its legacy boundary: 0/15/30/34/36/38, start_process
            // enters at 40 after custom_command enters at 39, wait_window at 42,
            // and the final report is 44/44.
            double FirstProgressOf(string stage) => progress.Progress[progress.Stages.IndexOf(stage)];
            AssertEqual(MinecraftLaunchStages.ProgressAt(0d), FirstProgressOf("login"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(15d), FirstProgressOf("complete_files"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(30d), FirstProgressOf("get_java"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(34d), FirstProgressOf("get_arguments"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(36d), FirstProgressOf("extract_natives"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(38d), FirstProgressOf("pre_launch"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(39d), FirstProgressOf("custom_command"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(40d), FirstProgressOf("start_process"));
            AssertEqual(MinecraftLaunchStages.ProgressAt(42d), FirstProgressOf("wait_window"));
            AssertEqual(1d, progress.Progress[^1]);
            // Two legal endings: the pipeline reached launched=true, or the instant-exit
            // process was reaped before the assertions and the subscribe-then-recheck reset
            // the truth (Empty snapshot retaining the session id). Both prove the narration
            // ran to the end stage; a stuck launched truth would fail the first branch.
            AssertTrue(progress.Stages.Contains(MinecraftLaunchStages.End)
                && progress.Stages.Contains(MinecraftLaunchStages.WaitWindow), "narration never reached the end stage");
            MinecraftLaunchProgressSnapshot final = host.StateStore
                .ReadAppliedValue(host.StateStore.Resolve(MinecraftLaunchProgressState.SnapshotKey))
                as MinecraftLaunchProgressSnapshot ?? MinecraftLaunchProgressSnapshot.Empty;
            bool stillLaunched = ReadProgressFlag(host.StateStore, MinecraftLaunchProgressState.LaunchedKey);
            bool reapedAfterEnd = final.Active == false && final.SessionId is { } id && id != Guid.Empty;
            AssertTrue(stillLaunched || reapedAfterEnd,
                "neither launched nor reaped: snapshot=" + final + " launched=" + stillLaunched);
            if (stillLaunched)
            {
                AssertEqual(1d, ReadProgressNumber(host.StateStore, MinecraftLaunchProgressState.ProgressKey));
                AssertEqual("end", ReadProgressText(host.StateStore, MinecraftLaunchProgressState.StageKey));
                AssertEqual("offline", ReadProgressText(host.StateStore, MinecraftLaunchProgressState.MethodKey));
            }

            AssertFalse(coordinator.CancelActiveLaunch());
            var ordinary = await coordinator.PrepareAsync("1.20.1", 0);
            AssertTrue(ordinary.IsSuccess);
            AssertTrue(string.IsNullOrEmpty(ordinary.Value.Request.Server));
            AssertFalse((await coordinator.StartServerAsync("1.20.1", 0, null, "https://invalid.example")).IsSuccess);
        }
        finally
        {
            if (port.LastProcess is { } process)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await DisposeLaunchProgressFixtureAsync(root, host, processes);
        }
    }

    private static async Task DisposeLaunchProgressFixtureAsync(string root, FoundationHost? host,
        MinecraftProcessService? processes, CancellationTokenSource? launchLifetime = null,
        Task<XsrResult>? pendingLaunch = null)
    {
        launchLifetime?.Cancel();
        if (pendingLaunch is not null)
        {
            try { await pendingLaunch.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) when (launchLifetime?.IsCancellationRequested == true) { }
        }
        // Capture scope before disposal clears the session registry. Even a terminal
        // process can still own an executor's asynchronous game-directory cleanup.
        string[] gameDirectories = processes?.ListSessions()
            .Select(session => session.GameDirectory)
            .Distinct(Nexa.Core.PathIdentity.Comparer).ToArray() ?? [];
        if (processes is not null) await processes.DisposeAsync();
        foreach (string gameDirectory in gameDirectories)
            await WaitForLaunchGameDirectoryReleasedAsync(gameDirectory);
        host?.Dispose();
        Directory.Delete(root, recursive: true);
    }
}
