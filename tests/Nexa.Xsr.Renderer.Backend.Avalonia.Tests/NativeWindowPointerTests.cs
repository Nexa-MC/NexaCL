using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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

            nint handle = window.TryGetPlatformHandle()!.Handle;
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
                // Grips belong to the outer HWND root, not the inset scene. Their centers
                // must retain the actual OS sizing zones used for mouse, pen and touch.
                Border[] grips = window.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Tag is WindowEdge).ToArray();
                AssertEqual(8, grips.Length);
                foreach (Border grip in grips)
                {
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
        const uint nonClientHitTest = 0x0084;
        // WM_NCHITTEST packs signed screen coordinates, including negative monitor origins.
        nint position = unchecked((nint)(int)((uint)(ushort)screen.X | ((uint)(ushort)screen.Y << 16)));
        int actual = NativePointerSendMessage(handle, nonClientHitTest, 0, position).ToInt32();
        if (actual != expected)
            throw new InvalidOperationException($"Native {region} at {screen}: expected hit {expected}, got {actual}.");
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint NativePointerSendMessage(nint window, uint message, nint wParam, nint lParam);
}
