using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Runtime;
using SkiaSharp;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly string[] NativeThemeLaunchSignatureKeys = ["TitleMain", "CardAccount", "CardVersion", "LaunchButton"];

    // Native theme IO, services, session, controllers and decoders are real. Only
    // game/account/instance data and explicit picker selections are isolated fixtures.
    private static int RunNativeThemePreview(string mode, string outputDirectory) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(() => RunNativeThemePreviewAsync(mode, Path.GetFullPath(outputDirectory)));

    private static async Task<int> RunNativeThemePreviewAsync(string mode, string outputDirectory)
    {
        if (mode is not ("light" or "dark")) throw new ArgumentException("Expected light or dark.", nameof(mode));
        Directory.CreateDirectory(outputDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], disableHardwareAcceleration: true,
            new AvaloniaUiStartupAppearance(mode == "dark" ? XsrUiThemeMode.Dark : XsrUiThemeMode.Light, ReducedMotion: true),
            cancellationToken: timeout.Token);
        NativeNxmlSettingsHarness? harness = null;
        ThemePackageService? service = null;
        DesktopThemeSession? session = null;
        DesktopAppearanceSession? appearance = null;
        DesktopPresentationSession? presentation = null;
        CustomAppearanceSession? customAppearance = null;
        DesktopMediaSession? media = null;
        AvaloniaUiShellWindow? window = null;
        AvaloniaUiSceneSurface? surface = null;
        Size acceptedWindowSize = default;
        Task<int>? lifetime = null;
        var actions = new AvaloniaUiPlatformActions();
        actions.SetDesktopPolicy(new(TrayEnabled: false));
        var effectiveAppearance = new DesktopEffectiveAppearanceSource();
        string? nextImport = null;
        string externalImage = "";
        string externalMedia = "";
        var trace = new ConcurrentQueue<string>();
        var showTrace = new JsonArray();
        int captureCount = 0, pickerCount = 0, outcome = 1;
        JsonObject NativeWindowFacts()
        {
            var candidate = window!;
            var screen = candidate.Screens.ScreenFromWindow(candidate) ?? candidate.Screens.Primary;
            var frame = candidate.FrameSize;
            return new()
            {
                ["renderScaling"] = candidate.RenderScaling,
                ["acceptedWidth"] = acceptedWindowSize.Width,
                ["acceptedHeight"] = acceptedWindowSize.Height,
                ["boundsWidth"] = candidate.Bounds.Width,
                ["boundsHeight"] = candidate.Bounds.Height,
                ["clientWidth"] = candidate.ClientSize.Width,
                ["clientHeight"] = candidate.ClientSize.Height,
                ["frameWidth"] = frame?.Width,
                ["frameHeight"] = frame?.Height,
                ["minimumWidth"] = candidate.MinWidth,
                ["minimumHeight"] = candidate.MinHeight,
                ["positionX"] = candidate.Position.X,
                ["positionY"] = candidate.Position.Y,
                ["surfaceWidth"] = surface?.Bounds.Width,
                ["surfaceHeight"] = surface?.Bounds.Height,
                ["workArea"] = screen is null ? null : new JsonObject
                {
                    ["x"] = screen.WorkingArea.X,
                    ["y"] = screen.WorkingArea.Y,
                    ["width"] = screen.WorkingArea.Width,
                    ["height"] = screen.WorkingArea.Height
                }
            };
        }
        try
        {
            await startup.InvokeAsync(() =>
            {
                harness = new();
                var fixture = harness.Fixture;
                AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
                AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("appearance.theme-mode", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, mode == "dark" ? "1" : "0"))).IsSuccess);
                AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("appearance.reduced-motion", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
                appearance = new(fixture.Shell, fixture.Store, actions, effectiveAppearance: effectiveAppearance);
                fixture.Shell.Renderer.ReducedMotion = true;
                string themesRoot = Path.Combine(AppContext.BaseDirectory, "Themes");
                AssertTrue(File.Exists(Path.Combine(themesRoot, "Advanced", "theme.json")));
                string fixtureDirectory = Nexa.Desktop.Program.CanonicalThemeValidationDirectory(fixture.TemporaryDirectory);
                externalImage = Path.Combine(fixtureDirectory, "user-selected-background.svg");
                File.Copy(Path.Combine(themesRoot, "Advanced", "assets", "background.svg"), externalImage);
                externalMedia = Path.Combine(fixtureDirectory, "user-selected-playlist");
                Directory.CreateDirectory(externalMedia);
                File.Copy(Path.Combine(themesRoot, "Advanced", "media", "sample.wav"),
                    Path.Combine(externalMedia, "first.wav"));
                File.Copy(Path.Combine(themesRoot, "Advanced", "media", "sample.wav"),
                    Path.Combine(externalMedia, "second.wav"));
                var queries = WorldFixtureQueries(fixture);
                var commands = new XsrCommandRouterBuilder();
                AssertTrue(fixture.Foundation.Commands.TryResolve(SettingsPolicyContract.SetCommand, out var setRoute));
                commands.Register<SettingsMutation>(SettingsPolicyContract.SetCommand, (command, token) =>
                    new(fixture.Foundation.Commands.Dispatch(setRoute, command, cancellationToken: token).Completion));
                service = ThemesRuntime.Register(queries, commands, fixture.TemporaryDirectory,
                    Path.Combine(themesRoot, "BuiltIn"), DesktopThemeSession.SupportedCapabilities());
                var queryRouter = queries.Build(new NoopDispatchObserver());
                var commandRouter = commands.Build(new NoopDispatchObserver());
                session = new(queryRouter, commandRouter, fixture.Store, fixture.Shell, actions, effectiveAppearance, trace.Enqueue);
                harness.Global.ThemeSession = session;
                harness.Global.ConfigureThemePickers((request, token) =>
                {
                    token.ThrowIfCancellationRequested(); pickerCount++;
                    if (request.Kind == ThemeOptionKind.Directory && request.ResourceKind is null)
                        return Task.FromResult<IReadOnlyList<string>>([nextImport ?? throw new InvalidOperationException("Missing explicit directory fixture selection.")]);
                    if (request.Kind == ThemeOptionKind.File && request.ResourceKind == ThemeResourceKind.Image)
                        return Task.FromResult<IReadOnlyList<string>>([externalImage]);
                    if (request.Kind == ThemeOptionKind.Directory && request.ResourceKind == ThemeResourceKind.Audio)
                        return Task.FromResult<IReadOnlyList<string>>([externalMedia]);
                    if (request.Kind == ThemeOptionKind.Files && request.ResourceKind == ThemeResourceKind.Audio)
                        return Task.FromResult<IReadOnlyList<string>>([Path.Combine(externalMedia, "first.wav"), Path.Combine(externalMedia, "second.wav")]);
                    throw new InvalidOperationException("Unexpected picker route: " + request);
                }, _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
                customAppearance = new(queryRouter, fixture.Store, fixture.Shell, actions, trace.Enqueue, effectiveAppearance);
                media = new(queryRouter, commandRouter, fixture.Store, fixture.Shell, fixture.Intents, actions, trace.Enqueue,
                    effectiveAppearance, saveThemeVolumeAsync: session.SetMediaVolumeAsync);
                session.WaitForAppearanceConsumers = media.WaitForGenerationAsync;
                presentation = new(fixture.Shell, fixture.Store, actions.SetWindowResizeEnabled, actions.SetAnimationFrameRate,
                    queryRouter, actions.PostToWindow, effectiveAppearance);
                fixture.Shell.Render(new(1040, 740));
            }, timeout.Token);
            var h = harness!;
            var f = h.Fixture;
            var themes = session!;
            await Pump(themes.InitialReady);
            await Pump(customAppearance!.InitialReady);
            await Pump(media!.InitialReady);
            await Pump(presentation!.InitialReady);
            await startup.PrepareShellAsync(f.Shell, actions, timeout.Token);
            await startup.WarmUpShellAsync(f.Shell, timeout.Token);
            lifetime = Task.Run(() => AvaloniaUiShellHost.Run(startup, f.Shell, actions));
            await Eventually(() =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not AvaloniaUiShellWindow candidate || !candidate.IsVisible) return false;
                window = candidate;
                surface = candidate.GetVisualDescendants().OfType<AvaloniaUiSceneSurface>().Single();
                SetPreviewWindowSize(1040);
                return Has("AccountName") && Find("AccountName").Text == "Player" && !themes.Snapshot.Busy;
            });

            string builtin = themes.Snapshot.Catalogue.Themes.Single(entry => entry.SourceKind == ThemeSourceKind.BuiltIn).SourceIdentity;
            string[] baseline = [];
            await SettleLaunch();
            await startup.InvokeAsync(() => baseline = LaunchSignature(), timeout.Token);
            await SessionOperation(() => themes.ActivateAsync(builtin, false, timeout.Token));
            await SettleLaunch();
            await startup.InvokeAsync(() =>
            {
                string[] after = LaunchSignature();
                for (int index = 0; index < baseline.Length; index++)
                    if (!string.Equals(baseline[index], after[index], StringComparison.Ordinal))
                        Console.Error.WriteLine("NTP_BUILTIN_DIFF\tbefore=" + baseline[index] + "\tafter=" + after[index]);
                AssertTrue(baseline.SequenceEqual(after, StringComparer.Ordinal));
            }, timeout.Token);
            await CapturePair("builtin-launch");

            await NavigateSettings(h.Global, "appearance");
            await CapturePair("builtin-settings");
            nextImport = Path.Combine(AppContext.BaseDirectory, "Themes", "Minimal");
            await Activate("Theme.ImportDirectory");
            await Eventually(() => themes.Snapshot.Catalogue.Themes.Any(entry => entry.Manifest.Id == "nexa.minimal")
                && PresentedId() == "nexa.minimal" && !themes.Snapshot.Busy);
            string minimal = themes.Snapshot.PresentedSourceIdentity!;
            await NavigateLaunch();
            await startup.InvokeAsync(() =>
            {
                AssertEqual(new XsrUiColor(0x68, 0xa5, 0xff), Find("TitleMain").VisualStyle.Foreground);
                AssertEqual(26d, Find("TitleMain").VisualStyle.FontSize);
            }, timeout.Token);
            await CapturePair("minimal-launch");

            await NavigateSettings(h.Global, "appearance");
            nextImport = Path.Combine(AppContext.BaseDirectory, "Themes", "Advanced");
            await Activate("Theme.ImportDirectory");
            await Eventually(() => PresentedId() == "nexa.advanced" && !themes.Snapshot.Busy);
            string advanced = themes.Snapshot.PresentedSourceIdentity!;
            await NavigateLaunch();
            await CapturePair("advanced-launch");
            await startup.InvokeAsync(() =>
            {
                AssertTrue(Find("TitleMain").FontFamily is not null);
                AssertTrue(Find("TitleMain").Transform is not null);
                AssertTrue(Find("CardAccount").Shadow is not null);
                AssertTrue(Find("CardAccount").BackgroundGradient is not null);
                AssertTrue(f.Shell.Renderer.TextLayoutProvider is not null);
                AssertTrue(actions.PreparedTextLayoutProvider is not null);
                AssertEqual(f.Shell.Renderer.ThemeActivationGeneration, actions.ThemeAppearanceGeneration);
                AssertTrue(surface!.Scene!.Nodes.Any(node => node.BackgroundImage is not null));
            }, timeout.Token);

            await NavigateSettings(h.Global, "appearance");
            string descriptionKey = OptionKey(advanced, "description") + ".Input";
            var description = await Draft(descriptionKey, "Native <draft> {state private}");
            await Activate(OptionKey(advanced, "title-size") + ".Increase");
            await Activate(OptionKey(advanced, "radius") + ".Decrease");
            await Draft(OptionKey(advanced, "accent") + ".Input", "#73b8ff");
            await Activate(OptionKey(advanced, "music.enabled") + ".Toggle");
            await Eventually(() => Exists(OptionKey(advanced, "music.path") + ".Pick"));
            await Activate(OptionKey(advanced, "background-fit") + ".Choice." + ThemePreviewIdentity("contain"));
            await Activate(OptionKey(advanced, "background-path") + ".Pick");
            await Eventually(() => themes.Snapshot.Current.Grants.Any(grant => grant.SourceIdentity == advanced && grant.CanonicalPath == externalImage)
                && !themes.Snapshot.Busy);
            await Activate(OptionKey(advanced, "media-directory") + ".Pick");
            await Activate(OptionKey(advanced, "music.path") + ".Pick");
            await Activate("Theme.OptionsSave");
            await Eventually(() => SavedOptions(advanced)?.Values.GetValueOrDefault("title-size")?.Value == "27"
                && SavedOptions(advanced)?.Values.GetValueOrDefault("background-path")?.Value == externalImage && !themes.Snapshot.Busy);
            await startup.InvokeAsync(() =>
            {
                var options = SavedOptions(advanced)!;
                AssertEqual("15", options.Values["radius"].Value);
                AssertEqual("contain", options.Values["background-fit"].Value);
                AssertEqual("#73b8ff", options.Values["accent"].Value);
                AssertEqual("true", options.Values["music.enabled"].Value);
                AssertEqual(externalMedia, options.Values["media-directory"].Value);
                AssertEqual(2, options.Values["music.path"].Values!.Count);
                AssertTrue(effectiveAppearance.Current!.AudioPlaylist.Count >= 3);
                AssertTrue(effectiveAppearance.Current.AudioPlaylist.All(File.Exists));
                AssertEqual("Native <draft> {state private}", options.Values["description"].Value);
            }, timeout.Token);
            description = await Draft(descriptionKey, "Unsaved native <draft> {state private}");
            await CapturePair("advanced-options", description);
            await Show(OptionKey(advanced, "ornament") + ".Toggle");
            await CapturePair("advanced-controls-style");
            await Show(OptionKey(advanced, "music.autoplay") + ".Toggle");
            await CapturePair("advanced-controls-media");
            await Show("Theme.Grants");
            await CapturePair("advanced-grants");

            // Actual hot activation while an unrelated business editor is focused.
            await NavigateSettings(h.Global, "game");
            var argument = await Draft("SettingsArgument.game.arguments.0", "--retained literal <argument> {state private}");
            XsrUiTextInput? argumentInput = null;
            int argumentCaret = 0;
            double argumentScroll = 0;
            await startup.InvokeAsync(() =>
            {
                argumentInput = f.Shell.Tree.GetComponent<XsrUiTextInput>(argument)!;
                argumentCaret = argumentInput.SelectionEnd;
                argumentScroll = Sections().Scroll!.Value.OffsetY;
            }, timeout.Token);
            await SessionOperation(() => themes.ActivateAsync(minimal, false, timeout.Token));
            await SessionOperation(() => themes.ActivateAsync(advanced, false, timeout.Token));
            await startup.InvokeAsync(() =>
            {
                AssertEqual(argument, Owned("SettingsArgument.game.arguments.0"));
                AssertTrue(ReferenceEquals(argumentInput, f.Shell.Tree.GetComponent<XsrUiTextInput>(argument)));
                AssertEqual("--retained literal <argument> {state private}", argumentInput!.ReadDraft());
                AssertEqual(argumentCaret, argumentInput.SelectionEnd);
                AssertEqual(argument, f.Shell.Renderer.Focused);
                AssertEqual(argumentScroll, Sections().Scroll!.Value.OffsetY);
            }, timeout.Token);
            await CapturePair("retained-game-editor", argument);

            await NavigateSettings(h.Instance, "mods");
            await Eventually(() => surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity) == "ManagementContentRow"));
            XsrUiEntityId contentRow = default;
            double contentScroll = 0;
            await startup.InvokeAsync(() =>
            {
                var sections = Sections();
                AssertTrue(f.Shell.Renderer.PointerScroll(new(sections.Rect.X + 20, sections.Rect.Y + 24), 260));
                surface!.CommitScene();
                contentRow = surface.Scene!.Nodes.First(node => f.Shell.Tree.Name(node.Entity) == "ManagementContentRow" && Visible(node)).Entity;
                contentScroll = Sections().Scroll!.Value.OffsetY;
                AssertTrue(contentScroll > 0);
            }, timeout.Token);
            await SessionOperation(() => themes.ActivateAsync(minimal, false, timeout.Token));
            await SessionOperation(() => themes.ActivateAsync(advanced, false, timeout.Token));
            await startup.InvokeAsync(() =>
            {
                AssertTrue(f.Shell.Tree.IsAlive(contentRow));
                AssertTrue(surface!.Scene!.Nodes.Any(node => node.Entity == contentRow));
                AssertEqual(contentScroll, Sections().Scroll!.Value.OffsetY);
            }, timeout.Token);
            await CapturePair("retained-content-list");

            await NavigateSettings(h.Instance, "screenshots");
            await Eventually(() => surface!.Scene!.Nodes.Any(node => f.Shell.Tree.Name(node.Entity).StartsWith("ManagementScreenshot.", StringComparison.Ordinal) && Visible(node)));
            await startup.InvokeAsync(() =>
            {
                var screenshot = surface!.Scene!.Nodes.First(node => f.Shell.Tree.Name(node.Entity).StartsWith("ManagementScreenshot.", StringComparison.Ordinal) && Visible(node));
                AssertTrue(f.Shell.Renderer.Focus(screenshot.Entity));
                AssertTrue(f.Shell.Renderer.Activate(screenshot.Entity));
                surface.CommitScene();
            }, timeout.Token);
            await Eventually(() => Has("ScreenshotPreview.Crop"));
            await Activate("ScreenshotPreview.Crop");
            var crop = await Draft("ScreenshotCropWidth", "480");
            await SessionOperation(() => themes.ActivateAsync(minimal, false, timeout.Token));
            await SessionOperation(() => themes.ActivateAsync(advanced, false, timeout.Token));
            await startup.InvokeAsync(() =>
            {
                AssertEqual(crop, Owned("ScreenshotCropWidth"));
                AssertEqual(crop, f.Shell.Renderer.Focused);
                AssertEqual("480", f.Shell.Tree.GetComponent<XsrUiTextInput>(crop)!.ReadDraft());
                AssertEqual(0, h.CropWrites);
            }, timeout.Token);
            await CapturePair("retained-screenshot-modal", crop);
            await Activate("ScreenshotPreview.Close");
            await Eventually(() => !Has("ScreenshotPreviewImage"));

            await NavigateLaunch();
            await SetReducedMotionPreference(false);
            await startup.InvokeAsync(() =>
            {
                SetPreviewWindowSize(1040);
                ActivateThemePreviewWindow(window!);
            }, timeout.Token);
            await SettlePreviewWindowSize();
            await Eventually(() => window!.IsActive && actions.IsWindowActive && !f.Shell.Renderer.EffectiveReducedMotion
                && f.Shell.Renderer.HasThemeAnimations);
            var animationValues = new List<double>();
            for (int frame = 0; frame < 6; frame++)
            {
                await Task.Delay(160, timeout.Token);
                await startup.InvokeAsync(() =>
                {
                    surface!.CommitScene(); animationValues.Add(Find("TitleMain").VisualStyle.Opacity);
                    Capture("advanced-animation-frame-" + frame.ToString("00", CultureInfo.InvariantCulture), 1040);
                }, timeout.Token);
            }
            AssertTrue(animationValues.Max() - animationValues.Min() > .01);
            await SetReducedMotionPreference(true);
            await startup.InvokeAsync(() =>
            {
                surface!.CommitScene();
                AssertTrue(f.Shell.Renderer.ReducedMotion);
                AssertFalse(f.Shell.Renderer.HasThemeAnimations);
                AssertEqual(1d, Find("TitleMain").VisualStyle.Opacity);
                AssertEqual(0, h.CropWrites); AssertEqual(0, h.ServerWrites); AssertEqual(0, h.ExportWrites);
                AssertTrue(pickerCount >= 5);
            }, timeout.Token);
            outcome = 0;

            string? PresentedId() => themes.Snapshot.Catalogue.Themes.FirstOrDefault(entry => entry.SourceIdentity == themes.Snapshot.PresentedSourceIdentity)?.Manifest.Id;
            ThemeOptionState? SavedOptions(string identity) => themes.Snapshot.Current.Options.FirstOrDefault(options => options.SourceIdentity == identity);
            bool Has(string key) => surface?.Scene?.Nodes.Any(node => f.Shell.Tree.Name(node.Entity) == key) == true;
            bool Exists(string key) => OwnedOptional(key).IsAssigned;
            XsrUiSceneNode Find(string key) => FindByKey(f.Shell, surface!.Scene!, key);
            bool Visible(XsrUiSceneNode node) => node.Rect.Width > 0 && node.Rect.Height > 0
                && node.Rect.Y < f.Shell.Renderer.Viewport.Height && node.Rect.Y + node.Rect.Height > 0
                && (node.ClipRect is null || node.ClipRect.Value.Contains(new(node.Rect.X + Math.Min(node.Rect.Width / 2, 12), node.Rect.Y + Math.Min(node.Rect.Height / 2, 12))));
            XsrUiEntityId OwnedOptional(string key)
            {
                List<XsrUiEntityId> matches = [];
                foreach (var root in f.Shell.Stage.Overlays.Prepend(f.Shell.Stage.Navigation.Current))
                    f.Shell.Tree.Walk(root, entity =>
                    {
                        var parent = f.Shell.Tree.Parent(entity);
                        if (parent.IsAssigned && f.Shell.Tree.Name(parent) == "SettingsPager"
                            && f.Shell.Tree.GetComponent<XsrUiPager>(parent) is { } pager)
                        {
                            var pages = f.Shell.Tree.Children(parent);
                            if (pager.PageIndex < 0 || pager.PageIndex >= pages.Count || pages[pager.PageIndex] != entity) return false;
                        }
                        if (f.Shell.Tree.Name(entity) == key) matches.Add(entity);
                        return true;
                    });
                AssertTrue(matches.Count <= 1);
                return matches.SingleOrDefault();
            }
            XsrUiEntityId Owned(string key)
            { var entity = OwnedOptional(key); AssertTrue(entity.IsAssigned); return entity; }
            XsrUiSceneNode Sections() => surface!.Scene!.Nodes.Single(node => f.Shell.Tree.Name(node.Entity) == "SettingsSections" && node.Scroll is not null);
            string[] LaunchSignature() => NativeThemeLaunchSignatureKeys.Select(key =>
            {
                var node = Find(key);
                var command = f.Shell.Tree.GetComponent<XsrUiCommandBinding>(node.Entity)?.Command;
                return key + "|" + node.Entity + "|" + command + "|" + node.Role + "|" + node.Rect + "|" + node.Text + "|" + node.VisualStyle;
            }).ToArray();
            async Task SettleLaunch()
            {
                Task? pending = null;
                await startup.InvokeAsync(() => pending = f.Controller.WaitUntilIdle(), timeout.Token);
                await Pump(pending!);
                await SessionOperation(() => themes.RefreshAsync(timeout.Token));
                await startup.InvokeAsync(() => SetPreviewWindowSize(1040), timeout.Token);
                string[] previous = [];
                long stableSince = Stopwatch.GetTimestamp();
                await Eventually(() =>
                {
                    string[] current = LaunchSignature();
                    bool boundsReady = NativeWindowSizeReady();
                    if (!boundsReady || themes.Snapshot.Busy || !previous.SequenceEqual(current, StringComparer.Ordinal))
                    { previous = current; stableSince = Stopwatch.GetTimestamp(); return false; }
                    return Stopwatch.GetElapsedTime(stableSince) >= TimeSpan.FromMilliseconds(150);
                });
            }
            void SetPreviewWindowSize(double requestedWidth)
            {
                var candidate = window!;
                var screen = candidate.Screens.ScreenFromWindow(candidate) ?? candidate.Screens.Primary
                    ?? throw new InvalidOperationException("Native theme preview has no available screen.");
                double scale = candidate.RenderScaling;
                AssertTrue(double.IsFinite(scale) && scale > 0);
                var frame = candidate.FrameSize ?? candidate.ClientSize;
                double nativeWidth = Math.Max(0, frame.Width - candidate.ClientSize.Width);
                double nativeHeight = Math.Max(0, frame.Height - candidate.ClientSize.Height);
                // Work areas use physical pixels, while native client requests use DIPs.
                // Leave two physical pixels per edge so the native frame fits as well.
                double availableWidth = Math.Floor((screen.WorkingArea.Width - 4) / scale - nativeWidth);
                double availableHeight = Math.Floor((screen.WorkingArea.Height - 4) / scale - nativeHeight);
                if (availableWidth < candidate.MinWidth || availableHeight < candidate.MinHeight)
                    throw new InvalidOperationException("Native screen work area cannot fit the launcher's minimum client size: "
                        + NativeWindowFacts().ToJsonString());
                acceptedWindowSize = new(Math.Min(Math.Max(requestedWidth, candidate.MinWidth), availableWidth),
                    Math.Min(Math.Max(740, candidate.MinHeight), availableHeight));
                candidate.Width = acceptedWindowSize.Width;
                candidate.Height = acceptedWindowSize.Height;
                candidate.Position = new(screen.WorkingArea.X + Math.Max(0,
                        (screen.WorkingArea.Width - (int)Math.Ceiling((acceptedWindowSize.Width + nativeWidth) * scale)) / 2),
                    screen.WorkingArea.Y + Math.Max(0,
                        (screen.WorkingArea.Height - (int)Math.Ceiling((acceptedWindowSize.Height + nativeHeight) * scale)) / 2));
                candidate.UpdateLayout(); surface!.CommitScene();
                Console.WriteLine("NTP_NATIVE_WINDOW_SIZE\trequested=" + requestedWidth.ToString(CultureInfo.InvariantCulture)
                    + "\t" + NativeWindowFacts().ToJsonString());
            }
            bool NativeWindowSizeReady()
            {
                double tolerance = 1 / window!.RenderScaling + .01;
                bool Near(double actual, double expected) => Math.Abs(actual - expected) <= tolerance;
                var viewport = f.Shell.Renderer.Viewport;
                return Near(window.Bounds.Width, acceptedWindowSize.Width) && Near(window.Bounds.Height, acceptedWindowSize.Height)
                    && Near(window.ClientSize.Width, acceptedWindowSize.Width) && Near(window.ClientSize.Height, acceptedWindowSize.Height)
                    && surface!.Bounds.Width > 0 && surface.Bounds.Height > 0
                    && surface.Bounds.Width <= window.ClientSize.Width + tolerance && surface.Bounds.Height <= window.ClientSize.Height + tolerance
                    && Near(viewport.Width, surface.Bounds.Width) && Near(viewport.Height, surface.Bounds.Height);
            }
            async Task SettlePreviewWindowSize()
            {
                Size previousClient = default;
                XsrUiSize previousViewport = default;
                long stableSince = Stopwatch.GetTimestamp();
                await Eventually(() =>
                {
                    var currentClient = window!.ClientSize;
                    var currentViewport = f.Shell.Renderer.Viewport;
                    if (!NativeWindowSizeReady() || previousClient != currentClient || previousViewport != currentViewport)
                    {
                        previousClient = currentClient; previousViewport = currentViewport;
                        stableSince = Stopwatch.GetTimestamp(); return false;
                    }
                    return Stopwatch.GetElapsedTime(stableSince) >= TimeSpan.FromMilliseconds(150);
                });
            }
            async Task Pump(Task operation)
            {
                while (!operation.IsCompleted)
                {
                    await startup.InvokeAsync(() =>
                    {
                        if (surface is null) harness!.Fixture.Shell.Render(new(1040, 740));
                        else surface.CommitScene();
                    }, timeout.Token);
                    await Task.Delay(10, timeout.Token);
                }
                await operation.WaitAsync(timeout.Token);
            }
            async Task SessionOperation(Func<Task<XsrResult>> start)
            {
                Task<XsrResult>? operation = null;
                await startup.InvokeAsync(() => operation = start(), timeout.Token);
                await Pump(operation!);
                AssertTrue(operation!.Result.IsSuccess);
                await Eventually(() => !themes.Snapshot.Busy);
            }
            async Task SetReducedMotionPreference(bool reduced)
            {
                Task<XsrResult>? operation = null;
                await startup.InvokeAsync(() =>
                {
                    AssertTrue(f.Foundation.Commands.TryResolve(SettingsPolicyContract.SetCommand, out var route));
                    operation = f.Foundation.Commands.Dispatch(route,
                        new SettingsMutation("appearance.reduced-motion", SettingsLayer.Global,
                            new(SettingsOverrideMode.Custom, reduced ? "true" : "false")), cancellationToken: timeout.Token).Completion;
                }, timeout.Token);
                await Pump(operation!); AssertTrue(operation!.Result.IsSuccess);
                await Eventually(() => !themes.Snapshot.Busy && f.Shell.Renderer.ReducedMotion == reduced
                    && effectiveAppearance.Current?.Settings.Values.Any(value => value.Key == "appearance.reduced-motion"
                        && value.Value.Value == (reduced ? "true" : "false")) == true);
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
            async Task NavigateLaunch()
            {
                await startup.InvokeAsync(() => Emit(f.Intents, "ui.navigation.launch"), timeout.Token);
                Task? pending = null;
                await startup.InvokeAsync(() => pending = f.Controller.WaitUntilIdle(), timeout.Token);
                await Pump(pending!);
                await Eventually(() => Has("LaunchHomeWorkspace") && Has("AccountName"));
            }
            async Task NavigateSettings(SettingsPageController controller, string section)
            {
                await startup.InvokeAsync(() => { f.Shell.Stage.Navigation.Replace(controller.Page); surface!.CommitScene(); }, timeout.Token);
                await Eventually(() => Has("SettingsNav." + section));
                await startup.InvokeAsync(() => { Emit(f.Intents, "ui.settings.section", Find("SettingsNav." + section).Entity); surface!.CommitScene(); }, timeout.Token);
                await Eventually(() => controller.SelectedSection == section && (section != "appearance" || Has("Theme.ImportDirectory")));
                await startup.InvokeAsync(() =>
                {
                    var sections = Sections();
                    f.Shell.Tree.GetComponent<XsrUiScroll>(sections.Entity)!.OffsetY = 0;
                    f.Shell.Tree.MarkDirty(sections.Entity, XsrUiDirtyKinds.Layout); surface!.CommitScene();
                }, timeout.Token);
            }
            async Task Show(string key)
            {
                int missingDirection = 1, missingBoundaries = 0;
                for (int step = 0; step < 100; step++)
                {
                    bool visible = false;
                    await startup.InvokeAsync(() =>
                    {
                        surface!.CommitScene();
                        var entity = Owned(key);
                        var target = surface.Scene!.Nodes.Where(node => node.Entity == entity).Select(node => (XsrUiSceneNode?)node).SingleOrDefault();
                        visible = target is { } shown && Visible(shown);
                        if (visible) return;
                        XsrUiSceneNode? scroll = null;
                        for (var ancestor = f.Shell.Tree.Parent(entity); ancestor.IsAssigned; ancestor = f.Shell.Tree.Parent(ancestor))
                        {
                            if (f.Shell.Tree.GetComponent<XsrUiScroll>(ancestor) is null) continue;
                            scroll = surface.Scene.Nodes.Where(node => node.Entity == ancestor).Select(node => (XsrUiSceneNode?)node).SingleOrDefault();
                            if (scroll is not null) break;
                        }
                        if (scroll is not { } owner) throw new InvalidOperationException("Theme preview target has no visible scroll owner: " + key);
                        double delta;
                        if (target is { } above) delta = above.Rect.Y < owner.Rect.Y ? -180 : 180;
                        else if (owner.Scroll is { } scrollFacts)
                        {
                            // Culling omits both rows above and rows below the viewport.
                            // Scan with actual wheel input; absence is not a direction hint.
                            if (missingDirection > 0 && scrollFacts.OffsetY >= scrollFacts.MaximumOffsetY - .5)
                            { missingDirection = -1; missingBoundaries++; }
                            else if (missingDirection < 0 && scrollFacts.OffsetY <= .5)
                            { missingDirection = 1; missingBoundaries++; }
                            if (missingBoundaries >= 2)
                                throw new InvalidOperationException("Theme preview target remained culled after scanning its complete scroll range: " + key);
                            delta = missingDirection * Math.Max(16, Math.Min(180, scrollFacts.ViewportHeight * .75));
                        }
                        else throw new InvalidOperationException("Theme preview target has no bounded scroll facts: " + key);
                        var point = new XsrUiPoint(owner.Rect.X + 20, owner.Rect.Y + 24);
                        bool handled = f.Shell.Renderer.PointerScroll(point, delta);
                        if (step < 3 || step >= 97)
                            showTrace.Add((JsonNode)new JsonObject
                            {
                                ["key"] = key,
                                ["step"] = step,
                                ["target"] = target is { } actual ? NodeFacts(f.Shell, actual) : null,
                                ["owner"] = NodeFacts(f.Shell, owner),
                                ["pointerX"] = point.X,
                                ["pointerY"] = point.Y,
                                ["delta"] = delta,
                                ["handled"] = handled,
                                ["offsetAfterInput"] = f.Shell.Tree.GetComponent<XsrUiScroll>(owner.Entity)?.OffsetY,
                            });
                        surface.CommitScene();
                    }, timeout.Token);
                    if (visible) return;
                    await Task.Delay(10, timeout.Token);
                }
                throw new InvalidOperationException("Theme preview target did not enter the viewport: " + key);
            }
            async Task Activate(string key)
            {
                await Show(key);
                await startup.InvokeAsync(() => { AssertTrue(f.Shell.Renderer.Activate(Find(key).Entity)); surface!.CommitScene(); }, timeout.Token);
                await Eventually(() =>
                {
                    var current = OwnedOptional(key);
                    return (!current.IsAssigned || f.Shell.Tree.GetComponent<XsrUiInput>(current)?.Enabled != false)
                        && !themes.Snapshot.Busy;
                });
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
                    await startup.InvokeAsync(() => SetPreviewWindowSize(width), timeout.Token);
                    await SettlePreviewWindowSize();
                    await Task.Delay(80, timeout.Token);
                    await startup.InvokeAsync(() =>
                    {
                        surface!.CommitScene();
                        AssertTrue(NativeWindowSizeReady());
                        AssertEqual(width > 900, f.Shell.Renderer.Viewport.Width > 900);
                        AssertEqual(mode == "dark", f.Shell.Renderer.ColorScheme.IsDark);
                        if (retainedInput.IsAssigned)
                        {
                            AssertTrue(f.Shell.Tree.IsAlive(retainedInput)); AssertEqual(retainedInput, f.Shell.Renderer.Focused);
                            AssertEqual(draft, input!.ReadDraft()); AssertEqual(caret, input.SelectionEnd);
                        }
                        if (stage.EndsWith("-launch", StringComparison.Ordinal))
                        {
                            var account = Find("CardAccount"); var right = Find("RightColumn");
                            AssertTrue(account.Rect.Width > 100 && right.Rect.Width > 100);
                            if (stage == "advanced-launch")
                            {
                                AssertEqual(width > 900, right.Rect.X > account.Rect.X + account.Rect.Width / 2);
                                var launch = Find("LaunchButton");
                                AssertTrue(Visible(launch) && launch.Rect.Y + launch.Rect.Height <= f.Shell.Renderer.Viewport.Height + 1);
                            }
                        }
                        Capture(stage, width);
                    }, timeout.Token);
                }
            }
            void Capture(string stage, double width)
            {
                window!.UpdateLayout();
                AssertEqual(f.Shell.Renderer.ThemeActivationGeneration, actions.ThemeAppearanceGeneration);
                double scale = window.RenderScaling;
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                bitmap.Render(window);
                string name = "ntp-" + mode + "-" + stage + "-" + width.ToString(CultureInfo.InvariantCulture);
                string pngPath = Path.Combine(outputDirectory, name + ".png");
                bitmap.Save(pngPath, PngBitmapEncoderOptions.Default);
                var titleRect = Find("TitleMain").Rect;
                Point titleOrigin = surface!.TranslatePoint(new(titleRect.X, titleRect.Y), window)
                    ?? throw new InvalidOperationException("Actual title surface is detached from its capture window.");
                var titleCaptureRect = new XsrUiRect(titleOrigin.X, titleOrigin.Y, titleRect.Width, titleRect.Height);
                int titleInkPixels = TitleInkPixels(pngPath, titleCaptureRect, scale);
                if (stage == "advanced-launch" || stage.StartsWith("advanced-animation-frame-", StringComparison.Ordinal))
                {
                    Console.WriteLine("NTP_NATIVE_TITLE_INK\t" + name + "\t" + titleInkPixels.ToString(CultureInfo.InvariantCulture));
                    AssertTrue(titleInkPixels >= 12);
                }
                var manifest = new JsonObject
                {
                    ["stage"] = stage,
                    ["mode"] = mode,
                    ["themeId"] = PresentedId(),
                    ["width"] = bitmap.PixelSize.Width,
                    ["height"] = bitmap.PixelSize.Height,
                    ["requestedWidth"] = width,
                    ["nativeWindow"] = NativeWindowFacts(),
                    ["viewportWidth"] = f.Shell.Renderer.Viewport.Width,
                    ["viewportHeight"] = f.Shell.Renderer.Viewport.Height,
                    ["activationGeneration"] = f.Shell.Renderer.ThemeActivationGeneration,
                    ["nativeGeneration"] = actions.ThemeAppearanceGeneration,
                    ["fontProviderRevision"] = f.Shell.Renderer.TextLayoutProvider?.Revision,
                    ["fontResource"] = Find("TitleMain").FontFamily,
                    ["titleScene"] = NodeFacts(f.Shell, Find("TitleMain")),
                    ["titleCaptureRect"] = RectFacts(titleCaptureRect),
                    ["titleContrastingPixels"] = titleInkPixels,
                    ["windowActive"] = window.IsActive,
                    ["platformWindowActive"] = actions.IsWindowActive,
                    ["reducedMotion"] = f.Shell.Renderer.ReducedMotion,
                    ["optionalMotionSuspended"] = f.Shell.Renderer.OptionalMotionSuspended,
                    ["effectiveReducedMotion"] = f.Shell.Renderer.EffectiveReducedMotion,
                    ["titleOpacity"] = Find("TitleMain").VisualStyle.Opacity,
                    ["backgroundResources"] = surface!.Scene!.Nodes.Count(node => node.BackgroundImage is not null),
                    ["shadowNodes"] = surface.Scene.Nodes.Count(node => node.Shadow is not null),
                    ["gradientNodes"] = surface.Scene.Nodes.Count(node => node.BackgroundGradient is not null),
                    ["focused"] = f.Shell.Renderer.Focused.IsAssigned ? f.Shell.Tree.Name(f.Shell.Renderer.Focused) : null,
                    ["grants"] = themes.Snapshot.Current.Grants.Count,
                    ["sceneNodes"] = surface.Scene.Count,
                    ["renderer"] = "actual software Avalonia native window RenderTargetBitmap, not OS compositor capture",
                    ["runtime"] = "real ThemesRuntime, ThemePackageService, DesktopThemeSession, SettingsPageController, native SVG/font resources, shipped NXML/NSS/theme files",
                    ["fixtures"] = "isolated settings/theme files, explicit picker selections, offline profile and controlled typed instance data; no network, authentication, game, installation or export/crop/server writes",
                };
                File.WriteAllText(Path.Combine(outputDirectory, name + ".json"), manifest.ToJsonString());
                Console.WriteLine("NTP_NATIVE_PREVIEW_STAGE\t" + mode + "\t" + stage + "\t" + width.ToString(CultureInfo.InvariantCulture));
                captureCount++;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            Console.Error.WriteLine("FAIL: native NTP preview\n" + error);
            foreach (string message in trace) Console.Error.WriteLine("NTP native fixture: " + message);
            if (window is not null && surface?.Scene is not null && harness is not null && !startup.Completion.IsCompleted)
            {
                try
                {
                    await startup.InvokeAsync(() =>
                    {
                        window.UpdateLayout(); surface.CommitScene();
                        double scale = window.RenderScaling;
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                        bitmap.Render(window);
                        bitmap.Save(Path.Combine(outputDirectory, "failure-" + mode + ".png"), PngBitmapEncoderOptions.Default);
                        var nodes = new JsonArray();
                        foreach (var node in surface.Scene!.Nodes) nodes.Add((JsonNode)NodeFacts(harness.Fixture.Shell, node));
                        var failure = new JsonObject
                        {
                            ["error"] = error.Message,
                            ["mode"] = mode,
                            ["showAttempts"] = showTrace,
                            ["width"] = bitmap.PixelSize.Width,
                            ["height"] = bitmap.PixelSize.Height,
                            ["nativeWindow"] = NativeWindowFacts(),
                            ["viewportWidth"] = harness.Fixture.Shell.Renderer.Viewport.Width,
                            ["viewportHeight"] = harness.Fixture.Shell.Renderer.Viewport.Height,
                            ["scene"] = nodes
                        };
                        File.WriteAllText(Path.Combine(outputDirectory, "failure-" + mode + ".json"), failure.ToJsonString());
                        Console.Error.WriteLine("NTP_NATIVE_FAILURE_CAPTURE\t" + Path.Combine(outputDirectory, "failure-" + mode + ".json"));
                        Console.Error.WriteLine("NTP_NATIVE_SHOW_TRACE\t" + showTrace.ToJsonString());
                    }, CancellationToken.None);
                }
                catch (Exception captureError) when (captureError is not OutOfMemoryException and not AccessViolationException)
                { Console.Error.WriteLine("NTP native failure capture could not complete: " + captureError); }
            }
        }
        finally
        {
            if (harness is not null && !startup.Completion.IsCompleted)
                await startup.InvokeAsync(() => { harness.Global.ThemeSession = null; harness.Fixture.Shell.Renderer.ReducedMotion = true; }, CancellationToken.None);
            if (media is not null) await media.DisposeAsync();
            if (customAppearance is not null) await customAppearance.DisposeAsync();
            if (session is not null) await session.DisposeAsync();
            void Cleanup() { window?.Close(); presentation?.Dispose(); appearance?.Dispose(); service?.Dispose(); harness?.Dispose(); }
            if (!startup.Completion.IsCompleted) await Dispatcher.UIThread.InvokeAsync(Cleanup);
            else Cleanup();
            if (lifetime is not null) AssertEqual(0, await lifetime.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Console.WriteLine(outcome == 0
            ? "PASS: actual native NTP preview; " + captureCount.ToString(CultureInfo.InvariantCulture) + " captured stages, two viewport widths, real theme admission/parameter/grant routes, SVG/fonts/grid/paint/motion and retained business editor/modal/list state; normal teardown."
            : "FAIL: native NTP preview did not complete.");
        return outcome;
    }

    private static string ThemePreviewIdentity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20];
    private static string OptionKey(string identity, string option) => "Theme.Option." + ThemePreviewIdentity(identity + "/" + option);
    private static void ActivateThemePreviewWindow(AvaloniaUiShellWindow window)
    {
        window.Activate();
        if (!OperatingSystem.IsLinux()) return;
        var handle = window.TryGetPlatformHandle();
        AssertTrue(handle?.HandleDescriptor == "XID" && handle.Handle != 0);
        nint display = ThemePreviewX11.XOpenDisplay(0);
        AssertTrue(display != 0);
        try
        {
            // A normal desktop WM activates this visible main window. A standalone
            // Xvfb display requires actual core focus; never synthesize host activity.
            _ = ThemePreviewX11.XSetInputFocus(display, (nuint)handle!.Handle, 2, 0);
            _ = ThemePreviewX11.XSync(display, 0);
            _ = ThemePreviewX11.XGetInputFocus(display, out nuint focused, out _);
            AssertEqual((nuint)handle.Handle, focused);
        }
        finally { _ = ThemePreviewX11.XCloseDisplay(display); }
    }
    private static class ThemePreviewX11
    {
        [DllImport("libX11.so.6", ExactSpelling = true)] internal static extern nint XOpenDisplay(nint name);
        [DllImport("libX11.so.6", ExactSpelling = true)] internal static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6", ExactSpelling = true)] internal static extern int XSetInputFocus(nint display, nuint window, int revertTo, nuint time);
        [DllImport("libX11.so.6", ExactSpelling = true)] internal static extern int XGetInputFocus(nint display, out nuint window, out int revertTo);
        [DllImport("libX11.so.6", ExactSpelling = true)] internal static extern int XSync(nint display, int discard);
    }
    private static JsonObject RectFacts(XsrUiRect rect) => new() { ["x"] = rect.X, ["y"] = rect.Y, ["width"] = rect.Width, ["height"] = rect.Height };
    private static int TitleInkPixels(string pngPath, XsrUiRect rect, double scale)
    {
        using var decoded = SKBitmap.Decode(pngPath) ?? throw new InvalidOperationException("Cannot decode the actual title capture.");
        int left = Math.Clamp((int)Math.Floor(rect.X * scale), 0, decoded.Width);
        int top = Math.Clamp((int)Math.Floor(rect.Y * scale), 0, decoded.Height);
        int right = Math.Clamp((int)Math.Ceiling((rect.X + rect.Width) * scale), left, decoded.Width);
        int bottom = Math.Clamp((int)Math.Ceiling((rect.Y + rect.Height) * scale), top, decoded.Height);
        AssertTrue(right > left && bottom > top && (long)(right - left) * (bottom - top) <= 131072);
        Dictionary<int, int> counts = [];
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                var pixel = decoded.GetPixel(x, y);
                int color = (pixel.Red << 16) | (pixel.Green << 8) | pixel.Blue;
                counts[color] = counts.GetValueOrDefault(color) + 1;
            }
        int background = counts.MaxBy(pair => pair.Value).Key;
        return counts.Where(pair => Math.Abs((pair.Key >> 16) - (background >> 16))
            + Math.Abs(((pair.Key >> 8) & 255) - ((background >> 8) & 255))
            + Math.Abs((pair.Key & 255) - (background & 255)) >= 48).Sum(pair => pair.Value);
    }
    private static JsonObject NodeFacts(XsrUiShell shell, XsrUiSceneNode node) => new()
    {
        ["key"] = shell.Tree.Name(node.Entity),
        ["entity"] = node.Entity.ToString(),
        ["text"] = node.Text,
        ["rect"] = RectFacts(node.Rect),
        ["layoutRect"] = node.LayoutRect is { } layout ? RectFacts(layout) : null,
        ["clipRect"] = node.ClipRect is { } clip ? RectFacts(clip) : null,
        ["transform"] = node.Transform?.ToString(),
        ["font"] = node.FontFamily,
        ["fontSize"] = node.VisualStyle.FontSize,
        ["foreground"] = node.VisualStyle.Foreground.ToString(),
        ["opacity"] = node.VisualStyle.Opacity,
        ["presentationOpacity"] = node.PresentationOpacity,
        ["role"] = node.Role.ToString(),
        ["enabled"] = node.IsEnabled,
        ["scrollOffsetY"] = node.Scroll?.OffsetY,
        ["scrollFacts"] = node.Scroll?.ToString(),
    };
}
