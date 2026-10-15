using System.Globalization;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{

    /// <summary>
    /// Resolves the top-most scene entity at one point. Hit testing reads the last produced
    /// scene, so it never triggers layout or state reads.
    /// </summary>
    public XsrUiEntityId HitTest(XsrUiPoint point)
    {
        if (_scene is null)
        {
            return default;
        }

        for (int index = _scene.Count - 1; index >= 0; index--)
        {
            XsrUiSceneNode node = _scene[index];
            if (!_tree.IsAlive(node.Entity)) continue;
            // Transparent nonmodal composition layers do not become hit targets merely
            // because they span the viewport. Their real children remain reachable.
            bool passThrough = _tree.GetComponent<XsrUiOverlayLayer>(node.Entity) is { IsModal: false }
                && node.Role == XsrUiSemanticRole.None && !node.IsClickable && !node.IsFocusable
                && node.Text is null && node.ImageSource is null && node.VisualStyle.Background.Alpha == 0;
            if (!passThrough && node.IsAccessible && SceneNodeContains(node, point) && SceneNodeClipContains(node, point))
            {
                return node.Entity;
            }
        }

        return default;
    }

    private static bool SceneNodeContains(XsrUiSceneNode node, XsrUiPoint point) =>
        node.Transform is { } transform && node.LayoutRect is { } layout
            ? layout.Contains(transform.Unmap(point)) : node.Rect.Contains(point);

    private static bool SceneNodeClipContains(XsrUiSceneNode node, XsrUiPoint point)
    {
        if (node.ClipRect is { } bounds && !bounds.Contains(point)) return false;
        if (node.ClipGeometry is null) return true;
        for (int index = 0; index < node.ClipGeometry.Count; index++)
        {
            XsrUiSceneClip clip = node.ClipGeometry[index];
            if (!clip.Rect.Contains(clip.Transform?.Unmap(point) ?? point)) return false;
        }
        return true;
    }

    private XsrUiPoint ScenePointFor(XsrUiEntityId entity, XsrUiPoint point)
    {
        if (_scene is null) return point;
        for (int index = 0; index < _scene.Count; index++)
            if (_scene[index].Entity == entity) return _scene[index].Transform?.Unmap(point) ?? point;
        return point;
    }

    /// <summary>
    /// Resolves native pointer presentation through the same ancestor-aware input hit test used
    /// by press, release, and hover routing. A backend never needs to inspect live tree state.
    /// </summary>
    public XsrUiPointerCursor PointerCursorAt(XsrUiPoint point)
    {
        if (TryHitScrollbar(point, out _, out _)) return XsrUiPointerCursor.Default;
        XsrUiEntityId entity = StableHoverTarget(point, InputAt(point));
        if (!entity.IsAssigned || _tree.GetComponent<XsrUiInput>(entity) is not { } input
            || !IsEnabled(input))
        {
            return XsrUiPointerCursor.Default;
        }

        if (_tree.GetComponent<XsrUiTextInput>(entity) is not null)
        {
            return XsrUiPointerCursor.Text;
        }

        return input.Clickable ? XsrUiPointerCursor.Hand : XsrUiPointerCursor.Default;
    }

    /// <summary>
    /// Routes a pointer press. Returns true when a clickable entity absorbed it.
    /// </summary>
    public bool PointerPressed(XsrUiPoint point)
    {
        // Page replacement can destroy the focused entity between pointer events.
        // Clear the stale generation before either focusing or blurring a control.
        if (_focused.IsAssigned && !_tree.IsAlive(_focused))
        {
            _focused = default;
        }
        if (TryHitScrollbar(point, out XsrUiEntityId scrollEntity, out XsrUiScrollbarSnapshot scrollbar))
            return BeginScrollbarGesture(scrollEntity, scrollbar, point);
        XsrUiEntityId entity = InputAt(point);
        XsrUiInput? input = entity.IsAssigned ? _tree.GetComponent<XsrUiInput>(entity) : null;
        if (entity.IsAssigned && _tree.GetComponent<XsrUiTextInput>(entity) is not null && IsEnabled(input))
        {
            // Native text editing owns the entire gesture, including selection drags.
            // Arming an ancestor pager here would steal focus again on pointer release.
            return Focus(entity, showIndicator: false);
        }
        if (entity.IsAssigned && _tree.GetComponent<XsrUiRangeInput>(entity) is not null && IsEnabled(input))
            return BeginRangeGesture(entity, point);
        if (BeginSegmentDrag(point)) return true;
        if (entity.IsAssigned && IsEnabled(input) && BeginGraphGesture(entity, point)) return true;
        bool scrollGesture = BeginScrollGesture(point);
        bool pagerGesture = BeginPagerGesture(point);
        if (entity.IsAssigned && input is { Clickable: true } && IsEnabled(input))
        {
            _ = Focus(entity, showIndicator: false);
            input.IsPressed = true;
            _pressed = entity;
            _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
            return true;
        }

        if (_focused.IsAssigned && _tree.GetComponent<XsrUiTextInput>(_focused) is not null)
        {
            XsrUiInput? focusedInput = _tree.GetComponent<XsrUiInput>(_focused);
            if (focusedInput is not null) { focusedInput.IsFocused = false; focusedInput.IsFocusVisible = false; }
            _tree.MarkDirty(_focused, XsrUiDirtyKinds.Paint);
            _focused = default;
        }
        return pagerGesture || scrollGesture;
    }

    /// <summary>
    /// Routes a pointer release. Releasing over the pressed entity activates its command binding
    /// and emits one intent with a renderer-produced correlation ID.
    /// </summary>
    public bool PointerReleased(XsrUiPoint point) => PointerReleased(point, XsrUiClickModifiers.None);

    public bool PointerReleased(XsrUiPoint point, XsrUiClickModifiers modifiers)
    {
        if (EndRangeGesture(point)) return true;
        if (EndGraphGesture(point)) return true;
        if (EndSegmentDrag()) return true;
        if (EndScrollGesture(cancelled: false)) return true;
        if (EndPagerGesture()) return true;
        if (!_pressed.IsAssigned || !_tree.IsAlive(_pressed))
        {
            _pressed = default;
            return false;
        }

        XsrUiEntityId pressed = _pressed;
        XsrUiInput? input = _tree.GetComponent<XsrUiInput>(pressed);
        if (input is not null)
        {
            input.IsPressed = false;
        }
        _pressed = default;
        _tree.MarkDirty(pressed, XsrUiDirtyKinds.Paint);
        if (!InputAt(point).Equals(pressed)) return false;
        if (modifiers != XsrUiClickModifiers.None && _tree.GetComponent<XsrUiModifiedClick>(pressed) is { } modified
            && IsEnabled(input) && IsInVisibleTree(pressed))
        {
            XsrSemanticId command = modifiers.HasFlag(XsrUiClickModifiers.Extend)
                ? modifiers.HasFlag(XsrUiClickModifiers.Toggle) ? modified.AddRange : modified.Extend : modified.Toggle;
            if (_sink is null || !command.IsAssigned) return false;
            _sink.Emit(command, pressed, XsrCorrelationId.Create());
            return true;
        }
        return Activate(pressed);
    }

    // Both hover and cursor use the same target while capsule geometry moves underneath
    // a stationary pointer. Physical movement still resolves the current hit geometry.
    private XsrUiEntityId StableHoverTarget(XsrUiPoint point, XsrUiEntityId hit)
    {
        double dx = point.X - _hoverDecisionPoint.X, dy = point.Y - _hoverDecisionPoint.Y;
        return point.X >= 0 && point.Y >= 0 && hit != _hovered && _hovered.IsAssigned
            && _tree.IsAlive(_hovered) && IsInVisibleTree(_hovered)
            && _tree.GetComponent<XsrUiVisualStyle>(_hovered)?.HoverExpand == true
            && dx * dx + dy * dy <= 9 ? _hovered : hit;
    }

    /// <summary>
    /// Routes a pointer move, updating hover state on input entities. The return value reports
    /// whether presentation state changed, so a backend can commit a frame when the pointer
    /// leaves an input as well as when it enters one.
    /// </summary>
    public bool PointerMoved(XsrUiPoint point)
    {
        if (MoveRangeGesture(point)) return true;
        if (MoveGraphGesture(point)) return true;
        if (MoveSegmentDrag(point)) return true;
        if (MoveScrollGesture(point)) return true;
        if (MovePagerGesture(point)) { AbandonScrollGesture(); return true; }
        XsrUiEntityId entity = InputAt(point);
        XsrUiEntityId stable = StableHoverTarget(point, entity);
        if (stable == entity) _hoverDecisionPoint = point;
        entity = stable;
        XsrUiInput? input = entity.IsAssigned ? _tree.GetComponent<XsrUiInput>(entity) : null;
        bool overInput = input is not null && IsEnabled(input);
        bool changed = false;

        if (_hovered.IsAssigned && !_tree.IsAlive(_hovered))
        {
            _hovered = default;
        }

        if (_hovered.IsAssigned && !_hovered.Equals(entity))
        {
            XsrUiInput? previous = _tree.GetComponent<XsrUiInput>(_hovered);
            if (previous is { IsHovered: true })
            {
                previous.IsHovered = false;
                MarkInputDirty(_hovered);
                changed = true;
            }
        }

        _hovered = overInput ? entity : default;
        if (overInput && input is not null && !input.IsHovered)
        {
            input.IsHovered = true;
            MarkInputDirty(entity);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Moves keyboard focus to the next focusable entity in scene (tab) order, wrapping around.
    /// Returns false when no focusable entity exists.
    /// </summary>
    public bool FocusNext() => MoveFocus(1);

    public bool FocusPrevious() => MoveFocus(-1);

    private bool MoveFocus(int step)
    {
        if (_scene is null)
        {
            return false;
        }

        if (_focused.IsAssigned && _tree.IsAlive(_focused) && IsInVisibleTree(_focused))
        {
            for (XsrUiEntityId ancestor = _focused; ancestor.IsAssigned; ancestor = _tree.Parent(ancestor))
            {
                if (_tree.GetComponent<XsrUiFocusNavigation>(ancestor) is not { } navigation) continue;
                XsrUiEntityId target = navigation.Resolve(_focused, step > 0);
                if (target.IsAssigned && _tree.IsAlive(target) && Focus(target)) return true;
                break;
            }
        }

        List<XsrUiEntityId> focusable = [];
        for (int index = 0; index < _scene.Count; index++)
        {
            if (_scene[index].IsFocusable && _scene[index].IsEnabled)
            {
                focusable.Add(_scene[index].Entity);
            }
        }

        if (focusable.Count == 0)
        {
            return false;
        }

        if (_focused.IsAssigned && !_tree.IsAlive(_focused))
        {
            _focused = default;
        }

        int currentIndex = focusable.FindIndex(entity => entity.Equals(_focused));
        int next = (currentIndex + step + focusable.Count) % focusable.Count;
        return Focus(focusable[next]);
    }

    /// <summary>
    /// Sets keyboard focus to one focusable entity.
    /// </summary>
    public bool Focus(XsrUiEntityId entity, bool showIndicator = true)
    {
        if (!_tree.IsAlive(entity))
        {
            _focused = default;
            return false;
        }

        XsrUiInput? input = _tree.GetComponent<XsrUiInput>(entity);
        if (input is not { Focusable: true } || !IsEnabled(input) || !IsInVisibleTree(entity))
        {
            return false;
        }

        if (_focused.IsAssigned && !_focused.Equals(entity))
        {
            // The previously focused entity may have been destroyed by a rebuild between the
            // two interactions; unfocusing a dead handle must not throw.
            if (_tree.IsAlive(_focused)
                && _tree.GetComponent<XsrUiInput>(_focused) is { } previous)
            {
                previous.IsFocused = false;
                previous.IsFocusVisible = false;
                MarkInputDirty(_focused);
            }
        }

        _focused = entity;
        input.IsFocused = true;
        input.IsFocusVisible = showIndicator;
        MarkInputDirty(entity);
        return true;
    }

    /// <summary>
    /// Routes one wheel scroll to the nearest scroll container under the point, walking up the
    /// hierarchy. Offsets clamp during the next arrange.
    /// </summary>
    public bool PointerScroll(XsrUiPoint point, double deltaY, double deltaX = 0)
    {
        if (_scene is null || !double.IsFinite(deltaY) || !double.IsFinite(deltaX))
        {
            return false;
        }

        HashSet<int> visited = [];
        for (int index = _scene.Count - 1; index >= 0; index--)
        {
            XsrUiSceneNode node = _scene[index];
            if (!node.IsAccessible || !node.Rect.Contains(point)
                || node.ClipRect is { } clip && !clip.Contains(point))
            {
                continue;
            }

            XsrUiEntityId entity = node.Entity;
            while (entity.IsAssigned && _tree.IsAlive(entity))
            {
                if (!visited.Add(entity.Index))
                {
                    break;
                }

                // Paging is deliberately asymmetric: a vertical pager owns the conventional
                // wheel axis, while a horizontal pager keeps wheel and trackpad scrolling for
                // its surrounding content. Horizontal paging remains direct-manipulation only
                // (drag or Left/Right), so a catalog cannot change accidentally.
                if (_tree.GetComponent<XsrUiPager>(entity) is { Direction: XsrUiOrientation.Vertical }
                    && deltaY != 0)
                {
                    _ = MovePager(entity, Math.Sign(deltaY));
                    return true;
                }
                if (_tree.GetComponent<XsrUiScroll>(entity) is { } scroll && CanUseScroll(entity))
                {
                    StopScrollMotion(entity);
                    StopSegmentScroll(entity);
                    bool horizontalWheel = scroll.UseVerticalWheelForHorizontalScroll;
                    double horizontalDelta = horizontalWheel && deltaX == 0 ? deltaY : deltaX;
                    double targetX = Math.Clamp(scroll.OffsetX + horizontalDelta, 0, scroll.MaximumOffsetX);
                    double targetY = horizontalWheel ? scroll.OffsetY
                        : Math.Clamp(scroll.OffsetY + deltaY, 0, scroll.MaximumOffsetY);
                    if (Math.Abs(targetX - scroll.OffsetX) > .001
                        || Math.Abs(targetY - scroll.OffsetY) > .001)
                    {
                        scroll.OffsetX = targetX;
                        scroll.OffsetY = targetY;
                        _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout);
                        return true;
                    }
                }

                entity = _tree.Parent(entity);
            }
        }

        return false;
    }

    /// <summary>
    /// Routes one keyboard key: Tab moves focus, Enter and Space activate the focused entity,
    /// Back pops navigation through the intent sink as a plain false (navigation is owned by
    /// composition). Returns true when the key was handled.
    /// </summary>
    public bool HandleKey(XsrUiKey key)
    {
        if (MoveRangeForKey(key)) return true;
        if (MoveSegmentForKey(key)) return true;
        if (MoveCapsuleCaptionForKey(key)) return true;
        return key switch
        {
            XsrUiKey.Tab => FocusNext(),
            XsrUiKey.Enter or XsrUiKey.Space => _focused.IsAssigned && Activate(_focused),
            XsrUiKey.Up => MovePagerForDirection(XsrUiOrientation.Vertical, -1),
            XsrUiKey.Down => MovePagerForDirection(XsrUiOrientation.Vertical, 1),
            XsrUiKey.Left => MovePagerForDirection(XsrUiOrientation.Horizontal, -1),
            XsrUiKey.Right => MovePagerForDirection(XsrUiOrientation.Horizontal, 1),
            XsrUiKey.Escape => DismissActiveOverlay(),
            _ => false,
        };
    }

    private bool DismissActiveOverlay()
    {
        if (_scene is null || _sink is null)
        {
            return false;
        }

        for (int index = _scene.Count - 1; index >= 0; index--)
        {
            XsrUiSceneNode node = _scene[index];
            if (!node.IsAccessible || !_tree.IsAlive(node.Entity) || !IsInVisibleTree(node.Entity)) continue;
            XsrUiDismissBinding? binding = _tree.GetComponent<XsrUiDismissBinding>(node.Entity);
            if (binding is null || !binding.Command.IsAssigned)
            {
                continue;
            }

            _sink.Emit(binding.Command, node.Entity, XsrCorrelationId.Create());
            return true;
        }

        return false;
    }

    /// <summary>
    /// Activates one command-bound entity and emits a renderer-generated correlation ID. Native
    /// input, keyboard input, and accessibility providers all use this one path.
    /// </summary>
    public bool Activate(XsrUiEntityId entity)
    {
        if (!_tree.IsAlive(entity))
        {
            return false;
        }

        if (_tree.GetComponent<XsrUiInput>(entity) is { } input
            && !IsEnabled(input))
        {
            return false;
        }

        if (!IsInVisibleTree(entity))
        {
            return false;
        }

        XsrUiCommandBinding? binding = _tree.GetComponent<XsrUiCommandBinding>(entity);
        if (binding is null || _sink is null || !binding.Command.IsAssigned)
        {
            return false;
        }

        // Mouse, keyboard and accessibility activation share viewport revelation. A captured
        // thumb drag owns proportional scrolling and must not be re-scrolled for each intent.
        XsrUiEntityId parent = _tree.Parent(entity);
        if (parent.IsAssigned && parent != _segmentDrag && _tree.GetComponent<XsrUiSegmentedTrack>(parent) is not null)
            RevealSegment(parent, entity);

        _sink.Emit(binding.Command, entity, XsrCorrelationId.Create());
        return true;
    }

    private bool IsEnabled(XsrUiInput? input) => input is null || (input.Enabled
        && (!input.BoundEnabled.IsAssigned || _state.ReadAppliedValue(input.BoundEnabled) is true));

    private bool IsInVisibleTree(XsrUiEntityId entity)
    {
        while (entity.IsAssigned && _tree.IsAlive(entity))
        {
            if (!IsVisible(entity)) return false;
            if (entity == _root) return true;
            XsrUiEntityId parent = _tree.Parent(entity);
            if (parent.IsAssigned)
            {
                // A modal overlay is a structural input barrier as soon as it is staged. This
                // protects direct activation/focus and automation calls too, including the frame
                // before the newly attached overlay has entered the immutable scene.
                int visibleIndex = 0, entityIndex = -1, modalIndex = -1;
                for (int index = 0, count = _tree.ChildCount(parent); index < count; index++)
                {
                    XsrUiEntityId sibling = _tree.ChildAt(parent, index);
                    if (!IsVisible(sibling)) continue;
                    if (sibling == entity) entityIndex = visibleIndex;
                    if (_tree.GetComponent<XsrUiOverlayLayer>(sibling)?.IsModal == true) modalIndex = visibleIndex;
                    visibleIndex++;
                }
                if (_tree.GetComponent<XsrUiPager>(parent) is { } pager
                    && (entityIndex < 0 || entityIndex != pager.PageIndex)) return false;
                if (modalIndex >= 0 && entityIndex >= 0 && entityIndex < modalIndex)
                {
                    return false;
                }
            }
            entity = parent;
        }
        return false;
    }

    private XsrUiEntityId InputAt(XsrUiPoint point)
    {
        if (TryHitScrollbar(point, out _, out _)) return default;
        if (_scene is null)
        {
            return default;
        }

        _inputVisited.Clear();
        for (int index = _scene.Count - 1; index >= 0; index--)
        {
            XsrUiSceneNode node = _scene[index];
            if (!node.IsAccessible || !node.Rect.Contains(point)
                || node.ClipRect is { } clip && !clip.Contains(point))
            {
                continue;
            }

            XsrUiEntityId entity = node.Entity;
            while (entity.IsAssigned && _tree.IsAlive(entity))
            {
                if (!_inputVisited.Add(entity.Index))
                {
                    break;
                }

                if (_tree.GetComponent<XsrUiInput>(entity) is not null)
                {
                    return entity;
                }
                entity = _tree.Parent(entity);
            }
        }

        return default;
    }
}
