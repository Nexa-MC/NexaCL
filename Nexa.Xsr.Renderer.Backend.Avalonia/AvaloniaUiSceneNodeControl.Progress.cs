using Avalonia;
using Avalonia.Media;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal sealed partial class AvaloniaUiSceneNodeControl
{
    private bool _progressRingAttached;
    private AvaloniaUiOutcomeAnimation? _outcomeAnimation;
    private AvaloniaUiProgressRingAnimation? _progressRingAnimation;

    private void UpdateProgressRing()
    {
        if (AvaloniaUiOutcomeAnimation.IsOutcome(_node.ImageSource))
        {
            _outcomeAnimation ??= new(InvalidateVisual);
            _outcomeAnimation.Update(_node.ImageSource,
                !_presentationReleased && _rasterPresentationEnabled && _progressRingAttached && RasterVisible(_node, _rasterViewport), _reducedMotion());
        }
        else _outcomeAnimation?.Update(null, false, _reducedMotion());
        if (_node.ProgressRing is null) { _progressRingAnimation?.Stop(); return; }
        _progressRingAnimation ??= new(() => _node.ProgressRing is { Indeterminate: true }
            && !_presentationReleased && _rasterPresentationEnabled && !_reducedMotion()
            && _progressRingAttached && RasterVisible(_node, _rasterViewport), InvalidateVisual);
        _progressRingAnimation.Update();
    }

    private void DrawProgressRing(DrawingContext context, Rect rect, XsrUiProgressRingSnapshot ring)
    {
        XsrUiColor color = _node.VisualStyle.Foreground.Alpha > 0
            ? _node.VisualStyle.Foreground : _node.ColorScheme.AccentText;
        AvaloniaUiProgressRingDrawing.Draw(context, rect, Brush(color) ?? Brushes.DodgerBlue, _progressRingAnimation?.Phase ?? .25,
            ring.Indeterminate, ring.Value);
    }
}
