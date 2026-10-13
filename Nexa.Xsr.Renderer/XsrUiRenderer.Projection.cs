using System.Globalization;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    private XsrUiSceneNode[] CollectNodes(XsrUiEntityId root, int depth)
    {
        _outgoingLayers.Clear();
        _entryOrders.Clear();
        _collectedNodes.Clear();
        CollectNode(root, depth, _collectedNodes);
        var snapshot = _collectedNodes.ToArray();
        _collectedNodes.Clear(); // Release node references; retain only reusable capacity.
        return snapshot;
    }

    private void CollectNode(XsrUiEntityId entity, int depth, List<XsrUiSceneNode> nodes,
        XsrUiRect? clip = null, bool accessible = true, double offsetX = 0, double offsetY = 0,
        double opacity = 1, XsrUiOverlayMotionKind overlayMotion = XsrUiOverlayMotionKind.None,
        bool overlayClosing = false, XsrUiRect? overlayAnchor = null)
    {
        if (!IsVisible(entity))
        {
            return;
        }

        var components = _tree.ReadComponents(entity);
        XsrUiSemantic? semantic = components.Get<XsrUiSemantic>();
        XsrUiText? text = components.Get<XsrUiText>();
        string? content = text is null ? null : ResolveText(text);
        string? semanticLabel = ResolveSemanticLabel(semantic);

        XsrUiRect rect = _paintRects.TryGetValue(entity.Index, out XsrUiRect paintRect)
            ? paintRect
            : default;
        XsrUiAnimation? animation = components.Get<XsrUiAnimation>();
        XsrUiImage? image = components.Get<XsrUiImage>();
        XsrUiTransition? transition = components.Get<XsrUiTransition>();
        string? transitionKey = transition?.BoundKey.IsAssigned == true
            ? _state.ReadAppliedValue(transition.BoundKey) as string ?? string.Empty : transition?.Key;
        rect = rect with { X = rect.X + offsetX, Y = rect.Y + offsetY };
        if (transition is not null)
        {
            XsrUiEntityId parent = _tree.Parent(entity);
            XsrUiRect bounds = transition.MovesSelf && parent.IsAssigned && _paintRects.TryGetValue(parent.Index, out XsrUiRect parentBounds)
                ? parentBounds with { X = parentBounds.X + offsetX, Y = parentBounds.Y + offsetY } : rect;
            PrepareTransition(entity, transition, transitionKey, bounds, clip);
            if (transition.MovesSelf)
            {
                rect = rect with { X = rect.X + transition.PresentedOffsetX, Y = rect.Y + transition.PresentedOffsetY };
                clip = clip is { } outer ? Intersect(bounds, outer) : bounds;
            }
        }
        if (components.Get<XsrUiOverlayMotion>() is { } localOverlayMotion)
        {
            overlayMotion = localOverlayMotion.Kind;
            overlayClosing = localOverlayMotion.IsClosing;
            overlayAnchor = rect;
        }
        XsrUiVisualStyle? visualStyle = components.Get<XsrUiVisualStyle>();
        XsrUiSelection? selection = components.Get<XsrUiSelection>();
        XsrUiInput? input = components.Get<XsrUiInput>();
        bool enabled = accessible && IsEnabled(input) && (components.Get<XsrUiSegmentReveal>()?.Expanded ?? true);
        if (!enabled && input is not null)
        {
            input.IsHovered = false;
            input.IsPressed = false;
            input.IsFocused = false;
            input.IsFocusVisible = false;
            if (_pressed == entity) _pressed = default;
            if (_hovered == entity) _hovered = default;
            if (_focused == entity) _focused = default;
        }
        // A zero-width initial fill still needs to reach the backend to start its animation.
        XsrUiRect clipBounds = components.Get<XsrUiProgress>() is not null
            && _arrangedSlots.TryGetValue(entity.Index, out XsrUiRect progressSlot)
                ? progressSlot with { X = progressSlot.X + offsetX, Y = progressSlot.Y + offsetY }
                : rect;
        XsrUiRect? visibleClip = clip is { } parentClip ? Intersect(clipBounds, parentClip) : null;
        if (components.Get<XsrUiSegmentReveal>() is not null) visibleClip = Intersect(rect, visibleClip ?? rect);
        if (visibleClip is { Width: <= 0 } or { Height: <= 0 }) return;
        int entryOrder = -1;
        if (transition is { StaggerEntry: true } && transitionKey is not null && accessible)
        {
            entryOrder = _entryOrders.GetValueOrDefault(transitionKey);
            _entryOrders[transitionKey] = entryOrder + 1;
            double remaining = Math.Clamp(Math.Max(
                transition.StartOffsetX == 0 ? 0 : transition.PresentedOffsetX / transition.StartOffsetX,
                transition.StartOffsetY == 0 ? 0 : transition.PresentedOffsetY / transition.StartOffsetY), 0, 1);
            opacity *= 1 - remaining;
        }
        XsrUiScroll? scrollState = components.Get<XsrUiScroll>();
        XsrUiScrollSnapshot? scrollSnapshot = null;
        if (scrollState is not null)
        {
            scrollSnapshot = ProjectScroll(entity, scrollState, rect);
        }
        nodes.Add(new XsrUiSceneNode(
            entity,
            rect,
            depth,
            semantic?.Role ?? XsrUiSemanticRole.None,
            semanticLabel,
            content,
            image?.Source,
            enabled && (input?.IsFocused ?? false),
            animation?.Progress,
            animation is { Keyframes.Count: > 0 } ? animation.Value : null,
            ProjectStyle(visualStyle),
            selection?.IsSelected ?? false,
            enabled && (input?.Focusable ?? false),
            enabled && (input?.Clickable ?? false),
            enabled && (input?.IsHovered ?? false),
            enabled && (input?.IsPressed ?? false),
            enabled,
            visibleClip,
            enabled && (input?.IsFocusVisible ?? false),
            input?.CapsuleExpansionProgress ?? 0,
            components.Get<XsrUiPager>()?.Snapshot(),
            accessible,
            LocalizeInput(components.Get<XsrUiTextInput>(), semantic?.Localize ?? true),
            image?.Raster,
            transitionKey, transition?.OffsetX ?? 0, transition?.PresentedOffsetX ?? 0,
            transition?.OffsetY ?? 0, opacity, transition?.PresentedOffsetY ?? 0, entryOrder,
            components.Get<XsrUiProgress>()?.Presented,
            components.Get<XsrUiLiveRegion>()?.Setting ?? XsrUiLiveSetting.Off,
            overlayMotion,
            overlayClosing,
            overlayAnchor,
            text?.MaxLines ?? 0,
            text?.TrimOverflow ?? false,
            scrollSnapshot,
            components.Get<XsrUiSegmentReveal>() is { } reveal ? new(reveal.Expanded, reveal.Progress) : null,
            components.Get<XsrUiScrollGesture>() is { } motion ? new(motion.Revision, motion.Dragging, motion.Velocity) : null,
            IsStableContent(entity), ProjectTextRuns(text?.Runs))
        {
            ColorScheme = _colorScheme,
            VerticalScrollbar = scrollSnapshot?.Scrollbar(rect, XsrUiOrientation.Vertical),
            HorizontalScrollbar = scrollSnapshot?.Scrollbar(rect, XsrUiOrientation.Horizontal),
            Graph = components.Get<XsrUiGraph>()?.Snapshot(),
            ContextMenu = ProjectContextMenu(entity, components.Get<XsrUiContextMenu>()),
            IsChecked = components.Get<XsrUiToggle>()?.IsChecked,
            IsSelectionRequired = components.Get<XsrUiSelectionGroup>()?.IsSelectionRequired ?? true,
            SegmentScroll = components.Get<XsrUiSegmentedTrack>() is { Dragging: false, ScrollTargetX: { } targetX } segmentTrack
                ? new(segmentTrack.ScrollMotionRevision, targetX) : null,
        });

        if (components.Get<XsrUiSegmentedTrack>() is { } track
            && _tree.IsAlive(track.Thumb) && _paintRects.TryGetValue(track.Selected.Index, out XsrUiRect segment))
        {
            XsrUiRect target = segment with { X = segment.X + offsetX, Y = segment.Y + offsetY };
            XsrUiTransition thumbTransition = _tree.GetComponent<XsrUiTransition>(track.Thumb)!;
            bool moved = track.PresentedSelection.IsAssigned && track.PresentedSelection != track.Selected;
            if (moved && !track.Dragging)
            {
                thumbTransition.OffsetX = track.LastTarget.X - target.X;
                thumbTransition.PresentedOffsetX += thumbTransition.OffsetX;
                thumbTransition.Key = track.Selected.ToString();
            }
            track.PresentedSelection = track.Selected;
            track.LastTarget = target;
            if (!track.Dragging) PrepareTransition(track.Thumb, thumbTransition, thumbTransition.Key, rect, clip);
            thumbTransition.Outgoing = [];
            XsrUiRect presented = target with { X = track.Dragging ? track.DragX : target.X + thumbTransition.PresentedOffsetX };
            nodes.Add(new XsrUiSceneNode(track.Thumb, presented, depth + 1, XsrUiSemanticRole.None,
                null, null, null, false, null, null,
                ProjectStyle(_tree.GetComponent<XsrUiVisualStyle>(track.Thumb)),
                IsEnabled: false, ClipRect: Intersect(visibleClip ?? rect, presented), IsAccessible: false,
                TransitionKey: thumbTransition.Key, TransitionOffsetX: thumbTransition.OffsetX,
                TransitionPresentedOffsetX: thumbTransition.PresentedOffsetX)
            { ColorScheme = _colorScheme });
        }

        XsrUiPager? pageContainer = components.Get<XsrUiPager>();
        XsrUiRect? childClip = transition is { MovesSelf: false, OffsetX: not 0 } or { MovesSelf: false, OffsetY: not 0 }
            || pageContainer is not null || components.Get<XsrUiScroll>() is not null || components.Get<XsrUiSegmentReveal>() is not null
            || components.Get<XsrUiSegmentedTrack>() is not null
            ? visibleClip ?? rect
            : clip;
        if (scrollState is not null)
        {
            XsrUiRect scrollViewport = rect with
            {
                Width = Math.Max(0, rect.Width - VerticalIndicatorGutter(entity)),
                Height = Math.Max(0, rect.Height - HorizontalIndicatorGutter(entity)),
            };
            childClip = Intersect(scrollViewport, childClip ?? scrollViewport);
        }
        if (transition is { MovesSelf: false })
        {
            offsetX += transition.PresentedOffsetX;
            offsetY += transition.PresentedOffsetY;
        }

        XsrUiEntityId[] children = [.. _tree.Children(entity).Where(IsVisible)];
        int modalIndex = -1;
        for (int index = 0; index < children.Length; index++)
        {
            if (_tree.GetComponent<XsrUiOverlayLayer>(children[index])?.IsModal == true)
            {
                modalIndex = index;
            }
        }

        int pageIndex = 0;
        for (int index = 0; index < children.Length; index++)
        {
            XsrUiEntityId child = children[index];
            CollectNode(child, depth + 1, nodes, childClip,
                accessible
                && (pageContainer is null || pageContainer.PageIndex == pageIndex)
                && (modalIndex < 0 || index >= modalIndex),
                offsetX, offsetY, opacity, overlayMotion, overlayClosing, overlayAnchor);
            pageIndex++;
        }
    }

    private bool IsStableContent(XsrUiEntityId entity)
    {
        while (entity.IsAssigned)
        {
            if (_tree.GetComponent<XsrUiStableContent>(entity) is not null) return true;
            entity = _tree.Parent(entity);
        }
        return false;
    }

    private static XsrUiRect Intersect(XsrUiRect a, XsrUiRect b)
    {
        double x = Math.Max(a.X, b.X);
        double y = Math.Max(a.Y, b.Y);
        return new XsrUiRect(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x),
            Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    private string ResolveText(XsrUiText text)
    {
        XsrStateId bound = text.BoundState;
        if (!bound.IsAssigned)
        {
            return text.Localize && text.Runs.Count == 0 ? LocalizeText(text.Content) : text.Content;
        }

        object? value = _state.ReadAppliedValue(bound);
        string content = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Localize && text.Runs.Count == 0 ? LocalizeText(content) : content;
    }

    private string? ResolveSemanticLabel(XsrUiSemantic? semantic)
    {
        if (semantic is null || !semantic.BoundLabel.IsAssigned)
        {
            return semantic is { Localize: true, Label: { } label } ? LocalizeText(label) : semantic?.Label;
        }

        object? value = _state.ReadAppliedValue(semantic.BoundLabel);
        string boundLabel = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return semantic.Localize ? LocalizeText(boundLabel) : boundLabel;
    }

    private XsrUiTextInputSnapshot? LocalizeInput(XsrUiTextInput? input, bool localize)
    {
        if (input is null) return null;
        var snapshot = input.Snapshot();
        return localize ? snapshot with { Placeholder = LocalizeText(snapshot.Placeholder) } : snapshot;
    }

    // UI.Next uses a deliberately deterministic, backend-neutral text metric. Native backends
    // select their own typeface at commit time, but intrinsic text sizing must be available before
    // any backend exists so PXML layout (including title and command text) has real hit geometry.
    private static XsrUiSize MeasureText(
        string text,
        double fontSize,
        double maximumWidth,
        int maximumLines)
    {
        double scale = fontSize > 0 ? fontSize / 14 : 1;
        double lineHeight = Math.Ceiling(20 * Math.Max(1, scale));
        double currentWidth = 0;
        double measuredWidth = 0;
        int lines = 1;
        bool wraps = double.IsFinite(maximumWidth) && maximumWidth > 0;
        foreach (char character in text)
        {
            if (character == '\r')
            {
                continue;
            }
            if (character == '\n')
            {
                measuredWidth = Math.Max(measuredWidth, currentWidth);
                currentWidth = 0;
                lines++;
                continue;
            }

            double characterWidth = 0;
            if (character == '\t')
            {
                characterWidth = 28 * scale;
            }
            else if (!char.IsControl(character))
            {
                characterWidth = (character <= 0x7f ? 7 : 14) * scale;
            }

            if (wraps && currentWidth > 0 && currentWidth + characterWidth > maximumWidth)
            {
                measuredWidth = Math.Max(measuredWidth, currentWidth);
                currentWidth = characterWidth;
                lines++;
            }
            else
            {
                currentWidth += characterWidth;
            }
        }

        measuredWidth = Math.Max(measuredWidth, currentWidth);
        if (wraps)
        {
            measuredWidth = Math.Min(measuredWidth, maximumWidth);
        }
        if (maximumLines > 0)
        {
            lines = Math.Min(lines, maximumLines);
        }

        return new XsrUiSize(measuredWidth, Math.Max(1, lines) * lineHeight);
    }
}
