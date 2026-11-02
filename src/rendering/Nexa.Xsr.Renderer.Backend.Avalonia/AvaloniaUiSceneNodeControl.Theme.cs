using Avalonia;
using Avalonia.Media;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal sealed partial class AvaloniaUiSceneNodeControl
{
    private AvaloniaUiPreparedThemeAppearance? _themeResources;
    private readonly MatrixTransform _themeTransform = new();
    internal long PresentedThemeGeneration => _themeResources?.Appearance.Generation ?? 0;
    internal void SetThemeResources(AvaloniaUiPreparedThemeAppearance? resources)
    {
        if (ReferenceEquals(resources, _themeResources)) return;
        resources?.Borrow();
        AvaloniaUiPreparedThemeAppearance? previous = _themeResources;
        _themeResources = resources;
        previous?.ReleaseBorrow();
        _formattedText = null; _textKey = null; _inputCaretKey = null; _inputCarets = null;
        InvalidateVisual();
    }

    private Typeface ThemeTypeface(FontWeight weight, FontStyle style = FontStyle.Normal) =>
        _themeResources?.TextLayout?.Resolve(_node.FontFamily, weight, style) ?? CachedTypeface(weight, style);

    private void ApplyThemeGeometry(XsrUiSceneNode node)
    {
        XsrUiRect layout = node.LayoutRect ?? node.Rect;
        Matrix local = node.Transform is { } transform
            ? Matrix.CreateTranslation(layout.X, layout.Y)
                * new Matrix(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY)
                * Matrix.CreateTranslation(-layout.X, -layout.Y)
            : Matrix.Identity;
        // The existing host interaction group is center-origin; cancel that origin for the canonical scene matrix.
        _themeTransform.Matrix = Matrix.CreateTranslation(layout.Width / 2, layout.Height / 2) * local
            * Matrix.CreateTranslation(-layout.Width / 2, -layout.Height / 2);
        ClipToBounds = node.Transform is null && node.Shadow is null;
        // Blur is applied to the actual composited node. Glass backdrop is a separate platform capability.
        Effect = node.VisualStyle.BlurRadius > 0 ? new BlurEffect { Radius = node.VisualStyle.BlurRadius } : null;
    }

    private static Geometry CreateThemeClip(XsrUiSceneNode node, IReadOnlyList<XsrUiSceneClip> clips, XsrUiRect layout)
    {
        if (clips.Count > 64) throw new InvalidDataException("Scene clip count exceeds its budget.");
        Geometry? intersection = null;
        foreach (XsrUiSceneClip clip in clips)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                XsrUiPoint[] corners = [new(clip.Rect.X, clip.Rect.Y), new(clip.Rect.X + clip.Rect.Width, clip.Rect.Y),
                    new(clip.Rect.X + clip.Rect.Width, clip.Rect.Y + clip.Rect.Height), new(clip.Rect.X, clip.Rect.Y + clip.Rect.Height)];
                for (int index = 0; index < corners.Length; index++)
                {
                    XsrUiPoint global = clip.Transform?.Map(corners[index]) ?? corners[index];
                    XsrUiPoint local = node.Transform?.Unmap(global) ?? global;
                    Point point = new(local.X - layout.X, local.Y - layout.Y);
                    if (index == 0) path.BeginFigure(point, true); else path.LineTo(point);
                }
                path.EndFigure(true);
            }
            intersection = intersection is null ? geometry : new CombinedGeometry(GeometryCombineMode.Intersect, intersection, geometry);
        }
        return intersection!;
    }

    private XsrUiLinearGradient? _themeGradientSource;
    private IBrush? _themeGradientBrush;
    private XsrUiSurfaceKind _themeGradientSurface;
    private IBrush? ThemeGradient(XsrUiLinearGradient? gradient)
    {
        if (gradient is null) { _themeGradientSource = null; _themeGradientBrush = null; return null; }
        if (_themeGradientSource == gradient && _themeGradientBrush is not null && _themeGradientSurface == _node.VisualStyle.Surface) return _themeGradientBrush;
        _themeGradientSource = gradient; _themeGradientSurface = _node.VisualStyle.Surface;
        double radians = gradient.Angle * Math.PI / 180;
        double x = Math.Cos(radians) / 2, y = Math.Sin(radians) / 2;
        return _themeGradientBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(.5 - x, .5 - y, RelativeUnit.Relative),
            EndPoint = new RelativePoint(.5 + x, .5 + y, RelativeUnit.Relative),
            GradientStops = [.. gradient.Stops.Select(s => new GradientStop(
                Color.FromArgb(MaterialColor(s.Color, _node.VisualStyle.Surface).Alpha, s.Color.Red, s.Color.Green, s.Color.Blue), s.Position))],
        };
    }

    private static double MaterialOpacity(XsrUiSurfaceKind surface) => surface switch
    { XsrUiSurfaceKind.Translucent => .72, XsrUiSurfaceKind.Glass => .56, _ => 1 };
    private static XsrUiColor MaterialColor(XsrUiColor color, XsrUiSurfaceKind surface) =>
        color with { Alpha = (byte)Math.Round(color.Alpha * MaterialOpacity(surface)) };
    private static void DrawMaterialGlaze(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style)
    {
        if (style.Surface != XsrUiSurfaceKind.Glass) return;
        var sheen = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromArgb(20, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), .65)],
        };
        context.DrawRectangle(sheen, new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), 1),
            new RoundedRect(rect.Deflate(.5), new CornerRadius(Math.Max(0, style.CornerRadius - .5))));
    }

    private BoxShadows ThemeShadow() => _node.Shadow is { } shadow ? new BoxShadows(new BoxShadow
    {
        Color = Color.FromArgb(shadow.Color.Alpha, shadow.Color.Red, shadow.Color.Green, shadow.Color.Blue),
        OffsetX = shadow.OffsetX,
        OffsetY = shadow.OffsetY,
        Blur = shadow.BlurRadius,
    }) : default;

    private void DrawThemeBackgroundImage(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style)
    {
        if (_node.BackgroundImage is not { } image || _themeResources is null) return;
        using var clip = context.PushClip(new RoundedRect(rect, new CornerRadius(style.CornerRadius)));
        _themeResources.DrawImage(context, image.ResourceId, rect, image.Fit, image.Opacity * MaterialOpacity(style.Surface));
    }

    private bool DrawThemeImage(DrawingContext context, Rect rect) => _node.ImageSource is { } source
        && _themeResources?.DrawImage(context, source, rect, "contain") == true;

    internal XsrUiPoint LocalTextPoint(XsrUiPoint global)
    {
        XsrUiPoint raw = _node.Transform?.Unmap(global) ?? global;
        XsrUiRect layout = _node.LayoutRect ?? _node.Rect;
        return new(raw.X - layout.X, raw.Y - layout.Y);
    }
}
