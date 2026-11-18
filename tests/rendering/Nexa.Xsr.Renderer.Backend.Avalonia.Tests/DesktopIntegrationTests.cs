using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void VerifySessionEndClosesWindow(WindowCloseReason reason)
    {
        var shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
        var window = new AvaloniaUiShellWindow(shell);
        int trayRequests = 0, guardRequests = 0;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.HideToTrayRequested = () => { trayRequests++; return true; };
        window.CloseGuard = () => { guardRequests++; return false; };
        window.Show();
        try
        {
            var closing = GetPlatformClosing(window.PlatformImpl!)!;
            AssertFalse(closing(reason));
            AssertTrue(closed);
            AssertEqual(0, trayRequests);
            AssertEqual(0, guardRequests);
        }
        finally
        {
            if (!closed)
            {
                window.HideToTrayRequested = null;
                window.CloseGuard = null;
                shell.Renderer.ReducedMotion = true;
                window.Close();
            }
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Closing")]
    private static extern Func<WindowCloseReason, bool>? GetPlatformClosing(IWindowImpl impl);

    private static void VerifyTrayCloseAndExplicitExitAdmission(AvaloniaUiShellWindow window, XsrUiShell shell)
    {
        using (var integration = new AvaloniaUiDesktopIntegration(window, () => { window.Show(); window.Activate(); }, () => { }, value => value))
        {
            // A native host without an icon must never hide an inaccessible window.
            integration.Apply(new(true, true, true));
            AssertFalse(integration.TrayAvailable);
            AssertTrue(window.HideToTrayRequested!());
            AssertTrue(window.IsVisible);
            AssertEqual(WindowState.Minimized, window.WindowState);
            window.WindowState = WindowState.Normal;
        }
        int restores = 0;
        window.StartupRevealCompleted += OnReveal;
        void OnReveal(object? sender, EventArgs args) => restores++;
        int hidden = 0, guarded = 0;
        var oldGuard = window.CloseGuard;
        window.HideToTrayRequested = () => { hidden++; window.Hide(); return true; };
        window.CloseGuard = () => { guarded++; return false; };
        try
        {
            window.RequestWindowClose();
            AssertEqual(1, hidden); AssertEqual(0, guarded); AssertFalse(window.IsVisible);
            window.Show(); window.Activate();
            AssertTrue(window.IsVisible); AssertEqual(0, restores);
            // Explicit tray exit must go through pending-install admission, never hide again.
            window.RequestClose();
            AssertEqual(1, hidden); AssertEqual(1, guarded); AssertTrue(window.IsVisible);
        }
        finally
        {
            window.CloseGuard = oldGuard; window.HideToTrayRequested = null;
            window.StartupRevealCompleted -= OnReveal;
        }
    }
}
