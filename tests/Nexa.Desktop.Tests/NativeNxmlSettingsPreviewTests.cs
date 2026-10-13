using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Core.Media;
using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

// A separate native process uses real product controllers and the software Avalonia
// window. Management and install metadata are controlled typed fixtures, never a game.
internal static partial class Program
{
    private static int RunNativeNxmlSettingsPreview(string theme, string outputDirectory) =>
        RunNativeNxmlSettingsPreviewAsync(theme, Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();

    private static async Task<int> RunNativeNxmlSettingsPreviewAsync(string theme, string outputDirectory)
    {
        if (theme is not ("light" or "dark")) throw new ArgumentException("Expected light or dark.", nameof(theme));
        Directory.CreateDirectory(outputDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], disableHardwareAcceleration: true,
            new AvaloniaUiStartupAppearance(theme == "dark" ? XsrUiThemeMode.Dark : XsrUiThemeMode.Light, ReducedMotion: true),
            cancellationToken: timeout.Token);
        NativeNxmlSettingsHarness? harness = null;
        DesktopAppearanceSession? appearance = null;
        AvaloniaUiShellWindow? window = null;
        AvaloniaUiSceneSurface? surface = null;
        Task<int>? lifetime = null;
        var actions = new AvaloniaUiPlatformActions();
        actions.SetDesktopPolicy(new(TrayEnabled: false));
        int outcome = 1;
        try
        {
            await startup.InvokeAsync(() =>
            {
                harness = new();
                var configured = harness;
                AssertTrue(configured.Fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
                AssertTrue(configured.Fixture.Foundation.Host.SettingsPolicy.Set(new("appearance.theme-mode", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, theme == "dark" ? "1" : "0"))).IsSuccess);
                AssertTrue(configured.Fixture.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
                EnableInstanceDevelopmentMode(configured.Fixture, configured.InstanceDirectory);
                appearance = new(configured.Fixture.Shell, configured.Fixture.Store, actions);
                configured.Fixture.Shell.Stage.Navigation.Replace(configured.Global.Page);
                configured.Fixture.Shell.Render(new(1040, 740));
            }, timeout.Token);
            var h = harness!;
            var f = h.Fixture;
            await startup.PrepareShellAsync(f.Shell, actions, timeout.Token);
            await startup.WarmUpShellAsync(f.Shell, timeout.Token);
            lifetime = Task.Run(() => AvaloniaUiShellHost.Run(f.Shell, platformActions: actions));
            await Eventually(() =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not AvaloniaUiShellWindow candidate || !candidate.IsVisible) return false;
                window = candidate; surface = candidate.GetVisualDescendants().OfType<AvaloniaUiSceneSurface>().Single();
                candidate.Width = 1040; candidate.Height = 740; candidate.Position = new(20, 20);
                candidate.UpdateLayout(); surface.CommitScene();
                return Has("SettingsNav.game") && f.Shell.Renderer.ColorScheme.IsDark == (theme == "dark");
            });

            await Select(h.Global, "game");
            await Show("SettingsArgument.game.arguments.0");
            var argument = await Draft("SettingsArgument.game.arguments.0", "--demo literal <argument> {state private}");
            await Activate("SettingsArgumentAdd.game.arguments");
            await Eventually(() => Has("SettingsArgument.game.arguments.1"));
            argument = await Draft("SettingsArgument.game.arguments.0", "--demo literal <argument> {state private}");
            await startup.InvokeAsync(() =>
            {
                AssertEqual(argument, f.Shell.Renderer.Focused);
                AssertEqual("--demo literal <argument> {state private}", f.Shell.Tree.GetComponent<XsrUiTextInput>(argument)!.ReadDraft());
                AssertFalse(h.Global.SettingsWritePending);
            }, timeout.Token);
            await CapturePair("global-catalog-editor", argument);

            await Select(h.Global, "network");
            await Activate("SettingsProxyMode.2");
            await Show("SettingsInput.network.proxy-address");
            var proxy = await Draft("SettingsInput.network.proxy-address", "http://fixture.invalid:8181");
            await startup.InvokeAsync(() =>
            {
                var password = FindNativeNxmlSettingsTree(h.Global.Page, "SettingsInput.network.proxy-password", f.Shell.Tree);
                AssertTrue(f.Shell.Tree.GetComponent<XsrUiTextInput>(password)!.IsPassword);
                f.Shell.Renderer.SetTextInputValue(password, "fixture-password"); surface!.CommitScene();
                AssertFalse(surface.Scene!.Nodes.Any(node => node.Text?.Contains("fixture-password", StringComparison.Ordinal) == true));
            }, timeout.Token);
            await CapturePair("global-proxy", proxy);

            await Select(h.Global, "advanced");
            await Show("AdvancedWorkspace.Navigation");
            await Show("AdvancedWorkspace.Capture");
            await Activate("AdvancedWorkspace.Capture");
            await CapturePair("global-custom-workspace");

            await startup.InvokeAsync(() => { f.Shell.Stage.Navigation.Replace(h.Instance.Page); surface!.CommitScene(); }, timeout.Token);
            await Eventually(() => Has("SettingsNav.mods"));
            await Select(h.Instance, "mods");
            await Eventually(() => surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity).Equals("ManagementContentRow", StringComparison.Ordinal)));
            await startup.InvokeAsync(() =>
            {
                var list = FindNativeNxmlSettingsTree(h.Instance.Page, "ManagementContentList", f.Shell.Tree);
                AssertTrue(f.Shell.Tree.GetComponent<NxmlDynamicSlot>(list) is not null);
                var sections = Sections();
                AssertTrue(f.Shell.Renderer.PointerScroll(new(sections.Rect.X + 24, sections.Rect.Y + 32), 320));
                surface!.CommitScene(); AssertTrue(Sections().Scroll!.Value.OffsetY > 0);
            }, timeout.Token);
            await CapturePair("instance-content-scrolled");
            await Top();
            var search = await Draft("ManagementContentSearch", "Fixture");
            await CapturePair("instance-content-search", search);

            await Select(h.Instance, "screenshots");
            await Eventually(() => surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity).StartsWith("ManagementScreenshot.", StringComparison.Ordinal)));
            await startup.InvokeAsync(() =>
            {
                var screenshot = surface!.Scene!.Nodes.First(node => f.Shell.Tree.Name(node.Entity).StartsWith("ManagementScreenshot.", StringComparison.Ordinal) && Visible(node));
                AssertTrue(f.Shell.Renderer.Activate(screenshot.Entity)); surface.CommitScene();
            }, timeout.Token);
            await Eventually(() => Has("ScreenshotPreviewImage") && Has("ScreenshotPreview.Crop"));
            await startup.InvokeAsync(() => AssertTrue(f.Shell.Tree.GetComponent<XsrUiImage>(Find("ScreenshotPreviewImage").Entity)!.Raster is not null), timeout.Token);
            await CapturePair("instance-screenshot-modal");
            await Activate("ScreenshotPreview.Crop");
            await Eventually(() => Has("ScreenshotCropWidth") && Has("ScreenshotPreview.SaveCrop"));
            var crop = await Draft("ScreenshotCropWidth", "480");
            await CapturePair("instance-screenshot-crop", crop);
            await Activate("ScreenshotPreview.Close");
            await Eventually(() => !Has("ScreenshotPreviewImage"));
            await startup.InvokeAsync(() => AssertEqual(0, h.CropWrites), timeout.Token);

            await Select(h.Instance, "servers");
            await Eventually(() => Has("Management.编辑"));
            await Activate("Management.编辑");
            await Show("ServerName");
            var serverName = await Draft("ServerName", "Draft <server> {state private}");
            await Activate("Management.检查状态");
            await Eventually(() => h.StatusReads == 1 && surface!.Scene!.Nodes.Any(node => node.Text?.Contains("fixture server", StringComparison.Ordinal) == true));
            await startup.InvokeAsync(() =>
            {
                AssertEqual(serverName, Find("ServerName").Entity); AssertEqual(serverName, f.Shell.Renderer.Focused);
                AssertOwned("ServersWorkspace"); AssertOwned("ServerEntry.0");
            }, timeout.Token);
            await Show("ServerName");
            await CapturePair("instance-server-editor", serverName);

            await Select(h.Instance, "saves");
            await Eventually(() => Has("ManagementContentDetails.FixtureWorld"));
            await Activate("ManagementContentDetails.FixtureWorld");
            await Eventually(() => Has("WorldDetails"));
            await Activate("Management.检查世界健康");
            await Eventually(() => h.HealthReads == 1 && surface!.Scene!.Nodes.Any(node => node.Text == "已完成检查，未发现结构损坏。"));
            await Activate("Management.读取快照历史");
            await Eventually(() => h.SnapshotReads == 1 && surface!.Scene!.Nodes.Any(node => node.Text?.Contains("7 个文件", StringComparison.Ordinal) == true));
            await Show("WorldSnapshots");
            await startup.InvokeAsync(() => { AssertOwned("WorldDetails"); AssertOwned("WorldSnapshots"); }, timeout.Token);
            await CapturePair("instance-world-snapshots");

            await Select(h.Instance, "recovery");
            await Show("RecoveryChanges.Next");
            await Show("RecoveryChanges");
            await startup.InvokeAsync(() => AssertOwned("RecoveryWorkspace"), timeout.Token);
            await CapturePair("instance-recovery");
            await Activate("RecoveryChanges.Next");
            await Eventually(() => surface!.Scene!.Nodes.Any(node => node.Text?.Contains("fixture-change-08", StringComparison.Ordinal) == true));
            await CapturePair("instance-recovery-next");

            await Select(h.Instance, "modpack");
            await Show("ExportCategoryChoice.saves");
            await Show("ExportName");
            var exportName = await Draft("ExportName", "Draft <pack> {state private}");
            await startup.InvokeAsync(() =>
            {
                f.Shell.Renderer.SetTextInputValue(FindNativeNxmlSettingsTree(h.Instance.Page, "ExportVersion", f.Shell.Tree), "2.3");
                f.Shell.Renderer.SetTextInputValue(FindNativeNxmlSettingsTree(h.Instance.Page, "ExportPath", f.Shell.Tree), Path.Combine(h.InstanceDirectory, "fixture.mrpack"));
            }, timeout.Token);
            await Activate("ExportCategoryChoice.saves");
            await Eventually(() => Find("ExportCategoryChoice.saves").IsChecked == true);
            await startup.InvokeAsync(() =>
            {
                AssertEqual(exportName, Find("ExportName").Entity); AssertEqual(exportName, f.Shell.Renderer.Focused);
                AssertOwned("ExportWorkspace"); AssertEqual(0, h.ExportWrites); AssertEqual(0, h.ServerWrites);
            }, timeout.Token);
            await Show("ExportName");
            await CapturePair("instance-export-editor", exportName);

            await NavigateLaunch("ui.navigation.launch", "LaunchPage");
            await Eventually(() => Has("AccountName") && Find("AccountName").Text == "Player");
            await CapturePair("launch-home");

            await NavigateLaunch("ui.navigation.download", "InstallPage");
            await Activate("InstallJavaChoice");
            await WaitForLaunchIdle();
            await Eventually(() => Has("CatalogRow:game:1.21.1"));
            await Activate("CatalogRow:game:1.21.1");
            await WaitForLaunchIdle();
            await Eventually(() => Has("JavaFabricTab"));
            await Activate("JavaFabricTab");
            await WaitForLaunchIdle();
            await Eventually(() => Has("CatalogRow:loader:0.16.10"));
            await Activate("CatalogRow:loader:0.16.10");
            await Eventually(() => Find("CatalogRow:loader:0.16.10").IsSelected);
            var installName = await Draft("JavaInstallVersionInput", "Preview <install> {state private}");
            await CapturePair("java-install", installName);

            await NavigateLaunch("ui.navigation.download", "InstallPage");
            await Activate("InstallBedrockChoice");
            await Eventually(() => Has("BedrockInstallPage") && Has("BedrockInstallDescription"));
            await CapturePair("bedrock-install");
            outcome = 0;

            bool Has(string key) => surface?.Scene?.Nodes.Any(node => f.Shell.Tree.Name(node.Entity) == key) == true;
            XsrUiSceneNode Find(string key) => FindByKey(f.Shell, surface!.Scene!, key);
            bool Visible(XsrUiSceneNode node) => node.Rect.Width > 0 && node.Rect.Height > 0
                && (node.ClipRect is null || node.ClipRect.Value.Contains(new(node.Rect.X + Math.Min(node.Rect.Width / 2, 12), node.Rect.Y + Math.Min(node.Rect.Height / 2, 12))));
            void AssertOwned(string key)
            {
                var entity = Find(key).Entity;
                AssertTrue(f.Shell.Tree.GetComponent<NxmlDynamicSlot>(f.Shell.Tree.Parent(entity)) is not null);
            }
            XsrUiSceneNode Sections() => surface!.Scene!.Nodes.First(node => f.Shell.Tree.Name(node.Entity) == "SettingsSections" && node.Scroll is not null);
            async Task Top()
            {
                await startup.InvokeAsync(() =>
                {
                    var sections = Sections(); f.Shell.Tree.GetComponent<XsrUiScroll>(sections.Entity)!.OffsetY = 0;
                    f.Shell.Tree.MarkDirty(sections.Entity, XsrUiDirtyKinds.Layout); surface!.CommitScene();
                }, timeout.Token);
            }
            async Task Select(SettingsPageController page, string id)
            {
                await Eventually(() => Has("SettingsNav." + id));
                await startup.InvokeAsync(() => { Emit(f.Intents, "ui.settings.section", Find("SettingsNav." + id).Entity); surface!.CommitScene(); }, timeout.Token);
                await Eventually(() => page.SelectedSection == id);
                await Top();
            }
            async Task Activate(string key)
            {
                await Show(key);
                await startup.InvokeAsync(() => { AssertTrue(f.Shell.Renderer.Activate(Find(key).Entity)); surface!.CommitScene(); }, timeout.Token);
            }
            async Task NavigateLaunch(string command, string page)
            {
                await startup.InvokeAsync(() => { Emit(f.Intents, command); surface!.CommitScene(); }, timeout.Token);
                await WaitForLaunchIdle();
                await Eventually(() => Has(page) && f.Shell.Tree.Name(f.Shell.Stage.Navigation.Current) == page);
            }
            async Task WaitForLaunchIdle()
            {
                Task pending = Task.CompletedTask;
                await startup.InvokeAsync(() => { pending = f.Controller.WaitUntilIdle(); }, timeout.Token);
                await pending.WaitAsync(timeout.Token);
            }
            async Task Show(string key)
            {
                for (int step = 0; step < 64; step++)
                {
                    bool visible = false;
                    await startup.InvokeAsync(() =>
                    {
                        surface!.CommitScene();
                        var target = surface.Scene!.Nodes.Where(node => f.Shell.Tree.Name(node.Entity) == key).Select(node => (XsrUiSceneNode?)node).FirstOrDefault();
                        visible = target is { } shown && Visible(shown);
                        if (visible) return;
                        XsrUiSceneNode? scroll = null;
                        if (target is { } targetNode)
                        {
                            for (var ancestor = f.Shell.Tree.Parent(targetNode.Entity); ancestor.IsAssigned; ancestor = f.Shell.Tree.Parent(ancestor))
                            {
                                if (f.Shell.Tree.GetComponent<XsrUiScroll>(ancestor) is null) continue;
                                scroll = surface.Scene.Nodes.Single(node => node.Entity == ancestor); break;
                            }
                        }
                        scroll ??= surface.Scene.Nodes.Where(node => f.Shell.Tree.Name(node.Entity) == "SettingsSections" && node.Scroll is not null)
                            .Select(node => (XsrUiSceneNode?)node).FirstOrDefault();
                        if (scroll is not { } sections)
                            throw new InvalidOperationException("Native preview target has no visible scroll owner: " + key);
                        double delta = target is { } above && above.Rect.Y < sections.Rect.Y ? -120 : 120;
                        f.Shell.Renderer.PointerScroll(new(sections.Rect.X + 24, sections.Rect.Y + 24), delta); surface.CommitScene();
                    }, timeout.Token);
                    if (visible) return;
                    await Task.Delay(10, timeout.Token);
                }
                throw new InvalidOperationException("Native preview control did not enter the viewport: " + key);
            }
            async Task<XsrUiEntityId> Draft(string key, string value)
            {
                await Show(key); XsrUiEntityId entity = default;
                await startup.InvokeAsync(() =>
                {
                    entity = Find(key).Entity; f.Shell.Renderer.SetTextInputValue(entity, value);
                    AssertTrue(f.Shell.Renderer.Focus(entity)); f.Shell.Renderer.EditText(XsrUiTextEdit.Left); surface!.CommitScene();
                }, timeout.Token);
                return entity;
            }
            async Task CapturePair(string stage, XsrUiEntityId retainedInput = default)
            {
                XsrUiTextInput? input = null; string? draft = null; int caret = 0;
                await startup.InvokeAsync(() =>
                {
                    if (retainedInput.IsAssigned) { input = f.Shell.Tree.GetComponent<XsrUiTextInput>(retainedInput)!; draft = input.ReadDraft(); caret = input.SelectionEnd; }
                }, timeout.Token);
                foreach (double width in NativeNxmlSettingsWidths)
                {
                    await startup.InvokeAsync(() => { window!.Width = Math.Max(width, window.MinWidth); window.Height = 740; window.UpdateLayout(); surface!.CommitScene(); }, timeout.Token);
                    await Eventually(() => AvaloniaUiRuntimeDiagnostics.Capture().ActiveMotionTracks == 0
                        && window!.ClientSize.Width > 0 && f.Shell.Renderer.Viewport.Width <= window.ClientSize.Width);
                    await Task.Delay(80, timeout.Token);
                    await startup.InvokeAsync(() =>
                    {
                        surface!.CommitScene(); window!.UpdateLayout();
                        AssertEqual(theme == "dark", f.Shell.Renderer.ColorScheme.IsDark);
                        AssertLaunchGeometry(stage);
                        if (retainedInput.IsAssigned)
                        {
                            AssertTrue(f.Shell.Tree.IsAlive(retainedInput)); AssertEqual(retainedInput, f.Shell.Renderer.Focused);
                            AssertEqual(draft, input!.ReadDraft()); AssertEqual(caret, input.SelectionEnd);
                        }
                        double scale = window.RenderScaling;
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                        bitmap.Render(window);
                        string name = "nxml-settings-" + theme + "-" + stage + "-" + width.ToString(CultureInfo.InvariantCulture);
                        bitmap.Save(Path.Combine(outputDirectory, name + ".png"), PngBitmapEncoderOptions.Default);
                        var manifest = new JsonObject
                        {
                            ["stage"] = stage,
                            ["theme"] = theme,
                            ["width"] = bitmap.PixelSize.Width,
                            ["height"] = bitmap.PixelSize.Height,
                            ["requestedWidth"] = width,
                            ["viewportWidth"] = f.Shell.Renderer.Viewport.Width,
                            ["viewportHeight"] = f.Shell.Renderer.Viewport.Height,
                            ["minimumWindowWidth"] = window.MinWidth,
                            ["renderer"] = stage is "launch-home" or "java-install" or "bedrock-install"
                                ? "actual Avalonia native window RenderTargetBitmap; product LaunchPageController, InstallCatalogRuntime, bound BedrockInstallPageController and shipped NXML/NSS"
                                : "actual Avalonia native window RenderTargetBitmap; product SettingsPageController and shipped NXML/NSS",
                            ["data"] = stage is "launch-home" or "java-install" or "bedrock-install"
                                ? "controlled immediate typed game and Fabric metadata, offline profile in isolated fixture ports, empty installed-instance scan, actual platform Store support gate; no network, authentication, game or Store/browser operations"
                                : "controlled typed management, identity, server, world, recovery, export and encoded PNG fixtures; temporary real settings store; actual local read-only advanced runtime capture; no network, account or game operations",
                            ["interactions"] = stage is "launch-home" or "java-install" or "bedrock-install"
                                ? "actual launch navigation intents and installation choice/game/Fabric control activation; install draft/caret/focus retained across widths; owned workspace and visible geometry checks; no install/start/Store/browser actions"
                                : "actual renderer command activation, draft editing, caret/focus checks, pointer scrolling, screenshot modal/crop toggle and close; no export/server/crop writes",
                            ["focused"] = f.Shell.Renderer.Focused.IsAssigned ? f.Shell.Tree.Name(f.Shell.Renderer.Focused) : null,
                            ["sceneNodes"] = surface.Scene!.Count,
                        };
                        File.WriteAllText(Path.Combine(outputDirectory, name + ".json"), manifest.ToJsonString());
                        File.WriteAllText(Path.Combine(outputDirectory, "nxml-settings-preview.marker.tsv"), stage + "\t" + theme + "\t" + width.ToString(CultureInfo.InvariantCulture) + "\t" + name + ".png\n");
                        Console.WriteLine("NXML_SETTINGS_PREVIEW_STAGE\t" + stage + "\t" + theme + "\t" + width.ToString(CultureInfo.InvariantCulture));
                    }, timeout.Token);
                }
            }
            void AssertLaunchGeometry(string stage)
            {
                if (stage == "launch-home")
                {
                    AssertOwned("LaunchHomeWorkspace");
                    foreach (string key in new[] { "CardAccount", "CardVersion", "CardAbout", "LaunchButton" }) AssertInViewport(key);
                    AssertEqual("Player", Find("AccountName").Text);
                }
                else if (stage == "java-install")
                {
                    AssertOwned("JavaInstallContent"); AssertOwned("CatalogRow:loader:0.16.10");
                    foreach (string key in new[] { "JavaInstallVersionInput", "JavaInstallStart", "JavaInstallPager", "CatalogRow:loader:0.16.10" }) AssertInViewport(key);
                    AssertTrue(Find("CatalogRow:loader:0.16.10").IsSelected);
                    var track = f.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(Find("JavaInstallPagerTabs").Entity)!;
                    AssertEqual(Find("JavaFabricTab").Entity, track.Selected);
                    AssertTrue(f.Shell.Tree.IsAlive(track.Thumb));
                    AssertEqual("JavaInstallThumb", f.Shell.Tree.Name(track.Thumb));
                }
                else if (stage == "bedrock-install")
                {
                    AssertOwned("BedrockInstallHost");
                    foreach (string key in new[] { "BedrockInstallCard", "BedrockInstallDescription", "BedrockInstallStore", "BedrockInstallWebsite" }) AssertInViewport(key);
                    AssertTrue(Find("BedrockInstallDescription").Text!.Contains("Microsoft Store", StringComparison.Ordinal));
                    var store = f.Shell.Tree.GetComponent<XsrUiInput>(Find("BedrockInstallStore").Entity)!;
                    AssertEqual(OperatingSystem.IsWindows(), store.Enabled);
                    AssertEqual(OperatingSystem.IsWindows(), store.Clickable);
                    AssertTrue(f.Shell.Tree.GetComponent<XsrUiInput>(Find("BedrockInstallWebsite").Entity)!.Clickable);
                }
            }
            void AssertInViewport(string key)
            {
                var node = Find(key);
                AssertTrue(Visible(node));
                AssertTrue(node.Rect.X >= 0 && node.Rect.Y >= 0);
                AssertTrue(node.Rect.X + node.Rect.Width <= f.Shell.Renderer.Viewport.Width + 1);
                AssertTrue(node.Rect.Y + node.Rect.Height <= f.Shell.Renderer.Viewport.Height + 1);
            }
            async Task Eventually(Func<bool> condition)
            {
                while (true)
                {
                    bool ready = false; await startup.InvokeAsync(() => { surface?.CommitScene(); ready = condition(); }, timeout.Token);
                    if (ready) return;
                    await Task.Delay(10, timeout.Token);
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { Console.Error.WriteLine("FAIL: native NXML settings preview\n" + error); }
        finally
        {
            void Cleanup()
            {
                if (harness is not null) harness.Fixture.Shell.Renderer.ReducedMotion = true;
                window?.Close(); appearance?.Dispose(); harness?.Dispose();
            }
            if (!startup.Completion.IsCompleted) await Dispatcher.UIThread.InvokeAsync(Cleanup);
            else Cleanup();
            if (lifetime is not null) AssertEqual(0, await lifetime.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Console.WriteLine(outcome == 0
            ? "PASS: actual native NXML settings and launch/install preview; both viewport widths, literal fixtures, retained drafts/caret/focus and modal/scroll interactions; normal teardown."
            : "FAIL: native NXML settings preview did not complete.");
        return outcome;
    }

    private static readonly double[] NativeNxmlSettingsWidths = [1040, 810];

    private static XsrUiEntityId FindNativeNxmlSettingsTree(XsrUiEntityId root, string key, XsrUiTree tree)
    {
        XsrUiEntityId found = default; tree.Walk(root, entity => { if (tree.Name(entity) == key) found = entity; return true; });
        AssertTrue(found.IsAssigned); return found;
    }

    private sealed class NativeNxmlSettingsHarness : IDisposable
    {
        internal LaunchPageFixture Fixture { get; } = new(new ImmediateInstanceSource([]), addProfile: true, installSource: new NativeNxmlPreviewInstallSource());
        internal SettingsPageController Global { get; }
        internal SettingsPageController Instance { get; }
        internal string InstanceDirectory { get; }
        internal int CropWrites { get; private set; }
        internal int ExportWrites { get; private set; }
        internal int ServerWrites { get; private set; }
        internal int StatusReads { get; private set; }
        internal int HealthReads { get; private set; }
        internal int SnapshotReads { get; private set; }
        private readonly Dictionary<string, PngImage> _images = new(StringComparer.Ordinal);
        private readonly InstanceContentEntry[] _screenshots;
        private readonly InstanceManagementSnapshot _snapshot;

        internal NativeNxmlSettingsHarness()
        {
            InstanceDirectory = Path.Combine(Fixture.TemporaryDirectory, "fixture-instance");
            string screenshots = Path.Combine(InstanceDirectory, "screenshots"); Directory.CreateDirectory(screenshots);
            _screenshots = Enumerable.Range(0, 12).Select(index =>
            {
                string name = "fixture-" + index.ToString("00", CultureInfo.InvariantCulture) + ".png";
                var image = CreateScreenshotPreviewFixture(index % 2 == 0 ? 960 : 480, index % 2 == 0 ? 540 : 720, index);
                _images.Add(name, image);
                return new InstanceContentEntry(name, false, image.Bytes.Length)
                { ModifiedUtcTicks = new DateTime(2026, 10, 13, 12, index, 0, DateTimeKind.Utc).Ticks, ImageWidth = image.Width, ImageHeight = image.Height, Icon = index % 2 == 0 ? image : null };
            }).ToArray();
            var world = new InstanceWorldMetadata("Fixture 世界 <literal>", "1.21.1", 4189, DateTimeOffset.Parse("2026-10-13T12:00:00Z", CultureInfo.InvariantCulture),
                42, 1, 2, false, 1024 * 1024, true, "world-revision", false, [new("vanilla", "vanilla", true, true), new("file/fixture", "Fixture 数据包", false, false)])
            { DataPacksSupported = true };
            var revision = Guid.Parse("68f78247-788a-48af-a4b4-c4a1d4d463c9");
            _snapshot = new(InstanceDirectory, InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("game", "游戏设置"), new("mods", "模组", Path.Combine(InstanceDirectory, "mods")),
                 new("screenshots", "截图", screenshots), new("saves", "世界", Path.Combine(InstanceDirectory, "saves")), new("servers", "服务器"), new("recovery", "快照与存储"), new("modpack", "整合包与导出")], true, "1.0.0")
            {
                Description = "Controlled native NXML preview fixture",
                Contents = [new("mods", Enumerable.Range(0, 40).Select(index => new InstanceContentEntry("preview-" + index.ToString("00", CultureInfo.InvariantCulture) + ".jar", false, 1024 * (index + 1))
                    { DisplayName = "Fixture 模组 " + index.ToString("00", CultureInfo.InvariantCulture), Version = "1.0", Enabled = index % 3 != 0, PackageReadable = true, Icon = _images.Values.First() }).ToArray(), true, null),
                    new("screenshots", _screenshots, true, null), new("saves", [new("FixtureWorld", true, world.Size) { World = world }], true, null)],
                RecoveryStorage = new(1024 * 1024 * 24, 1024 * 1024 * 8, true, [new(revision, DateTimeOffset.Parse("2026-10-12T12:00:00Z", CultureInfo.InvariantCulture), 9, 2048)]),
                RecoveryComparison = new(InstanceDirectory, revision, DateTimeOffset.Parse("2026-10-12T12:00:00Z", CultureInfo.InvariantCulture),
                    Enumerable.Range(0, 11).Select(index => new InstanceRecoveryChange(InstanceRecoveryChangeKind.Modified, index % 2 == 0 ? "模组" : "配置", "fixture-change-" + index.ToString("00", CultureInfo.InvariantCulture) + ".txt")).ToArray(), "fixture-fingerprint")
            };
            var queries = WorldFixtureQueries(Fixture);
            queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (query, _) =>
            { AssertEqual(InstanceDirectory, query.InstanceDirectory); return new(XsrResult.Success(_snapshot)); });
            queries.Register<InstanceIdentityQuery, InstanceIdentitySnapshot>(InstanceIdentityContract.Query, (_, _) => new(XsrResult.Success(
                new InstanceIdentitySnapshot("identity-revision", "fixture-identity", new("Fixture instance", "Read-only fixture", "", false, [], "", "", ""),
                    new(0, "", "", "", "fixture.invalid", true, "1.21.1", "", []), false)
                { Loader = "Fixture loader", LaunchCount = 7 })));
            queries.Register<InstanceServerListQuery, InstanceServerList>(InstanceServerListContract.Read, (_, _) => new(XsrResult.Success(new InstanceServerList("server-revision", [new(7, "Fixture server", "fixture.invalid:25565")]))));
            queries.Register<InstanceServerStatusQuery, InstanceServerStatus>(InstanceServerListContract.Status, (query, _) =>
            {
                AssertEqual(InstanceDirectory, query.InstanceDirectory); AssertEqual("server-revision", query.ExpectedRevision); AssertEqual(7, query.SourceIndex); StatusReads++;
                return new(XsrResult.Success(new InstanceServerStatus(true, "fixture server", "1.21.1", 3, 20, 113) { ClientProtocol = 767, ServerProtocol = 767 }));
            });
            queries.Register<InstanceScreenshotQuery, InstanceScreenshot>(InstanceScreenshotContract.Read, (query, _) =>
            {
                AssertEqual(InstanceDirectory, query.InstanceDirectory); var entry = _screenshots.Single(item => item.Name == query.Name);
                AssertEqual(entry.Size!.Value, query.ExpectedSize); AssertEqual(entry.ModifiedUtcTicks, query.ExpectedModifiedUtcTicks);
                return new(XsrResult.Success(new InstanceScreenshot(Path.Combine(screenshots, query.Name), _images[query.Name])));
            });
            queries.Register<InstanceWorldHealthQuery, InstanceWorldHealth>(InstanceWorldHealthContract.Read, (query, _) =>
            { AssertEqual(InstanceDirectory, query.InstanceDirectory); AssertEqual("FixtureWorld", query.WorldName); HealthReads++; return new(XsrResult.Success(new InstanceWorldHealth(true, 2, 128, 0, []))); });
            queries.Register<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(InstanceWorldSnapshotContract.List, (query, _) =>
            { AssertEqual(InstanceDirectory, query.InstanceDirectory); AssertEqual("FixtureWorld", query.WorldName); SnapshotReads++; return new(XsrResult.Success<IReadOnlyList<InstanceWorldSnapshot>>([new(new string('a', 64), DateTimeOffset.Parse("2026-10-12T12:00:00Z", CultureInfo.InvariantCulture), 7, 1048576)])); });
            queries.Register<InstanceModpackExportQuery, InstanceModpackExportPreview>(InstanceModpackExportContract.Preview, (query, _) =>
            { AssertEqual(InstanceDirectory, query.InstanceDirectory); return new(XsrResult.Success(new InstanceModpackExportPreview(InstanceDirectory, [new("mods", "模组", 40, 1048576), new("config", "配置", 8, 2048), new("saves", "存档", 1, 1048576)]))); });
            var commands = new XsrCommandRouterBuilder();
            commands.Register<InstanceScreenshotCropCommand>(InstanceScreenshotContract.Crop, (_, _) => { CropWrites++; return new(XsrResult.Success()); });
            commands.Register<InstanceModpackExportCommand>(InstanceModpackExportContract.Export, (_, _) => { ExportWrites++; return new(XsrResult.Success()); });
            commands.Register<InstanceServerListSaveCommand>(InstanceServerListContract.Save, (_, _) => { ServerWrites++; return new(XsrResult.Success()); });
            Global = new(Fixture.Shell, Fixture.Intents, Fixture.Foundation.Queries, Fixture.Foundation.Commands, Fixture.Store, Fixture.Feedback)
            { NavigateAdvancedCommand = _ => { }, OpenAdvancedCommandPalette = () => true };
            Instance = new(Fixture.Shell, Fixture.Intents, queries.Build(new NoopDispatchObserver()), commands.Build(new NoopDispatchObserver()), Fixture.Store, Fixture.Feedback, () => InstanceDirectory)
            { OpenManagementDirectory = _ => { }, CopyScreenshotAsync = _ => Task.CompletedTask, ShareScreenshotAsync = (_, _, _) => Task.CompletedTask };
        }
        public void Dispose() { Instance.Dispose(); Global.Dispose(); Fixture.Dispose(); }
    }

    private sealed class NativeNxmlPreviewInstallSource : IInstallCatalogSource
    {
        public Task<IReadOnlyList<InstallCatalogVersion>> GetGamesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<InstallCatalogVersion>>([new("1.21.1", "controlled release"), new("1.20.6", "controlled release"), new("1.20.1", "controlled release")]);
        }

        public Task<IReadOnlyList<InstallCatalogVersion>> GetLoadersAsync(InstallLoader loader, string game, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); AssertEqual("1.21.1", game);
            return Task.FromResult<IReadOnlyList<InstallCatalogVersion>>(loader == InstallLoader.Fabric
                ? [new("0.16.10", "controlled Fabric <metadata> {state private}"), new("0.16.9", "controlled Fabric metadata")]
                : []);
        }
    }
}
