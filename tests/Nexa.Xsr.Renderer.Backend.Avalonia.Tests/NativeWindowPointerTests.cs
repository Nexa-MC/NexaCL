using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static async Task VerifyNativeWindowPointerHitTestingAsync(
        XsrUiShell shell, AvaloniaUiShellWindow window)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native pointer hit testing requires Windows.");

        // A real clickable title-bar entity exercises the exclusion used by navigation/back
        // controls. The native fixture otherwise uses the same shell as the lifecycle probe.
        XsrUiEntityId back = shell.Tree.Create("native-pointer-back");
        shell.Tree.SetComponent(back, new XsrUiElement
        {
            Width = 48,
            Height = 36,
            VerticalAlignment = XsrUiAlignment.Center,
        });
        shell.Tree.SetComponent(back, new XsrUiInput { Clickable = true, Focusable = true });
        shell.Tree.SetComponent(back, new XsrUiText("返回"));
        shell.Tree.SetComponent(back, new XsrUiSemantic(XsrUiSemanticRole.Button, "返回"));
        shell.Tree.Attach(back, shell.TitleBar);
        try
        {
            window.Surface.CommitScene();
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            // Native non-client hit testing uses the presented composition tree.
            // A dispatcher/layout barrier alone need not publish the newly added control.
            var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.RequestAnimationFrame(_ => window.RequestAnimationFrame(_ => presented.SetResult()));
            await presented.Task.WaitAsync(TimeSpan.FromSeconds(5));

            nint handle = window.TryGetPlatformHandle()!.Handle;
            Border[] grips = window.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Tag is WindowEdge).ToArray();
            AssertEqual(8, grips.Length);
            LogNativePointerGeometry(window, handle, grips);
            XsrUiScene scene = AssertNotNull(window.Surface.Scene);
            XsrUiRect title = Node(scene, shell.TitleBar).Rect;
            Point titleBlank = new(title.X + title.Width * .65, title.Y + title.Height / 2);
            AssertTrue(scene.Nodes.All(node => !node.IsClickable
                || !node.Rect.Contains(new(titleBlank.X, titleBlank.Y))));

            // PointToScreen includes both the 24 DIP composited inset and the active DPI.
            // Checking a raw y=26 in client coordinates would accidentally probe that inset.
            AssertNativePointerHit(handle, window.Surface.PointToScreen(titleBlank), 2, "title-bar blank");
            XsrUiRect backRect = Node(scene, back).Rect;
            AssertNativePointerHit(handle, window.Surface.PointToScreen(new Point(
                backRect.X + backRect.Width / 2, backRect.Y + backRect.Height / 2)), 1, "back button");
            AssertNativePointerHit(handle, window.Surface.PointToScreen(new Point(
                window.Surface.Bounds.Width / 2, XsrUiShell.TitleBarHeight + 100)), 1, "page body");

            AvaloniaNativeWindowActions.WindowActionButton[] buttons = window.GetVisualDescendants()
                .OfType<AvaloniaNativeWindowActions.WindowActionButton>().ToArray();
            AssertEqual(3, buttons.Length);
            foreach (AvaloniaNativeWindowActions.WindowActionButton button in buttons)
            {
                AssertNativePointerHit(handle, button.PointToScreen(new Point(
                    button.Bounds.Width / 2, button.Bounds.Height / 2)), 1, "native window action");
            }

            if (window.WindowState == WindowState.Normal && window.CanResize)
            {
                // These are client-area grips, inside the HWND's own non-client frame.
                // Their explicit decoration roles must expose the same native sizing
                // zones instead of depending on the platform's outer frame thickness.
                VerifyActiveNativeResizeGrips(handle, grips);

                window.CanResize = false;
                try
                {
                    await WaitForNativePointerLayoutAsync(window);
                    LogNativePointerGeometry(window, handle, grips);
                    VerifyInactiveNativeResizeGrips(handle, grips);
                }
                finally
                {
                    window.CanResize = true;
                    await WaitForNativePointerLayoutAsync(window);
                }
                LogNativePointerGeometry(window, handle, grips);
                VerifyActiveNativeResizeGrips(handle, grips);
            }
            else VerifyInactiveNativeResizeGrips(handle, grips);

            Console.WriteLine($"PASS: actual HWND WM_NCHITTEST preserves caption drag, back/window actions and page client input in {window.WindowState}");
        }
        finally
        {
            shell.Tree.Destroy(back);
            window.Surface.CommitScene();
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }

    private static void AssertNativePointerHit(nint handle, PixelPoint screen, int expected, string region)
    {
        int actual = NativePointerHit(handle, screen);
        if (actual != expected)
            throw new InvalidOperationException($"Native {region} at {screen}: expected hit {expected}, got {actual}.");
    }

    private static async Task WaitForNativePointerLayoutAsync(AvaloniaUiShellWindow window)
    {
        window.UpdateLayout();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void VerifyInactiveNativeResizeGrips(nint handle, Border[] grips)
    {
        foreach (Border grip in grips)
        {
            AssertFalse(grip.IsHitTestVisible);
            AssertEqual(global::Avalonia.Input.WindowDecorationsElementRole.None,
                WindowDecorationProperties.GetElementRole(grip));
            PixelPoint screen = grip.PointToScreen(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2));
            int actual = NativePointerHit(handle, screen);
            // A maximized top edge may legitimately expose the title beneath the disabled
            // grip (HTCAPTION). It must never advertise a resize operation.
            if (actual is >= 10 and <= 17)
                throw new InvalidOperationException($"Inactive native resize {grip.Tag} at {screen} returned sizing hit {actual}.");
        }
        Console.WriteLine("PASS: inactive HWND sizing grips pass through without advertising native resize");
    }

    private static void VerifyActiveNativeResizeGrips(nint handle, Border[] grips)
    {
        foreach (Border grip in grips)
        {
            AssertTrue(grip.IsHitTestVisible);
            int expected = (WindowEdge)grip.Tag! switch
            {
                WindowEdge.North => 12,
                WindowEdge.South => 15,
                WindowEdge.West => 10,
                WindowEdge.East => 11,
                WindowEdge.NorthWest => 13,
                WindowEdge.NorthEast => 14,
                WindowEdge.SouthWest => 16,
                WindowEdge.SouthEast => 17,
                _ => throw new InvalidOperationException("Unexpected window sizing edge."),
            };
            AssertNativePointerHit(handle, grip.PointToScreen(new Point(
                grip.Bounds.Width / 2, grip.Bounds.Height / 2)), expected, $"resize {grip.Tag}");
        }
        Console.WriteLine("PASS: all 8 actual HWND sizing zones retain their native resize hit codes");
    }

    private static void LogNativePointerGeometry(AvaloniaUiShellWindow window, nint handle, Border[] grips)
    {
        AssertTrue(GetWindowRectForChrome(handle, out ChromeNativeRect bounds));
        AssertTrue(NativePointerGetClientRect(handle, out ChromeNativeRect client));
        NativePointerPoint origin = default;
        AssertTrue(NativePointerClientToScreen(handle, ref origin));
        var root = (Control)window.Content!;
        Console.WriteLine($"native-input state={window.WindowState}, canResize={window.CanResize}, dpi={window.RenderScaling}, "
            + $"hwnd={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}, client={origin.X},{origin.Y},{client.Right},{client.Bottom}, "
            + $"root={root.PointToScreen(default)} size={root.Bounds.Size}, scene={window.Surface.PointToScreen(default)} size={window.Surface.Bounds.Size}");
        // Log every edge before asserting any of them, so an early failure retains the
        // complete relationship between client grips and the platform's outer frame.
        foreach (Border grip in grips)
        {
            Console.WriteLine($"native-grip edge={grip.Tag}, bounds={grip.Bounds}, root={grip.TranslatePoint(default, root)}, "
                + $"screen={grip.PointToScreen(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2))}, "
                + $"role={WindowDecorationProperties.GetElementRole(grip)}, hitVisible={grip.IsHitTestVisible}");
        }
    }

    private static int NativePointerHit(nint handle, PixelPoint screen)
    {
        const uint nonClientHitTest = 0x0084;
        // WM_NCHITTEST packs signed screen coordinates, including negative monitor origins.
        nint position = unchecked((nint)(int)((uint)(ushort)screen.X | ((uint)(ushort)screen.Y << 16)));
        return NativePointerSendMessage(handle, nonClientHitTest, 0, position).ToInt32();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePointerPoint { public int X, Y; }

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativePointerGetClientRect(nint window, out ChromeNativeRect rectangle);

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativePointerClientToScreen(nint window, ref NativePointerPoint point);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint NativePointerSendMessage(nint window, uint message, nint wParam, nint lParam);
}
