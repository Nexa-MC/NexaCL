using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;
using SkiaSharp;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static bool _themeActivationVerified;
    private static void ThemeNativeActivationRetainsWindowTrayAndRetiresResources() => AssertTrue(_themeActivationVerified);

    private static void ThemeRecoveryBeforeNativeAttachmentReturnsOwnedLease()
    {
        AssertTrue(Application.Current is null);
        var actions = new AvaloniaUiPlatformActions();
        using var prepared = actions.PrepareThemeAppearanceAsync(new(1, UseNativeTextLayout: false)).GetAwaiter().GetResult();
        AssertFalse(prepared.IsNativePrepared);
        AssertTrue(actions.CommitThemeAppearance(prepared) is null);
        AssertTrue(ReferenceEquals(prepared, actions.ClearThemeAppearance(2)));
        AssertFalse(prepared.IsRetired);
        AssertEqual(2L, actions.ThemeAppearanceGeneration);
        AssertTrue(actions.PreparedTextLayoutProvider is null);
        bool stale = false;
        try { actions.ClearThemeAppearance(2); } catch (InvalidOperationException) { stale = true; }
        AssertTrue(stale);
        prepared.Dispose(); AssertTrue(prepared.IsDisposed);
    }

    private static void ThemeSvgRejectsExternalContentAndDrawsAdmittedGeometry()
    {
        const string vector = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='100' height='100' fill='url(#g)'/><g transform='translate(50,50)'><circle r='12' fill='#00ff00'/></g></svg>";
        var svg = AvaloniaUiThemeSvg.Parse(Encoding.UTF8.GetBytes(vector));
        using var bitmap = svg.Rasterize(100);
        using var pixels = ReadBrandPixels(bitmap);
        AssertEqual(SKColors.Lime, pixels.GetPixel(50, 50));
        AssertTrue(pixels.GetPixel(10, 10).Red > pixels.GetPixel(10, 10).Blue);
        AssertTrue(pixels.GetPixel(90, 90).Blue > pixels.GetPixel(90, 90).Red);
        foreach (string invalid in new[]
        {
            "<!DOCTYPE svg [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><svg viewBox='0 0 10 10'><path d='M0 0'/></svg>",
            "<svg viewBox='0 0 10 10'><script>run()</script></svg>",
            "<svg viewBox='0 0 10 10'><image href='https://invalid/image.png'/></svg>",
            "<svg viewBox='0 0 10 10'><path d='M0 0' onclick='run()'/></svg>",
            "<svg viewBox='0 0 10 10'><path d='M0 0 L1e300 5'/></svg>",
            "<svg viewBox='0 0 10 10'><rect width='10' height='10' fill='url(https://invalid/g)'/></svg>",
            "<svg viewBox='0 0 1e-308 1e-308'><rect width='1' height='1'/></svg>",
            "<svg viewBox='0 0 10 10'><g transform='scale(1000000)'><g transform='scale(1000000)'><rect width='1' height='1'/></g></g></svg>",
        })
        {
            foreach (bool native in new[] { false, true })
            {
                bool rejected = false;
                try { AvaloniaUiThemeSvg.Parse(Encoding.UTF8.GetBytes(invalid), nativeGeometry: native); }
                catch (Exception error) when (error is System.Xml.XmlException or InvalidDataException or FormatException) { rejected = true; }
                AssertTrue(rejected);
            }
        }
        Console.WriteLine("PASS: admitted external SVG draws real gradient/geometry; external entities, network references, script and event attributes are rejected");
    }

    private static void ThemePaintConsumesGradientTextureShadowOpacityAndCanonicalTransform()
    {
        var resource = new AvaloniaUiThemeResource("asset(texture)", "image/png", ThemeSolidPng(SKColors.Lime));
        using var prepared = new AvaloniaUiPreparedThemeAppearance(new(20, Resources: [resource]));
        var tree = new XsrUiTree();
        var node = Node(tree.Create("theme-paint"), XsrUiSemanticRole.Content) with
        {
            Rect = new(0, 0, 100, 100),
            VisualStyle = new XsrUiVisualStyle { Opacity = .5, Background = new(255, 255, 255), CornerRadius = 3 }.Snapshot(),
            BackgroundGradient = new(0, [new(0, new(255, 0, 0)), new(1, new(0, 0, 255))]),
            Shadow = new(new(0, 0, 0, 180), 3, 4, 8),
            ThemeActivationGeneration = 20,
        };
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
        control.SetThemeResources(prepared);
        try
        {
            control.Apply(node);
            using (var pixels = DrawComposite(control))
            {
                AssertTrue(pixels.GetPixel(10, 50).Red > pixels.GetPixel(10, 50).Blue);
                AssertTrue(pixels.GetPixel(90, 50).Blue > pixels.GetPixel(90, 50).Red);
                AssertTrue(pixels.GetPixel(50, 50).Alpha is >= 126 and <= 129);
            }
            control.Apply(node with { BackgroundImage = new("asset(texture)", "tile", .5) });
            using (var pixels = DrawComposite(control)) AssertTrue(pixels.GetPixel(50, 50).Green > 90);
            AssertEqual(.5, control.Opacity);
            control.Apply(node with
            {
                LayoutRect = new(0, 0, 100, 100),
                Rect = new(20, 0, 100, 100),
                Transform = new XsrUiSceneTransform(1, 0, 0, 1, 20, 0),
                Shadow = null,
                BackgroundGradient = null,
                VisualStyle = new XsrUiVisualStyle { Background = new(255, 0, 0) }.Snapshot(),
            });
            using (var pixels = DrawComposite(control))
            {
                AssertEqual((byte)0, pixels.GetPixel(5, 50).Alpha);
                AssertEqual(SKColors.Red, pixels.GetPixel(50, 50));
            }
            var transform = new XsrUiSceneTransform(0, 1, -1, 0, 200, 0);
            control.Apply(node with { LayoutRect = new(30, 40, 100, 100), Rect = transform.Bounds(new(30, 40, 100, 100)), Transform = transform });
            XsrUiPoint raw = control.LocalTextPoint(transform.Map(new(48, 60)));
            AssertEqual(new XsrUiPoint(18, 20), raw);
            AssertFalse(control.ClipToBounds);
        }
        finally { control.ReleasePresentation(); }
    }

    private static void ThemeFontBytesShapeMeasureAndPlaceInputCaretConsistently()
    {
        using Stream source = AssertNotNull(typeof(Program).Assembly.GetManifestResourceStream("Nexa.Theme.Tests.DejaVuSans.ttf"));
        using var bytes = new MemoryStream(); source.CopyTo(bytes);
        using var prepared = new AvaloniaUiPreparedThemeAppearance(new(21, Resources:
            [new("asset(font)", "font/ttf", bytes.ToArray())]));
        var provider = AssertNotNull(prepared.TextLayout);
        const string text = "AV 中文 e\u0301 😀";
        var request = new XsrUiTextLayoutRequest(text, "asset(font)", 19, 400, IncludeCaretPositions: true);
        var measured = provider.Measure(request);
        AssertTrue(measured.Size.Width > 0 && measured.Size.Height > 0);
        AssertTrue(ReferenceEquals(measured, provider.Measure(request)));
        AssertEqual(text.Length + 1, AssertNotNull(measured.CaretPositions).Count);
        AssertTrue(provider.Resolve("asset(font)", FontWeight.Normal).FontFamily.Key is not null);
        var tree = new XsrUiTree();
        var node = Node(tree.Create("theme-font-input"), XsrUiSemanticRole.TextInput) with
        {
            Rect = new(0, 0, 600, 45),
            FontFamily = "asset(font)",
            IsFocused = true,
            VisualStyle = new XsrUiVisualStyle { FontSize = 19, FontWeight = 400, Foreground = new(0, 0, 0), Background = new(255, 255, 255) }.Snapshot(),
            TextInput = new(text, "", false, 0, text.Length, ""),
        };
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
        control.SetThemeResources(prepared);
        try
        {
            control.Apply(node); control.Measure(new(600, 45)); control.Arrange(new(0, 0, 600, 45));
            using var target = new RenderTargetBitmap(new(600, 45), new(96, 96));
            using (DrawingContext context = target.CreateDrawingContext()) control.Render(context);
            double end = AssertNotNull(measured.CaretPositions)[text.Length];
            AssertTrue(Math.Abs(control.TextCursorRectangle.X - (12 + end)) < .01);
            AssertEqual(text.Length, control.TextPositionAt(12 + end));
            int cache = provider.CachedMeasurements;
            provider.Measure(request with { IsPassword = true });
            AssertEqual(cache, provider.CachedMeasurements);
        }
        finally { control.ReleasePresentation(); }
        prepared.Dispose();
        bool disposed = false;
        try { provider.Measure(request); } catch (ObjectDisposedException) { disposed = true; }
        AssertTrue(disposed);
        Console.WriteLine("PASS: admitted TTF uses native shaping with mixed CJK, combining and surrogate text; intrinsic size, drawing, caret and cache retirement agree");
    }

    private static void ThemeRangePaintAndAutomationUseConfirmedSceneValues()
    {
        var tree = new XsrUiTree();
        var node = Node(tree.Create("theme-range"), XsrUiSemanticRole.Slider) with
        {
            Rect = new(0, 0, 100, 40),
            Range = new(0, 100, 5, 25),
            IsFocusable = true,
            VisualStyle = new XsrUiVisualStyle { Background = new(180, 193, 208), Foreground = new(20, 100, 230) }.Snapshot(),
        };
        double requested = -1;
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true,
            rangeFromAutomation: (_, value) => requested = value);
        try
        {
            control.Apply(node);
            var peer = (IRangeValueProvider)ControlAutomationPeer.CreatePeerForElement(control);
            AssertEqual(25d, peer.Value); AssertEqual(5d, peer.SmallChange); AssertEqual(0d, peer.Minimum); AssertEqual(100d, peer.Maximum);
            peer.SetValue(75); AssertEqual(75d, requested); AssertEqual(25d, peer.Value);
            control.Apply(node with { Range = new(0, 100, 5, 75) }); AssertEqual(75d, peer.Value);
            using (var pixels = DrawComposite(control))
            {
                AssertEqual(new SKColor(20, 100, 230), pixels.GetPixel(25, 20));
                AssertEqual(new SKColor(180, 193, 208), pixels.GetPixel(85, 20));
            }
            control.Apply(node with { IsEnabled = false });
            bool rejected = false;
            try { peer.SetValue(50); } catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected); AssertEqual(75d, requested); AssertTrue(peer.IsReadOnly);
        }
        finally { control.ReleasePresentation(); }
    }

    private static void ThemeResourceRetirementHoldsSnapshotBorrowUntilRelease()
    {
        var resource = new AvaloniaUiThemeResource("asset(snapshot)", "image/png", ThemeSolidPng(SKColors.Lime));
        var prepared = new AvaloniaUiPreparedThemeAppearance(new(22, Resources: [resource]));
        var tree = new XsrUiTree();
        var node = Node(tree.Create("theme-outgoing-borrow"), XsrUiSemanticRole.Image) with
        { ImageSource = resource.ResourceId, ThemeActivationGeneration = 22 };
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
        try
        {
            control.SetThemeResources(prepared); control.Apply(node); prepared.Dispose();
            AssertTrue(prepared.IsRetired); AssertFalse(prepared.IsDisposed);
            using (var pixels = DrawComposite(control)) AssertEqual(SKColors.Lime, pixels.GetPixel(50, 20));
            control.ReleasePresentation(); AssertTrue(prepared.IsDisposed);
        }
        finally { control.ReleasePresentation(); prepared.Dispose(); }
    }

    private static void ThemeOwnerRetirementAndFinalNativeBorrowReleaseAreConcurrentSafe()
    {
        var resource = new AvaloniaUiThemeResource("asset(concurrent)", "image/png", ThemeSolidPng(SKColors.Lime));
        for (int iteration = 0; iteration < 64; iteration++)
        {
            var prepared = new AvaloniaUiPreparedThemeAppearance(new(50, Resources: [resource]));
            var tree = new XsrUiTree();
            var node = Node(tree.Create("concurrent-lease"), XsrUiSemanticRole.Image) with
            { ImageSource = resource.ResourceId, ThemeActivationGeneration = 50 };
            var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
            control.SetThemeResources(prepared); control.Apply(node);
            // A retained outgoing snapshot holds the last resource after the live control retires.
            prepared.Borrow();
            using (var pixels = DrawComposite(control)) AssertEqual(SKColors.Lime, pixels.GetPixel(50, 20));
            control.ReleasePresentation();
            using var boundary = new Barrier(2);
            Task retirement = Task.Run(() => { boundary.SignalAndWait(); prepared.Dispose(); });
            boundary.SignalAndWait(); prepared.ReleaseBorrow();
            retirement.GetAwaiter().GetResult();
            AssertTrue(prepared.IsRetired); AssertTrue(prepared.IsDisposed);
            AssertEqual(0L, prepared.DecodedBitmapBytes);
            prepared.Dispose();
        }
    }

    private static void ThemeSvgTileSubmitsBoundedRepeatingPictureForLargeViewports()
    {
        var vector = new AvaloniaUiThemeResource("asset(tiny-tile)", "image/svg+xml",
            Encoding.UTF8.GetBytes("<svg viewBox='10 20 1 1'><rect x='10' y='20' width='1' height='1' fill='#00ff00'/></svg>"));
        using var prepared = new AvaloniaUiPreparedThemeAppearance(new(51, Resources: [vector]));
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
        using (DrawingContext context = bitmap.CreateDrawingContext())
            AssertTrue(prepared.DrawImage(context, vector.ResourceId, new Rect(0, 0, 800, 600), "tile"));
        using var pixels = ReadBrandPixels(bitmap);
        AssertEqual(SKColors.Lime, pixels.GetPixel(1, 1));
        AssertEqual(SKColors.Lime, pixels.GetPixel(400, 300));
        AssertEqual(SKColors.Lime, pixels.GetPixel(798, 598));
    }

    private static void ThemePortableMaterialsProduceVisibleTintWithoutBlurringText()
    {
        var tree = new XsrUiTree();
        var node = Node(tree.Create("theme-material"), XsrUiSemanticRole.Content) with
        { Rect = new(0, 0, 100, 100), VisualStyle = new XsrUiVisualStyle { Background = new(60, 90, 160), Surface = XsrUiSurfaceKind.Solid }.Snapshot() };
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
        try
        {
            control.Apply(node);
            using (var solid = DrawComposite(control)) AssertEqual((byte)255, solid.GetPixel(50, 50).Alpha);
            control.Apply(node with { VisualStyle = node.VisualStyle with { Surface = XsrUiSurfaceKind.Translucent } });
            using (var tint = DrawComposite(control)) AssertTrue(tint.GetPixel(50, 50).Alpha is >= 182 and <= 185);
            control.Apply(node with { VisualStyle = node.VisualStyle with { Surface = XsrUiSurfaceKind.Glass } });
            using (var glass = DrawComposite(control))
            {
                AssertTrue(glass.GetPixel(50, 50).Alpha is > 142 and < 160);
                AssertTrue(glass.GetPixel(50, 5).Alpha > glass.GetPixel(50, 70).Alpha);
            }
            AssertTrue(control.Effect is null);
        }
        finally { control.ReleasePresentation(); }
    }

    private static void ThemeDecodedBudgetRejectsHighlyCompressibleRasterCollections()
    {
        byte[] bytes = ThemeSolidPng(SKColors.Lime, 2048);
        AssertTrue(bytes.Length < 1048576);
        AvaloniaUiThemeResource[] resources = Enumerable.Range(0, 5)
            .Select(index => new AvaloniaUiThemeResource("large-" + index, "image/png", bytes)).ToArray();
        bool rejected = false;
        try { using var excessive = new AvaloniaUiPreparedThemeAppearance(new(23, Resources: resources)); }
        catch (InvalidDataException error) { rejected = error.Message.Contains("combined decoded budget", StringComparison.Ordinal); }
        AssertTrue(rejected);
        using var admitted = new AvaloniaUiPreparedThemeAppearance(new(24, Resources: resources[..2]));
        AssertEqual(32L * 1048576, admitted.DecodedBitmapBytes);
        admitted.Dispose(); AssertEqual(0L, admitted.DecodedBitmapBytes);
    }

    private static byte[] ThemeSolidPng(SKColor color, int size = 32)
    {
        using var pixels = new SKBitmap(size, size); pixels.Erase(color);
        using var image = SKImage.FromBitmap(pixels); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static async Task VerifyNativeThemeActivationAsync()
    {
        _themeActivationVerified = false;
        var shell = new XsrUiShell(new XsrStateStoreBuilder().Build());
        shell.Renderer.ReducedMotion = true;
        var window = new AvaloniaUiShellWindow(shell);
        using Stream builtinLight = AssertNotNull(typeof(Program).Assembly.GetManifestResourceStream("Nexa.Brand.Tests.light.png"));
        using Stream builtinDark = AssertNotNull(typeof(Program).Assembly.GetManifestResourceStream("Nexa.Brand.Tests.dark.png"));
        var builtin = AvaloniaUiBrandImages.Load(builtinLight, builtinDark);
        window.SetOwnedBrandImages(builtin);
        var actions = new AvaloniaUiPlatformActions(); actions.Attach(window);
        var tray = new TrayIcon();
        using var integration = new AvaloniaUiDesktopIntegration(window, () => { }, () => { }, text => text, () => tray);
        var light = new AvaloniaUiThemeResource("light", "image/png", ThemeSolidPng(SKColors.Red));
        var dark = new AvaloniaUiThemeResource("dark", "image/png", ThemeSolidPng(SKColors.Blue));
        AvaloniaUiPreparedThemeAppearance? current = null;
        try
        {
            window.Show(); await Task.Delay(20); window.Surface.CommitScene(); integration.Apply(new(true));
            Console.WriteLine("Theme activation fixture: native candidate preparation");
            current = await actions.PrepareThemeAppearanceAsync(new(100, light, dark));
            AssertTrue(current.IsNativePrepared);
            AssertTrue(actions.CommitThemeAppearance(current) is null);
            integration.Apply(new(true));
            Console.WriteLine("Theme activation fixture: runtime window and tray palette");
            WindowIcon first = AssertNotNull(window.Icon);
            AssertTrue(ReferenceEquals(first, tray.Icon));
            shell.Renderer.ColorScheme = new(true); window.Surface.CommitScene();
            AssertFalse(ReferenceEquals(first, window.Icon)); AssertTrue(ReferenceEquals(window.Icon, tray.Icon));
            Console.WriteLine("Theme activation fixture: independent icon slots and retirement");
            var next = await actions.PrepareThemeAppearanceAsync(new(101, dark, light));
            var retired = AssertNotNull(actions.CommitThemeAppearance(next));
            AssertTrue(ReferenceEquals(current, retired)); AssertFalse(retired.IsDisposed);
            retired.Dispose(); AssertTrue(retired.IsRetired);
            window.Surface.CommitScene(); AssertTrue(retired.IsDisposed); current = next;
            var independent = await actions.PrepareThemeAppearanceAsync(new(102, light, dark,
                WindowIconLight: light, WindowIconDark: light, TrayIconLight: dark, TrayIconDark: dark,
                SplashIconLight: dark, SplashIconDark: light, ResizePolicy: AvaloniaUiThemeResizePolicy.Animated));
            actions.CommitThemeAppearance(independent)?.Dispose(); current = independent;
            AssertFalse(ReferenceEquals(window.Icon, tray.Icon));
            AssertTrue(ReferenceEquals(window.ThemeTrayIcon, tray.Icon));
            AssertTrue(AssertNotNull(independent.StartupSnapshot).BrandLightPng.Length > 0);
            bool stale = false;
            using var old = await actions.PrepareThemeAppearanceAsync(new(100, light, dark));
            try { actions.CommitThemeAppearance(old); } catch (InvalidOperationException) { stale = true; }
            AssertTrue(stale); AssertEqual(102L, actions.ThemeAppearanceGeneration);
            Console.WriteLine("Theme activation fixture: canonical viewport resize");
            shell.Renderer.ReducedMotion = false;
            double initialWidth = window.Surface.PresentedViewport.Width;
            window.Width += 96;
            DateTime resizeDeadline = DateTime.UtcNow.AddSeconds(2);
            while (window.Surface.Bounds.Width <= initialWidth && DateTime.UtcNow < resizeDeadline) await Task.Delay(10);
            AssertTrue(window.Surface.Bounds.Width > initialWidth);
            window.Surface.CommitScene();
            AssertTrue(window.Surface.PresentedViewport.Width < window.Surface.Bounds.Width);
            AssertEqual(window.Surface.PresentedViewport.Width, Node(AssertNotNull(window.Surface.Scene), shell.Root).Rect.Width);
            while (Math.Abs(window.Surface.PresentedViewport.Width - window.Surface.Bounds.Width) > .01 && DateTime.UtcNow < resizeDeadline) await Task.Delay(10);
            AssertTrue(Math.Abs(window.Surface.PresentedViewport.Width - window.Surface.Bounds.Width) < .01);
            shell.Renderer.ReducedMotion = true;
            WindowIcon committed = AssertNotNull(window.Icon);
            bool invalid = false;
            try { await actions.PrepareThemeAppearanceAsync(new(103, light, dark, [new("bad", "font/ttf", new byte[12])])); }
            catch (InvalidDataException) { invalid = true; }
            AssertTrue(invalid); AssertTrue(ReferenceEquals(committed, window.Icon));
            Console.WriteLine("Theme activation fixture: finite splash appearance");
            var snapshot = new AvaloniaUiThemeStartupSnapshot(101, light.Content, dark.Content,
                new(new(16, 24, 32), new(240, 245, 250), new(34, 210, 140))).Snapshot();
            using var card = new AvaloniaUiStartupCard(text => text);
            card.SetThemeSnapshot(snapshot); card.ApplyAppearance(false, "2.0.0.alpha.6");
            AssertEqual(Color.FromRgb(16, 24, 32), ((SolidColorBrush)AssertNotNull(card.Background)).Color);
            AssertTrue(card.ProductIcon is not null);
            card.ApplySplashMotion(AvaloniaUiThemeSplashMotion.Breathe, false);
            AssertTrue(AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks > 0);
            card.ApplySplashMotion(AvaloniaUiThemeSplashMotion.None, true);
            AssertTrue(card.GetVisualDescendants().OfType<global::Avalonia.Controls.Image>().Single().Opacity == 1);

            Console.WriteLine("Theme activation fixture: finite recovery restores retained product artwork");
            var recovered = AssertNotNull(actions.ClearThemeAppearance(103));
            AssertTrue(ReferenceEquals(current, recovered)); AssertFalse(recovered.IsRetired);
            shell.Renderer.ClearTheme(103); window.Surface.CommitScene();
            AssertEqual(103L, actions.ThemeAppearanceGeneration);
            AssertTrue(actions.PreparedTextLayoutProvider is null);
            AssertEqual(AvaloniaUiThemeWindowMaterial.Default, window.PresentedThemeMaterial);
            AssertEqual(AvaloniaUiThemeResizePolicy.Auto, window.Surface.PresentedResizePolicy);
            AssertTrue(ReferenceEquals(builtin.ResolveIcon(true), window.Icon));
            AssertTrue(ReferenceEquals(window.Icon, tray.Icon)); AssertFalse(builtin.IsDisposed);
            recovered.Dispose(); AssertTrue(recovered.IsDisposed); current = null;

            // XSR-833 retains native caption capabilities for DWM and macOS traffic lights.
            AssertEqual(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? WindowDecorations.Full : WindowDecorations.None, window.WindowDecorations);
            _themeActivationVerified = true;
        }
        finally { window.Close(); current?.Dispose(); }
        AssertTrue(builtin.IsDisposed);
        Console.WriteLine("PASS: native theme generations update retained window/tray, reject invalid/stale candidates, retire old resources and consume finite splash appearance");
    }
}
