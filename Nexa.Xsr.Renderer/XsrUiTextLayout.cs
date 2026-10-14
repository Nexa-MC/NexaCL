namespace Nexa.Xsr.Renderer;

/// <summary>Backend-independent request used for both intrinsic layout and native text drawing.</summary>
public sealed record XsrUiTextLayoutRequest(string Text, string? FontFamily, double FontSize,
    double FontWeight, double MaximumWidth = double.PositiveInfinity, int MaximumLines = 0,
    bool IsPassword = false, bool IncludeCaretPositions = false, IReadOnlyList<XsrUiTextRun>? TextRuns = null);
public sealed record XsrUiTextLayoutMetrics(XsrUiSize Size, IReadOnlyList<double>? CaretPositions = null);
public interface IXsrUiTextLayoutProvider
{
    long Revision { get; }
    XsrUiTextLayoutMetrics Measure(XsrUiTextLayoutRequest request);
}

/// <summary>Affine scene geometry shared by drawing, accessibility and inverse pointer hit testing.</summary>
public readonly record struct XsrUiSceneTransform(double M11, double M12, double M21, double M22,
    double OffsetX, double OffsetY)
{
    public static XsrUiSceneTransform Identity => new(1, 0, 0, 1, 0, 0);
    public XsrUiPoint Map(XsrUiPoint point) => new(point.X * M11 + point.Y * M21 + OffsetX,
        point.X * M12 + point.Y * M22 + OffsetY);
    public XsrUiPoint Unmap(XsrUiPoint point)
    {
        double determinant = M11 * M22 - M12 * M21;
        if (Math.Abs(determinant) < 1e-9) return new(double.NaN, double.NaN);
        double x = point.X - OffsetX, y = point.Y - OffsetY;
        return new((x * M22 - y * M21) / determinant, (y * M11 - x * M12) / determinant);
    }
    public XsrUiRect Bounds(XsrUiRect rect)
    {
        XsrUiPoint a = Map(new(rect.X, rect.Y)), b = Map(new(rect.X + rect.Width, rect.Y)),
            c = Map(new(rect.X, rect.Y + rect.Height)), d = Map(new(rect.X + rect.Width, rect.Y + rect.Height));
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        return new(left, top, Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - left,
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - top);
    }
    public XsrUiSceneTransform Then(XsrUiSceneTransform next) => new(
        M11 * next.M11 + M12 * next.M21, M11 * next.M12 + M12 * next.M22,
        M21 * next.M11 + M22 * next.M21, M21 * next.M12 + M22 * next.M22,
        OffsetX * next.M11 + OffsetY * next.M21 + next.OffsetX,
        OffsetX * next.M12 + OffsetY * next.M22 + next.OffsetY);
}
