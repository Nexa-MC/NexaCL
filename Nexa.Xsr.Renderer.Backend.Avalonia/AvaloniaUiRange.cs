using Avalonia;
using Avalonia.Automation.Provider;
using Avalonia.Media;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal sealed partial class AvaloniaUiSceneNodeControl
{
    private readonly Action<XsrUiEntityId, double>? _rangeFromAutomation;
    private void DrawRange(DrawingContext context, Rect rect, XsrUiVisualStyleSnapshot style, XsrUiRangeSnapshot range)
    {
        XsrUiRect rawTrack = range.Track(new(0, 0, rect.Width, rect.Height));
        XsrUiRect rawThumb = range.Thumb(new(0, 0, rect.Width, rect.Height));
        var track = new Rect(rawTrack.X, rawTrack.Y, rawTrack.Width, rawTrack.Height);
        var thumb = new Rect(rawThumb.X, rawThumb.Y, rawThumb.Width, rawThumb.Height);
        IBrush? rail = Brush(style.Background.Alpha > 0 ? style.Background : _node.ColorScheme.Foreground(new(180, 193, 208)));
        IBrush? accent = Brush(style.Foreground.Alpha > 0 ? style.Foreground : _node.ColorScheme.AccentFill);
        context.DrawRectangle(ThemeGradient(_node.BackgroundGradient) ?? rail, null, new RoundedRect(track, new CornerRadius(2)), ThemeShadow());
        DrawThemeBackgroundImage(context, track, style with { CornerRadius = 2 });
        context.DrawRectangle(accent, null, new RoundedRect(new Rect(track.X, track.Y, track.Width * range.Progress, track.Height), new CornerRadius(2)));
        context.DrawEllipse(accent, new Pen(Brush(style.Border.Alpha > 0 ? style.Border : _node.ColorScheme.AccentText), 1), thumb);
        if (_node.IsFocusVisible)
            context.DrawEllipse(null, new Pen(Brush(_node.ColorScheme.AccentText), 2), thumb.Inflate(3));
    }
    internal void SetRangeFromAutomation(double value)
    {
        if (!_node.IsEnabled) throw new InvalidOperationException("The range control is disabled.");
        if (!double.IsFinite(value) || _node.Range is not { } range || value < range.Minimum || value > range.Maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
        _rangeFromAutomation?.Invoke(_node.Entity, value);
    }
}

internal sealed class AvaloniaUiSceneRangeAutomationPeer(AvaloniaUiSceneNodeControl owner)
    : AvaloniaUiSceneNodeAutomationPeer(owner), IRangeValueProvider
{
    public bool IsReadOnly => !SceneOwner.Node.IsEnabled;
    public double Minimum => SceneOwner.Node.Range?.Minimum ?? 0;
    public double Maximum => SceneOwner.Node.Range?.Maximum ?? 1;
    public double Value => SceneOwner.Node.Range?.Value ?? 0;
    public double SmallChange => SceneOwner.Node.Range?.Step ?? 1;
    public double LargeChange => Math.Max(SmallChange, (Maximum - Minimum) / 10);
    public void SetValue(double value) => SceneOwner.SetRangeFromAutomation(value);
}
