using Avalonia.Media;
using Nexa.UI.Next;

namespace Nexa.UI.Next.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void SharedPressFeedbackIsImmediateBoundedAndAvailableWithoutMotion()
    {
        var tree = new XsrUiTree();
        var node = Node(tree.Create("wide-action"), XsrUiSemanticRole.Button, clickable: true) with
        { Rect = new(0, 0, 900, 48), IsPressed = true };
        double scale = AvaloniaUiSceneNodeControl.InteractionScale(node, false);
        AssertTrue(scale < 1 && (1 - scale) * node.Rect.Width <= 2.00001);
        AssertEqual(1d, AvaloniaUiSceneNodeControl.InteractionScale(node, true));
        AssertEqual(1d, AvaloniaUiSceneNodeControl.InteractionScale(node with { IsEnabled = false }, false));
        AssertEqual(1d, AvaloniaUiSceneNodeControl.InteractionScale(node with { IsPressed = false }, false));
        var style = new XsrUiVisualStyle().Snapshot();
        // Explicitly transparent checkmarks/text are hidden, not white in dark mode.
        var hidden = new XsrUiVisualStyle { Foreground = XsrUiColor.Transparent }.Snapshot();
        AssertEqual((byte)0, ((ISolidColorBrush)AvaloniaUiSceneNodeControl.ForegroundBrush(hidden)).Color.A);
        AssertEqual((byte)255, ((ISolidColorBrush)AvaloniaUiSceneNodeControl.ForegroundBrush(default)).Color.A);
        AssertTrue(AvaloniaUiSceneNodeControl.InteractionColor(node, style).Alpha > 0);
        AssertEqual((byte)0, AvaloniaUiSceneNodeControl.InteractionColor(node with { IsEnabled = false }, style).Alpha);
        var custom = new XsrUiVisualStyle { Hover = new(220, 90, 40, 64) }.Snapshot();
        AssertEqual(custom.Hover, AvaloniaUiSceneNodeControl.InteractionColor(node with { IsPressed = false }, custom));
        AssertTrue(AvaloniaUiSceneNodeControl.InteractionColor(node, custom).Alpha > custom.Hover.Alpha);
        // The actual retained control follows press and disabled facts synchronously in reduced motion.
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
        control.Apply(node);
        AssertEqual(1d, control.PresentedInteractionScale);
        control.Apply(node with { IsEnabled = false });
        AssertEqual(1d, control.PresentedInteractionScale);
    }
}
