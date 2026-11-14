using Avalonia.Controls;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void NativeReadinessFollowsPresentationAndCurrentOwner()
    {
        var actions = new AvaloniaUiPlatformActions();
        var first = new Window(); var second = new Window();
        try
        {
            AssertFalse(actions.IsNativeWindowReady);
            actions.AttachForStartup(first);
            first.Show();
            AssertFalse(actions.IsNativeWindowReady);
            actions.CompleteStartup();
            AssertTrue(actions.IsNativeWindowReady);
            first.Hide();
            AssertTrue(actions.IsNativeWindowReady);
            actions.AttachForStartup(second);
            AssertFalse(actions.IsNativeWindowReady);
            actions.CompleteStartup();
            AssertFalse(actions.IsNativeWindowReady);
            second.Show();
            AssertTrue(actions.IsNativeWindowReady);
            first.Close();
            AssertTrue(actions.IsNativeWindowReady);
            second.Close();
            AssertFalse(actions.IsNativeWindowReady);
            actions.CompleteStartup();
            AssertFalse(actions.IsNativeWindowReady);
        }
        finally { first.Close(); second.Close(); }
    }
}
