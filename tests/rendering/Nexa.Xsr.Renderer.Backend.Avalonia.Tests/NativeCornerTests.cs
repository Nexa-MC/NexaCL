using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Chrome;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static int RunNativeCornerSmoke()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native corner smoke requires Windows.");
        return AppBuilder.Configure<NativeCornerProbeApp>().UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                CompositionMode = [Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface],
            }).StartWithClassicDesktopLifetime([]);
    }

    public sealed class NativeCornerProbeApp : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            // Match production: an absent Fluent theme can hide a stale implicit caption.
            Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
                shell.Renderer.ReducedMotion = true;
                var window = new AvaloniaUiShellWindow(shell);
                var actions = new AvaloniaUiPlatformActions();
                actions.Attach(window);
                int openings = 0, reveals = 0;
                window.StartupRevealCompleted += (_, _) => reveals++;
                VerifyNativeCornerManagedDecorations(window);
                window.PrepareStartupScene();
                AssertFalse(window.IsVisible);
                AssertTrue(window.Surface.Scene is { Count: > 0 });
                VerifyNativeCornerManagedDecorations(window);
                nint handle = window.TryGetPlatformHandle()!.Handle;
                AssertTrue(handle != 0);
                desktop.MainWindow = window;
                window.Opened += (_, _) =>
                {
                    // These are synchronous public Opened observations: a delayed repair
                    // must not make an incorrect first show or tray restoration pass.
                    AssertEqual(handle, window.TryGetPlatformHandle()!.Handle);
                    VerifyNativeCornerManagedDecorations(window);
                    VerifyNativeDecorationPolicy(window);
                    openings++;
                    if (openings == 1)
                        Dispatcher.UIThread.Post(() => _ = VerifyNativeCornerLifecycleAsync(
                            desktop, shell, window, actions, handle, () => openings, () => reveals));
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    private static async Task VerifyNativeCornerLifecycleAsync(
        IClassicDesktopStyleApplicationLifetime desktop, XsrUiShell shell, AvaloniaUiShellWindow window,
        AvaloniaUiPlatformActions actions, nint handle, Func<int> openings, Func<int> reveals)
    {
        try
        {
            await VerifyNativeWindowsMediaControlsReadinessAsync(handle);
            VerifyNativeDwmSetterSupport();
            await WaitForNativeCornerLayoutAsync(() => NativeCornerViewportMatches(window, 960, 600));
            VerifyCompositedViewport(window, 960, 600);
            await VerifyNativeWindowPointerHitTestingAsync(shell, window);
            AssertEqual(1, openings()); AssertEqual(1, reveals());
            // Keep the resize within the 1024 px virtual desktop used by Windows CI.
            window.Width -= 100; window.Height -= 50;
            await WaitForNativeCornerLayoutAsync(() => NativeCornerViewportMatches(window, 860, 550));
            VerifyCompositedViewport(window, 860, 550);

            // A normal hidden window can reopen without any size or state notification.
            // Force the stale DWM policy so this cannot pass merely because it stayed off.
            for (int restore = 0; restore < 2; restore++)
            {
                window.Hide();
                ForceNativeCornerStaleFrame(window);
                window.Show(); window.Activate();
                VerifyNativeCornerRestoration(window, handle, WindowState.Normal, reveals);
                VerifyCompositedViewport(window, 860, 550);
            }
            AssertEqual(3, openings());
            actions.HideWindow();
            ForceNativeCornerStaleFrame(window);
            actions.RestoreWindow();
            VerifyNativeCornerRestoration(window, handle, WindowState.Normal, reveals);
            VerifyCompositedViewport(window, 860, 550);
            AssertEqual(4, openings());
            await VerifyNativeWindowPointerHitTestingAsync(shell, window);

            window.WindowState = WindowState.Maximized;
            await WaitForNativeCornerLayoutAsync(() => NativeCornerMaximizedViewportMatches(window));
            VerifyNativeCornerMaximizedViewport(window);
            Size maximizedViewport = window.Surface.Bounds.Size;
            actions.HideWindow();
            ForceNativeCornerStaleFrame(window);
            actions.RestoreWindow();
            VerifyNativeCornerRestoration(window, handle, WindowState.Maximized, reveals);
            await WaitForNativeCornerLayoutAsync(() => NativeCornerMaximizedViewportMatches(window));
            VerifyNativeCornerMaximizedViewport(window);
            AssertEqual(maximizedViewport, window.Surface.Bounds.Size);
            AssertEqual(5, openings());
            await VerifyNativeWindowPointerHitTestingAsync(shell, window);

            window.WindowState = WindowState.Normal;
            await WaitForNativeCornerLayoutAsync(() => NativeCornerViewportMatches(window, 860, 550));
            VerifyCompositedViewport(window, 860, 550);
            await VerifyAnimatedNativeTrayAsync(shell, window, actions, handle, reveals);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                // Reproduce a stale platform checkpoint after restoration through the OS.
                RestorePlacement normalPlacement = ReadRestorePlacement(handle);
                window.WindowState = WindowState.Maximized;
                await WaitForNativeCornerLayoutAsync(() => NativeCornerMaximizedViewportMatches(window));
                VerifyRestorePlacement(handle, normalPlacement);
                actions.MinimizeWindow();
                await WaitForNativeCornerLayoutAsync(() => window.WindowState == WindowState.Minimized && NativeCornerIsIconic(handle));
                VerifyRestorePlacement(handle, normalPlacement);
                // Windows first restores the maximized placement; then the user unmaximizes.
                window.WindowState = WindowState.Maximized;
                await WaitForNativeCornerLayoutAsync(() => !NativeCornerIsIconic(handle) && NativeCornerMaximizedViewportMatches(window));
                VerifyRestorePlacement(handle, normalPlacement);
                window.WindowState = WindowState.Normal;
                await WaitForNativeCornerLayoutAsync(() => window.WindowState == WindowState.Normal && window.Surface.Bounds.Width < 1000);
                VerifyRestorePlacement(handle, normalPlacement);
                // Native restoration must preserve the viewport without a corrective resize.
                await WaitForNativeCornerLayoutAsync(() => NativeCornerViewportMatches(window, 860, 550));
                window.WindowState = WindowState.Minimized;
                await WaitForNativeCornerLayoutAsync(() => NativeCornerIsIconic(handle));
                actions.HideWindow(); actions.RestoreWindow();
                VerifyNativeCornerRestoration(window, handle, WindowState.Normal, reveals);
                await WaitForNativeCornerLayoutAsync(() => NativeCornerViewportMatches(window, 860, 550));
                Console.WriteLine("PASS: tray restore uses latest native normal state after an earlier maximized minimize; viewport retained");
            }
            await VerifyNativeWindowPointerHitTestingAsync(shell, window);
            window.WindowState = WindowState.Minimized;
            await WaitForNativeCornerLayoutAsync(() => window.WindowState == WindowState.Minimized && NativeCornerIsIconic(handle));
            window.WindowState = WindowState.Normal;
            await WaitForNativeCornerLayoutAsync(() => NativeCornerViewportMatches(window, 860, 550));
            VerifyCompositedViewport(window, 860, 550);
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            await WaitForNativeCornerLayoutAsync(() => !window.UsesCompositedEdges
                && ((Control)window.Content!).Bounds.Size == window.Surface.Bounds.Size);
            AssertFalse(window.UsesCompositedEdges);
            AssertEqual(((Control)window.Content!).Bounds.Size, window.Surface.Bounds.Size);
            AssertTrue(Math.Abs(window.Surface.Bounds.Width - 860) <= 1 / window.RenderScaling);
            AssertTrue(window.Background is LinearGradientBrush fallback && fallback.GradientStops.All(stop => stop.Color.A == 255));
            window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
            await WaitForNativeCornerLayoutAsync(() => window.UsesCompositedEdges
                && NativeCornerViewportMatches(window, 860, 550));
            VerifyCompositedViewport(window, 860, 550);
            AssertEqual(handle, window.TryGetPlatformHandle()!.Handle);
            AssertEqual(1, reveals());
            Console.WriteLine("PASS: empty managed decorations before show; immediate DWM suppression on every opening; same HWND, viewport and input origin through repeated normal/maximized and animated reversible tray restores; once-only entrance, native composition and opaque fallback; documented DWM getters verified");
            desktop.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
    }

    private static async Task VerifyAnimatedNativeTrayAsync(XsrUiShell shell, AvaloniaUiShellWindow window,
        AvaloniaUiPlatformActions actions, nint handle, Func<int> reveals)
    {
        shell.Renderer.ReducedMotion = false;
        Grid content = TrayPresentation(window);
        actions.HideWindow();
        AssertTrue(window.IsVisible && window.IsHidingToTray);
        await WaitForNativeCornerLayoutAsync(() => content.Opacity is > 0 and < 1);
        double presented = content.Opacity;
        VerifyCompositedViewport(window, 860, 550);
        actions.RestoreWindow();
        AssertEqual(presented, content.Opacity);
        await WaitForNativeCornerLayoutAsync(() => content.Opacity == 1);
        await Task.Delay(AvaloniaMotionTokens.TrayHideMilliseconds + 40).ConfigureAwait(true);
        VerifyNativeCornerRestoration(window, handle, WindowState.Normal, reveals);

        actions.HideWindow();
        await WaitForNativeCornerLayoutAsync(() => !window.IsVisible);
        AssertEqual(0d, content.Opacity);
        shell.Renderer.OptionalMotionSuspended = true;
        AssertTrue(shell.Renderer.OptionalMotionSuspended && !shell.Renderer.ReducedMotion);
        ForceNativeCornerStaleFrame(window);
        actions.RestoreWindow();
        AssertEqual(0d, content.Opacity);
        VerifyNativeCornerRestoration(window, handle, WindowState.Normal, reveals);
        await WaitForNativeCornerLayoutAsync(() => content.Opacity is > 0 and < 1);
        AssertEqual(1d, window.Opacity);
        VerifyCompositedViewport(window, 860, 550);
        presented = content.Opacity;
        actions.HideWindow();
        AssertEqual(presented, content.Opacity);
        await WaitForNativeCornerLayoutAsync(() => !window.IsVisible);
        actions.RestoreWindow();
        await WaitForNativeCornerLayoutAsync(() => content.Opacity == 1);
        VerifyNativeCornerRestoration(window, handle, WindowState.Normal, reveals);
        VerifyCompositedViewport(window, 860, 550);
        AssertTrue(content.Clip is null && content.RenderTransform is null);
        AssertEqual(1d, window.Opacity);
        shell.Renderer.OptionalMotionSuspended = false;
        shell.Renderer.ReducedMotion = true;
        Console.WriteLine("PASS: actual intermediate tray fades reverse continuously; native Hide is deferred; hidden scene suspension does not suppress restore; no layered HWND or viewport/input changes");
    }

    private static void VerifyNativeCornerManagedDecorations(AvaloniaUiShellWindow window)
    {
        ControlTheme theme = AssertNotNull(window.WindowDecorationsTheme);
        AssertEqual(typeof(WindowDrawnDecorations), theme.TargetType);
        var template = (IWindowDrawnDecorationsTemplate)theme.Setters.OfType<Setter>()
            .Single(setter => setter.Property == WindowDrawnDecorations.TemplateProperty).Value!;
        VerifyNativeCornerEmptyDecorationContent(template.Build().Result);
        // Decorations belong to the host above Window, alongside its visual layers.
        var host = (StyledElement)AssertNotNull(window.GetVisualParent());
        var decorations = host.GetLogicalDescendants().OfType<WindowDrawnDecorations>().Single();
        AssertTrue(ReferenceEquals(theme, decorations.Theme));
        VerifyNativeCornerEmptyDecorationContent(AssertNotNull(decorations.Content));
    }

    private static void VerifyNativeCornerEmptyDecorationContent(WindowDrawnDecorationsContent content)
    {
        AssertTrue(content.Overlay is null);
        AssertTrue(content.Underlay is null);
        AssertTrue(content.FullscreenPopover is null);
    }

    private static void ForceNativeCornerStaleFrame(AvaloniaUiShellWindow window)
    {
        AssertFalse(window.IsVisible);
        AvaloniaWindowsFrame.SetNonClientRendering(window, true);
        AssertEqual(0, DwmGetWindowAttribute(window.TryGetPlatformHandle()!.Handle, 1, out int enabled, sizeof(int)));
        AssertEqual(1, enabled);
    }

    private static void VerifyNativeCornerRestoration(AvaloniaUiShellWindow window, nint handle,
        WindowState state, Func<int> reveals)
    {
        AssertTrue(window.IsVisible);
        AssertEqual(state, window.WindowState);
        AssertEqual(handle, window.TryGetPlatformHandle()!.Handle);
        AssertEqual(1, reveals());
        VerifyNativeCornerManagedDecorations(window);
        VerifyNativeDecorationPolicy(window);
    }

    private static bool NativeCornerViewportMatches(AvaloniaUiShellWindow window, double width, double height)
        => Math.Abs(width - window.Surface.Bounds.Width) <= 1 / window.RenderScaling
            && Math.Abs(height - window.Surface.Bounds.Height) <= 1 / window.RenderScaling;

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeCornerIsIconic(nint handle);

    private static RestorePlacement ReadRestorePlacement(nint handle)
    {
        var placement = new RestorePlacement { Length = Marshal.SizeOf<RestorePlacement>() };
        AssertTrue(GetRestorePlacement(handle, ref placement));
        return placement;
    }

    private static void VerifyRestorePlacement(nint handle, RestorePlacement expected)
    {
        RestorePlacement actual = ReadRestorePlacement(handle);
        AssertEqual((expected.Left, expected.Top, expected.Right, expected.Bottom),
            (actual.Left, actual.Top, actual.Right, actual.Bottom));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RestorePlacement { public int Length, Flags, Show, MinX, MinY, MaxX, MaxY, Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetRestorePlacement(nint handle, ref RestorePlacement placement);

    private static bool NativeCornerMaximizedViewportMatches(AvaloniaUiShellWindow window)
        => window.WindowState == WindowState.Maximized
            && ((Control)window.Content!).Bounds.Size == window.Surface.Bounds.Size;

    private static async Task WaitForNativeCornerLayoutAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // State setters can preserve the old bounds while a render-priority frame repair
        // is still queued. This is a layout barrier; Opened policy checks never use it.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        while (!ready()) await Task.Delay(10, timeout.Token);
    }

    private static void VerifyNativeCornerMaximizedViewport(AvaloniaUiShellWindow window)
    {
        VerifyNoClientCornerClipping(window);
        VerifyNativeDecorationPolicy(window);
        VerifyNativeCornerManagedDecorations(window);
        var root = (Control)window.Content!;
        AssertEqual(root.Bounds.Size, window.Surface.Bounds.Size);
        AssertEqual(new Point(0, 0), window.Surface.TranslatePoint(default, root)!.Value);
    }

    private static void VerifyCompositedViewport(AvaloniaUiShellWindow window, double width, double height)
    {
        Console.WriteLine($"transparency={window.ActualTransparencyLevel}, layered={AvaloniaWindowsFrame.IsLayered(window)}, scene={window.Surface.Bounds.Size}");
        AssertTrue(window.UsesCompositedEdges);
        AssertFalse(AvaloniaWindowsFrame.IsLayered(window));
        VerifyNoClientCornerClipping(window);
        VerifyNativeDecorationPolicy(window);
        VerifyNativeCornerManagedDecorations(window);
        // Native pixel bounds round fractional DIPs at scales such as 125%.
        AssertTrue(Math.Abs(width - window.Surface.Bounds.Width) <= 1 / window.RenderScaling);
        AssertTrue(Math.Abs(height - window.Surface.Bounds.Height) <= 1 / window.RenderScaling);
        var root = (Control)window.Content!;
        AssertEqual(new Size(window.Surface.Bounds.Width + 48, window.Surface.Bounds.Height + 48), root.Bounds.Size);
        AssertEqual(new Point(24, 24), window.Surface.TranslatePoint(default, root)!.Value);
        AssertEqual(new Point(0, 0), root.TranslatePoint(new Point(24, 24), window.Surface)!.Value);
        AssertTrue(window.Background is ISolidColorBrush { Color.A: 0 });
        var chrome = (Border)window.Surface.Parent!.Parent!;
        AssertEqual(new CornerRadius(24), chrome.CornerRadius);
        AssertTrue(chrome.Clip is RectangleGeometry { RadiusX: 24, RadiusY: 24 });
        AssertTrue(chrome.Background is LinearGradientBrush background && background.GradientStops.All(stop => stop.Color.A == 255));
    }
    private static void VerifyNoClientCornerClipping(AvaloniaUiShellWindow window)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            int kind = GetWindowRgn(window.TryGetPlatformHandle()!.Handle, region);
            Console.WriteLine($"region={kind}");
            if (kind != 0)
            {
                AssertTrue(GetWindowRectForChrome(window.TryGetPlatformHandle()!.Handle, out var bounds));
                var root = (Control)window.Content!;
                var origin = root.PointToScreen(default);
                int left = origin.X - bounds.Left, top = origin.Y - bounds.Top;
                int right = left + (int)Math.Round(root.Bounds.Width * window.RenderScaling) - 1;
                int bottom = top + (int)Math.Round(root.Bounds.Height * window.RenderScaling) - 1;
                Console.WriteLine($"region corners={PtInRegion(region, left, top)},{PtInRegion(region, right, top)},{PtInRegion(region, left, bottom)},{PtInRegion(region, right, bottom)}");
                AssertTrue(PtInRegion(region, left, top) != 0 && PtInRegion(region, right, top) != 0
                    && PtInRegion(region, left, bottom) != 0 && PtInRegion(region, right, bottom) != 0);
            }
        }
        finally { _ = DeleteObject(region); }
    }
    private static void VerifyNativeDecorationPolicy(AvaloniaUiShellWindow window)
    {
        nint handle = window.TryGetPlatformHandle()!.Handle;
        AssertEqual(0, DwmGetWindowAttribute(handle, 1, out int nonClientEnabled, sizeof(int)));
        AssertEqual(0, nonClientEnabled);
        AssertEqual(0, DwmIsCompositionEnabled(out int compositionEnabled));
        AssertEqual(1, compositionEnabled);
        // Attributes 3, 33 and 34 are documented for DwmSetWindowAttribute only.
        // An undocumented getter's E_INVALIDARG is not an unsupported OS feature.
        long style = GetWindowLongPtrForChrome(handle, -16).ToInt64();
        const long nativeCapabilities = 0x00C00000 | 0x00040000 | 0x00020000 | 0x00010000;
        AssertEqual(nativeCapabilities, style & nativeCapabilities); // caption, resize, minimize, maximize
    }

    private static void VerifyNativeDwmSetterSupport()
    {
        // Use a separate HWND: writing these values to the product window would
        // mask a regression in its own decoration policy. This proves API admission,
        // not the visual appearance or smoothness of system transitions.
        var probe = new Window { Width = 160, Height = 100, ShowActivated = false, Title = "Nexa DWM probe" };
        try
        {
            probe.Show();
            nint handle = probe.TryGetPlatformHandle()!.Handle;
            int enabledTransitions = 0;
            AssertEqual(0, NativeCornerSetAttribute(handle, 3, in enabledTransitions, sizeof(int)));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int corners = 1, border = unchecked((int)AvaloniaWindowsFrame.NoBorderColor);
                AssertEqual(0, NativeCornerSetAttribute(handle, 33, in corners, sizeof(int)));
                AssertEqual(0, NativeCornerSetAttribute(handle, 34, in border, sizeof(int)));
            }
            Console.WriteLine("PASS: documented DWM transition/corner/border setters accepted on a separate HWND; no unsupported getter inference");
        }
        finally { probe.Close(); }
    }
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static partial int NativeCornerSetAttribute(nint window, int attribute, in int value, int size);
    [LibraryImport("dwmapi.dll")] private static partial int DwmIsCompositionEnabled(out int enabled);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct ChromeNativeRect { public int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRectForChrome(nint window, out ChromeNativeRect rect);
    [LibraryImport("gdi32.dll")] private static partial int PtInRegion(nint region, int x, int y);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtrForChrome(nint window, int index);
    [LibraryImport("user32.dll")] private static partial int GetWindowRgn(nint window, nint region);
    [LibraryImport("gdi32.dll")] private static partial nint CreateRectRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint region);
}

