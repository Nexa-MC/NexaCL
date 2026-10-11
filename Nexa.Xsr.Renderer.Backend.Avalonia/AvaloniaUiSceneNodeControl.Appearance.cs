namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal sealed partial class AvaloniaUiSceneNodeControl
{
    private static readonly XsrUiVisualStyleSnapshot OwnedToggleSurface = new XsrUiVisualStyle
    {
        Background = new(255, 255, 255),
        Border = new(178, 189, 204),
    }.Snapshot();
    private static readonly XsrUiVisualStyleSnapshot OwnedToggleRail = new XsrUiVisualStyle
    {
        Background = new(218, 225, 234),
    }.Snapshot();
    private static readonly XsrUiVisualStyleSnapshot OwnedCapsuleHighlight = new XsrUiVisualStyle
    {
        Background = new(255, 255, 255, 245),
    }.Snapshot();

    internal double PresentedInteractionScale => _pressScale.ScaleX;

    // Geometry remains fixed for hit testing; presentation feedback must not pull a wide row away.
    internal static double InteractionScale(XsrUiSceneNode node, bool reducedMotion)
    {
        if (!node.IsEnabled || !node.IsPressed || reducedMotion || node.TextInput is not null) return 1;
        double extent = Math.Max(node.Rect.Width, node.Rect.Height);
        return 1 - Math.Min(1 - AvaloniaMotionTokens.PressScale, 2 / Math.Max(1, extent));
    }

    internal static XsrUiColor InteractionColor(XsrUiSceneNode node, XsrUiVisualStyleSnapshot style)
    {
        if (!node.IsEnabled) return XsrUiColor.Transparent;
        if (style.Hover.Alpha > 0) return node.IsPressed
            ? style.Hover with { Alpha = (byte)Math.Min(255, style.Hover.Alpha + 20) }
            : style.Hover;
        return node.IsClickable && node.TextInput is null
            ? node.ColorScheme.AccentText with { Alpha = node.IsPressed ? (byte)32 : (byte)16 }
            : XsrUiColor.Transparent;
    }

    private XsrUiColor ToggleFill(XsrUiVisualStyleSnapshot style)
    {
        XsrUiColor original = style.Border.Alpha > 0 ? style.Border : style.Foreground;
        XsrUiColorScheme scheme = _node.ColorScheme;
        var rgb = (original.Red, original.Green, original.Blue);
        if (rgb == (scheme.AccentText.Red, scheme.AccentText.Green, scheme.AccentText.Blue)
            || rgb == (scheme.AccentFill.Red, scheme.AccentFill.Green, scheme.AccentFill.Blue)
            || rgb is (11, 91, 203) or (19, 112, 243) or (28, 97, 210))
            return scheme.AccentFill with { Alpha = original.Alpha };
        // Explicit custom scene fill colors are already projected by the renderer and
        // must not be replaced merely because they are used by a toggle.
        return original;
    }
}
