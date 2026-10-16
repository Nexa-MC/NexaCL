using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Desktop.Ui;
using Nexa.Services.Settings;
using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static int RunNativeLauncherMigrationPreview(string theme, string outputDirectory) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(() => RunNativeLauncherMigrationPreviewAsync(theme, Path.GetFullPath(outputDirectory)));

    private static async Task<int> RunNativeLauncherMigrationPreviewAsync(string theme, string outputDirectory)
    {
        if (theme is not ("light" or "dark")) throw new ArgumentException("Expected light or dark.", nameof(theme));
        Directory.CreateDirectory(outputDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], disableHardwareAcceleration: true,
            new AvaloniaUiStartupAppearance(theme == "dark" ? XsrUiThemeMode.Dark : XsrUiThemeMode.Light, ReducedMotion: true), cancellationToken: timeout.Token);
        LauncherMigrationUiFixture? harness = null;
        DesktopAppearanceSession? appearance = null;
        AvaloniaUiShellWindow? window = null;
        AvaloniaUiSceneSurface? surface = null;
        Task<int>? lifetime = null;
        var actions = new AvaloniaUiPlatformActions();
        actions.SetDesktopPolicy(new(TrayEnabled: false));
        int outcome = 1, captures = 0;
        double requestedWidth = 1040, requestedHeight = 920;
        Size acceptedWindowSize = default;
        try
        {
            await startup.InvokeAsync(() =>
            {
                harness = new();
                var f = harness.Fixture;
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("appearance.theme-mode", SettingsLayer.Global, new(SettingsOverrideMode.Custom, theme == "dark" ? "1" : "0"))).IsSuccess);
                AssertTrue(f.Foundation.Host.SettingsPolicy.Set(new("appearance.reduced-motion", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
                appearance = new(f.Shell, f.Store, actions);
                f.Controller.SettingsPage = harness.Settings.Page;
                Emit(f.Intents, "ui.navigation.settings");
                harness.DiscoveryHandler = (_, _) => new(XsrResult.Success(new LauncherMigrationDiscoveryResult(
                    [new(LauncherMigrationSourceKind.Prism, "Prism / MultiMC / PolyMC", Path.Combine(harness.Source, "prism")),
                     new(LauncherMigrationSourceKind.GdLauncher, "GDLauncher", harness.Source),
                     new(LauncherMigrationSourceKind.Ftb, "FTB App", Path.Combine(harness.Source, "ftb")),
                     new(LauncherMigrationSourceKind.Technic, "Technic Launcher", Path.Combine(harness.Source, "technic"))],
                    ["受控常用位置检测结果；便携或自定义安装可手动选择。在线账户需要重新登录。"])));
                harness.PreviewHandler = (query, _) => new(XsrResult.Success(new LauncherMigrationPreview(query, "controlled-preview-revision",
                    [new("one", "生存世界 · Fabric", harness.Source, "1.21.1", "Fabric", "0.16.10", 346030080, 842, true, null, []),
                     new("two", "朋友的模组整合包", harness.Source, "1.20.1", "Forge", "47.2.0", 721420288, 1736, true, null, ["转换时将下载游戏与加载器；保留原启动器文件。"]),
                     new("unsupported", "自定义加载器实例", harness.Source, "1.20.1", "Custom", null, 104857600, 94, false, "此加载器暂不支持转换；请继续使用原实例。", [])],
                    ["受控本地预览数据；在线账户需要重新登录。"])));
            }, timeout.Token);
            var h = harness!; var f = h.Fixture;
            await startup.PrepareShellAsync(f.Shell, actions, timeout.Token);
            await startup.WarmUpShellAsync(f.Shell, timeout.Token);
            lifetime = Task.Run(() => AvaloniaUiShellHost.Run(startup, f.Shell, actions));
            await Eventually(() =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not AvaloniaUiShellWindow candidate || !candidate.IsVisible) return false;
                window = candidate; surface = candidate.GetVisualDescendants().OfType<AvaloniaUiSceneSurface>().Single();
                SetSize(1040);
                return surface.Scene is { Count: > 0 } && f.Shell.Renderer.ColorScheme.IsDark == (theme == "dark");
            });
            await CapturePair("initial", "LauncherMigrationWorkspace");
            await startup.InvokeAsync(() => Activate("LauncherMigration.discover"), timeout.Token);
            await Eventually(() => h.Find("LauncherMigration.Detected.3", false).IsAssigned && h.Enabled("discover"));
            await CapturePair("detected", "LauncherMigration.DetectedSource.0");
            await startup.InvokeAsync(() => Activate("LauncherMigration.Detected.1"), timeout.Token);
            await Eventually(() => h.Text(h.Find("LauncherMigration.Source")) == h.Source);
            await CapturePair("detected-selected", "LauncherMigration.DetectedSource.0");
            await startup.InvokeAsync(() => { h.Picks.Enqueue(f.TemporaryDirectory); Activate("LauncherMigration.destination"); }, timeout.Token);
            await Eventually(() => h.Enabled("preview"));
            await startup.InvokeAsync(() => Activate("LauncherMigration.preview"), timeout.Token);
            await Eventually(() => h.Find("LauncherMigration.Select.unsupported", false).IsAssigned);
            await CapturePair("preview", "LauncherMigration.Status");
            await CapturePair("preview-instances", "LauncherMigration.Instance.one");
            await startup.InvokeAsync(() => { Activate("LauncherMigration.Select.two"); Activate("LauncherMigration.apply"); }, timeout.Token);
            await Eventually(() => f.Feedback.Snapshot().Dialog is not null && Has("DialogCard"));
            await CapturePair("confirmation", "LauncherMigration.Instance.one");
            await startup.InvokeAsync(() => Activate("DialogAccept", "ui.feedback.dialog.accept"), timeout.Token);
            await Eventually(() => h.Receipt is not null && h.Enabled("register") && !Has("DialogCard"));
            await CapturePair("completed", "LauncherMigration.Status");
            await startup.InvokeAsync(() => { h.Picks.Enqueue(h.Receipt!.DestinationDirectory); Activate("LauncherMigration.recover"); }, timeout.Token);
            await Eventually(() => h.ReceiptReads >= 2 && h.Enabled("register"));
            await CapturePair("recovered", "LauncherMigration.Status");
            outcome = 0;

            bool Has(string key) => surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity) == key);
            XsrUiSceneNode Node(string key) => surface!.Scene!.Nodes.Single(node => f.Shell.Tree.Name(node.Entity) == key);
            void Activate(string key, string command = "ui.settings.launcher-migration.action")
            {
                XsrUiEntityId entity = key.StartsWith("Dialog", StringComparison.Ordinal) ? Node(key).Entity : h.Find(key);
                AssertTrue(f.Shell.Tree.GetComponent<XsrUiInput>(entity)?.Enabled == true);
                Emit(f.Intents, command, entity); surface!.CommitScene();
            }
            XsrUiSceneNode CurrentBody()
            {
                var pager = h.Find("SettingsPager");
                var current = f.Shell.Tree.Children(pager)[f.Shell.Tree.GetComponent<XsrUiPager>(pager)!.PageIndex];
                return surface!.Scene!.Nodes.Single(node => node.Entity == current);
            }
            bool IsDescendant(XsrUiEntityId entity, XsrUiEntityId ancestor)
            {
                for (var current = entity; current.IsAssigned; current = f.Shell.Tree.Parent(current))
                    if (current == ancestor) return true;
                return false;
            }
            void Reveal(string key)
            {
                var anchorEntity = h.Find(key);
                surface!.CommitScene();
                var body = CurrentBody();
                var scroll = f.Shell.Tree.GetComponent<XsrUiScroll>(body.Entity)!;
                scroll.OffsetY = 0;
                for (int step = 0; step < 256; step++)
                {
                    f.Shell.Tree.MarkDirty(body.Entity, XsrUiDirtyKinds.Layout); surface.CommitScene();
                    body = CurrentBody();
                    var candidates = surface.Scene!.Nodes.Where(node => IsDescendant(node.Entity, anchorEntity)).ToArray();
                    if (candidates.Length > 0)
                    {
                        double target = scroll.OffsetY + candidates[0].Rect.Y - body.Rect.Y - 10;
                        scroll.OffsetY = Math.Clamp(target, 0, body.Scroll!.Value.MaximumOffsetY);
                        f.Shell.Tree.MarkDirty(body.Entity, XsrUiDirtyKinds.Layout); surface.CommitScene();
                        return;
                    }
                    double next = Math.Min(body.Scroll!.Value.MaximumOffsetY, scroll.OffsetY + Math.Max(120, body.Rect.Height / 3));
                    if (next <= scroll.OffsetY) break;
                    scroll.OffsetY = next;
                }
                throw new InvalidOperationException("Migration preview anchor did not enter the actual scroll viewport: " + key);
            }
            void SetSize(double width)
            {
                requestedWidth = width;
                var screen = window!.Screens.ScreenFromWindow(window) ?? window.Screens.Primary
                    ?? throw new InvalidOperationException("Native migration preview has no available screen.");
                double scale = window.RenderScaling;
                var frame = window.FrameSize ?? window.ClientSize;
                double nativeWidth = Math.Max(0, frame.Width - window.ClientSize.Width);
                double nativeHeight = Math.Max(0, frame.Height - window.ClientSize.Height);
                double availableWidth = Math.Floor((screen.WorkingArea.Width - 4) / scale - nativeWidth);
                double availableHeight = Math.Floor((screen.WorkingArea.Height - 4) / scale - nativeHeight);
                if (availableWidth < window.MinWidth || availableHeight < window.MinHeight)
                    throw new InvalidOperationException("Native migration preview cannot fit the launcher's minimum client size.");
                requestedHeight = 920;
                acceptedWindowSize = new(Math.Min(Math.Max(width, window.MinWidth), availableWidth), Math.Min(Math.Max(requestedHeight, window.MinHeight), availableHeight));
                window.Width = acceptedWindowSize.Width; window.Height = acceptedWindowSize.Height;
                window.Position = new(screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - (int)Math.Ceiling((acceptedWindowSize.Width + nativeWidth) * scale)) / 2),
                    screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - (int)Math.Ceiling((acceptedWindowSize.Height + nativeHeight) * scale)) / 2));
                window.UpdateLayout(); surface!.CommitScene();
            }
            async Task CapturePair(string stage, string anchor)
            {
                foreach (double width in new[] { 1040d, 810d })
                {
                    await startup.InvokeAsync(() => SetSize(width), timeout.Token);
                    await Eventually(() => Math.Abs(window!.ClientSize.Width - acceptedWindowSize.Width) <= 1 / window.RenderScaling + .01
                        && Math.Abs(window.ClientSize.Height - acceptedWindowSize.Height) <= 1 / window.RenderScaling + .01
                        && Math.Abs(f.Shell.Renderer.Viewport.Width - surface!.Bounds.Width) <= 1 / window.RenderScaling + .01
                        && Math.Abs(f.Shell.Renderer.Viewport.Height - surface.Bounds.Height) <= 1 / window.RenderScaling + .01
                        && f.Shell.Renderer.ColorScheme.IsDark == (theme == "dark"));
                    await startup.InvokeAsync(() => Reveal(anchor), timeout.Token);
                    await Task.Delay(100, timeout.Token); // Allow the actual native layout/paint to commit after resize.
                    await startup.InvokeAsync(() => Capture(stage), timeout.Token);
                }
            }
            async Task Eventually(Func<bool> condition)
            {
                while (true)
                {
                    bool ready = false;
                    await startup.InvokeAsync(() => { surface?.CommitScene(); ready = condition(); }, timeout.Token);
                    if (ready) return;
                    await Task.Delay(15, timeout.Token);
                }
            }
            void Capture(string stage)
            {
                window!.UpdateLayout(); surface!.CommitScene();
                var workspace = h.Find("LauncherMigrationWorkspace");
                var children = f.Shell.Tree.Children(workspace);
                var card = children[children.Count - 1];
                var body = f.Shell.Tree.Children(card).Single();
                var padding = f.Shell.Tree.GetComponent<XsrUiElement>(body)!.Padding;
                AssertTrue(padding.Left > 0 && padding.Top > 0 && padding.Right > 0 && padding.Bottom > 0);
                double scale = window.RenderScaling;
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.ClientSize.Width * scale),
                    (int)Math.Ceiling(window.ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
                bitmap.Render(window);
                string stem = "launcher-migration-" + theme + "-" + stage + "-" + requestedWidth.ToString("0", CultureInfo.InvariantCulture);
                bitmap.Save(Path.Combine(outputDirectory, stem + ".png"), PngBitmapEncoderOptions.Default);
                JsonObject? mainCardInsets = null;
                var firstBodyChild = f.Shell.Tree.Children(body)[0];
                var paintedCards = surface.Scene!.Nodes.Where(node => node.Entity == card).ToArray();
                var paintedFirstChildren = surface.Scene.Nodes.Where(node => node.Entity == firstBodyChild).ToArray();
                if (paintedCards.Length == 1 && paintedFirstChildren.Length == 1)
                {
                    var cardRect = paintedCards[0].Rect; var childRect = paintedFirstChildren[0].Rect;
                    double left = childRect.X - cardRect.X;
                    double right = cardRect.X + cardRect.Width - childRect.X - childRect.Width;
                    AssertTrue(left >= 15.99 && right >= 15.99);
                    mainCardInsets = new JsonObject
                    {
                        ["left"] = left,
                        ["right"] = right,
                        ["cardRect"] = RectFacts(cardRect),
                        ["firstChildRect"] = RectFacts(childRect)
                    };
                }
                var instanceInsets = new JsonArray();
                foreach (string identity in new[] { "one", "two", "unsupported" })
                {
                    string cardKey = "LauncherMigration.Instance." + identity;
                    string choiceKey = "LauncherMigration.Select." + identity;
                    if (!Has(cardKey) || !Has(choiceKey)) continue;
                    var instanceCard = Node(cardKey); var choice = Node(choiceKey);
                    double left = choice.Rect.X - instanceCard.Rect.X;
                    double right = instanceCard.Rect.X + instanceCard.Rect.Width - choice.Rect.X - choice.Rect.Width;
                    AssertTrue(left >= 11.99 && right >= 11.99);
                    instanceInsets.Add((JsonNode)new JsonObject
                    {
                        ["instance"] = identity,
                        ["left"] = left,
                        ["right"] = right,
                        ["cardRect"] = RectFacts(instanceCard.Rect),
                        ["choiceRect"] = RectFacts(choice.Rect)
                    });
                }
                var nodes = new JsonArray();
                foreach (var node in surface.Scene!.Nodes) nodes.Add((JsonNode)NodeFacts(f.Shell, node));
                var document = new JsonObject
                {
                    ["stage"] = stage,
                    ["theme"] = theme,
                    ["requestedWidth"] = requestedWidth,
                    ["requestedHeight"] = requestedHeight,
                    ["acceptedWidth"] = acceptedWindowSize.Width,
                    ["acceptedHeight"] = acceptedWindowSize.Height,
                    ["nativeClientWidth"] = window.ClientSize.Width,
                    ["nativeClientHeight"] = window.ClientSize.Height,
                    ["viewportWidth"] = f.Shell.Renderer.Viewport.Width,
                    ["viewportHeight"] = f.Shell.Renderer.Viewport.Height,
                    ["renderScaling"] = scale,
                    ["pixelWidth"] = bitmap.PixelSize.Width,
                    ["pixelHeight"] = bitmap.PixelSize.Height,
                    ["mainCardInsets"] = mainCardInsets,
                    ["instanceCardInsets"] = instanceInsets,
                    ["migrationBodyPadding"] = new JsonObject { ["left"] = padding.Left, ["top"] = padding.Top, ["right"] = padding.Right, ["bottom"] = padding.Bottom },
                    ["workspaceVisibleNodes"] = surface.Scene!.Nodes.Count(node => IsDescendant(node.Entity, workspace)),
                    ["selectedCount"] = h.Applied?.SelectedInstanceIds.Count,
                    ["applyCalls"] = h.ApplyCalls,
                    ["receiptReads"] = h.ReceiptReads,
                    ["discoveryCalls"] = h.DiscoveryCalls,
                    ["selectedSourceKind"] = h.Text(h.Find("LauncherMigration.SourceKind")),
                    ["selectedSourceDirectory"] = h.Text(h.Find("LauncherMigration.Source")),
                    ["renderer"] = "actual software Avalonia native window RenderTargetBitmap; not OS compositor capture",
                    ["runtime"] = "shipped SettingsPageController, NXML/NSS, feedback presenter, renderer, native backend and real Minecraft library commands/state",
                    ["fixtures"] = "controlled typed foreign-launcher discovery/preview/apply/receipt routes, explicit detected-source and directory picker selections, temporary data directories and instance discovery; no real third-party accounts, downloads or game launch",
                    ["scene"] = nodes
                };
                File.WriteAllText(Path.Combine(outputDirectory, stem + ".json"), document.ToJsonString());
                Console.WriteLine("LAUNCHER_MIGRATION_NATIVE_PREVIEW_STAGE\t" + stem); captures++;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { Console.Error.WriteLine("FAIL: native launcher migration preview\n" + error); }
        finally
        {
            void Cleanup() { window?.Close(); appearance?.Dispose(); harness?.Dispose(); }
            if (!startup.Completion.IsCompleted) await Dispatcher.UIThread.InvokeAsync(Cleanup);
            else Cleanup();
            if (lifetime is not null) AssertEqual(0, await lifetime.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Console.WriteLine(outcome == 0 ? "PASS: native launcher migration preview; " + captures.ToString(CultureInfo.InvariantCulture) + " actual captures; normal teardown." : "FAIL: native launcher migration preview did not complete.");
        return outcome;
    }
}
