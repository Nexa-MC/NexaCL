namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    private readonly TimeProvider _gestureTime;
    private XsrUiEntityId _gestureScroll;
    private XsrUiPoint _scrollGrab;
    private double _scrollGrabOffset, _scrollLastOffset;
    private long _scrollLastTime;
    private bool _scrollCommitted;
    private ScrollbarGesture _scrollbarGesture;
    private XsrUiOrientation _scrollbarDirection;
    private readonly Queue<(long Time, double Offset)> _scrollSamples = new();
    private readonly List<(XsrUiEntityId Entity, XsrUiPoint Grab, double OffsetX, double OffsetY)> _scrollCandidates = [];

    private enum ScrollbarGesture { None, Thumb, Track }

    /// <summary>True while a scrollbar owns the captured press, excluding native content drags.</summary>
    public bool IsScrollbarGestureActive => _scrollbarGesture != ScrollbarGesture.None;

    private bool CanUseScroll(XsrUiEntityId entity)
    {
        if (!_tree.IsAlive(entity) || !IsInVisibleTree(entity)) return false;
        for (XsrUiEntityId ancestor = entity; ancestor.IsAssigned; ancestor = _tree.Parent(ancestor))
            if (!IsEnabled(_tree.GetComponent<XsrUiInput>(ancestor))
                || _tree.GetComponent<XsrUiSegmentReveal>(ancestor) is { Expanded: false }) return false;
        // A modal attached inside the scrolling container also blocks the container's own bar.
        for (int index = 0; index < _tree.ChildCount(entity); index++)
        {
            XsrUiEntityId child = _tree.ChildAt(entity, index);
            if (IsVisible(child) && _tree.GetComponent<XsrUiOverlayLayer>(child)?.IsModal == true) return false;
        }
        return true;
    }

    private bool TryHitScrollbar(XsrUiPoint point, out XsrUiEntityId entity, out XsrUiScrollbarSnapshot scrollbar)
    {
        entity = default; scrollbar = default;
        if (_scene is null) return false;
        for (int index = _scene.Count - 1; index >= 0; index--)
        {
            XsrUiSceneNode node = _scene[index];
            if (!node.IsAccessible || !SceneNodeContains(node, point) || !SceneNodeClipContains(node, point)) continue;
            XsrUiPoint logicalPoint = node.Transform?.Unmap(point) ?? point;
            // Content is clipped to the viewport excluding its own gutters. The first painted
            // entity under this point therefore either owns the bar or occludes bars behind it.
            XsrUiScrollbarSnapshot? hit = node.VerticalScrollbar is { } vertical && vertical.HitRect.Contains(logicalPoint)
                ? vertical : node.HorizontalScrollbar is { } horizontal && horizontal.HitRect.Contains(logicalPoint) ? horizontal : null;
            if (hit is null) return false;
            entity = node.Entity; scrollbar = hit.Value;
            return true;
        }
        return false;
    }

    private bool BeginScrollbarGesture(XsrUiEntityId entity, XsrUiScrollbarSnapshot scrollbar, XsrUiPoint point)
    {
        if (scrollbar.Travel <= 0 || !CanUseScroll(entity)
            || !TryGetSceneScrollbar(entity, scrollbar.Direction, out _)
            || _tree.GetComponent<XsrUiScroll>(entity) is not { } scroll
            || !(scrollbar.Direction == XsrUiOrientation.Vertical ? scroll.ShowsVerticalIndicator : scroll.ShowsHorizontalIndicator)) return false;
        point = ScenePointFor(entity, point);
        CancelPointerGesture();
        StopScrollMotion(entity); StopSegmentScroll(entity);
        _gestureScroll = entity; _scrollGrab = point; _scrollbarDirection = scrollbar.Direction;
        _scrollCommitted = true;
        _scrollbarGesture = scrollbar.HitsThumb(point) ? ScrollbarGesture.Thumb : ScrollbarGesture.Track;
        if (_tree.GetComponent<XsrUiScrollGesture>(entity) is { } motion)
        {
            motion.Dragging = true;
            motion.Direction = scrollbar.Direction;
        }
        if (_scrollbarGesture == ScrollbarGesture.Track)
        {
            bool vertical = _scrollbarDirection == XsrUiOrientation.Vertical;
            double coordinate = vertical ? point.Y : point.X;
            double thumbStart = vertical ? scrollbar.Thumb.Y : scrollbar.Thumb.X;
            double viewport = TryGetScrollSnapshot(entity, out var snapshot)
                ? vertical ? snapshot.ViewportHeight : snapshot.ViewportWidth : 0;
            double offset = vertical ? scroll.OffsetY : scroll.OffsetX;
            double maximum = vertical ? scroll.MaximumOffsetY : scroll.MaximumOffsetX;
            SetScrollbarOffset(scroll, Math.Clamp(offset + (coordinate < thumbStart ? -viewport : viewport), 0, maximum));
        }
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout);
        return true;
    }

    private void SetScrollbarOffset(XsrUiScroll scroll, double offset)
    {
        if (_scrollbarDirection == XsrUiOrientation.Vertical) scroll.OffsetY = offset;
        else scroll.OffsetX = offset;
    }

    private bool MoveScrollbarGesture(XsrUiPoint point)
    {
        if (!CanUseScroll(_gestureScroll) || _tree.GetComponent<XsrUiScroll>(_gestureScroll) is not { } scroll
            || !(_scrollbarDirection == XsrUiOrientation.Vertical ? scroll.ShowsVerticalIndicator : scroll.ShowsHorizontalIndicator)
            || !TryGetSceneScrollbar(_gestureScroll, _scrollbarDirection, out XsrUiScrollbarSnapshot bar)
            || bar.Travel <= 0)
        {
            EndScrollGesture(cancelled: true); return true;
        }
        if (_scrollbarGesture == ScrollbarGesture.Track) return true;
        point = ScenePointFor(_gestureScroll, point);
        bool vertical = _scrollbarDirection == XsrUiOrientation.Vertical;
        double delta = vertical ? point.Y - _scrollGrab.Y : point.X - _scrollGrab.X;
        _scrollGrab = point;
        double maximum = vertical ? scroll.MaximumOffsetY : scroll.MaximumOffsetX;
        double offset = vertical ? scroll.OffsetY : scroll.OffsetX;
        SetScrollbarOffset(scroll, Math.Clamp(offset + delta * maximum / bar.Travel, 0, maximum));
        _tree.MarkDirty(_gestureScroll, XsrUiDirtyKinds.Layout);
        return true;
    }

    private bool TryGetSceneScrollbar(XsrUiEntityId entity, XsrUiOrientation direction, out XsrUiScrollbarSnapshot scrollbar)
    {
        scrollbar = default;
        if (_scene is null) return false;
        for (int index = 0; index < _scene.Count; index++)
        {
            XsrUiSceneNode node = _scene[index];
            if (node.Entity != entity) continue;
            XsrUiScrollbarSnapshot? bar = direction == XsrUiOrientation.Vertical ? node.VerticalScrollbar : node.HorizontalScrollbar;
            if (!node.IsAccessible || !node.IsEnabled || bar is null) return false;
            scrollbar = bar.Value; return true;
        }
        return false;
    }

    private XsrUiScrollSnapshot ProjectScroll(XsrUiEntityId entity, XsrUiScroll scroll, XsrUiRect rect)
    {
        if (IsScrollableCapsule(entity)) rect = CapsuleCaptionRect(rect);
        XsrUiSize content = _stackContentSizes.TryGetValue(entity.Index, out XsrUiSize value)
            ? value : new XsrUiSize(rect.Width, rect.Height);
        return new(scroll.OffsetX, scroll.OffsetY,
            Math.Max(0, rect.Width - VerticalIndicatorGutter(entity)),
            Math.Max(0, rect.Height - HorizontalIndicatorGutter(entity)),
            content.Width, content.Height, scroll.ShowsVerticalIndicator)
        { ShowsHorizontalIndicator = scroll.ShowsHorizontalIndicator };
    }

    private XsrUiScrollbarSnapshot? ProjectScrollbar(XsrUiEntityId entity, XsrUiScrollSnapshot? scroll,
        XsrUiRect rect, XsrUiOrientation direction) =>
        scroll?.Scrollbar(IsScrollableCapsule(entity) ? CapsuleCaptionRect(rect) : rect, direction);

    private void RetireScrollGesture()
    {
        if (!_gestureScroll.IsAssigned) return;
        if (!CanUseScroll(_gestureScroll) || _tree.GetComponent<XsrUiScroll>(_gestureScroll) is not { } scroll
            || (_scrollbarGesture == ScrollbarGesture.None
                && (_tree.GetComponent<XsrUiScrollGesture>(_gestureScroll) is not { } motion
                    || _scrollCommitted && (motion.Direction == XsrUiOrientation.Horizontal ? scroll.MaximumOffsetX : scroll.MaximumOffsetY) <= 0))
            || (_scrollbarGesture != ScrollbarGesture.None
                && (_scrollbarDirection == XsrUiOrientation.Vertical
                    ? !scroll.ShowsVerticalIndicator || scroll.MaximumOffsetY <= 0
                    : !scroll.ShowsHorizontalIndicator || scroll.MaximumOffsetX <= 0))
            || (_scrollbarGesture != ScrollbarGesture.None
                && (!_paintRects.TryGetValue(_gestureScroll.Index, out XsrUiRect rect)
                    || ProjectScrollbar(_gestureScroll, ProjectScroll(_gestureScroll, scroll, rect), rect, _scrollbarDirection) is not { Travel: > 0 })))
            EndScrollGesture(cancelled: true);
    }

    public double GetScrollPresentationOffset(XsrUiEntityId entity) =>
        _tree.GetComponent<XsrUiScroll>(entity) is { } scroll
            ? _tree.GetComponent<XsrUiScrollGesture>(entity)?.Direction == XsrUiOrientation.Horizontal ? scroll.OffsetX : scroll.OffsetY
            : 0;
    public void SetScrollPresentationOffset(XsrUiEntityId entity, double offset)
    {
        if (!double.IsFinite(offset) || !CanUseScroll(entity)
            || _tree.GetComponent<XsrUiScrollGesture>(entity) is not { Dragging: false, Velocity: not 0 } motion
            || _tree.GetComponent<XsrUiScroll>(entity) is not { } scroll || EffectiveReducedMotion) return;
        if (motion.Direction == XsrUiOrientation.Horizontal) scroll.OffsetX = Math.Clamp(offset, 0, scroll.MaximumOffsetX);
        else scroll.OffsetY = Math.Clamp(offset, 0, scroll.MaximumOffsetY);
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout);
    }
    /// <summary>Ends released motion when its clock finishes or the viewport leaves the scene.</summary>
    public void FinishScrollInertia(XsrUiEntityId entity)
    {
        if (_tree.IsAlive(entity) && _tree.GetComponent<XsrUiScrollGesture>(entity) is { Dragging: false, Velocity: not 0 })
            StopScrollMotion(entity);
    }
    private void StopScrollMotion(XsrUiEntityId entity)
    {
        if (_tree.GetComponent<XsrUiScrollGesture>(entity) is not { } motion) return;
        motion.Velocity = 0; motion.Revision++;
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }
    private bool BeginScrollGesture(XsrUiPoint point)
    {
        _scrollCandidates.Clear();
        XsrUiEntityId entity = HitTest(point);
        while (entity.IsAssigned && _tree.IsAlive(entity))
        {
            if (_tree.GetComponent<XsrUiScrollGesture>(entity) is not null
                && _tree.GetComponent<XsrUiScroll>(entity) is { } scroll
                && (scroll.MaximumOffsetX > 0 || scroll.MaximumOffsetY > 0) && CanUseScroll(entity))
            {
                StopScrollMotion(entity);
                _scrollCandidates.Add((entity, ScenePointFor(entity, point), scroll.OffsetX, scroll.OffsetY));
            }
            entity = _tree.Parent(entity);
        }
        if (_scrollCandidates.Count == 0) return false;
        _gestureScroll = _scrollCandidates[0].Entity;
        _scrollLastTime = _gestureTime.GetTimestamp();
        _scrollCommitted = false;
        _tree.GetComponent<XsrUiScrollGesture>(_gestureScroll)!.Dragging = true;
        return true;
    }
    private bool CommitScrollDirection(XsrUiPoint point)
    {
        foreach (var candidate in _scrollCandidates)
        {
            if (!CanUseScroll(candidate.Entity)
                || _tree.GetComponent<XsrUiScrollGesture>(candidate.Entity) is not { } motion
                || _tree.GetComponent<XsrUiScroll>(candidate.Entity) is not { } scroll) continue;
            XsrUiPoint local = ScenePointFor(candidate.Entity, point);
            double x = Math.Abs(local.X - candidate.Grab.X), y = Math.Abs(local.Y - candidate.Grab.Y);
            if (Math.Max(x, y) < 8 || x == y) continue;
            XsrUiOrientation direction = x > y ? XsrUiOrientation.Horizontal : XsrUiOrientation.Vertical;
            if ((direction == XsrUiOrientation.Horizontal ? scroll.MaximumOffsetX : scroll.MaximumOffsetY) <= 0) continue;
            if (_gestureScroll != candidate.Entity && _tree.GetComponent<XsrUiScrollGesture>(_gestureScroll) is { } previous)
            {
                previous.Dragging = false;
                previous.Revision++;
                _tree.MarkDirty(_gestureScroll, XsrUiDirtyKinds.Paint);
            }
            _gestureScroll = candidate.Entity;
            _scrollGrab = candidate.Grab;
            _scrollGrabOffset = _scrollLastOffset = direction == XsrUiOrientation.Horizontal ? candidate.OffsetX : candidate.OffsetY;
            _scrollSamples.Clear();
            _scrollSamples.Enqueue((_scrollLastTime, _scrollGrabOffset));
            motion.Direction = direction;
            motion.Dragging = true;
            motion.Revision++;
            StopSegmentScroll(_gestureScroll);
            _scrollCommitted = true;
            ClearPointerPress();
            // The nearest scrolling content owns its axis before a containing pager.
            if (_gesturePager.IsAssigned && _tree.GetComponent<XsrUiPager>(_gesturePager) is { } pager)
            {
                pager.IsDragging = false; pager.ReleaseVelocity = 0;
                SetPagerTarget(_gesturePager, pager, pager.PageIndex);
            }
            _gesturePager = default; _pagerDragCommitted = false;
            return true;
        }
        return false;
    }
    private bool MoveScrollGesture(XsrUiPoint point)
    {
        if (!_gestureScroll.IsAssigned) return false;
        if (_scrollbarGesture != ScrollbarGesture.None) return MoveScrollbarGesture(point);
        if (!CanUseScroll(_gestureScroll) || _tree.GetComponent<XsrUiScroll>(_gestureScroll) is null
            || _tree.GetComponent<XsrUiScrollGesture>(_gestureScroll) is null) { AbandonScrollGesture(); return false; }
        if (!_scrollCommitted && !CommitScrollDirection(point)) return false;
        point = ScenePointFor(_gestureScroll, point);
        XsrUiScroll scroll = _tree.GetComponent<XsrUiScroll>(_gestureScroll)!;
        XsrUiScrollGesture motion = _tree.GetComponent<XsrUiScrollGesture>(_gestureScroll)!;
        bool horizontal = motion.Direction == XsrUiOrientation.Horizontal;
        double delta = horizontal ? _scrollGrab.X - point.X : _scrollGrab.Y - point.Y;
        double offset = Math.Clamp(_scrollGrabOffset + delta, 0, horizontal ? scroll.MaximumOffsetX : scroll.MaximumOffsetY);
        if (horizontal) scroll.OffsetX = offset;
        else scroll.OffsetY = offset;
        long now = _gestureTime.GetTimestamp();
        if ((offset - _scrollLastOffset) * motion.Velocity < 0)
        {
            _scrollSamples.Clear(); _scrollSamples.Enqueue((_scrollLastTime, _scrollLastOffset));
        }
        while (_scrollSamples.Count > 1 && _gestureTime.GetElapsedTime(_scrollSamples.Peek().Time, now).TotalMilliseconds > 80) _scrollSamples.Dequeue();
        var first = _scrollSamples.Peek();
        double seconds = _gestureTime.GetElapsedTime(first.Time, now).TotalSeconds;
        if (seconds > .001) motion.Velocity = Math.Clamp((offset - first.Offset) / seconds, -5000, 5000);
        _scrollSamples.Enqueue((now, offset));
        _scrollLastTime = now; _scrollLastOffset = offset;
        _tree.MarkDirty(_gestureScroll, XsrUiDirtyKinds.Layout);
        return true;
    }
    private bool EndScrollGesture(bool cancelled)
    {
        XsrUiEntityId entity = _gestureScroll; _gestureScroll = default;
        _scrollCandidates.Clear();
        bool scrollbar = _scrollbarGesture != ScrollbarGesture.None; _scrollbarGesture = ScrollbarGesture.None;
        bool committed = _scrollCommitted; _scrollCommitted = false;
        if (!entity.IsAssigned) return false;
        if (_tree.IsAlive(entity) && _tree.GetComponent<XsrUiScrollGesture>(entity) is { } motion)
        {
            motion.Dragging = false;
            if (cancelled || scrollbar || !committed || EffectiveReducedMotion || _gestureTime.GetElapsedTime(_scrollLastTime).TotalMilliseconds > 120)
                motion.Velocity = 0;
            motion.Revision++;
        }
        if (committed) ClearPointerPress();
        if (_tree.IsAlive(entity)) _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
        return committed;
    }
    private void AbandonScrollGesture() => EndScrollGesture(cancelled: true);
}
