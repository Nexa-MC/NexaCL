using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiSceneSurface
{

    private void ApplyScene(XsrUiScene scene)
    {
        foreach (AvaloniaUiSceneNodeControl control in _outgoingControls) Children.Remove(control);
        _currentEntities.Clear();
        _sceneNodes.Clear();
        foreach (var node in scene.Nodes) { _currentEntities.Add(node.Entity); _sceneNodes.Add(node.Entity, node); }
        bool geometryChanged = false;
        _retiredEntities.Clear();
        foreach (var entity in _controls.Keys)
            if (!_currentEntities.Contains(entity)) _retiredEntities.Add(entity);
        foreach (var entity in _retiredEntities)
        {
            var control = _controls[entity];
            Children.Remove(control);
            geometryChanged = true;
            control.ReleasePresentation();
            _controls.Remove(entity);
            _capsuleTargets.Remove(entity);
            _pagerRevisions.Remove(entity);
            _progressTargets.Remove(entity);
            _segmentTargets.Remove(entity);
            _segmentScrollTargets.Remove(entity);
            _scrollRevisions.Remove(entity);
            _shell.Renderer.FinishScrollInertia(entity);
            AvaloniaUiMotion.Cancel(this, ("scroll-inertia", entity));
            AvaloniaUiMotion.Cancel(this, ("segment-reveal", entity));
            AvaloniaUiMotion.Cancel(this, ("segment-scroll", entity));
            AvaloniaUiMotion.Cancel(this, ("capsule", entity));
            AvaloniaUiMotion.Cancel(this, ("progress", entity));
            AvaloniaUiMotion.Cancel(this, ("pager", entity));
            AvaloniaUiMotion.Cancel(this, ("slide-x", entity));
            AvaloniaUiMotion.Cancel(this, ("slide-y", entity));
        }

        _childIndexes.Clear();
        for (int index = 0; index < Children.Count; index++) _childIndexes[Children[index]] = index;
        for (int index = 0; index < scene.Count; index++)
        {
            XsrUiSceneNode node = scene[index];
            if (!_controls.TryGetValue(node.Entity, out AvaloniaUiSceneNodeControl? control))
            {
                control = new AvaloniaUiSceneNodeControl(
                    RouteAutomationFocus,
                    RouteAutomationInvoke,
                    () => _shell.Renderer.EffectiveReducedMotion,
                    new AvaloniaUiTextInputActions(
                        (entity, value) => { _shell.Renderer.SetTextInputValue(entity, value); CommitScene(); },
                        (entity, start, end) => { _shell.Renderer.SetTextSelection(entity, start, end); CommitScene(); },
                        (entity, value) => { _shell.Renderer.SetTextPreedit(entity, value); CommitScene(); }),
                    rangeFromAutomation: (entity, value) => { _shell.Renderer.RequestRangeValue(entity, value); CommitScene(); });
                _controls.Add(node.Entity, control);
                _childIndexes[control] = Children.Count;
                Children.Add(control);
                geometryChanged = true;
            }

            geometryChanged |= control.Node.Rect != node.Rect || control.Node.LayoutRect != node.LayoutRect;
            control.SetThemeResources(ResourcesFor(node));
            control.SetRasterPresentationEnabled(_rasterPresentationEnabled);
            control.Apply(node, new XsrUiSize(Bounds.Width, Bounds.Height));
            DriveSegmentReveal(node);
            DriveSegmentScroll(node);
            DriveScrollInertia(node);
            DriveCapsuleGeometry(node);
            DrivePagerGeometry(node);
            DriveProgressGeometry(node);
            int currentIndex = _childIndexes[control];
            if (currentIndex != index)
            {
                // Reordering must not detach live text inputs or cancel overlay springs.
                Children.Move(currentIndex, index);
                for (int moved = Math.Min(index, currentIndex); moved <= Math.Max(index, currentIndex); moved++)
                    _childIndexes[Children[moved]] = moved;
            }
        }

        ConfigureSelectionRelationships(scene);
        RunPageEnterAnimationsIfNavigated(scene);
        ApplyOutgoingLayers(scene);
        RetireUnusedThemeResources(scene);
        if (_pointerInside) UpdatePointerCursor(_lastPointerPoint);

        if (geometryChanged) { InvalidateMeasure(); InvalidateArrange(); }
        InvalidateVisual();
    }

    private void UpdatePointerCursor(XsrUiPoint point) =>
        SetPointerCursor(_shell.Renderer.PointerCursorAt(point));

    private void SetPointerCursor(XsrUiPointerCursor cursor)
    {
        if (_presentedPointerCursor == cursor) return;
        _presentedPointerCursor = cursor;
        Cursor = cursor switch
        {
            XsrUiPointerCursor.Hand => _handCursor,
            XsrUiPointerCursor.Text => _textCursor,
            _ => null,
        };
    }
    private void DriveScrollInertia(XsrUiSceneNode node)
    {
        if (node.ScrollMotion is not { } motion || node.Scroll is not { } scroll) return;
        if (_scrollRevisions.TryGetValue(node.Entity, out long revision) && revision == motion.Revision) return;
        _scrollRevisions[node.Entity] = motion.Revision;
        AvaloniaUiMotion.Cancel(this, ("scroll-inertia", node.Entity));
        if (motion.Dragging || Math.Abs(motion.Velocity) < 5 || _shell.Renderer.EffectiveReducedMotion) return;
        const double decay = 5;
        double duration = Math.Clamp(Math.Log(Math.Abs(motion.Velocity) / 5) / decay, .08, 1.4);
        double fraction = 1 - Math.Exp(-decay * duration);
        double offset = motion.Direction == XsrUiOrientation.Horizontal ? scroll.OffsetX : scroll.OffsetY;
        double target = offset + motion.Velocity / decay * fraction;
        AvaloniaUiMotion.Animate(this, ("scroll-inertia", node.Entity),
            () => _shell.Renderer.GetScrollPresentationOffset(node.Entity),
            value => _shell.Renderer.SetScrollPresentationOffset(node.Entity, value),
            target, duration * 1000, progress => (1 - Math.Exp(-decay * duration * progress)) / fraction,
            completed: () => _shell.Renderer.FinishScrollInertia(node.Entity),
            reducedMotion: () => _shell.Renderer.EffectiveReducedMotion);
    }
    private void DriveSegmentScroll(XsrUiSceneNode node)
    {
        if (node.SegmentScroll is not { } target)
        {
            if (_segmentScrollTargets.Remove(node.Entity)) AvaloniaUiMotion.Cancel(this, ("segment-scroll", node.Entity));
            return;
        }
        if (_segmentScrollTargets.TryGetValue(node.Entity, out var previous) && previous == target) return;
        _segmentScrollTargets[node.Entity] = target;
        AvaloniaUiMotion.AnimateSpring(this, ("segment-scroll", node.Entity),
            () => _shell.Renderer.GetSegmentScrollPresentationOffset(node.Entity),
            value => _shell.Renderer.SetSegmentScrollPresentationOffset(node.Entity, value, target.Revision),
            target.Target, AvaloniaMotionTokens.SegmentScrollSpringResponseSeconds,
            () => _shell.Renderer.EffectiveReducedMotion, readCurrentPosition: true);
    }
    private void DriveSegmentReveal(XsrUiSceneNode node)
    {
        if (node.SegmentReveal is not { } reveal) return;
        if (_segmentTargets.TryGetValue(node.Entity, out bool target) && target == reveal.Expanded) return;
        _segmentTargets[node.Entity] = reveal.Expanded;
        AvaloniaUiMotion.AnimateSpring(this, ("segment-reveal", node.Entity),
            () => _shell.Renderer.GetSegmentRevealProgress(node.Entity),
            value => _shell.Renderer.SetSegmentRevealProgress(node.Entity, value),
            reveal.Expanded ? 1 : 0, .25, () => _shell.Renderer.EffectiveReducedMotion);
    }

    private void SynchronizeNativeFocus(XsrUiScene scene)
    {
        XsrUiSceneNode focused = scene.Nodes.FirstOrDefault(node => node.IsFocused && node.IsFocusable);
        if (focused.Entity.IsAssigned && _controls.TryGetValue(focused.Entity, out AvaloniaUiSceneNodeControl? control)
            && !control.IsFocused)
            control.Focus(focused.IsFocusVisible ? NavigationMethod.Tab : NavigationMethod.Pointer);
        else if (!focused.Entity.IsAssigned && !IsFocused)
            Focus(NavigationMethod.Pointer);
    }

    private void DriveCapsuleGeometry(XsrUiSceneNode node)
    {
        if (!node.VisualStyle.HoverExpand) return;
        double target = node.IsEnabled && (node.IsHovered || node.IsFocusVisible) ? 1 : 0;
        if (_capsuleTargets.TryGetValue(node.Entity, out double previous) && previous == target) return;
        _capsuleTargets[node.Entity] = target;
        AvaloniaUiMotion.AnimateSpring(this, ("capsule", node.Entity),
            () => _scene is not null && TryGetNode(_scene, node.Entity, out XsrUiSceneNode current)
                ? current.CapsuleExpansionProgress : node.CapsuleExpansionProgress,
            progress => _shell.Renderer.SetCapsulePresentationProgress(node.Entity, progress),
            target, AvaloniaMotionTokens.CapsuleSpringResponseSeconds, () => _shell.Renderer.EffectiveReducedMotion);
    }

    private void DriveProgressGeometry(XsrUiSceneNode node)
    {
        if (node.Progress is not { } presented || node.ProgressRing is { Indeterminate: true }) return;
        double target = _shell.Renderer.GetProgressTarget(node.Entity);
        if (_progressTargets.TryGetValue(node.Entity, out double previous) && previous == target) return;
        _progressTargets[node.Entity] = target;
        if (_shell.Renderer.EffectiveReducedMotion)
        {
            _shell.Renderer.SetProgressPresentation(node.Entity, target);
            return;
        }

        AvaloniaUiMotion.Animate(this, ("progress", node.Entity),
            () => _scene is not null && TryGetNode(_scene, node.Entity, out XsrUiSceneNode current)
                ? current.Progress ?? 0 : presented,
            value => _shell.Renderer.SetProgressPresentation(node.Entity, value),
            target, AvaloniaMotionTokens.ProgressFillMilliseconds, AvaloniaUiMotion.EaseOut,
            reducedMotion: () => _shell.Renderer.EffectiveReducedMotion);
    }

    private void DrivePagerGeometry(XsrUiSceneNode node)
    {
        if (node.Pager is not { } pager) return;
        if (_pagerRevisions.TryGetValue(node.Entity, out long revision) && revision == pager.Revision) return;
        _pagerRevisions[node.Entity] = pager.Revision;
        if (pager.IsDragging || pager.Position == pager.PageIndex && pager.ReleaseVelocity == 0)
        {
            AvaloniaUiMotion.Cancel(this, ("pager", node.Entity));
            return;
        }
        AvaloniaUiMotion.AnimateSpring(this, ("pager", node.Entity), () => pager.Position,
            position => _shell.Renderer.SetPagerPresentationPosition(node.Entity, position),
            pager.PageIndex, AvaloniaMotionTokens.PagerSpringResponseSeconds,
            () => _shell.Renderer.EffectiveReducedMotion,
            pager.ReleaseVelocity == 0 ? null : pager.ReleaseVelocity);
    }

    /// <summary>
    /// Runs each declared spatial track independently. Undeclared scenes retain a simple
    /// compatibility fade; product pages with spatial controls never receive an extra group fade.
    /// </summary>
    private void RunPageEnterAnimationsIfNavigated(XsrUiScene scene)
    {
        XsrUiEntityId pageRoot = _shell.Stage.Navigation.Current;
        bool navigated = pageRoot != _lastPageRoot || _shell.SelectedNavigationId != _lastNavigation;
        _lastPageRoot = pageRoot;
        _lastNavigation = _shell.SelectedNavigationId;
        HashSet<XsrUiEntityId> animated = [], retained = [];
        var hostTransition = _shell.Tree.GetComponent<XsrUiTransition>(_shell.Stage.ContentHost);
        bool hasSpatialContent = hostTransition is { OffsetX: not 0 } or { OffsetY: not 0 }, inPage = false;
        int pageDepth = 0;
        foreach (XsrUiSceneNode candidate in scene.Nodes)
        {
            if (candidate.Entity == pageRoot) { inPage = true; pageDepth = candidate.Depth; }
            else if (inPage && candidate.Depth <= pageDepth) break;
            if (inPage && (candidate.TransitionOffsetX != 0 || candidate.TransitionOffsetY != 0))
            { hasSpatialContent = true; break; }
        }
        for (int index = 0; index < scene.Count; index++)
        {
            XsrUiSceneNode node = scene[index];
            bool changed = navigated && node.Entity == pageRoot && !hasSpatialContent;
            if (node.TransitionKey is { } key)
            {
                retained.Add(node.Entity);
                changed |= _transitionKeys.TryGetValue(node.Entity, out string? before) ? before != key
                    : node.TransitionPresentedOffsetX != 0 || node.TransitionPresentedOffsetY != 0;
                _transitionKeys[node.Entity] = key;
            }
            if (!changed) continue;
            if (node.TransitionOffsetX != 0 || node.TransitionOffsetY != 0)
            {
                XsrUiEntityId target = node.Entity;
                double delay = Math.Clamp(node.TransitionEntryOrder, 0, AvaloniaMotionTokens.PageEnterMaximumStaggerIndex)
                    * AvaloniaMotionTokens.PageEnterStaggerMilliseconds;
                AvaloniaUiMotion.AnimateSpring(this, ("slide-x", target),
                    () => _shell.Renderer.GetTransitionOffset(target),
                    value => _shell.Renderer.SetTransitionOffset(target, value),
                    0, AvaloniaMotionTokens.NavigationSpringResponseSeconds,
                    () => _shell.Renderer.EffectiveReducedMotion, readCurrentPosition: true, delayMilliseconds: delay);
                AvaloniaUiMotion.AnimateSpring(this, ("slide-y", target),
                    () => _shell.Renderer.GetTransitionOffsetY(target),
                    value => _shell.Renderer.SetTransitionOffsetY(target, value),
                    0, AvaloniaMotionTokens.ContentSpringResponseSeconds,
                    () => _shell.Renderer.EffectiveReducedMotion, readCurrentPosition: true, delayMilliseconds: delay);
                continue;
            }
            // A card and its descendants form one layer; chrome keeps its background in place.
            int first = node.Role == XsrUiSemanticRole.TitleBar ? index + 1 : index;
            for (int child = first; child < scene.Count && (child == index || scene[child].Depth > node.Depth); child++)
            {
                XsrUiEntityId entity = scene[child].Entity;
                if (!scene[child].SuppressEntryAnimation && animated.Add(entity) && _controls.TryGetValue(entity, out AvaloniaUiSceneNodeControl? control))
                    control.RunEnterAnimation();
            }
        }
        foreach (XsrUiEntityId entity in _transitionKeys.Keys.Where(entity => !retained.Contains(entity)).ToArray())
            _transitionKeys.Remove(entity);
    }

    /// <summary>
    /// Drives the shell's rail presentation progress on the shared motion clock. The geometry
    /// itself lives in UI.Next: every progress step re-commits the rail width there, which
    /// re-renders the scene, so the hit test and the drawn frame always share the presented
    /// geometry. Reduced motion never starts a track — the shell has already snapped the
    /// progress to its target.
    /// </summary>
    private void OnNavigationExpandedChanged(object? sender, EventArgs e)
    {
        if (_shell.Renderer.EffectiveReducedMotion)
        {
            // The shell already snapped the progress; make sure no older track can write over
            // the settled fact.
            AvaloniaUiMotion.Cancel(this, "rail-presentation");
            return;
        }

        AvaloniaUiMotion.AnimateSpring(
            this,
            "rail-presentation",
            () => _shell.RailPresentationProgress,
            value => _shell.SetRailPresentationProgress(value),
            _shell.IsNavigationExpanded ? 1 : 0,
            AvaloniaMotionTokens.RailSpringResponseSeconds,
            reducedMotion: () => _shell.Renderer.EffectiveReducedMotion);
    }

    private void ConfigureSelectionRelationships(XsrUiScene scene)
    {
        foreach (AvaloniaUiSceneNodeControl control in _controls.Values)
        {
            control.ResetSelectionRelationships();
        }

        Stack<XsrUiSceneNode> ancestors = [];
        for (int index = 0; index < scene.Count; index++)
        {
            XsrUiSceneNode node = scene[index];
            while (ancestors.Count > node.Depth)
            {
                _ = ancestors.Pop();
            }

            if (node.Role is XsrUiSemanticRole.NavigationItem or XsrUiSemanticRole.RadioButton
                && _controls.TryGetValue(node.Entity, out AvaloniaUiSceneNodeControl? item))
            {
                foreach (XsrUiSceneNode ancestor in ancestors)
                {
                    if (ancestor.Role != (node.Role == XsrUiSemanticRole.RadioButton ? XsrUiSemanticRole.RadioGroup : XsrUiSemanticRole.Navigation)
                        || !_controls.TryGetValue(ancestor.Entity, out AvaloniaUiSceneNodeControl? navigation))
                    {
                        continue;
                    }

                    item.SetSelectionContainer(navigation);
                    navigation.AddSelectionItem(item);
                    break;
                }
            }

            ancestors.Push(node);
        }
    }

    private void RouteAutomationFocus(XsrUiEntityId entity) => RouteAutomationAction(entity, activate: false);

    private void RouteAutomationInvoke(XsrUiEntityId entity) => RouteAutomationAction(entity, activate: true);

    private void RouteAutomationAction(XsrUiEntityId entity, bool activate)
    {
        lock (_commitGate)
        {
            if (_disposed)
            {
                return;
            }
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RouteAutomationAction(entity, activate));
            return;
        }

        _ = _shell.Renderer.Focus(entity);
        if (activate)
        {
            _ = _shell.Renderer.Activate(entity);
        }

        CommitScene();
    }

    internal bool IsNativeTitleBarPoint(XsrUiPoint point)
    {
        if (_scene is null) return false;
        XsrUiEntityId hit = _shell.Renderer.HitTest(point);
        int index = -1;
        for (int i = _scene.Count - 1; i >= 0; i--)
            if (_scene[i].Entity == hit) { index = i; break; }
        if (index < 0) return false;

        // Scene order is depth-first. Follow only the hit node's ancestors, so an
        // overlay cannot expose a caption underneath it and transformed/clipped
        // title children share the renderer's authoritative hit decision.
        int depth = int.MaxValue;
        for (; index >= 0; index--)
        {
            XsrUiSceneNode node = _scene[index];
            if (node.Depth >= depth) continue;
            depth = node.Depth;
            if (!node.IsAccessible || node.IsClickable || node.IsFocusable) return false;
            if (node.Role == XsrUiSemanticRole.TitleBar) return true;
        }
        return false;
    }

    private bool IsTitleBarPoint(XsrUiPoint point) => IsNativeTitleBarPoint(point);

    internal void RequestNativeTitleBarDrag(PointerPressedEventArgs args)
    {
        if (AvaloniaUiPointerPolicy.IsPrimaryPress(args, this))
            TitleBarDragRequested?.Invoke(this, args);
    }

    private bool TryGetNode(XsrUiScene scene, XsrUiEntityId entity, out XsrUiSceneNode result) =>
        _sceneNodes.TryGetValue(entity, out result);

    private void OnTreeRenderInvalidated(object? sender, EventArgs e) => RequestCommit();

    private void OnStateRenderRequested(object? sender, EventArgs e) => RequestCommit();
}
