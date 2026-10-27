using Avalonia;
using Avalonia.Media;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>One-shot companion to the loading arc. Reprojection never restarts a result.</summary>
internal sealed class AvaloniaUiOutcomeAnimation(Action invalidate)
{
    private string? _identity;
    internal double Progress { get; private set; } = 1;
    internal void Update(string? identity, bool visible, bool reducedMotion)
    {
        if (identity is null) { _identity = null; Stop(); return; }
        if (!visible) { Stop(); return; }
        if (reducedMotion) { _identity = identity; Stop(); return; }
        if (_identity == identity) return;
        _identity = identity;
        Progress = 0;
        AvaloniaUiMotion.Animate(this, "outcome", () => Progress,
            value => { Progress = value; invalidate(); }, 1, 620,
            easing: value => value);
    }
    internal void Stop() { AvaloniaUiMotion.CancelAll(this); Progress = 1; }
    internal static bool IsOutcome(string? source) => source is "lucide/circle-check" or "lucide/circle-x";
    internal static void Draw(DrawingContext context, Rect bounds, IBrush ink, bool success, double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        AvaloniaUiProgressRingDrawing.Draw(context, bounds, ink, 0, false, Math.Min(1, p / .55));
        double mark = Math.Clamp((p - .4) / .6, 0, 1);
        double size = Math.Min(bounds.Width, bounds.Height);
        var pen = new Pen(ink, Math.Clamp(size / 12, 2, 4), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Point At(double x, double y) => new(bounds.Center.X + x * size, bounds.Center.Y + y * size);
        void Segment(Point a, Point b, double amount)
        {
            if (amount > 0) context.DrawLine(pen, a, new(a.X + (b.X - a.X) * amount, a.Y + (b.Y - a.Y) * amount));
        }
        if (success)
        {
            Segment(At(-.22, 0), At(-.06, .16), Math.Min(1, mark * 2.5));
            Segment(At(-.06, .16), At(.23, -.19), Math.Clamp((mark - .4) / .6, 0, 1));
        }
        else
        {
            Segment(At(-.17, -.17), At(.17, .17), Math.Min(1, mark * 2));
            Segment(At(.17, -.17), At(-.17, .17), Math.Max(0, mark * 2 - 1));
        }
    }
}
