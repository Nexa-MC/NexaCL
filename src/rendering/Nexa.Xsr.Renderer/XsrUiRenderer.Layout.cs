using System.Globalization;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{

    private void Layout(XsrUiEntityId entity, XsrUiRect slot)
    {
        if (_tree.GetComponent<XsrUiAnchoredOverlay>(entity) is { } anchor && _tree.IsAlive(anchor.Anchor)
            && _paintRects.TryGetValue(anchor.Anchor.Index, out XsrUiRect bounds))
        {
            double top = bounds.Y + bounds.Height + anchor.Gap;
            slot = new(bounds.X, top, bounds.Width, Math.Max(0, slot.Y + slot.Height - top));
        }
        // Measure caching and arrange caching are separate concerns: a clean subtree keeps its
        // measured sizes, but any entity whose input slot moved must re-arrange, even when the
        // entity itself is clean — otherwise siblings keep stale coordinates.
        bool subtreeClean = !_tree.HasDirtyLayoutSubtree(entity);
        bool slotUnchanged = _arrangedSlots.TryGetValue(entity.Index, out XsrUiRect previous) && previous == slot;
        bool arranged = _paintRects.ContainsKey(entity.Index);
        if (subtreeClean && slotUnchanged && arranged && _tree.GetComponent<XsrUiOverlayLayer>(entity) is null)
        {
            return;
        }

        XsrUiSize desired = Measure(entity, new XsrUiSize(slot.Width, slot.Height));
        Arrange(entity, slot, desired);
    }

    private XsrUiSize Measure(XsrUiEntityId entity, XsrUiSize available)
    {
        // Subtrees without layout-relevant dirt keep the previous pass's desired size; anything
        // with a dirty descendant re-aggregates so containers pick up new child measurements.
        if (!_tree.HasDirtyLayoutSubtree(entity) && _desiredSizes.TryGetValue(entity.Index, out XsrUiSize cached)
            && (!_widthSensitiveMeasures.Contains(entity.Index)
                || _measureConstraints.TryGetValue(entity.Index, out XsrUiSize constraint) && constraint.Width == available.Width))
        {
            return cached;
        }
        _measureConstraints[entity.Index] = available;

        if (_measuredThisPass.Add(entity.Index))
        {
            _layoutVisits++;
        }
        XsrUiElement? element = EffectiveElement(entity);
        XsrUiThickness padding = element?.Padding ?? default;
        XsrUiThickness margin = element?.Margin ?? default;

        double contentWidth = Math.Max(0, available.Width - margin.Horizontal);
        double contentHeight = Math.Max(0, available.Height - margin.Vertical);
        // Descendants must measure against the content box this element will actually present.
        // Without this constraint an explicitly narrow card is first measured at the viewport
        // width, so wrapped text grows only during child arrange and the card keeps a stale
        // one-line intrinsic height.
        if (PresentedWidth(entity, element, available.Width) is { } presentedWidth)
        {
            contentWidth = Math.Min(contentWidth, presentedWidth + padding.Horizontal);
        }
        if (element?.Height is { } constrainedHeight)
        {
            contentHeight = Math.Min(contentHeight, constrainedHeight + padding.Vertical);
        }
        double indicatorGutter = VerticalIndicatorGutter(entity);
        double horizontalIndicatorGutter = HorizontalIndicatorGutter(entity);
        contentWidth = Math.Max(0, contentWidth - indicatorGutter);
        contentHeight = Math.Max(0, contentHeight - horizontalIndicatorGutter);
        double width = 0;
        double height = 0;
        bool widthSensitive = false;

        if (_tree.GetComponent<XsrUiText>(entity) is { } text)
        {
            XsrUiVisualStyle? textStyle = EffectiveVisual(entity);
            bool capsule = IsScrollableCapsule(entity);
            widthSensitive = textStyle?.WrapText == true || capsule;
            double wrapWidth = textStyle?.WrapText == true && !capsule
                ? element?.Width ?? Math.Max(0, contentWidth - padding.Horizontal)
                : double.PositiveInfinity;
            XsrUiSize textSize = MeasureText(
                ResolveText(text),
                textStyle?.FontSize ?? 0,
                wrapWidth,
                text.MaxLines,
                GetThemeStyle(entity)?.Style.FontFamily, textStyle?.FontWeight ?? 400, text.Runs);
            width = textSize.Width;
            height = textSize.Height;
        }

        XsrUiStackPanel? stack = EffectiveStack(entity);
        if (GetThemeStyle(entity)?.Style.Grid is { } themeGrid)
        {
            XsrUiSize gridSize = MeasureThemeGrid(entity, themeGrid, new(
                Math.Max(0, contentWidth - padding.Horizontal), Math.Max(0, contentHeight - padding.Vertical)));
            width = gridSize.Width + padding.Horizontal + indicatorGutter;
            height = gridSize.Height + padding.Vertical + horizontalIndicatorGutter;
            widthSensitive = true;
        }
        else if (_tree.GetComponent<XsrUiPager>(entity) is not null)
        {
            foreach (XsrUiEntityId child in OrderedChildren(entity).Where(IsVisible))
            {
                XsrUiSize size = Measure(child, new XsrUiSize(contentWidth, contentHeight));
                widthSensitive |= _widthSensitiveMeasures.Contains(child.Index);
                width = Math.Max(width, size.Width);
                height = Math.Max(height, size.Height);
            }
        }
        else if (stack is not null)
        {
            double main = 0;
            double cross = 0;
            int children = 0;
            foreach (XsrUiEntityId child in OrderedChildren(entity))
            {
                if (!ParticipatesInStackFlow(child))
                {
                    continue;
                }

                XsrUiSize childAvailable = new(
                    Math.Max(0, contentWidth - padding.Horizontal),
                    Math.Max(0, contentHeight - padding.Vertical));
                XsrUiSize childDesired = Measure(child, childAvailable);
                widthSensitive |= _widthSensitiveMeasures.Contains(child.Index);
                XsrUiThickness childMargin = EffectiveElement(child)?.Margin ?? default;
                bool isMainWidth = stack.Direction == XsrUiOrientation.Horizontal;
                double childMain = isMainWidth
                    ? childDesired.Width + childMargin.Horizontal
                    : childDesired.Height + childMargin.Vertical;
                double childCross = isMainWidth
                    ? childDesired.Height + childMargin.Vertical
                    : childDesired.Width + childMargin.Horizontal;
                main += childMain;
                cross = Math.Max(cross, childCross);
                children++;
            }

            main += Math.Max(0, children - 1) * stack.Spacing;
            width = stack.Direction == XsrUiOrientation.Horizontal ? main : cross;
            height = stack.Direction == XsrUiOrientation.Horizontal ? cross : main;
            _stackContentSizes[entity.Index] = new XsrUiSize(width, height);
            width += padding.Horizontal + indicatorGutter;
            height += padding.Vertical + horizontalIndicatorGutter;
        }

        // Explicit sizes constrain the content box; padding adds on top of them.
        if (PresentedWidth(entity, element, available.Width) is { } explicitWidth)
        {
            width = explicitWidth + padding.Horizontal;
        }

        if (element?.Height is { } explicitHeight)
        {
            height = explicitHeight + padding.Vertical;
        }

        width = ConstrainDimension(width, element?.MinWidth, element?.MaxWidth, padding.Horizontal);
        height = ConstrainDimension(height, element?.MinHeight, element?.MaxHeight, padding.Vertical);

        if (_tree.GetComponent<XsrUiSegmentedTrack>(entity) is not null)
        {
            width = Math.Min(width, Math.Max(0, available.Width - margin.Horizontal));
            widthSensitive = true;
        }

        XsrUiSize desired = new(width, height);
        if (widthSensitive) _widthSensitiveMeasures.Add(entity.Index);
        else _widthSensitiveMeasures.Remove(entity.Index);
        _desiredSizes[entity.Index] = desired;
        return desired;
    }

    private void Arrange(XsrUiEntityId entity, XsrUiRect slot, XsrUiSize desired)
    {
        XsrUiElement? element = EffectiveElement(entity);
        XsrUiThickness margin = element?.Margin ?? default;
        XsrUiThickness padding = element?.Padding ?? default;

        double slotX = slot.X + margin.Left;
        double slotY = slot.Y + margin.Top;
        double slotW = Math.Max(0, slot.Width - margin.Horizontal);
        double slotH = Math.Max(0, slot.Height - margin.Vertical);

        double contentDesiredW = PresentedWidth(entity, element, slot.Width) ?? Math.Max(0, desired.Width - padding.Horizontal);
        double contentDesiredH = element?.Height ?? Math.Max(0, desired.Height - padding.Vertical);
        double borderDesiredW = ConstrainDimension(
            contentDesiredW + padding.Horizontal,
            element?.MinWidth,
            element?.MaxWidth,
            padding.Horizontal);
        double borderDesiredH = ConstrainDimension(
            contentDesiredH + padding.Vertical,
            element?.MinHeight,
            element?.MaxHeight,
            padding.Vertical);

        // Stretch fills the slot only when no explicit size constrains the axis.
        bool stretchW = (element?.HorizontalAlignment ?? XsrUiAlignment.Stretch) == XsrUiAlignment.Stretch
            && element?.Width is null;
        bool stretchH = (element?.VerticalAlignment ?? XsrUiAlignment.Stretch) == XsrUiAlignment.Stretch
            && element?.Height is null;

        double borderW = Math.Min(
            slotW,
            ConstrainDimension(
                stretchW ? slotW : borderDesiredW,
                element?.MinWidth,
                element?.MaxWidth,
                padding.Horizontal));
        double borderH = Math.Min(
            slotH,
            ConstrainDimension(
                stretchH ? slotH : borderDesiredH,
                element?.MinHeight,
                element?.MaxHeight,
                padding.Vertical));
        XsrUiAlignment horizontal = element?.HorizontalAlignment ?? XsrUiAlignment.Stretch;
        XsrUiAlignment vertical = element?.VerticalAlignment ?? XsrUiAlignment.Stretch;
        double borderX = slotX + (horizontal switch
        {
            XsrUiAlignment.Center => (slotW - borderW) / 2,
            XsrUiAlignment.End => slotW - borderW,
            _ => 0,
        });
        double borderY = slotY + (vertical switch
        {
            XsrUiAlignment.Center => (slotH - borderH) / 2,
            XsrUiAlignment.End => slotH - borderH,
            _ => 0,
        });

        double contentX = borderX + padding.Left;
        double contentY = borderY + padding.Top;
        double contentWidth = Math.Max(0, borderW - padding.Horizontal);
        double contentHeight = Math.Max(0, borderH - padding.Vertical);
        _arrangedSlots[entity.Index] = slot;

        if (_tree.GetComponent<XsrUiProgress>(entity) is { } progress)
        {
            if (progress.BoundState.IsAssigned)
            {
                object? value = _state.ReadAppliedValue(progress.BoundState);
                progress.Target = Math.Clamp(Convert.ToDouble(value, CultureInfo.InvariantCulture), 0, 1);
            }

            // The element paints the fill bar: its rect is the presented fraction of the slot
            // its parent arranged it into. Leading anchors the fraction to the left edge (the
            // standard bar); Bottom rises from the bottom edge so round containers fill up.
            double fill = Math.Max(0, Math.Min(1, progress.Presented));
            _paintRects[entity.Index] = progress.Ring
                ? new XsrUiRect(borderX, borderY, borderW, borderH)
                : progress.Anchor == XsrUiProgressFillAnchor.Bottom
                ? new XsrUiRect(slotX, slotY + (slotH * (1 - fill)), slotW, slotH * fill)
                : new XsrUiRect(slotX, slotY, slotW * fill, slotH);
            return;
        }

        _paintRects[entity.Index] = new XsrUiRect(contentX, contentY, contentWidth, contentHeight);
        ArrangeCapsuleScroll(entity, _paintRects[entity.Index]);

        if (GetThemeStyle(entity)?.Style.Grid is { } themeGrid)
        {
            ArrangeThemeGrid(entity, themeGrid, new(contentX, contentY,
                Math.Max(0, contentWidth - VerticalIndicatorGutter(entity)),
                Math.Max(0, contentHeight - HorizontalIndicatorGutter(entity))));
            return;
        }

        if (_tree.GetComponent<XsrUiPager>(entity) is { } pager)
        {
            XsrUiEntityId[] pages = [.. OrderedChildren(entity).Where(IsVisible)];
            pager.PageCount = pages.Length;
            pager.PageIndex = Math.Clamp(pager.PageIndex, 0, Math.Max(0, pages.Length - 1));
            if (EffectiveReducedMotion && !pager.IsDragging) pager.Position = pager.PageIndex;
            for (int i = 0; i < pages.Length; i++)
            {
                double offset = i - pager.Position;
                Layout(pages[i], pager.Direction == XsrUiOrientation.Vertical
                    ? new XsrUiRect(contentX, contentY + offset * contentHeight, contentWidth, contentHeight)
                    : new XsrUiRect(contentX + offset * contentWidth, contentY, contentWidth, contentHeight));
            }
            return;
        }

        XsrUiStackPanel? stack = EffectiveStack(entity);
        if (stack is null)
        {
            // Without a stack component, children overlap the full content rect in attach order.
            foreach (XsrUiEntityId child in OrderedChildren(entity))
            {
                if (IsVisible(child))
                {
                    LayoutThemedChild(child, new XsrUiRect(contentX, contentY, contentWidth, contentHeight));
                }
            }

            return;
        }

        XsrUiScroll? scroll = _tree.GetComponent<XsrUiScroll>(entity);
        contentWidth = Math.Max(0, contentWidth - VerticalIndicatorGutter(entity));
        contentHeight = Math.Max(0, contentHeight - HorizontalIndicatorGutter(entity));
        if (scroll is not null)
        {
            // Clamp scroll offsets to the measured content extent; children are placed into
            // slots shifted by the offset, so hit testing follows scrolled content for free.
            XsrUiSize content = _stackContentSizes.TryGetValue(entity.Index, out XsrUiSize stackSize)
                ? stackSize
                : desired;
            double maxOffsetX = Math.Max(0, content.Width - contentWidth);
            double maxOffsetY = Math.Max(0, content.Height - contentHeight);
            bool wasAtEnd = scroll.StickToEnd
                && (scroll.OffsetY >= scroll.MaximumOffsetY - .5
                    || double.IsPositiveInfinity(scroll.OffsetY));
            scroll.OffsetX = Math.Clamp(scroll.OffsetX, 0, maxOffsetX);
            if (_tree.GetComponent<XsrUiSegmentedTrack>(entity) is { ScrollTargetX: { } segmentTarget } segmentTrack)
                segmentTrack.ScrollTargetX = Math.Clamp(segmentTarget, 0, maxOffsetX);
            scroll.OffsetY = wasAtEnd
                ? maxOffsetY
                : Math.Clamp(scroll.OffsetY, 0, maxOffsetY);
            scroll.MaximumOffsetY = maxOffsetY;
            scroll.MaximumOffsetX = maxOffsetX;
        }

        double scrollX = scroll?.OffsetX ?? 0;
        double scrollY = scroll?.OffsetY ?? 0;
        double cursor = (stack.Direction == XsrUiOrientation.Vertical ? contentY : contentX) - (stack.Direction == XsrUiOrientation.Vertical ? scrollY : scrollX);
        double crossAvailable = stack.Direction == XsrUiOrientation.Vertical ? contentWidth : contentHeight;

        double crossOrigin = stack.Direction == XsrUiOrientation.Vertical ? contentX : contentY;
        double crossScroll = stack.Direction == XsrUiOrientation.Vertical ? scrollX : scrollY;
        XsrUiEntityId[] visibleChildren = [.. OrderedChildren(entity)
            .Where(ParticipatesInStackFlow)];
        double availableMain = stack.Direction == XsrUiOrientation.Vertical ? contentHeight : contentWidth;
        Dictionary<int, double> weightedMainSizes = AllocateWeightedMainSizes(
            stack.Direction,
            visibleChildren,
            availableMain,
            stack.Spacing);
        bool hasWeightedChildren = weightedMainSizes.Count != 0;
        double consumedMain = 0;
        for (int childIndex = 0; childIndex < visibleChildren.Length; childIndex++)
        {
            XsrUiEntityId child = visibleChildren[childIndex];

            XsrUiSize childDesired = _desiredSizes.TryGetValue(child.Index, out XsrUiSize size)
                ? size
                : Measure(child, new XsrUiSize(contentWidth, contentHeight));
            XsrUiElement? childElement = EffectiveElement(child);
            XsrUiThickness childMargin = childElement?.Margin ?? default;
            double childMain = weightedMainSizes.TryGetValue(child.Index, out double weightedMain)
                ? weightedMain
                : stack.Direction == XsrUiOrientation.Vertical
                    ? childDesired.Height + childMargin.Vertical
                    : childDesired.Width + childMargin.Horizontal;
            if (!hasWeightedChildren
                && stack.StretchLastChild
                && childIndex == visibleChildren.Length - 1)
            {
                childMain = Math.Max(childMain, Math.Max(0, availableMain - consumedMain));
            }
            XsrUiAlignment crossAlignment = stack.Direction == XsrUiOrientation.Vertical
                ? childElement?.HorizontalAlignment ?? XsrUiAlignment.Stretch
                : childElement?.VerticalAlignment ?? XsrUiAlignment.Stretch;

            double crossSize = crossAlignment == XsrUiAlignment.Stretch
                ? crossAvailable
                : (stack.Direction == XsrUiOrientation.Vertical
                    ? childDesired.Width + childMargin.Horizontal
                    : childDesired.Height + childMargin.Vertical);
            double crossOffset = crossAlignment switch
            {
                XsrUiAlignment.Center => (crossAvailable - crossSize) / 2,
                XsrUiAlignment.End => crossAvailable - crossSize,
                _ => 0,
            };

            XsrUiRect childSlot = stack.Direction == XsrUiOrientation.Vertical
                ? new XsrUiRect(crossOrigin + crossOffset - crossScroll, cursor, crossSize, childMain)
                : new XsrUiRect(cursor, crossOrigin + crossOffset - crossScroll, childMain, crossSize);

            Layout(child, childSlot);
            cursor += childMain + stack.Spacing;
            consumedMain += childMain + stack.Spacing;
        }

        // Overlay children never participate in stack flow. They receive the parent's complete
        // content rectangle so their own alignment and margins anchor them inside the window.
        foreach (XsrUiEntityId overlay in OrderedChildren(entity).Where(child =>
                     IsVisible(child) && (_tree.GetComponent<XsrUiOverlayLayer>(child) is not null || IsThemedOutOfFlow(child))))
        {
            LayoutThemedChild(overlay, new XsrUiRect(contentX, contentY, contentWidth, contentHeight));
        }
    }

    private double VerticalIndicatorGutter(XsrUiEntityId entity) =>
        (EffectiveStack(entity) is not null || GetThemeStyle(entity)?.Style.Grid is not null)
        && _tree.GetComponent<XsrUiScroll>(entity) is { ShowsVerticalIndicator: true } ? 12 : 0;

    private double HorizontalIndicatorGutter(XsrUiEntityId entity) =>
        (EffectiveStack(entity) is not null || GetThemeStyle(entity)?.Style.Grid is not null)
        && _tree.GetComponent<XsrUiScroll>(entity) is { ShowsHorizontalIndicator: true } ? 12 : 0;

    private Dictionary<int, double> AllocateWeightedMainSizes(
        XsrUiOrientation direction,
        XsrUiEntityId[] children,
        double availableMain,
        double spacing)
    {
        List<WeightedSlot> weighted = [];
        double remaining = Math.Max(0, availableMain - Math.Max(0, children.Length - 1) * spacing);
        foreach (XsrUiEntityId child in children)
        {
            XsrUiElement? element = EffectiveElement(child);
            double weight = Math.Max(0, element?.Weight ?? 0);
            XsrUiSize desired = _desiredSizes.TryGetValue(child.Index, out XsrUiSize cached)
                ? cached
                : default;
            XsrUiThickness margin = element?.Margin ?? default;
            if (weight == 0)
            {
                remaining -= direction == XsrUiOrientation.Vertical
                    ? desired.Height + margin.Vertical
                    : desired.Width + margin.Horizontal;
                continue;
            }

            XsrUiThickness padding = element?.Padding ?? default;
            double minimum = direction == XsrUiOrientation.Vertical
                ? ConstraintWithBox(element?.MinHeight, padding.Vertical, margin.Vertical, fallback: 0)
                : ConstraintWithBox(element?.MinWidth, padding.Horizontal, margin.Horizontal, fallback: 0);
            double maximum = direction == XsrUiOrientation.Vertical
                ? ConstraintWithBox(element?.MaxHeight, padding.Vertical, margin.Vertical, double.PositiveInfinity)
                : ConstraintWithBox(element?.MaxWidth, padding.Horizontal, margin.Horizontal, double.PositiveInfinity);
            weighted.Add(new WeightedSlot(child.Index, weight, minimum, Math.Max(minimum, maximum)));
        }

        if (weighted.Count == 0)
        {
            return [];
        }

        Dictionary<int, double> result = [];
        List<WeightedSlot> unresolved = [.. weighted];
        remaining = Math.Max(0, remaining);
        double remainingWeight = unresolved.Sum(slot => slot.Weight);
        while (unresolved.Count > 0)
        {
            double unit = remainingWeight > 0 ? remaining / remainingWeight : 0;
            int constrainedIndex = -1;
            double constrainedSize = 0;
            for (int index = 0; index < unresolved.Count; index++)
            {
                WeightedSlot slot = unresolved[index];
                double proposed = unit * slot.Weight;
                if (proposed < slot.Minimum)
                {
                    constrainedIndex = index;
                    constrainedSize = slot.Minimum;
                    break;
                }

                if (proposed > slot.Maximum)
                {
                    constrainedIndex = index;
                    constrainedSize = slot.Maximum;
                    break;
                }
            }

            if (constrainedIndex < 0)
            {
                foreach (WeightedSlot slot in unresolved)
                {
                    result[slot.EntityIndex] = unit * slot.Weight;
                }

                break;
            }

            WeightedSlot constrained = unresolved[constrainedIndex];
            result[constrained.EntityIndex] = constrainedSize;
            remaining = Math.Max(0, remaining - constrainedSize);
            remainingWeight -= constrained.Weight;
            unresolved.RemoveAt(constrainedIndex);
        }

        return result;
    }

    private static double ConstraintWithBox(
        double? contentConstraint,
        double padding,
        double margin,
        double fallback) =>
        contentConstraint is { } value
            ? Math.Max(0, value) + padding + margin
            : fallback;

    private static double ConstrainDimension(
        double value,
        double? minimumContent,
        double? maximumContent,
        double padding)
    {
        double minimum = Math.Max(0, minimumContent ?? 0) + padding;
        double maximum = maximumContent is { } declaredMaximum
            ? Math.Max(minimum, Math.Max(0, declaredMaximum) + padding)
            : double.PositiveInfinity;
        return Math.Clamp(Math.Max(0, value), minimum, maximum);
    }

    private readonly record struct WeightedSlot(
        int EntityIndex,
        double Weight,
        double Minimum,
        double Maximum);
}
