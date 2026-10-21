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

    private static async ValueTask SecondConcurrentLaunchIsRejectedAsAlreadyActive()
    {
        LongLivedProcessPort processPort = new();
        MinecraftProcessService? processes = null;
        (MinecraftLaunchCoordinator coordinator, FoundationHost host, RecordingStubInstaller installer, string root) =
            ComposeAcquisitionCoordinator(installer: new RecordingStubInstaller(), processPort: processPort,
                processServiceCreated: service => processes = service);
        using CancellationTokenSource launchLifetime = new();
        Task<XsrResult>? first = null;
        try
        {
            // Park the first pipeline at the acquisition gate, then start a second one.
            first = Task.Run(
                () => coordinator.StartAsync("1.20.1", accountIndex: 0, launchLifetime.Token).AsTask());
            XsrStateStore store = host.StateStore;
            AssertTrue(SpinWait.SpinUntil(
                () => store.ReadAppliedValue(store.Resolve(MinecraftLaunchProgressState.AcquirePendingKey)) is bool waiting && waiting,
                TimeSpan.FromSeconds(5)));

            XsrResult second = await coordinator.StartAsync("1.20.1", accountIndex: 0);
            AssertFalse(second.IsSuccess);
            AssertEqual(MinecraftErrors.LaunchAlreadyActiveCode, second.Error!.Code);

            // The first pipeline keeps its registration: cancellation is single-flight too.
            AssertTrue(coordinator.DecideJavaAcquisition(approve: true));
            XsrResult firstResult = await first;
            if (!firstResult.IsSuccess)
            {
                Console.WriteLine("DIAG first launch failed: " + firstResult.Error?.Message);
            }

            AssertTrue(firstResult.IsSuccess);
            AssertEqual(1, installer.Calls);
        }
        finally
        {
            if (processPort.LastProcess is { } child)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await DisposeLaunchProgressFixtureAsync(root, host, processes, launchLifetime, first);
        }
    }

    private static async ValueTask UnsupportedAccountKindsRefuseToLaunch()
    {
        string root = CreateTempDirectory();
        try
        {
            using FoundationHost host = FoundationComposer.Compose(
                new InMemorySettingsPort(),
                LauncherDefaults.CreateSchema(),
                new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
            AssertTrue(AddFixtureProfile(host.Accounts, new LaunchProfile
            {
                Username = "Player",
                Kind = LaunchProfileKind.LittleSkin,
            }).IsSuccess);
            AccountLaunchIdentityResolver resolver = new(host.Accounts);
            LaunchProfile profile = host.Accounts.GetProfile(0).Value
                ?? throw new InvalidOperationException("the corpus profile was not persisted.");
            XsrResult<MinecraftLaunchIdentity> identity = await resolver.ResolveAsync(0, profile);
            AssertFalse(identity.IsSuccess);
            AssertEqual(AccountErrors.LaunchNotSupportedCode, identity.Error!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async ValueTask ImmediateExitAfterProcessStartResetsLaunchProgress()
    {
        // Mirrors the standalone repro exactly (Win32 platform, plain publisher, dead-JVM
        // port): a JVM that dies before StartAsync returns must still reset the narration.
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
        await metadataStore.SaveAsync(baseDirectory, new MinecraftInstanceMetadata());
        File.WriteAllBytes(Path.Combine(baseDirectory, "1.20.1.jar"), [0xCA, 0xFE]);

        SettingsSchema schema = LauncherDefaults.CreateSchema();
        FoundationHost host = FoundationComposer.Compose(
            new InMemorySettingsPort(),
            schema,
            new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
        AssertTrue(AddFixtureProfile(host.Accounts, new LaunchProfile
        {
            Username = "Player",
            Kind = LaunchProfileKind.Offline,
        }).IsSuccess);
        Console.WriteLine($"[immediate] corpus ready {DateTime.Now:HH:mm:ss.fff}");

        RecordingProgressPublisher progress = new(host.StateStore);
        IJavaRuntimeLocator locator = ComposeWorkingJavaLocator();
        MinecraftProcessService processes = new(new ExitedBeforeReturnProcessPort(), host.StateStore);
        MinecraftLaunchCoordinator coordinator = new(
            root,
            Path.Combine(root, "runtime"),
            new MinecraftInstanceDiscovery(
                versionDiscovery: new MinecraftVersionDiscovery(),
                metadataStore: metadataStore),
            host.Accounts,
            host.Settings,
            new JavaSelectionService(locator),
            new NeverJavaInstaller(),
            new MinecraftLaunchExecutor(processes),
            new MinecraftLaunchPlatform(
                MinecraftLibraryOperatingSystem.Linux,
                "6.12",
                Is64BitArchitecture: true,
                IsArm64Architecture: false),
            progress: progress,
            windowProbe: new ImmediateWindowProbe());

        try
        {
            Console.WriteLine($"[immediate] start {DateTime.Now:HH:mm:ss.fff}");
            XsrResult result = await coordinator.StartAsync("1.20.1", accountIndex: 0);
            Console.WriteLine($"[immediate] done success={result.IsSuccess} {DateTime.Now:HH:mm:ss.fff}");
            // A JVM that dies before its window appears is a FAILED launch, not a launched one.
            AssertFalse(result.IsSuccess);
            AssertEqual(MinecraftErrors.ExitedBeforeWindowCode, result.Error!.Code);
            XsrStateStore store = host.StateStore;
            AssertTrue(SpinWait.SpinUntil(
                () => store.ReadAppliedValue(store.Resolve(MinecraftLaunchProgressState.SnapshotKey))
                    is MinecraftLaunchProgressSnapshot snapshot && !snapshot.Active && snapshot.SessionId is not null,
                TimeSpan.FromSeconds(5)));
            AssertFalse(ReadProgressFlag(store, MinecraftLaunchProgressState.LaunchedKey));
            // Launch narration finishes before background JVM metadata collection. Do not
            // delete the fixture's manifest while that real collector is still reading it.
            var finishedSession = processes.ListSessions().Single();
            Guid sessionId = finishedSession.SessionId;
            AssertTrue(SpinWait.SpinUntil(
                () => store.ReadCollection<JvmRunContext>(store.Resolve(JvmHostStateContract.ContextsKey))
                    .Items.Any(context => context.SessionId == sessionId), TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await DisposeLaunchProgressFixtureAsync(root, host, processes);
        }
    }

    private static void OfflineLegacyUuidLaunchesCorrectlyWhenMigrationSaveFails()
    {
        // The durable roster rewrite is best-effort; the launch-time resolver must recognize
        // the alpha's byte-swapped UUID and derive the correct one regardless.
        string legacy = MinecraftOfflineIdentity.LegacyMismatchedUuid("Player");
        AssertEqual(MinecraftOfflineIdentity.UuidFromName("Player"),
            MinecraftOfflineIdentity.Resolve("Player", legacy).Uuid);
        AssertEqual(("Player", "5d8f8d5b51ba4c74ba6a89c5a21e94e5"),
            MinecraftOfflineIdentity.Resolve("Player", "5d8f8d5b51ba4c74ba6a89c5a21e94e5"));
    }

    private static async ValueTask CancelDuringWindowWaitTerminatesTheProcess()
    {
        // Identical corpus to the narration test; only the probe (never sees a window) and the
        // long-lived child differ, so the pipeline parks itself inside wait_window.
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
        await metadataStore.SaveAsync(baseDirectory, new MinecraftInstanceMetadata());
        File.WriteAllBytes(Path.Combine(baseDirectory, "1.20.1.jar"), [0xCA, 0xFE]);

        SettingsSchema schema = LauncherDefaults.CreateSchema();
        FoundationHost host = FoundationComposer.Compose(
            new InMemorySettingsPort(),
            schema,
            new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
        AssertTrue(AddFixtureProfile(host.Accounts, new LaunchProfile
        {
            Username = "Player",
            Kind = LaunchProfileKind.Offline,
        }).IsSuccess);
        Console.WriteLine($"[cancel-diag] corpus ready {DateTime.Now:HH:mm:ss.fff}");

        IJavaRuntimeLocator locator = ComposeWorkingJavaLocator();
        LongLivedProcessPort port = new();
        MinecraftProcessService processes = new(port, host.StateStore);
        MinecraftLaunchCoordinator coordinator = new(
            root,
            Path.Combine(root, "runtime"),
            new MinecraftInstanceDiscovery(
                versionDiscovery: new MinecraftVersionDiscovery(),
                metadataStore: metadataStore),
            host.Accounts,
            host.Settings,
            new JavaSelectionService(locator),
            new NeverJavaInstaller(),
            new MinecraftLaunchExecutor(processes),
            new MinecraftLaunchPlatform(
                MinecraftLibraryOperatingSystem.Linux,
                "6.12",
                Is64BitArchitecture: true,
                IsArm64Architecture: false),
            progress: new MinecraftLaunchProgressPublisher(host.StateStore),
            windowProbe: new BlindWindowProbe());

        using CancellationTokenSource launchLifetime = new();
        Task<XsrResult>? launch = null;
        try
        {
            launch = Task.Run(
                () => coordinator.StartAsync("1.20.1", accountIndex: 0, launchLifetime.Token).AsTask());
            XsrStateStore store = host.StateStore;
            bool reachedWait = SpinWait.SpinUntil(
                () => ReadProgressText(store, MinecraftLaunchProgressState.StageKey)
                    == MinecraftLaunchStages.WaitWindow,
                TimeSpan.FromSeconds(10));
            Console.Error.WriteLine($"[cancel-diag] reachedWait={reachedWait} {DateTime.Now:HH:mm:ss.fff}");
            AssertTrue(reachedWait);

            // Cancelling before the window confirmation must kill the game we created — the
            // window must never pop up after the user cancelled.
            AssertTrue(coordinator.CancelActiveLaunch());
            XsrResult result = await launch;
            Console.Error.WriteLine($"[cancel-diag] cancelled result={result.IsSuccess} {DateTime.Now:HH:mm:ss.fff}");
            AssertFalse(result.IsSuccess);
            AssertTrue(SpinWait.SpinUntil(() => port.LastProcess?.HasExited == true, TimeSpan.FromSeconds(5)));
            AssertTrue(ReadProgressText(store, MinecraftLaunchProgressState.StageKey) == string.Empty);
        }
        finally
        {
            coordinator.CancelActiveLaunch();
            await DisposeLaunchProgressFixtureAsync(root, host, processes, launchLifetime, launch);
        }
    }

    private static async ValueTask UnsupportedProbeSkipsTheWaitWithoutBurningTheLimit()
    {
        // White-box: an Unsupported probe must return immediately even for a LIVE process —
        // the pre-fix behavior burned the whole two-minute wait limit on such platforms.
        LongLivedProcessPort port = new();
        MinecraftProcessSession session = new(
            await port.StartAsync(new System.Diagnostics.ProcessStartInfo()),
            "instance",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Path.Combine(Path.GetTempPath(), "versions", "instance"), Path.GetTempPath());
        try
        {
            long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            MinecraftLaunchCoordinator.GameWindowWaitResult wait = await MinecraftLaunchCoordinator.WaitForGameWindowAsync(
                new UnsupportedWindowProbe(), null, session, CancellationToken.None);
            AssertEqual(MinecraftLaunchCoordinator.GameWindowWaitResult.Unsupported, wait);
            AssertFalse(session.Snapshot.GameWindowConfirmed);
            foreach (Exception decorationFailure in new Exception[] { new PlatformNotSupportedException("COM unavailable"), new InvalidOperationException("decoration failed") })
            {
                bool invoked = false;
                wait = await MinecraftLaunchCoordinator.WaitForGameWindowAsync(
                    new ImmediateWindowProbe(), null, session, CancellationToken.None, _ =>
                    {
                        invoked = true;
                        throw decorationFailure;
                    });
                AssertTrue(invoked);
                AssertEqual(MinecraftLaunchCoordinator.GameWindowWaitResult.Visible, wait);
                AssertTrue(session.Snapshot.GameWindowConfirmed);
                AssertFalse(port.LastProcess!.HasExited);
            }
            AssertTrue(System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(5),
                "the unsupported probe waited for the window limit");
        }
        finally
        {
            if (port.LastProcess is { } process)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await session.DisposeAsync();
        }
    }

    private static void OnboardingCompositionArmsRefreshCapabilityWithoutExplicitMicrosoft()
    {
        // Production never passes an explicit Microsoft service: the composer must create one
        // instance and share it between onboarding and the launch resolver.
        string root = Path.Combine(Path.GetTempPath(), "nexa-onboarding-cap", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using FoundationHost host = FoundationComposer.Compose(
                new InMemorySettingsPort(),
                LauncherDefaults.CreateSchema(),
                new LaunchProfileFilePort(Path.Combine(root, "profiles.json")));
            using AccountOnboardingRuntime runtime = AccountOnboardingRuntimeComposer.Compose(
                host,
                options: new AccountOnboardingOptions("client-from-embed", null));
            AssertTrue(runtime.LaunchIdentityResolver is not null);
            AssertTrue(runtime.LaunchIdentityResolver
                is AccountLaunchIdentityResolver { ComposedRefreshCapability: true });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A process port returning a long-lived child the test can cancel.</summary>
    private sealed class LongLivedProcessPort : IMinecraftProcessPort
    {
        public bool UsesPrivateArgumentTransport => true;

        public System.Diagnostics.Process? LastProcess { get; private set; }
        public System.Diagnostics.ProcessStartInfo? LastStartInfo { get; private set; }

        public ValueTask<System.Diagnostics.Process> StartAsync(
            System.Diagnostics.ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default)
        {
            LastStartInfo = startInfo;
            // `timeout` refuses redirected stdin and `sh -c sleep 30` parses "30" as $0 —
            // the platform primitives are the reliable wait: ping on Windows, /bin/sleep elsewhere.
            System.Diagnostics.ProcessStartInfo wait = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("cmd", "/c ping -n 30 127.0.0.1 > nul")
                : new System.Diagnostics.ProcessStartInfo("/bin/sleep", "30");
            wait.UseShellExecute = false;
            wait.CreateNoWindow = true;
            LastProcess = System.Diagnostics.Process.Start(wait)!;
            return ValueTask.FromResult(LastProcess);
        }
    }

    /// <summary>A probe that never sees a window while detection is supported.</summary>
    private sealed class BlindWindowProbe : IMinecraftWindowProbe
    {
        public ValueTask<MinecraftWindowProbeResult> ProbeAsync(int processId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MinecraftWindowProbeResult.NotVisible);
    }

    /// <summary>A probe whose platform has no window detection: the wait must be skipped.</summary>
    private sealed class UnsupportedWindowProbe : IMinecraftWindowProbe
    {
        public ValueTask<MinecraftWindowProbeResult> ProbeAsync(int processId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MinecraftWindowProbeResult.Unsupported);
    }

    private static async ValueTask CancelRacingWithLaunchCompletionNeverThrows()
    {
        // The cancel command is user-triggerable at any instant: a completion that nulls and
        // disposes the CTS between a split read and Cancel used to throw
        // ObjectDisposedException. Hammer both sides concurrently.
        MinecraftProcessService? processes = null;
        (MinecraftLaunchCoordinator coordinator, FoundationHost host, _, string root) =
            ComposeAcquisitionCoordinator(new RecordingStubInstaller(), processPort: new ExitedBeforeReturnProcessPort(),
                processServiceCreated: service => processes = service);
        using CancellationTokenSource stop = new();
        Task? cancelHammer = null;
        try
        {
            cancelHammer = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        coordinator.CancelActiveLaunch();
                    }
                    catch (ObjectDisposedException exception)
                    {
                        throw new InvalidOperationException(
                            "CancelActiveLaunch threw ObjectDisposedException", exception);
                    }

                    await Task.Yield();
                }
            });
            for (int round = 0; round < 40; round++)
            {
                await coordinator.StartAsync("1.20.1", accountIndex: 0);
            }

            stop.Cancel();
            await cancelHammer;
        }
        finally
        {
            stop.Cancel();
            try { if (cancelHammer is not null) await cancelHammer; }
            finally { await DisposeLaunchProgressFixtureAsync(root, host, processes); }
        }
    }

    private static async ValueTask X11WindowProbeFindsARealWindow()
    {
        // ABI smoke under a real X server (xvfb in CI): create a mapped window that owns a
        // _NET_WM_PID property for this process, then the probe must report it visible. This
        // is the only check that executes XGetWindowAttributes against a live server — the
        // native struct write is the hazard, so headless fallbacks cannot prove the ABI.
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DISPLAY") is not { Length: > 0 })
        {
            return;
        }

        nint display = X11TestApi.XOpenDisplay(0);
        AssertTrue(display != 0, "xvfb-run should provide a display for this test");
        try
        {
            nuint root = (nuint)X11TestApi.XDefaultRootWindow(display);
            nint black = X11TestApi.XBlackPixel(display, 0);
            nint white = X11TestApi.XWhitePixel(display, 0);
            nuint window = (nuint)X11TestApi.XCreateSimpleWindow(display, root, 0, 0, 64, 64, 0, black, white);
            nuint pidAtom = (nuint)X11TestApi.XInternAtom(display, "_NET_WM_PID", false);
            nint[] ownPid = [Environment.ProcessId];
            _ = X11TestApi.XChangeProperty(
                display, window, pidAtom, X11TestApi.XaCardinal, 32, X11TestApi.PropModeReplace, ownPid, 1);

            _ = X11TestApi.XMapWindow(display, window);
            _ = X11TestApi.XFlush(display);
            System.Threading.Thread.Sleep(200);

            nint probeDisplay = X11TestApi.XOpenDisplay(0);
            try
            {
                nuint probeRoot = (nuint)X11TestApi.XDefaultRootWindow(probeDisplay);
                bool visible = MinecraftWindowProbe.OwnedVisibleWindowExists(probeDisplay, probeRoot, (nuint)Environment.ProcessId);
                AssertTrue(visible, "the probe must see the mapped test window of this process");
            }
            finally
            {
                _ = X11TestApi.XDestroyWindow(probeDisplay, window);
                _ = X11TestApi.XCloseDisplay(probeDisplay);
            }
        }
        finally
        {
            _ = X11TestApi.XCloseDisplay(display);
        }
    }
}
