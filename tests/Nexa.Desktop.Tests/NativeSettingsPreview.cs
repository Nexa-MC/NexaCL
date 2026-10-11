using Nexa.Xsr.State;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Setup;
using Nexa.Services.Logging;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

// Separate process: actual product NXML, settings routes/controller and native backend.
// Only its temporary local data root and empty instance inventory are fixtures.
internal static partial class Program
{
    private static int RunNativeSettingsPreview(string theme, string outputDirectory)
    {
        if (theme is not ("light" or "dark"))
        {
            Console.Error.WriteLine("Expected --native-settings-preview light|dark outputDirectory.");
            return 2;
        }
        return RunNativeSettingsPreviewAsync(theme, Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
    }

    private static async Task<int> RunNativeSettingsPreviewAsync(string theme, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        bool dark = theme == "dark";
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], disableHardwareAcceleration: true,
            new AvaloniaUiStartupAppearance(dark ? XsrUiThemeMode.Dark : XsrUiThemeMode.Light, ReducedMotion: true),
            cancellationToken: timeout.Token);
        LaunchPageFixture? fixture = null;
        SettingsPageController? settings = null;
        ResourcesPageController? resources = null;
        DesktopAppearanceSession? appearance = null;
        DesktopPresentationSession? presentation = null;
        AvaloniaUiShellWindow? window = null;
        AvaloniaUiSceneSurface? surface = null;
        Task<int>? lifetime = null;
        var actions = new AvaloniaUiPlatformActions();
        int outcome = 1;
        try
        {
            await startup.InvokeAsync(async () =>
            {
                fixture = new(new ImmediateInstanceSource([]));
                AssertTrue(fixture.Foundation.Host.Settings.SetValue("UiDarkMode", dark ? 1 : 0).IsSuccess);
                appearance = new(fixture.Shell, fixture.Store, actions);
                presentation = new(fixture.Shell, fixture.Store, actions.SetWindowResizeEnabled,
                    actions.SetAnimationFrameRate, fixture.Foundation.Queries, action => Dispatcher.UIThread.Post(action));
                settings = new(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
                    fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
                fixture.Controller.SettingsPage = settings.Page;
                await settings.PrepareStartupAsync(timeout.Token);
                Emit(fixture.Intents, "ui.navigation.settings");
                fixture.Shell.Render(new(850, 500));
            }, timeout.Token);
            await presentation!.InitialReady.WaitAsync(timeout.Token);
            var f = fixture!;
            var controller = settings!;
            await startup.PrepareShellAsync(f.Shell, actions, timeout.Token);
            await startup.WarmUpShellAsync(f.Shell, timeout.Token);
            lifetime = Task.Run(() => AvaloniaUiShellHost.Run(f.Shell, platformActions: actions));
            await Eventually(() =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not AvaloniaUiShellWindow candidate || !candidate.IsVisible) return false;
                window = candidate;
                surface = candidate.GetVisualDescendants().OfType<AvaloniaUiSceneSurface>().Single();
                candidate.Width = 1040; candidate.Height = 740;
                if (candidate.Screens.Primary is { } screen)
                    candidate.Position = new(screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - (int)(1040 * candidate.RenderScaling)) / 2),
                        screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - (int)(740 * candidate.RenderScaling)) / 2));
                candidate.UpdateLayout(); surface!.CommitScene();
                return surface.Scene is { Count: > 0 };
            });
            await SelectCategory("appearance");
            await Eventually(() => !controller.SettingsWritePending && f.Shell.Renderer.ColorScheme.IsDark == dark
                && AvaloniaUiRuntimeDiagnostics.Capture().ActiveMotionTracks == 0);
            await startup.InvokeAsync(() =>
            {
                bool hasRetiredSetting = false;
                f.Shell.Tree.Walk(controller.Page, entity =>
                {
                    string? text = f.Shell.Tree.GetComponent<XsrUiText>(entity)?.Content;
                    if (text?.Contains("超低功耗", StringComparison.Ordinal) == true
                        || text?.Contains("低功耗模式", StringComparison.Ordinal) == true) hasRetiredSetting = true;
                    return true;
                });
                AssertFalse(hasRetiredSetting);
                Capture("top");
            }, timeout.Token);
            await Task.Delay(1200, timeout.Token); // Display hold for the external X11 recording.

            // Real pointer-scroll admission and live viewport, rather than a replacement layout.
            for (int step = 0; step < 128; step++)
            {
                bool ready = false, atEnd = false;
                await startup.InvokeAsync(() =>
                {
                    surface!.CommitScene();
                    var body = CurrentBody();
                    bool Visible(string label) => surface!.Scene!.Nodes.Any(node => node.Text == label
                        && node.Rect.Y >= body.Rect.Y && node.Rect.Y + node.Rect.Height <= body.Rect.Y + body.Rect.Height);
                    ready = Visible("UI 动画帧率") && Visible("减少动态效果");
                    atEnd = body.Scroll is { } scroll && scroll.OffsetY >= scroll.MaximumOffsetY;
                    if (!ready && !atEnd)
                    {
                        AssertTrue(f.Shell.Renderer.PointerScroll(new(body.Rect.X + 30, body.Rect.Y + 30), 80));
                        surface!.CommitScene();
                    }
                }, timeout.Token);
                if (ready) break;
                if (atEnd || step == 127) throw new InvalidOperationException("The actual animation settings did not enter the preview viewport.");
                await Task.Delay(40, timeout.Token);
            }
            await startup.InvokeAsync(() => Capture("motion"), timeout.Token);
            await Task.Delay(1200, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                var body = CurrentBody();
                var scroll = f.Shell.Tree.GetComponent<XsrUiScroll>(body.Entity)!;
                scroll.OffsetY = body.Scroll!.Value.MaximumOffsetY;
                f.Shell.Tree.MarkDirty(body.Entity, XsrUiDirtyKinds.Layout);
                surface!.CommitScene(); Capture("bottom");
            }, timeout.Token);
            await Task.Delay(1200, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                var body = CurrentBody();
                f.Shell.Tree.GetComponent<XsrUiScroll>(body.Entity)!.OffsetY = 0;
                f.Shell.Tree.MarkDirty(body.Entity, XsrUiDirtyKinds.Layout);
                surface!.CommitScene();
                var option = FindByKey(f.Shell, surface!.Scene!, "SettingsOption.appearance.theme-mode." + (dark ? "0" : "1"));
                Emit(f.Intents, "ui.settings.choice", option.Entity);
                surface!.CommitScene();
            }, timeout.Token);
            await Eventually(() => !controller.SettingsWritePending && f.Shell.Renderer.ColorScheme.IsDark != dark
                && surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity)
                    == "SettingsOption.appearance.theme-mode." + (dark ? "0" : "1") && node.IsSelected));
            await startup.InvokeAsync(() => Capture("theme-switch"), timeout.Token);
            await Task.Delay(1200, timeout.Token);
            await SelectCategory("game");
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                Capture("catalog-game");
                var formScene = surface!.Scene!;
                var argument = ShowSettingsFixtureControl(f, controller, ref formScene,
                    "SettingsArgument.game.arguments.0", f.Shell.Renderer.Viewport);
                surface.CommitScene();
                f.Shell.Renderer.SetTextInputValue(argument.Entity, "--demo");
                Emit(f.Intents, "ui.settings.argument.add", FindByKey(f.Shell, surface.Scene!, "SettingsArgumentAdd.game.arguments").Entity);
                surface.CommitScene(); Capture("catalog-arguments");
            }, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
                surface!.CommitScene();
            }, timeout.Token);
            await SelectCategory("advanced");
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                var advancedScene = surface!.Scene!;
                ShowSettingsFixtureControl(f, controller, ref advancedScene, "AdvancedWorkspace.Palette", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("advanced-navigation");
                var delay = ShowSettingsFixtureControl(f, controller, ref advancedScene, "AdvancedWorkspace.Delay.Start", f.Shell.Renderer.Viewport);
                surface.CommitScene(); f.Shell.Renderer.Activate(delay.Entity); surface.CommitScene();
            }, timeout.Token);
            await Eventually(() => surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity) == "AdvancedWorkspace.Delay.Value"));
            await startup.InvokeAsync(() =>
            {
                Capture("advanced-probe");
                var advancedScene = surface!.Scene!;
                var read = ShowSettingsFixtureControl(f, controller, ref advancedScene, "AdvancedWorkspace.Settings.Read", f.Shell.Renderer.Viewport);
                surface.CommitScene(); f.Shell.Renderer.Activate(read.Entity); surface.CommitScene();
                ShowSettingsFixtureControl(f, controller, ref advancedScene, "AdvancedWorkspace.Settings.Row", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("advanced-settings");
            }, timeout.Token);

            await startup.InvokeAsync(() =>
            {
                var diagnosticScene = surface!.Scene!;
                ShowSettingsFixtureControl(f, controller, ref diagnosticScene, "SettingsDiagnostics.Renderer.MotionPolicy.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("renderer-diagnostics");
                ShowSettingsFixtureControl(f, controller, ref diagnosticScene, "SettingsDiagnostics.State.Next", f.Shell.Renderer.Viewport);
                ShowStateRows();
                surface.CommitScene(); Capture("state-inspector");
                var nextStatePage = ShowSettingsFixtureControl(f, controller, ref diagnosticScene, "SettingsDiagnostics.State.Next", f.Shell.Renderer.Viewport);
                surface.CommitScene(); f.Shell.Renderer.Activate(nextStatePage.Entity);
                surface.CommitScene(); ShowStateRows(); Capture("state-inspector-next");
                void ShowStateRows()
                {
                    List<string> keys = [];
                    f.Shell.Tree.Walk(controller.Page, entity =>
                    {
                        string key = f.Shell.Tree.Name(entity);
                        if (key.StartsWith("SettingsDiagnostics.State.Entry.", StringComparison.Ordinal) && key.EndsWith(".Metadata", StringComparison.Ordinal)) keys.Add(key);
                        return true;
                    });
                    AssertTrue(keys.Count > 0);
                    ShowSettingsFixtureControl(f, controller, ref diagnosticScene, keys[Math.Min(4, keys.Count - 1)], f.Shell.Renderer.Viewport);
                    surface.CommitScene();
                }
            }, timeout.Token);
            await SelectCategory("appearance");
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                var runtimeScene = surface!.Scene!;
                ShowSettingsFixtureControl(f, controller, ref runtimeScene, "SettingsRuntime.Renderer.Refresh", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("runtime-renderer");
                ShowSettingsFixtureControl(f, controller, ref runtimeScene, "SettingsRuntime.Scheduler.MotionPolicy.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("runtime-scheduler");
                ShowSettingsFixtureControl(f, controller, ref runtimeScene, "SettingsRuntime.Performance.PresentationRate.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("runtime-performance");
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "false"))).IsSuccess);
                surface.CommitScene();
            }, timeout.Token);
            await SelectCategory("about");
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() => Capture("about"), timeout.Token);
            await startup.InvokeAsync(async () =>
            {
                var queries = new XsrQueryRouterBuilder();
                f.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
                f.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
                queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
                    (q, ct) => f.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, q, cancellationToken: ct));
                queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
                    (q, ct) => f.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, q, cancellationToken: ct));
                string javaHome = Path.GetFullPath("java-preview/Temurin 21");
                string executable = Path.Combine(javaHome, "bin", "java.exe");
                var runtime = new JavaRuntimeCandidate(new JavaInstallation(javaHome, executable, null, new Version(21, 0, 8), JavaBrand.EclipseTemurin, JavaArchitecture.X64, true, true));
                var inventory = new JavaRuntimeInventorySnapshot([runtime])
                { RegistryRevision = 1, ManagedRuntimes = [new(executable, javaHome, new string('A', 64))] };
                queries.Register<JavaRuntimeInventoryQuery, JavaRuntimeInventorySnapshot>(JavaRuntimeInventoryContract.Query,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(inventory)));
                string storage = Path.GetFullPath("storage-preview"), game = Path.Combine(storage, "Games", "Minecraft");
                var library = f.Store.Resolve(Nexa.Services.Minecraft.MinecraftLibraryContract.StateKey);
                long revision = f.Store.Read<Nexa.Services.Minecraft.MinecraftLibrarySnapshot>(library).Value!.Revision;
                queries.Register<StoragePreferencesQuery, StoragePreferencesStatus>(StoragePreferencesContract.Status,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new StoragePreferencesStatus(storage, false, false))));
                queries.Register<StorageLocationsQuery, StorageLocationsSnapshot>(StorageLocationsContract.Query,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new StorageLocationsSnapshot(revision, storage,
                        Path.Combine(storage, "settings"), Path.Combine(storage, "logs"), Path.Combine(storage, "cache"),
                        [javaHome], [game, Path.Combine(storage, "Games", "Modpacks")], game, Path.Combine(game, "versions"),
                        Path.Combine(game, "versions", "1.21.1-Fabric")))));
                queries.Register<ContentStoreStatisticsQuery, ContentStoreStatistics>(ContentWorkspaceContract.Statistics,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new ContentStoreStatistics(1, 8, 1048576, 2097152, 1048576, 0))));
                queries.Register<ContentBackupListQuery, IReadOnlyList<ContentBackupManifest>>(ContentWorkspaceContract.List,
                    (_, _) => ValueTask.FromResult(XsrResult.Success<IReadOnlyList<ContentBackupManifest>>(
                        [new("preview-backup", "Survival {state literal}", DateTimeOffset.UtcNow, [])])));
                queries.Register<ContentBackupRetentionQuery, ContentBackupRetentionPolicy>(ContentWorkspaceContract.Retention,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new ContentBackupRetentionPolicy())));
                queries.Register<Nexa.Services.Logging.DurableDiagnosticHistoryQuery, IReadOnlyList<Nexa.Services.Logging.DurableDiagnosticEntry>>(
                    Nexa.Services.Logging.DiagnosticWorkspaceContract.History,
                    (_, _) => ValueTask.FromResult(XsrResult.Success<IReadOnlyList<Nexa.Services.Logging.DurableDiagnosticEntry>>([])));
                // Readonly platform observations are explicit fixtures; no real hardware or
                // Java discovery participates in this visual acceptance run.
                DateTimeOffset observedAt = DateTimeOffset.UtcNow;
                var peak = new CapabilityDefinition<long>("resource.derived.load_peak", "加载峰值", "资源", "fixture");
                var shader = new CapabilityDefinition<long>("resource.shader.estimated_gpu_memory", "光影预算", "资源", "fixture");
                var action = (CapabilityDefinition<bool>)RemediationCatalog.Definitions().Single(item => item.Id == "remediation.memory.adjust_heap");
                ICapability[] observations =
                [MachineCapabilityCatalog.Os.Observe("Windows 11", observedAt, "readonly preview fixture"),
                    InputCatalog.InputKeyboardAvailable.Unavailable(CapabilityAvailability.NotImplemented, observedAt, ""),
                    InputCatalog.InputGyroscopeDevices.Observe([new("Wireless Controller", true), new("USB Controller", false)], observedAt, "readonly preview fixture"),
                    InputCatalog.InputHapticsDevices.Unavailable(CapabilityAvailability.PermissionDenied, observedAt, "设备查询被拒绝"),
                    MachineInstanceCatalog.MinecraftSettingsResourcePacks.Observe(["file/Vanilla Tweaks.zip", "file/Faithful 32x.zip"], observedAt, "readonly preview fixture"),
                    MachineInstanceCatalog.MinecraftSettingsRenderDistance.Observe(24, observedAt, "readonly preview fixture"),
                    peak.Observe(256L * 1048576, observedAt, "readonly preview fixture"),
                    shader.Observe(512L * 1048576, observedAt, "readonly preview fixture"), action.Observe(false, observedAt, "readonly preview fixture")];
                var projection = new ResourceEstimatorProjection().Project(observations.ToDictionary(item => item.Id), observedAt,
                    new("/fixture/platform", "preview"));
                var platform = new MachineCapabilitySnapshot(1, observedAt, observations.Concat(projection));
                var issue = new CapabilityPreflightIssue("MEM_HEAP_LAUNCH_LOW", PreflightSeverity.Warning, "memory", PreflightCertainty.Estimated,
                    false, ["readonly preview fixture"], remediations: [action.Id]);
                var preflight = new CapabilityPreflightReport([issue], [issue], PreflightSeverity.Warning);
                queries.Register<MachineCapabilityQuery, MachineCapabilitySnapshot>(MachineCapabilityStateContract.SnapshotQuery,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(platform)));
                queries.Register<LaunchPreflightQuery, CapabilityPreflightReport>(MachineCapabilityStateContract.PreflightQuery,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(preflight)));
                queries.Register<RuntimeTraceQuery, RuntimeTraceSnapshot>(RuntimeTraceContract.Read,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new RuntimeTraceSnapshot(256, 4,
                        Enumerable.Range(0, 48).Select(index => new RuntimeTraceEntry("Operation", "minecraft.preview." + index,
                            "preview-correlation", index, "completed {state literal}", true)).ToArray()))));
                var commands = new XsrCommandRouterBuilder();
                commands.Register<JavaRuntimeManageCommand>(JavaRuntimeInventoryContract.Manage,
                    (_, _) => throw new InvalidOperationException("Preview must not manage a Java runtime."));
                using var java = new SettingsPageController(f.Shell, f.Intents, queries.Build(new NoopDispatchObserver()),
                    commands.Build(new NoopDispatchObserver()), f.Store, f.Feedback)
                { PickRemediationJava = () => Task.FromResult<string?>(null) };
                XsrUiEntityId OwnedSection(string section)
                {
                    XsrUiEntityId found = default;
                    f.Shell.Tree.Walk(java.Page, entity =>
                    { if (f.Shell.Tree.Name(entity) == "SettingsNav." + section) found = entity; return true; });
                    AssertTrue(found.IsAssigned); return found;
                }
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("diagnostics.ai.enabled", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
                java.ConfigureContentWorkspace(_ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null), static () => "");
                java.ConfigureDiagnosticAi(true);
                await java.PrepareStartupAsync(timeout.Token);
                f.Shell.Stage.Navigation.Replace(java.Page); surface!.CommitScene();
                Emit(f.Intents, "ui.settings.section", OwnedSection("java"));
                surface.CommitScene(); await java.PrepareVisibleStartupAsync(timeout.Token);
                await Task.Delay(500, timeout.Token); surface.CommitScene();
                var javaScene = surface.Scene!;
                ShowSettingsFixtureControl(f, java, ref javaScene, "SettingsJavaChoose.0", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("java-inventory");
                f.Shell.Tree.Walk(java.Page, entity =>
                {
                    string key = f.Shell.Tree.Name(entity);
                    AssertFalse(key.StartsWith("JavaDiagnostics", StringComparison.Ordinal) || key.StartsWith("JavaManual", StringComparison.Ordinal)
                        || key.StartsWith("JavaPolicy", StringComparison.Ordinal));
                    return true;
                });
                var originalLibrary = f.Store.Read<Nexa.Services.Minecraft.MinecraftLibrarySnapshot>(library).Value!;
                var previewInstance = Instance("platform-preview");
                f.Store.Publish(library, originalLibrary with
                {
                    Revision = originalLibrary.Revision + 1,
                    Instances = [previewInstance],
                    SelectedInstanceId = previewInstance.Id,
                    IsProvisional = false
                });
                Emit(f.Intents, "ui.settings.section", OwnedSection("platform"));
                surface.CommitScene(); await java.PrepareVisibleStartupAsync(timeout.Token);
                AssertEqual("platform", java.SelectedSection);
                await Task.Delay(500, timeout.Token);
                surface.CommitScene(); Capture("platform-preflight");
                var platformScene = surface.Scene!;
                ShowSettingsFixtureControl(f, java, ref platformScene, "HardwareAdvice.Risk.Facts", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("platform-advice");
                ShowSettingsFixtureControl(f, java, ref platformScene, "PlatformCapability.input.gyroscope.devices", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("platform-devices");
                ShowSettingsFixtureControl(f, java, ref platformScene, "PlatformCapability.minecraft.settings.resource_packs", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("platform-resource-packs");
                f.Store.Publish(library, originalLibrary);
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
                surface.CommitScene();
                Emit(f.Intents, "ui.settings.section", OwnedSection("privacy"));
                surface.CommitScene(); await java.PrepareVisibleStartupAsync(timeout.Token);
                await Task.Delay(500, timeout.Token);
                var traceScene = surface.Scene!;
                var captureTrace = ShowSettingsFixtureControl(f, java, ref traceScene, "RuntimeTrace.Capture", f.Shell.Renderer.Viewport);
                surface.CommitScene(); f.Shell.Renderer.Activate(captureTrace.Entity); surface.CommitScene(); surface.CommitScene();
                ShowSettingsFixtureControl(f, java, ref traceScene, "RuntimeTrace.Detail.1", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("runtime-trace");
                var nextTrace = ShowSettingsFixtureControl(f, java, ref traceScene, "RuntimeTrace.Next", f.Shell.Renderer.Viewport);
                surface.CommitScene(); f.Shell.Renderer.Activate(nextTrace.Entity); surface.CommitScene();
                ShowSettingsFixtureControl(f, java, ref traceScene, "RuntimeTrace.Summary", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("runtime-trace-next");
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "false"))).IsSuccess);
                surface.CommitScene();
                Emit(f.Intents, "ui.settings.section", OwnedSection("storage"));
                surface.CommitScene(); await java.PrepareVisibleStartupAsync(timeout.Token);
                await Task.Delay(500, timeout.Token); surface.CommitScene(); Capture("storage-top");
                var storageScene = surface.Scene!;
                ShowSettingsFixtureControl(f, java, ref storageScene, "SettingsStoragePath.SelectedInstance", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("storage-locations");
                ShowSettingsFixtureControl(f, java, ref storageScene, "SettingsReset", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("storage-transfer");
                ShowSettingsFixtureControl(f, java, ref storageScene, "ContentWorkspace.Refresh", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("storage-backups");
                ShowSettingsFixtureControl(f, java, ref storageScene, "ContentWorkspace.Raw", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("storage-diagnostics");
                ShowSettingsFixtureControl(f, java, ref storageScene, "DiagnosticAi.endpoint", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("storage-ai");
                ShowSettingsFixtureControl(f, java, ref storageScene, "ContentWorkspace.AiFacts", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("storage-ai-actions");
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("diagnostics.ai.enabled", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "false"))).IsSuccess);
                f.Shell.Stage.Navigation.Replace(controller.Page); surface.CommitScene();
            }, timeout.Token);
            await startup.InvokeAsync(async () =>
            {
                // These are explicit readonly instance facts, not measurements of a game
                // launched by this visual check. The actual product controller and native
                // viewport own capture, scrolling, tabs and both kinds of pagination.
                string instance = Path.Combine(f.TemporaryDirectory, "minecraft", "versions", "instance-preview");
                EnableInstanceDevelopmentMode(f, instance);
                var queries = new XsrQueryRouterBuilder();
                f.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
                f.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
                queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
                    (q, ct) => f.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, q, cancellationToken: ct));
                queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
                    (q, ct) => f.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, q, cancellationToken: ct));
                queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (q, _) =>
                    ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory, "1.21.1", [],
                        [new("overview", "总览"), new("game", "游戏设置"), new("diagnostics", "实例诊断")], true, ""))));
                DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-2); Guid sessionId = Guid.NewGuid();
                var session = new MinecraftProcessSnapshot(sessionId, "Instance preview", 42, MinecraftProcessState.Exited, 0, started, started.AddMinutes(1))
                { InstanceDirectory = instance, GameWindowConfirmed = true };
                var sessionsId = f.Store.Resolve(MinecraftProcessStateComposition.SessionsKey);
                var originalSessions = f.Store.ReadCollection<MinecraftProcessSnapshot>(sessionsId);
                f.Store.PublishDelta(sessionsId, new XsrCollectionDelta<MinecraftProcessSnapshot, Guid>(originalSessions.Revision, [session], []));
                var observation = new JvmHostObservation(sessionId, "Instance preview", started, started.AddMinutes(1), 812, 786432000, 810549248, 32, 4500,
                    0, 0, 0, 0, 0, 0, 0, null, null, 0, [], [])
                {
                    CoreMetricsObserved = true,
                    CpuPercentObserved = true,
                    CpuPeakPercent = 45,
                    RuntimePhysicalP95Bytes = 734003200,
                    MeasuredHeapPeakBytes = null,
                    MeasuredNativePeakBytes = null,
                    LaunchWindowMilliseconds = 30000,
                    SystemEventsObserved = true,
                    SystemEvents = ["Readonly preview event; correlation does not establish a cause."]
                };
                var observationsId = f.Store.Resolve(JvmHostStateContract.ObservationsKey);
                var originalObservations = f.Store.ReadCollection<JvmHostObservation>(observationsId);
                f.Store.PublishDelta(observationsId, new XsrCollectionDelta<JvmHostObservation, Guid>(originalObservations.Revision, [observation], []));
                var context = new DiagnosticInstanceContext(new string('A', 64), sessionId)
                { StartedAt = started, EndedAt = started.AddMinutes(1), ExitCode = 0, LaunchDurationMilliseconds = 812 };
                var entries = Enumerable.Range(0, 15).Select(index => new DurableDiagnosticEntry(started.AddSeconds(index), "Process",
                    "GameLaunch", "preview-stage-" + index, DiagnosticOperationOutcome.Entered)
                { Instance = context }).ToArray();
                queries.Register<DurableInstanceDiagnosticHistoryQuery, IReadOnlyList<DurableDiagnosticEntry>>(DiagnosticWorkspaceContract.InstanceHistory,
                    (_, _) => ValueTask.FromResult(XsrResult.Success<IReadOnlyList<DurableDiagnosticEntry>>(entries)));
                queries.Register<MinecraftLaunchPlanDiagnosticQuery, MinecraftLaunchPlanDiagnosticSnapshot>(MinecraftLaunchPlanDiagnosticContract.Query,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new MinecraftLaunchPlanDiagnosticSnapshot(instance, started,
                        "Java/Temurin 21/bin/java", instance, "preview.Main", "Fabric", 4096, false,
                        ["-Xmx4096M", "preview.Main", "--accessToken", "<redacted>"], ["libraries/preview-library.jar"], [], ["PATH", "JAVA_HOME"], false))));
                queries.Register<InstanceLocalDocumentsQuery, InstanceLocalDocumentsSnapshot>(InstanceLocalDocumentsContract.Query,
                    (_, _) => ValueTask.FromResult(XsrResult.Success(new InstanceLocalDocumentsSnapshot(instance, started,
                        [new("manifest", "versions/instance-preview/instance-preview.json", "read",
                            Enumerable.Range(0, 70).Select(index => "preview manifest line " + index + " {data literal}").ToArray(), false)], true))));
                using var diagnostics = new SettingsPageController(f.Shell, f.Intents, queries.Build(new NoopDispatchObserver()),
                    f.Foundation.Commands, f.Store, f.Feedback, () => instance);
                await diagnostics.PrepareStartupAsync(timeout.Token);
                f.Shell.Stage.Navigation.Replace(diagnostics.Page); surface!.CommitScene();
                await diagnostics.PrepareVisibleStartupAsync(timeout.Token); surface.CommitScene();
                XsrUiEntityId navigation = default;
                f.Shell.Tree.Walk(diagnostics.Page, entity =>
                { if (f.Shell.Tree.Name(entity) == "SettingsNav.diagnostics") navigation = entity; return true; });
                AssertTrue(navigation.IsAssigned);
                Emit(f.Intents, "ui.settings.section", navigation); surface.CommitScene();
                await diagnostics.PrepareVisibleStartupAsync(timeout.Token); await Task.Delay(500, timeout.Token);
                surface.CommitScene();
                AssertEqual("diagnostics", diagnostics.SelectedSection);
                Capture("instance-entry");
                var diagnosticScene = surface.Scene!;
                ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "InstanceDiagnostics.Session.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("instance-status");
                ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "InstanceDiagnostics.Heap.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("instance-resources");
                ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "InstanceDiagnostics.HistorySession.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("instance-history");
                var mode = ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "InstanceDiagnostics.HistoryMode", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.Activate(mode.Entity); surface.CommitScene();
                ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "InstanceDiagnostics.OperationStage.Value", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("instance-operations");
                var next = ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "InstanceDiagnostics.OperationHistory.Next", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.Activate(next.Entity); surface.CommitScene(); Capture("instance-operations-next");
                var read = ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "AdvancedLaunch.Read", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.Activate(read.Entity); surface.CommitScene(); surface.CommitScene();
                ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "AdvancedLaunch.Line.Arguments", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.PointerScroll(new(600, 400), 112); surface.CommitScene(); Capture("instance-launch-plan");
                var manifest = ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "AdvancedLaunch.Tab.manifest", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.Activate(manifest.Entity); surface.CommitScene(); Capture("instance-manifest");
                var factsNext = ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "AdvancedLaunch.Next", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.Activate(factsNext.Entity); surface.CommitScene(); Capture("instance-manifest-next");
                window!.Width = 820; window.UpdateLayout(); surface.CommitScene();
                await Task.Delay(200, timeout.Token);
                var planTab = ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "AdvancedLaunch.Tab.plan", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.Activate(planTab.Entity); surface.CommitScene();
                ShowSettingsFixtureControl(f, diagnostics, ref diagnosticScene, "AdvancedLaunch.Line.Arguments", f.Shell.Renderer.Viewport);
                f.Shell.Renderer.PointerScroll(new(500, 400), 112); surface.CommitScene(); Capture("instance-narrow-plan");
                window.Width = 1040; window.UpdateLayout(); surface.CommitScene();

                f.Store.PublishDelta(sessionsId, new XsrCollectionDelta<MinecraftProcessSnapshot, Guid>(
                    f.Store.ReadCollection<MinecraftProcessSnapshot>(sessionsId).Revision, [], [sessionId]));
                f.Store.PublishDelta(observationsId, new XsrCollectionDelta<JvmHostObservation, Guid>(
                    f.Store.ReadCollection<JvmHostObservation>(observationsId).Revision, [], [sessionId]));
                f.Shell.Stage.Navigation.Replace(controller.Page); surface.CommitScene();
            }, timeout.Token);
            await SelectCategory("network");
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() => Capture("network-top"), timeout.Token);
            for (int step = 0; step < 128; step++)
            {
                bool ready = false;
                await startup.InvokeAsync(() =>
                {
                    surface!.CommitScene();
                    var body = CurrentBody();
                    ready = surface.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity) == "SettingsProxyMode.2"
                        && node.Rect.Y >= body.Rect.Y && node.Rect.Y + node.Rect.Height <= body.Rect.Y + body.Rect.Height - 220);
                    if (!ready)
                    {
                        f.Shell.Renderer.PointerScroll(new(body.Rect.X + 30, body.Rect.Y + 30), 80);
                        surface.CommitScene();
                    }
                }, timeout.Token);
                if (ready) break;
                if (step == 127) throw new InvalidOperationException("Proxy controls did not enter the viewport.");
                await Task.Delay(20, timeout.Token);
            }
            await startup.InvokeAsync(() =>
            {
                Capture("network-proxy");
                Emit(f.Intents, "ui.settings.proxy.mode", FindByKey(f.Shell, surface!.Scene!, "SettingsProxyMode.2").Entity);
                surface.CommitScene(); Capture("network-manual");
            }, timeout.Token);
            await Task.Delay(250, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                var queries = new Nexa.Xsr.Runtime.XsrQueryRouterBuilder();
                queries.Register<Nexa.Services.Resources.ResourceSearchQuery, Nexa.Services.Resources.ResourceSearchResult>(
                    Nexa.Services.Resources.ResourceCatalogContract.Search, (query, _) => new(Nexa.Xsr.XsrResult.Success(
                        new Nexa.Services.Resources.ResourceSearchResult(ContinuousProjects("Resource", 20), 20, query.Page) { HasMore = false })));
                resources = new(f.Shell, f.Intents, queries.Build(new NoopDispatchObserver()), f.Store, _ => { });
                f.Controller.ResourcesPage = resources.Page;
                Emit(f.Intents, "ui.navigation.community");
                surface!.CommitScene(); surface.CommitScene();
            }, timeout.Token);
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                Capture("resources");
                Emit(f.Intents, "ui.resources.action", resources!.Find("ResourceLoader"));
                surface!.CommitScene(); Capture("resources-loader");
                Emit(f.Intents, "ui.navigation.launch"); surface.CommitScene();
                f.Shell.Renderer.Activate(FindByKey(f.Shell, surface.Scene!, "InstanceListButton").Entity);
                surface.CommitScene();
                f.Shell.Renderer.Activate(FindByKey(f.Shell, surface.Scene!, "LibraryChooseDirectory").Entity);
                surface.CommitScene();
                var rename = surface.Scene!.Nodes.FirstOrDefault(node => f.Shell.Tree.Name(node.Entity).StartsWith("LibraryDirectoryRename:directory:", StringComparison.Ordinal));
                if (rename.Entity.IsAssigned) { f.Shell.Renderer.Activate(rename.Entity); surface.CommitScene(); }
            }, timeout.Token);
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                Capture("directories");
                f.Shell.Stage.Navigation.Replace(controller.Page); surface!.CommitScene();
            }, timeout.Token);
            DesktopTaskBubblePresenter? taskBubble = null;
            TaskCenterController? taskPage = null;
            using var taskRuntime = Nexa.Services.Composition.TaskCenterRuntimeComposer.Compose(f.Foundation.Host);
            using var previewTask = f.Foundation.Host.Tasks.Begin(new("preview:nxml", "安装 Minecraft", ["版本信息", "下载文件", "校验"]));
            using var completedTask = f.Foundation.Host.Tasks.Begin(new("preview:finished", "更新资源包", ["下载", "校验"]));
            previewTask.Report("下载文件", "游戏资源", 0.45, 9, 20, 1048576); completedTask.Complete("更新完成");
            await startup.InvokeAsync(() =>
            {
                taskBubble = new(f.Shell, f.Store);
                taskPage = new(f.Shell, f.Intents, taskRuntime.Commands, f.Store, taskBubble);
                Emit(f.Intents, "ui.tasks.open"); surface!.CommitScene();
                var details = surface.Scene!.Nodes.First(node => f.Shell.Tree.Name(node.Entity) == "TaskCardDetails");
                f.Shell.Renderer.Activate(details.Entity); surface.CommitScene();
            }, timeout.Token);
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                Capture("tasks");
                f.Shell.Stage.Navigation.Replace(controller.Page);
                taskPage!.Dispose(); taskBubble!.SetPageVisible(false); surface!.CommitScene();
            }, timeout.Token);
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() => { Capture("dock"); taskBubble!.Dispose(); }, timeout.Token);
            Guid logSession = Guid.NewGuid();
            using var logRuntime = ProcessLogRuntime((query, _) => ValueTask.FromResult(Nexa.Xsr.XsrResult.Success(
                new Nexa.Services.Minecraft.Process.MinecraftProcessOutputSnapshot(query.SessionId, "Minecraft 1.21.1",
                    Nexa.Services.Minecraft.Process.MinecraftProcessState.Running, 4, 0,
                    [new(1, Nexa.Services.Minecraft.Process.MinecraftProcessOutputChannel.Stdout, "Starting Minecraft 1.21.1"),
                     new(2, Nexa.Services.Minecraft.Process.MinecraftProcessOutputChannel.Stdout, "Loading resources…"),
                     new(3, Nexa.Services.Minecraft.Process.MinecraftProcessOutputChannel.Stderr, "[Example] fixture warning; {state literal}"),
                     new(4, Nexa.Services.Minecraft.Process.MinecraftProcessOutputChannel.Stdout, "Ready")]))));
            ProcessLogPageController? logs = null;
            await startup.InvokeAsync(() =>
            {
                PublishLogSessions(f, new Nexa.Services.Minecraft.Process.MinecraftProcessSnapshot(logSession, "Minecraft 1.21.1", 123, Nexa.Services.Minecraft.Process.MinecraftProcessState.Running,
                    null, DateTimeOffset.UtcNow, null));
                surface!.CommitScene();
            }, timeout.Token);
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                Capture("process-dock");
                logs = new(f.Shell, f.Intents, logRuntime.Queries, f.Store, f.Feedback, _ => Task.CompletedTask);
                f.Shell.Stage.Navigation.Replace(logs.Page); logs.Open(logSession);
            }, timeout.Token);
            await logs!.WaitUntilIdle();
            await Task.Delay(500, timeout.Token);
            await startup.InvokeAsync(() =>
            {
                surface!.CommitScene();
                var row = FindByKey(f.Shell, surface.Scene!, "ProcessLogEntry:3").Entity;
                Emit(f.Intents, "ui.process.log.select", row); surface.CommitScene();
                Capture("process-logs");
                f.Shell.Stage.Navigation.Replace(controller.Page); logs.Dispose();
                PublishLogSessions(f); surface.CommitScene();
            }, timeout.Token);
            await SelectCategory("general");
            await Task.Delay(600, timeout.Token);
            await SelectCategory("appearance");
            await Task.Delay(1000, timeout.Token);
            outcome = 0;

            XsrUiSceneNode CurrentBody()
            {
                XsrUiEntityId pager = default;
                f.Shell.Tree.Walk(controller.Page, entity => { if (f.Shell.Tree.Name(entity) == "SettingsPager") pager = entity; return true; });
                var pages = f.Shell.Tree.Children(pager);
                var current = pages[f.Shell.Tree.GetComponent<XsrUiPager>(pager)!.PageIndex];
                return surface!.Scene!.Nodes.Single(node => node.Entity == current);
            }

            async Task SelectCategory(string id)
            {
                await startup.InvokeAsync(() =>
                {
                    surface!.CommitScene();
                    Emit(f.Intents, "ui.settings.section", FindByKey(f.Shell, surface!.Scene!, "SettingsNav." + id).Entity);
                    surface!.CommitScene();
                }, timeout.Token);
                await Eventually(() => controller.SelectedSection == id);
            }

            async Task Eventually(Func<bool> condition)
            {
                while (true)
                {
                    bool ready = false;
                    await startup.InvokeAsync(() => { surface?.CommitScene(); ready = condition(); }, timeout.Token);
                    if (ready) return;
                    await Task.Delay(10, timeout.Token);
                }
            }

            void Capture(string phase)
            {
                window!.UpdateLayout(); surface!.CommitScene();
                double scale = window.RenderScaling;
                using var frame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale),
                    (int)Math.Ceiling(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                frame.Render(window);
                string stem = "settings-" + theme + "-" + phase;
                frame.Save(Path.Combine(outputDirectory, stem + ".png"), PngBitmapEncoderOptions.Default);
                var controls = new JsonArray();
                foreach (var node in surface!.Scene!.Nodes.Where(node => node.IsClickable
                    || f.Shell.Tree.Name(node.Entity) == "SettingsSections"))
                {
                    PixelPoint point = surface.PointToScreen(new(node.Rect.X + node.Rect.Width / 2,
                        node.Rect.Y + node.Rect.Height / 2));
                    JsonNode control = new JsonObject
                    {
                        ["key"] = f.Shell.Tree.Name(node.Entity),
                        ["label"] = node.Label,
                        ["x"] = point.X,
                        ["y"] = point.Y,
                        ["visible"] = node.ClipRect is null
                            || node.ClipRect.Value.Contains(new(node.Rect.X + node.Rect.Width / 2, node.Rect.Y + node.Rect.Height / 2))
                    };
                    controls.Add(control);
                }
                PixelPoint origin = window.PointToScreen(default);
                var metadata = new JsonObject
                {
                    ["page"] = "SettingsAppearance",
                    ["phase"] = phase,
                    ["theme"] = f.Shell.Renderer.ColorScheme.IsDark ? "dark" : "light",
                    ["data"] = "temporary local settings; empty instances; Java inventory and storage locations, task/process/log, Trace, instance diagnostics and resource fixtures; runtime metrics and probe are actual native read-only captures",
                    ["renderer"] = "actual product NXML/controller and software Avalonia native window",
                    ["version"] = f.Shell.Version,
                    ["x"] = origin.X,
                    ["y"] = origin.Y,
                    ["width"] = frame.PixelSize.Width,
                    ["height"] = frame.PixelSize.Height,
                    ["controls"] = controls
                };
                File.WriteAllText(Path.Combine(outputDirectory, stem + ".json"), metadata.ToJsonString());
                File.WriteAllText(Path.Combine(outputDirectory, stem + ".ready"), stem);
                Console.WriteLine("PREVIEW READY: " + stem);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            Console.Error.WriteLine("FAIL: native settings preview\n" + error);
        }
        finally
        {
            void Cleanup()
            {
                presentation?.Dispose(); appearance?.Dispose(); resources?.Dispose(); settings?.Dispose();
                if (window is not null) { fixture!.Shell.Renderer.ReducedMotion = true; window.Close(); }
                fixture?.Dispose();
            }
            if (!startup.Completion.IsCompleted) await Dispatcher.UIThread.InvokeAsync(Cleanup);
            else Cleanup();
            if (lifetime is not null) AssertEqual(0, await lifetime.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        if (outcome == 0) Console.WriteLine("PASS: native settings previews capture actual appearance/motion settings and confirmed theme/category transitions.");
        return outcome;
    }
}
