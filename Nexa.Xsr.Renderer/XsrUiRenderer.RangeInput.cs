using Nexa.Xsr;

namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    private XsrUiEntityId _rangeDrag;
    public double? GetRangeValue(XsrUiEntityId entity) => _tree.IsAlive(entity) ? _tree.GetComponent<XsrUiRangeInput>(entity)?.Value : null;
    public bool SetRangeValue(XsrUiEntityId entity, double value)
    {
        if (!_tree.IsAlive(entity) || _tree.GetComponent<XsrUiRangeInput>(entity) is not { } range || !double.IsFinite(value)) return false;
        if (range.SetValue(value)) _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
        return true;
    }
    public bool RequestRangeValue(XsrUiEntityId entity, double value)
    {
        if (!double.IsFinite(value) || !CanUseRange(entity) || _tree.GetComponent<XsrUiRangeInput>(entity) is not { } range) return false;
        if (range.SetValue(value)) EmitRangeChanged(entity);
        return true;
    }
    private bool CanUseRange(XsrUiEntityId entity) => _tree.IsAlive(entity) && IsInVisibleTree(entity)
        && IsEnabled(_tree.GetComponent<XsrUiInput>(entity)) && _tree.GetComponent<XsrUiRangeInput>(entity) is not null;
    private bool BeginRangeGesture(XsrUiEntityId entity, XsrUiPoint point)
    {
        if (!CanUseRange(entity)) return false;
        CancelPointerGesture(); _rangeDrag = entity;
        _ = Focus(entity, showIndicator: false);
        if (_tree.GetComponent<XsrUiInput>(entity) is { } input) input.IsPressed = true;
        UpdateRangeFromPoint(entity, point);
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
        return true;
    }
    private bool MoveRangeGesture(XsrUiPoint point)
    {
        if (!_rangeDrag.IsAssigned) return false;
        if (!CanUseRange(_rangeDrag)) { EndRangeGesture(null); return true; }
        UpdateRangeFromPoint(_rangeDrag, point); return true;
    }
    private bool EndRangeGesture(XsrUiPoint? point)
    {
        if (!_rangeDrag.IsAssigned) return false;
        XsrUiEntityId entity = _rangeDrag; _rangeDrag = default;
        if (_tree.IsAlive(entity))
        {
            if (point is { } final && CanUseRange(entity)) UpdateRangeFromPoint(entity, final);
            if (_tree.GetComponent<XsrUiInput>(entity) is { } input) input.IsPressed = false;
            _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
        }
        return true;
    }
    private void UpdateRangeFromPoint(XsrUiEntityId entity, XsrUiPoint point)
    {
        if (!_paintRects.TryGetValue(entity.Index, out XsrUiRect rect) || _tree.GetComponent<XsrUiRangeInput>(entity) is not { } range) return;
        point = ScenePointFor(entity, point);
        XsrUiRect track = range.Snapshot().Track(rect);
        if (track.Width <= 0) return;
        double progress = Math.Clamp((point.X - track.X) / track.Width, 0, 1);
        if (range.SetValue(range.Minimum + (range.Maximum - range.Minimum) * progress)) EmitRangeChanged(entity);
    }
    private bool MoveRangeForKey(XsrUiKey key)
    {
        if (!_focused.IsAssigned || !CanUseRange(_focused) || _tree.GetComponent<XsrUiRangeInput>(_focused) is not { } range) return false;
        double value;
        switch (key)
        {
            case XsrUiKey.Left:
            case XsrUiKey.Down:
                value = range.Minimum + Math.Floor((range.Value - range.Minimum) / range.Step - 1e-9) * range.Step; break;
            case XsrUiKey.Right:
            case XsrUiKey.Up:
                value = range.Minimum + Math.Ceiling((range.Value - range.Minimum) / range.Step + 1e-9) * range.Step; break;
            case XsrUiKey.Home: value = range.Minimum; break;
            case XsrUiKey.End: value = range.Maximum; break;
            default: return false;
        }
        if (range.SetValue(value)) EmitRangeChanged(_focused);
        return true;
    }
    private void EmitRangeChanged(XsrUiEntityId entity)
    {
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
        if (_sink is not null && _tree.GetComponent<XsrUiCommandBinding>(entity) is { Command.IsAssigned: true } binding)
            _sink.Emit(binding.Command, entity, XsrCorrelationId.Create());
    }
}
