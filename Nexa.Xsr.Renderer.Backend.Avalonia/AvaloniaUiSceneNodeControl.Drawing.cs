using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal sealed partial class AvaloniaUiSceneNodeControl
{

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        AvaloniaUiRuntimeDiagnostics.NodePainted();
        Rect rect = new(Bounds.Size);
        XsrUiVisualStyleSnapshot style = _node.VisualStyle;
        double enter = EnterProgress * (_node.OverlayMotion == XsrUiOverlayMotionKind.None
            ? 1
            : Math.Clamp(OverlayProgress, 0, 1));

        // Disabled nodes keep their layout and draw dimmed, so position and hit routing stay
        // stable while the state reads as unavailable.
        if (!_node.IsEnabled && _node.IsAccessible)
        {
            using (context.PushOpacity(0.45))
            {
                DrawWithEnter(context, rect, style, enter);
            }

            return;
        }

        if (enter < 1)
        {
            using (context.PushOpacity(Math.Clamp(enter, 0, 1)))
            {
                DrawContent(context, rect, style);
            }

            return;
        }

        DrawContent(context, rect, style);
    }

    private void DrawWithEnter(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style, double enter)
    {
        if (enter < 1)
        {
            using (context.PushOpacity(Math.Clamp(enter, 0, 1)))
            {
                DrawContent(context, rect, style);
            }

            return;
        }

        DrawContent(context, rect, style);
    }

    private void DrawContent(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style)
    {
        if (_node.Role is XsrUiSemanticRole.Switch or XsrUiSemanticRole.CheckBox)
        {
            DrawToggle(context, rect, style);
            return;
        }
        if (style.HoverExpand)
        {
            DrawHoverPill(context, rect, style);
            return;
        }

        IBrush? background = Brush(style.Background);
        IPen? border = style.BorderWidth > 0 ? new Pen(Brush(style.Border), style.BorderWidth) : null;
        if (style.CornerRadius > 0)
        {
            context.DrawRectangle(
                background,
                border,
                new RoundedRect(rect, new CornerRadius(style.CornerRadius)));
        }
        else
        {
            context.DrawRectangle(background, border, rect);
        }

        DrawHoverOverlay(context, rect, style);
        if (_node.Graph is { } graph) { DrawGraph(context, rect, graph); return; }
        if (_node.TextInput is { } textInput)
        {
            DrawTextInput(context, rect, style, textInput);
            return;
        }

        // Collapsed rail items center the vector icon and hide the label; expanded rail rows
        // keep the icon exactly where the collapsed state centered it (past the selection
        // pill) and reveal the label after it. Any other icon-bearing node without text (or a
        // collapsed rail item) centers its icon too.
        bool collapsedRailItem = style.NavigationLayout
            && Bounds.Width <= CollapsedRailCenteringWidth;
        bool textVisible = _node.Text is { Length: > 0 } && !collapsedRailItem;
        bool railRow = textVisible && style.NavigationLayout;
        if (DrawRaster(context, rect) || (_node.ImageSource is { Length: > 0 } avatarSource &&
            (AvaloniaUiAvatars.TryDraw(context, avatarSource, rect) || AvaloniaUiVersionImages.TryDraw(context, avatarSource, rect))))
        {
            if (textVisible) DrawText(context, style, 0);
        }
        else if (_node.ImageSource is { Length: > 0 } iconSource
            && AvaloniaUiIcons.TryGetGeometry(iconSource, out IReadOnlyList<Geometry> iconPaths))
        {
            double iconSize = _node.Role == XsrUiSemanticRole.Image
                ? Math.Min(Bounds.Width, Bounds.Height) : NavigationIconSize;
            double scale = iconSize / AvaloniaUiIcons.ViewBoxSize;
            double iconX = railRow
                ? RailRowIconOffset
                : textVisible
                    ? 0
                    : Math.Max(0, (Bounds.Width - iconSize) / 2);
            double iconY = Math.Max(0, (Bounds.Height - iconSize) / 2);
            IBrush iconBrush = ForegroundBrush(style);
            Pen iconPen = new(iconBrush, AvaloniaUiIcons.StrokeWidth)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            using (context.PushTransform(
                Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(iconX, iconY)))
            {
                foreach (Geometry path in iconPaths)
                {
                    context.DrawGeometry(null, iconPen, path);
                }
            }

            if (textVisible)
            {
                DrawText(context, style, iconX + NavigationIconSize + NavigationIconTextGap);
            }
        }
        else if (textVisible)
        {
            DrawText(context, style, 0);
        }

        DrawSelectionPill(context, style);
        if (_node.Scroll is { } scroll)
        {
            if (_node.VerticalScrollbar is { } vertical) DrawScrollbar(context, LocalScrollbar(vertical), _node.ColorScheme);
            else if (scroll.Scrollbar(new(0, 0, rect.Width, rect.Height), XsrUiOrientation.Vertical) is { } projectedVertical)
                DrawScrollbar(context, projectedVertical, _node.ColorScheme);
            if (_node.HorizontalScrollbar is { } horizontal) DrawScrollbar(context, LocalScrollbar(horizontal), _node.ColorScheme);
            else if (scroll.Scrollbar(new(0, 0, rect.Width, rect.Height), XsrUiOrientation.Horizontal) is { } projectedHorizontal)
                DrawScrollbar(context, projectedHorizontal, _node.ColorScheme);
        }

        if (_node.IsFocusVisible)
        {
            context.DrawRectangle(
                null,
                new Pen(Brush(_node.ColorScheme.AccentText)!, 2),
                new RoundedRect(rect.Deflate(1), new CornerRadius(Math.Max(0, style.CornerRadius - 1))));
        }
    }

    private void DrawHoverOverlay(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style)
    {
        double opacity = _node.IsEnabled ? (_node.IsPressed ? 1 : HoverOpacity) : 0;
        XsrUiColor color = InteractionColor(_node, style);
        if (opacity <= 0 || color.Alpha == 0)
        {
            return;
        }

        byte alpha = (byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1));
        IBrush hover = new SolidColorBrush(Color.FromArgb(alpha, color.Red, color.Green, color.Blue));
        context.DrawRectangle(
            hover,
            null,
            style.CornerRadius > 0
                ? new RoundedRect(rect, new CornerRadius(style.CornerRadius))
                : new RoundedRect(rect));
    }

    private void DrawSelectionPill(DrawingContext context, XsrUiVisualStyleSnapshot style)
    {
        double scale = PillScale;
        if (_node.Role != XsrUiSemanticRole.NavigationItem || scale <= 0)
        {
            return;
        }

        XsrUiColor pill = style.Border.Alpha > 0 ? style.Border : style.Foreground;
        double height = PillHeight * Math.Clamp(scale, 0, 1);
        double y = Math.Max(0, (Bounds.Height - height) / 2);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(pill.Alpha, pill.Red, pill.Green, pill.Blue)),
            null,
            new RoundedRect(new Rect(0, y, PillWidth, height), new CornerRadius(PillWidth / 2)));
    }

    private void DrawText(DrawingContext context, XsrUiVisualStyleSnapshot style, double x)
    {
        if (_node.Text is not { Length: > 0 } text)
        {
            return;
        }

        IBrush foreground = ForegroundBrush(style);
        // Explicit visual-style typography wins; otherwise the semantic role decides, mirroring
        // the legacy sizes (17 px title, 12 px rail items, 14 px body text).
        double fontSize = style.FontSize > 0 ? style.FontSize : FontSizeFor(_node);
        FontWeight weight = style.FontWeight >= 600 ? FontWeight.SemiBold : FontWeight.Normal;
        double maxWidth = (style.WrapText || _node.TextTrimsOverflow) && Bounds.Width > x ? Bounds.Width - x : 0;
        var key = new TextCacheKey(text, fontSize, weight, style.Foreground, maxWidth,
            _node.TextMaxLines, _node.TextTrimsOverflow, _node.TextRuns, CultureInfo.CurrentUICulture.Name);
        if (_textKey != key || _formattedText is null)
        {
            _textKey = key;
            _formattedText = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                CachedTypeface(weight), fontSize, foreground);
            FormattedText fresh = _formattedText;
            foreach (var run in _node.TextRuns ?? [])
            {
                if (run.Start < 0 || run.Length <= 0 || run.Start > text.Length - run.Length) continue;
                fresh.SetForegroundBrush(Brush(run.Foreground), run.Start, run.Length);
                fresh.SetFontFamily(CachedTypeface(run.Bold ? FontWeight.Bold : weight,
                    run.Italic ? FontStyle.Italic : FontStyle.Normal).FontFamily, run.Start, run.Length);
                fresh.SetFontWeight(run.Bold ? FontWeight.Bold : weight, run.Start, run.Length);
                fresh.SetFontStyle(run.Italic ? FontStyle.Italic : FontStyle.Normal, run.Start, run.Length);
                TextDecorationCollection decorations = [];
                if (run.Underline) decorations.Add(TextDecorations.Underline[0]);
                if (run.Strikethrough) decorations.Add(TextDecorations.Strikethrough[0]);
                if (decorations.Count > 0) fresh.SetTextDecorations(decorations, run.Start, run.Length);
            }
            if (maxWidth > 0) fresh.MaxTextWidth = maxWidth;
            if (_node.TextMaxLines > 0)
                fresh.MaxLineCount = _node.TextMaxLines;
            if (_node.TextTrimsOverflow)
                fresh.Trimming = TextTrimming.WordEllipsis;
        }
        FormattedText formatted = _formattedText;
        double alignedX = style.TextAlignment switch
        {
            XsrUiTextAlignment.Center => Math.Max(x, (Bounds.Width - formatted.Width) / 2),
            XsrUiTextAlignment.End => Math.Max(x, Bounds.Width - formatted.Width),
            _ => x,
        };
        double y = Math.Max(0, (Bounds.Height - formatted.Height) / 2);
        // Minecraft names often explicitly request white or yellow. Retain their color while
        // giving pale runs a small contrasting edge on a light settings surface.
        if (_node.TextRuns?.Any(run => run.Foreground.Red > 200 && run.Foreground.Green > 200) == true)
        {
            formatted.SetForegroundBrush(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)));
            context.DrawText(formatted, new Point(alignedX + .7, y + .7));
            formatted.SetForegroundBrush(foreground);
            foreach (var run in _node.TextRuns)
                if (run.Start >= 0 && run.Length > 0 && run.Start <= text.Length - run.Length)
                    formatted.SetForegroundBrush(Brush(run.Foreground), run.Start, run.Length);
        }
        context.DrawText(formatted, new Point(alignedX, y));
    }

    internal static void DrawScrollIndicator(DrawingContext context, Rect rect, XsrUiScrollSnapshot scroll, XsrUiColorScheme scheme = default)
    {
        if (scroll.Scrollbar(new(0, 0, rect.Width, rect.Height), XsrUiOrientation.Vertical) is { } vertical)
            DrawScrollbar(context, vertical, scheme);
        if (scroll.Scrollbar(new(0, 0, rect.Width, rect.Height), XsrUiOrientation.Horizontal) is { } horizontal)
            DrawScrollbar(context, horizontal, scheme);
    }

    private XsrUiScrollbarSnapshot LocalScrollbar(XsrUiScrollbarSnapshot scrollbar) => scrollbar with
    {
        Track = scrollbar.Track with { X = scrollbar.Track.X - _node.Rect.X, Y = scrollbar.Track.Y - _node.Rect.Y },
        Thumb = scrollbar.Thumb with { X = scrollbar.Thumb.X - _node.Rect.X, Y = scrollbar.Thumb.Y - _node.Rect.Y },
        HitRect = scrollbar.HitRect with { X = scrollbar.HitRect.X - _node.Rect.X, Y = scrollbar.HitRect.Y - _node.Rect.Y },
    };

    private static void DrawScrollbar(DrawingContext context, XsrUiScrollbarSnapshot scrollbar, XsrUiColorScheme scheme)
    {
        Rect track = new(scrollbar.Track.X, scrollbar.Track.Y, scrollbar.Track.Width, scrollbar.Track.Height);
        Rect thumb = new(scrollbar.Thumb.X, scrollbar.Thumb.Y, scrollbar.Thumb.Width, scrollbar.Thumb.Height);
        XsrUiColor ink = scheme.Foreground(new(91, 105, 122));
        context.DrawRectangle(
            Brush(ink with { Alpha = 28 }),
            null,
            new RoundedRect(track, new CornerRadius(1.5)));
        context.DrawRectangle(
            Brush(ink with { Alpha = scheme.IsDark ? (byte)185 : (byte)124 }),
            null,
            new RoundedRect(thumb, new CornerRadius(1.5)));
    }

    /// <summary>
    /// Hover-expanding capsule: at rest an icon circle pinned to the node's right edge; on
    /// hover/focus the pill grows leftward from that circle and the node's scene text fades in
    /// beside the icon. Layout and hit regions are already the presented width in the scene.
    /// </summary>
    private void DrawHoverPill(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style)
    {
        double progress = Math.Clamp(_node.CapsuleExpansionProgress, 0, 1);
        double pillHeight = rect.Height;
        double fontSize = style.FontSize > 0 ? style.FontSize : 13;
        double pillWidth = rect.Width;
        double pillX = rect.Right - pillWidth;
        var pillRect = new Rect(pillX, rect.Y, pillWidth, rect.Height);
        XsrUiColor highlight = _node.ColorScheme.Project(OwnedCapsuleHighlight).Background;
        IBrush? pillBackground = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(highlight.Alpha, highlight.Red, highlight.Green, highlight.Blue), 0),
                new GradientStop(Color.FromArgb(style.Background.Alpha, style.Background.Red,
                    style.Background.Green, style.Background.Blue), 1),
            ],
        };
        IPen? pillBorder = style.BorderWidth > 0 ? new Pen(Brush(style.Border), style.BorderWidth) : null;
        context.DrawRectangle(
            pillBackground,
            pillBorder,
            new RoundedRect(pillRect, new CornerRadius(pillHeight / 2)));

        if (_node.IsPressed) DrawHoverOverlay(context, pillRect, style with { CornerRadius = pillHeight / 2 });

        // Keep the icon anchored at the trailing edge throughout expansion and reversal.
        double iconX = rect.Right - (pillHeight + NavigationIconSize) / 2;
        double iconY = Math.Max(0, (rect.Height - NavigationIconSize) / 2);
        if (_node.ImageSource is { Length: > 0 } iconSource
            && AvaloniaUiIcons.TryGetGeometry(iconSource, out IReadOnlyList<Geometry> iconPaths))
        {
            double scale = NavigationIconSize / AvaloniaUiIcons.ViewBoxSize;
            IBrush iconBrush = ForegroundBrush(style);
            Pen iconPen = new(iconBrush, AvaloniaUiIcons.StrokeWidth)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            using (context.PushTransform(
                Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(iconX, iconY)))
            {
                foreach (Geometry path in iconPaths)
                {
                    context.DrawGeometry(null, iconPen, path);
                }
            }
        }
        else if (_node.Text is { Length: > 0 } glyph)
        {
            IBrush glyphBrush = ForegroundBrush(style);
            FormattedText glyphText = new(
                glyph,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                CachedTypeface(FontWeightFor(style)),
                Math.Max(12, style.FontSize > 0 ? style.FontSize : 14),
                glyphBrush);
            double glyphX = rect.Right - ((pillHeight + glyphText.Width) / 2);
            double glyphY = Math.Max(0, (rect.Height - glyphText.Height) / 2);
            context.DrawText(glyphText, new Point(glyphX, glyphY));
        }

        if (progress > 0.01 && _node.Text is { Length: > 0 } label)
        {
            byte alpha = (byte)Math.Round(255 * progress);
            IBrush labelBrush = new SolidColorBrush(Color.FromArgb(
                alpha,
                style.Foreground.Red,
                style.Foreground.Green,
                style.Foreground.Blue));
            FormattedText labelText = new(
                label,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                CachedTypeface(FontWeightFor(style)),
                fontSize,
                labelBrush);
            double labelX = iconX - CapsuleCaptionIconGap - labelText.Width;
            double labelY = Math.Max(0, (rect.Height - labelText.Height) / 2);
            using (context.PushClip(new Rect(pillX + 8, rect.Y,
                Math.Max(0, iconX - CapsuleCaptionIconGap - (pillX + 8)), rect.Height)))
                context.DrawText(labelText, new Point(labelX, labelY));
        }

        if (_node.IsFocusVisible)
            context.DrawRectangle(null, new Pen(Brush(_node.ColorScheme.AccentText)!, 2),
                new RoundedRect(pillRect.Deflate(2), new CornerRadius(pillHeight / 2 - 2)));
    }

    private static FontWeight FontWeightFor(XsrUiVisualStyleSnapshot style) =>
        style.FontWeight >= 600 ? FontWeight.SemiBold : FontWeight.Normal;

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    private static double FontSizeFor(XsrUiSceneNode node) => node.Role switch
    {
        XsrUiSemanticRole.TitleBar => 17,
        XsrUiSemanticRole.NavigationItem => 12,
        XsrUiSemanticRole.Text => 14,
        _ => 14,
    };

    private static AutomationControlType ControlTypeFor(XsrUiSemanticRole role, bool clickable) => role switch
    {
        XsrUiSemanticRole.Button => AutomationControlType.Button,
        XsrUiSemanticRole.Switch or XsrUiSemanticRole.CheckBox => AutomationControlType.CheckBox,
        XsrUiSemanticRole.RadioGroup => AutomationControlType.Group,
        XsrUiSemanticRole.RadioButton => AutomationControlType.RadioButton,
        XsrUiSemanticRole.NavigationItem => AutomationControlType.ListItem,
        XsrUiSemanticRole.Navigation => AutomationControlType.List,
        XsrUiSemanticRole.TitleBar => AutomationControlType.TitleBar,
        XsrUiSemanticRole.Text => AutomationControlType.Text,
        XsrUiSemanticRole.TextInput => AutomationControlType.Edit,
        XsrUiSemanticRole.Image => AutomationControlType.Image,
        XsrUiSemanticRole.ProgressBar => AutomationControlType.ProgressBar,
        XsrUiSemanticRole.Dialog => AutomationControlType.Window,
        XsrUiSemanticRole.Status when clickable => AutomationControlType.Button,
        XsrUiSemanticRole.Status => AutomationControlType.Text,
        XsrUiSemanticRole.Content => AutomationControlType.Pane,
        _ when clickable => AutomationControlType.Button,
        _ => AutomationControlType.Custom,
    };

    private TextCacheKey? _textKey;
    private FormattedText? _formattedText;
    private readonly record struct TextCacheKey(string Text, double Size, FontWeight Weight, XsrUiColor Color,
        double Width, int Lines, bool Trim, IReadOnlyList<XsrUiTextRun>? Runs, string Culture);
    private static readonly Dictionary<XsrUiColor, IBrush> BrushesByColor = [];
    private static Typeface CachedTypeface(FontWeight weight, FontStyle style = FontStyle.Normal) =>
        AvaloniaUiTypefaceCache.GetDefault(weight, style);
    internal static IBrush ForegroundBrush(XsrUiVisualStyleSnapshot style) =>
        Brush(style.Foreground) ?? (style.IsDefined ? Brushes.Transparent : Brushes.White);

    private static IBrush? Brush(XsrUiColor color)
    {
        if (color.Alpha == 0) return null;
        if (BrushesByColor.TryGetValue(color, out var brush)) return brush;
        if (BrushesByColor.Count >= 1024) BrushesByColor.Clear();
        brush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
        BrushesByColor.Add(color, brush);
        return brush;
    }
}
