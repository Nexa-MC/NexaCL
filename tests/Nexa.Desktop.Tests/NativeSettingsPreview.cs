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
                var commands = new XsrCommandRouterBuilder();
                commands.Register<JavaRuntimeManageCommand>(JavaRuntimeInventoryContract.Manage,
                    (_, _) => throw new InvalidOperationException("Preview must not manage a Java runtime."));
                using var java = new SettingsPageController(f.Shell, f.Intents, queries.Build(new NoopDispatchObserver()),
                    commands.Build(new NoopDispatchObserver()), f.Store, f.Feedback)
                { PickRemediationJava = () => Task.FromResult<string?>(null) };
                await java.PrepareStartupAsync(timeout.Token);
                f.Shell.Stage.Navigation.Replace(java.Page); surface!.CommitScene();
                Emit(f.Intents, "ui.settings.section", FindByKey(f.Shell, surface.Scene!, "SettingsNav.java").Entity);
                surface.CommitScene(); await java.PrepareVisibleStartupAsync(timeout.Token);
                await Task.Delay(500, timeout.Token); surface.CommitScene();
                var javaScene = surface.Scene!;
                ShowSettingsFixtureControl(f, java, ref javaScene, "SettingsJavaChoose.0", f.Shell.Renderer.Viewport);
                surface.CommitScene(); Capture("java-inventory");
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
                    ["data"] = "temporary local settings; empty instances; Java inventory, task/process/log and resource fixtures",
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
