using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>One smooth rounded arc, shared by scene progress and the native startup surface.</summary>
internal static class AvaloniaUiProgressRingDrawing
{
    internal static (double Start, double Sweep) Sample(double phase, bool indeterminate, double value)
    {
        if (!indeterminate) return (-90, Math.Clamp(value, 0, 1) * 360);
        phase -= Math.Floor(phase);
        // The head leads while the tail catches up, without a discontinuity at the cycle seam.
        double wave = (1 - Math.Cos(phase * Math.PI * 2)) / 2;
        return (-90 + phase * 720 - wave * 140, 28 + wave * 280);
    }

    internal static void Draw(DrawingContext context, Rect bounds, IBrush ink, double phase,
        bool indeterminate, double value)
    {
        double diameter = Math.Min(bounds.Width, bounds.Height);
        if (diameter < 4) return;
        double stroke = Math.Clamp(diameter / 12, 2, 4);
        double radius = (diameter - stroke) / 2;
        Point center = bounds.Center;
        var pen = new Pen(ink, stroke, lineCap: PenLineCap.Round);
        if (!indeterminate && value >= 1)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        (double start, double sweep) = Sample(phase, indeterminate, value);
        if (sweep <= 0) return;
        Point At(double degrees) => new(center.X + radius * Math.Cos(degrees * Math.PI / 180),
            center.Y + radius * Math.Sin(degrees * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(start), false);
            path.ArcTo(At(start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>Paint-only loop on the shared motion clock; no per-indicator timer or scene commits.</summary>
internal sealed class AvaloniaUiProgressRingAnimation(Func<bool> enabled, Action invalidate)
{
    private bool _running;
    internal double Phase { get; private set; } = .25;

    internal void Update()
    {
        if (!enabled()) { Stop(); return; }
        if (_running) return;
        _running = true;
        Cycle();
    }

    private void Cycle()
    {
        if (!enabled()) { Stop(); return; }
        Phase = 0;
        AvaloniaUiMotion.Animate(this, "phase", () => Phase, value => { Phase = value; invalidate(); },
            1, 2000, easing: value => value,
            completed: () => { _running = false; Update(); }, reducedMotion: () => !enabled());
    }

    internal void Stop()
    {
        bool changed = _running || Phase != .25;
        AvaloniaUiMotion.CancelAll(this);
        _running = false;
        Phase = .25;
        // Startup applies its initial motion policy from Render. Invalidation must wait
        // until that composition pass finishes, and repeated stops must remain idle.
        if (changed) Dispatcher.UIThread.Post(invalidate);
    }
}

internal sealed class AvaloniaUiProgressRing : Control, IDisposable
{
    private readonly AvaloniaUiProgressRingAnimation _animation;
    private bool _disposed, _attached;
    internal IBrush Ink { get; set; } = Brushes.DodgerBlue;
    internal bool ReducedMotion { get; set; }

    internal AvaloniaUiProgressRing()
    {
        Width = Height = 40;
        IsHitTestVisible = false;
        _animation = new(() => !_disposed && !ReducedMotion && IsVisible && _attached,
            InvalidateVisual);
    }

    internal void UpdateMotion() => _animation?.Update();
    public override void Render(DrawingContext context) =>
        AvaloniaUiProgressRingDrawing.Draw(context, new Rect(Bounds.Size), Ink, _animation.Phase, true, 0);
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _attached = true; UpdateMotion(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _attached = false; _animation.Stop(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == IsVisibleProperty) UpdateMotion(); }
    public void Dispose() { _disposed = true; _animation.Stop(); }
}
