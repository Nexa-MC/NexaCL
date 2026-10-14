namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    private IXsrUiThemeStyleProgram? _themeProgram;
    private XsrUiThemeEnvironment _themeEnvironment = new();
    private Dictionary<XsrUiEntityId, ThemeStyleEntry> _themeStyles = [];

    private IXsrUiTextLayoutProvider? _textLayoutProvider;
    public event EventHandler<XsrUiThemeStyleFailureEventArgs>? ThemeStyleFailed;
    public long ThemeActivationGeneration { get; private set; }
    public long ThemeStyleComputeCount { get; private set; }
    public int ThemeStyleCacheCount => _themeStyles.Count;
    public bool HasThemeAnimations
    {
        get
        {
            if (EffectiveReducedMotion || _scene is null) return false;
            DateTimeOffset now = _gestureTime.GetUtcNow();
            foreach (var node in _scene.Nodes)
            {
                if (node.Rect.Width <= 0 || node.Rect.Height <= 0 || !_tree.IsAlive(node.Entity)
                    || !_themeStyles.TryGetValue(node.Entity, out var entry) || entry.Style.Animations is not { Count: > 0 } animations) continue;
                if (!_themeAnimationClocks.TryGetValue(node.Entity, out var clock)) return true;
                foreach (var animation in animations)
                    if (StyleAnimationChangesValue(animation) && (animation.Repeat || now - clock.Started < animation.Duration || clock.LastRendered - clock.Started < animation.Duration))
                        return true;
            }
            return false;
        }
    }

    public IXsrUiTextLayoutProvider? TextLayoutProvider
    {
        get => _textLayoutProvider;
        set
        {
            if (ReferenceEquals(_textLayoutProvider, value)) return;
            bool changedMetrics = (_textLayoutProvider is null) != (value is null)
                || (_textLayoutProvider?.Revision ?? 0) != (value?.Revision ?? 0);
            _textLayoutProvider = value;
            if (changedMetrics) InvalidateThemeLayout();
        }
    }

    /// <summary>Checks a complete candidate against active, hidden and detached host structure without applying it.</summary>
    public void ValidateTheme(IXsrUiThemeStyleProgram program, XsrUiThemeEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        XsrUiThemeEnvironment candidateEnvironment = environment ?? new();
        Dictionary<XsrUiEntityId, ThemeStyleEntry> candidate = [];
        foreach (XsrUiEntityId entity in _tree.LiveEntities)
            candidate.Add(entity, ComputeThemeStyle(entity, CreateThemeStyleKey(entity, program.Revision, candidateEnvironment), program, candidateEnvironment));
        ValidateThemeGridPlacements(candidate);
    }

    /// <summary>Prepares every live and retained node before committing a style-only generation.</summary>
    public void ApplyTheme(IXsrUiThemeStyleProgram program, long activationGeneration,
        XsrUiThemeEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (activationGeneration <= ThemeActivationGeneration)
            throw new InvalidOperationException("A theme activation generation must increase monotonically.");
        XsrUiThemeEnvironment candidateEnvironment = environment ?? new();
        Dictionary<XsrUiEntityId, ThemeStyleEntry> candidate = [];
        HashSet<XsrUiEntityId> layoutChanges = [];
        foreach (XsrUiEntityId entity in _tree.LiveEntities)
        {
            ThemeStyleKey key = CreateThemeStyleKey(entity, program.Revision, candidateEnvironment);
            ThemeStyleEntry next = ComputeThemeStyle(entity, key, program, candidateEnvironment);
            ThemeStyleEntry previous = _themeStyles.TryGetValue(entity, out var cached) ? cached : BaseThemeEntry(entity, key);
            if (!SameThemeLayout(previous, next) || _themeProgram is not null && !_themeStyles.ContainsKey(entity)) layoutChanges.Add(entity);
            candidate.Add(entity, next);
        }
        ValidateThemeGridPlacements(candidate);
        _themeProgram = program;
        _themeEnvironment = candidateEnvironment;
        _themeStyles = candidate;
        ThemeActivationGeneration = activationGeneration;
        RetainThemeAnimationClocks(candidate);
        RetireThemeOutgoingSnapshots();
        foreach (XsrUiEntityId entity in candidate.Keys)
            _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint | (layoutChanges.Contains(entity) ? XsrUiDirtyKinds.Layout : 0));
    }

    private void RetainThemeAnimationClocks(IReadOnlyDictionary<XsrUiEntityId, ThemeStyleEntry> candidate)
    {
        foreach (XsrUiEntityId entity in _themeAnimationClocks.Keys.ToArray())
        {
            if (candidate.TryGetValue(entity, out var next)
                && SameThemeAnimations(_themeAnimationClocks[entity].Animations, next.Style.Animations ?? [])) continue;
            _themeAnimationClocks.Remove(entity);
            _themeAnimationVisible.Remove(entity);
        }
    }

    public void ClearTheme(long activationGeneration)
    {
        if (activationGeneration <= ThemeActivationGeneration)
            throw new InvalidOperationException("A theme activation generation must increase monotonically.");
        HashSet<XsrUiEntityId> layoutChanges = [];
        foreach (XsrUiEntityId entity in _tree.LiveEntities)
            if (_themeStyles.TryGetValue(entity, out var previous)
                ? !SameThemeLayout(previous, BaseThemeEntry(entity, previous.Key))
                : _themeProgram is not null)
                layoutChanges.Add(entity);
        _themeProgram = null;
        _themeStyles.Clear();
        _themeGridMeasurements.Clear();
        _themeAnimationClocks.Clear();
        _themeAnimationVisible.Clear();
        ThemeActivationGeneration = activationGeneration;
        RetireThemeOutgoingSnapshots();
        foreach (XsrUiEntityId entity in _tree.LiveEntities)
            _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint | (layoutChanges.Contains(entity) ? XsrUiDirtyKinds.Layout : 0));
    }

    /// <summary>Only schedules a new scene; finite keyframe values are sampled from the host presentation clock.</summary>
    public bool AdvanceThemeAnimations()
    {
        if (!HasThemeAnimations) return false;
        if (_root.IsAssigned && _tree.IsAlive(_root)) _tree.MarkDirty(_root, XsrUiDirtyKinds.Paint);
        return true;
    }

    public void RegisterVirtualLayout(XsrUiEntityId entity, XsrUiVirtualLayoutContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!_tree.IsAlive(entity) || contract.ItemWidth < 0 || contract.ItemHeight <= 0
            || contract.Spacing < 0 || contract.Columns < 1 || contract.RealizationLimit is < 1 or > 8192
            || !double.IsFinite(contract.ItemWidth) || !double.IsFinite(contract.ItemHeight) || !double.IsFinite(contract.Spacing))
            throw new ArgumentException("A virtual layout contract must be finite and belong to a live entity.", nameof(contract));
        _tree.SetComponent(entity, contract);
        _themeStyles.Remove(entity);
    }

    /// <summary>Protects one host-projected row or spacer rectangle while permitting theme paint and nested typography.</summary>
    public void ProtectVirtualGeometry(XsrUiEntityId entity)
    {
        if (!_tree.IsAlive(entity)) throw new ArgumentException("A virtual geometry guard requires a live entity.", nameof(entity));
        if (_tree.GetComponent<XsrUiVirtualGeometry>(entity) is not null) return;
        _tree.SetComponent(entity, new XsrUiVirtualGeometry());
        _themeStyles.Remove(entity);
    }

    private long _lastTextLayoutRevision;
    private long _lastThemeLifetimeRevision;
    private XsrUiSize _lastThemeViewport;
    private void PrepareThemeFrame()
    {
        long fontRevision = _textLayoutProvider?.Revision ?? 0;
        if (fontRevision != _lastTextLayoutRevision)
        {
            _lastTextLayoutRevision = fontRevision;
            InvalidateThemeLayout();
        }
        if (_lastThemeLifetimeRevision != _tree.EntityLifetimeRevision)
        {
            _lastThemeLifetimeRevision = _tree.EntityLifetimeRevision;
            foreach (XsrUiEntityId dead in _themeStyles.Keys.Where(entity => !_tree.IsAlive(entity)).ToArray())
                _themeStyles.Remove(dead);
            foreach (XsrUiEntityId dead in _themeGridMeasurements.Keys.Where(entity => !_tree.IsAlive(entity)).ToArray())
                _themeGridMeasurements.Remove(dead);
        }
        if (_themeProgram is null || !_tree.HasDirtySubtree(_root)) return;
        bool structural = SubtreeHasKind(_root, XsrUiDirtyKinds.Structure);
        bool responsive = _lastThemeViewport != _viewport;
        _lastThemeViewport = _viewport;
        _tree.Walk(_root, entity =>
        {
            if (structural || responsive || _tree.DirtyKinds(entity) != XsrUiDirtyKinds.None)
            {
                _themeStyles.TryGetValue(entity, out ThemeStyleEntry? previous);
                ThemeStyleEntry? next = GetThemeStyle(entity);
                if (previous is null || next is not null && !SameThemeLayout(previous, next))
                    _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
            }
            return true;
        }, structural || responsive ? null : entity => _tree.HasDirtySubtree(entity));
    }

    private static bool SameThemeLayout(ThemeStyleEntry first, ThemeStyleEntry second) =>
        first.Element.Width == second.Element.Width && first.Element.Height == second.Element.Height
        && first.Element.MinWidth == second.Element.MinWidth && first.Element.MaxWidth == second.Element.MaxWidth
        && first.Element.MinHeight == second.Element.MinHeight && first.Element.MaxHeight == second.Element.MaxHeight
        && first.Element.Weight == second.Element.Weight && first.Element.Margin == second.Element.Margin
        && first.Element.Padding == second.Element.Padding && first.Element.HorizontalAlignment == second.Element.HorizontalAlignment
        && first.Element.VerticalAlignment == second.Element.VerticalAlignment && first.Stack?.Direction == second.Stack?.Direction
        && first.Stack?.Spacing == second.Stack?.Spacing && first.Visual.FontSize == second.Visual.FontSize
        && first.Visual.FontWeight == second.Visual.FontWeight && first.Visual.WrapText == second.Visual.WrapText
        && first.Style.FontFamily == second.Style.FontFamily && first.Style.Grid == second.Style.Grid
        && first.Style.Position == second.Style.Position && first.Style.Order == second.Style.Order
        && first.Style.Left == second.Style.Left && first.Style.Top == second.Style.Top
        && first.Style.Right == second.Style.Right && first.Style.Bottom == second.Style.Bottom
        && first.Style.GridColumn == second.Style.GridColumn && first.Style.GridRow == second.Style.GridRow
        && first.Style.GridColumnSpan == second.Style.GridColumnSpan && first.Style.GridRowSpan == second.Style.GridRowSpan;

    public bool TryGetLayoutFacts(XsrUiEntityId entity, out XsrUiLayoutFacts facts)
    {
        facts = default;
        if (!_tree.IsAlive(entity) || EffectiveElement(entity) is not { } element) return false;
        facts = new(element.Width, element.Height, element.MinWidth, element.MaxWidth,
            element.MinHeight, element.MaxHeight, element.Margin, element.Padding);
        return true;
    }

    public bool TryGetVirtualLayoutMetrics(XsrUiEntityId entity, out XsrUiVirtualLayoutMetrics metrics)
    {
        metrics = default;
        if (!_tree.IsAlive(entity) || _tree.GetComponent<XsrUiVirtualLayoutContract>(entity) is not { } contract) return false;
        XsrUiComputedStyle style = GetThemeStyle(entity)?.Style ?? XsrUiComputedStyle.Empty;
        metrics = new(ThemeActivationGeneration, _themeProgram?.Revision ?? 0,
            contract.SupportsThemeMetrics ? style.VirtualItemWidth ?? contract.ItemWidth : contract.ItemWidth,
            contract.SupportsThemeMetrics ? style.VirtualItemHeight ?? contract.ItemHeight : contract.ItemHeight,
            contract.SupportsThemeMetrics ? style.VirtualItemSpacing ?? contract.Spacing : contract.Spacing,
            contract.SupportsThemeMetrics ? style.VirtualItemColumns ?? contract.Columns : contract.Columns,
            contract.RealizationLimit)
        {
            Kind = contract.Kind,
            HasItemWidthOverride = contract.SupportsThemeMetrics && style.VirtualItemWidth is not null,
            HasItemHeightOverride = contract.SupportsThemeMetrics && style.VirtualItemHeight is not null,
            HasColumnsOverride = contract.SupportsThemeMetrics && style.VirtualItemColumns is not null,
        };
        return true;
    }

    private void InvalidateThemeLayout()
    {
        _desiredSizes.Clear();
        _stackContentSizes.Clear();
        _measureConstraints.Clear();
        _widthSensitiveMeasures.Clear();
        foreach (XsrUiEntityId entity in _tree.LiveEntities)
            _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    private ThemeStyleEntry? GetThemeStyle(XsrUiEntityId entity)
    {
        if (_themeProgram is null) return null;
        ThemeStyleKey key = CreateThemeStyleKey(entity, _themeProgram.Revision, _themeEnvironment);
        if (_themeStyles.TryGetValue(entity, out ThemeStyleEntry? existing) && existing.Key == key) return existing;
        ThemeStyleEntry computed = ComputeThemeStyle(entity, key, _themeProgram, _themeEnvironment);
        _themeStyles[entity] = computed;
        return computed;
    }

    private XsrUiElement? EffectiveElement(XsrUiEntityId entity) =>
        GetThemeStyle(entity)?.Element ?? _tree.GetComponent<XsrUiElement>(entity);
    private XsrUiStackPanel? EffectiveStack(XsrUiEntityId entity) =>
        GetThemeStyle(entity)?.Stack ?? _tree.GetComponent<XsrUiStackPanel>(entity);
    private XsrUiVisualStyle? EffectiveVisual(XsrUiEntityId entity) =>
        GetThemeStyle(entity)?.Visual ?? _tree.GetComponent<XsrUiVisualStyle>(entity);

    private XsrUiStyleIdentity StyleIdentity(XsrUiEntityId entity) =>
        _tree.GetComponent<XsrUiStyleIdentity>(entity) ?? new("Node", _tree.Name(entity), [],
            _tree.GetComponent<XsrUiSemantic>(entity)?.Role.ToString());

    private ThemeStyleKey CreateThemeStyleKey(XsrUiEntityId entity, long revision, XsrUiThemeEnvironment environment)
    {
        XsrUiElement? element = _tree.GetComponent<XsrUiElement>(entity);
        XsrUiStackPanel? stack = _tree.GetComponent<XsrUiStackPanel>(entity);
        XsrUiVisualStyleSnapshot visual = _tree.GetComponent<XsrUiVisualStyle>(entity)?.Snapshot() ?? default;
        XsrUiInput? input = _tree.GetComponent<XsrUiInput>(entity);
        XsrUiThemeInteractionState state = XsrUiThemeInteractionState.None;
        bool enabled = IsEnabled(input);
        if (enabled && input?.IsHovered == true) state |= XsrUiThemeInteractionState.Hover;
        if (enabled && input?.IsPressed == true) state |= XsrUiThemeInteractionState.Pressed;
        if (enabled && input?.IsFocused == true) state |= XsrUiThemeInteractionState.Focus;
        if (enabled && input?.IsFocusVisible == true) state |= XsrUiThemeInteractionState.FocusVisible;
        if (!enabled) state |= XsrUiThemeInteractionState.Disabled;
        if (_tree.GetComponent<XsrUiSelection>(entity)?.IsSelected == true) state |= XsrUiThemeInteractionState.Selected;
        if (_tree.GetComponent<XsrUiToggle>(entity)?.IsChecked == true) state |= XsrUiThemeInteractionState.Checked;
        XsrUiStyleIdentity identity = StyleIdentity(entity);
        HashCode parents = new();
        for (XsrUiEntityId parent = _tree.Parent(entity); parent.IsAssigned; parent = _tree.Parent(parent))
        {
            parents.Add(parent); parents.Add(StyleIdentity(parent));
        }
        return new(revision, environment, _viewport, _colorScheme, _textLayoutProvider?.Revision ?? 0, identity,
            parents.ToHashCode(), state, new(element?.Width, element?.Height, element?.MinWidth, element?.MaxWidth,
                element?.MinHeight, element?.MaxHeight, element?.Weight ?? 0, element?.Margin ?? default,
                element?.Padding ?? default, element?.HorizontalAlignment ?? default, element?.VerticalAlignment ?? default),
            stack?.Direction, stack?.Spacing ?? 0, stack?.StretchLastChild ?? false, visual);
    }

    private ThemeStyleEntry BaseThemeEntry(XsrUiEntityId entity, ThemeStyleKey key) => new(key, XsrUiComputedStyle.Empty,
        _tree.GetComponent<XsrUiElement>(entity) ?? new(), _tree.GetComponent<XsrUiStackPanel>(entity),
        _tree.GetComponent<XsrUiVisualStyle>(entity) ?? new());

    private ThemeStyleEntry ComputeThemeStyle(XsrUiEntityId entity, ThemeStyleKey key,
        IXsrUiThemeStyleProgram program, XsrUiThemeEnvironment environment)
    {
        List<XsrUiStyleIdentity> parents = [];
        for (XsrUiEntityId parent = _tree.Parent(entity); parent.IsAssigned; parent = _tree.Parent(parent))
            parents.Add(StyleIdentity(parent));
        XsrUiComputedStyle style = program.Compute(new(entity, key.Identity, parents, key.State, _viewport, environment, key.Visual))
            ?? throw new XsrUiThemeStyleException(entity, "A theme style program returned no computed style.");
        ValidateComputedStyle(entity, style);
        style = ApplyHostStylePolicy(entity, style);
        ThemeStyleComputeCount++;
        XsrUiElement source = _tree.GetComponent<XsrUiElement>(entity) ?? new();
        XsrUiElement element = new()
        {
            IsVisible = source.IsVisible,
            BoundVisibility = source.BoundVisibility,
            Width = style.Width ?? source.Width,
            Height = style.Height ?? source.Height,
            MinWidth = style.MinWidth ?? source.MinWidth,
            MaxWidth = style.MaxWidth ?? source.MaxWidth,
            MinHeight = style.MinHeight ?? source.MinHeight,
            MaxHeight = style.MaxHeight ?? source.MaxHeight,
            Weight = style.Weight ?? source.Weight,
            Margin = style.Margin ?? source.Margin,
            Padding = style.Padding ?? source.Padding,
            HorizontalAlignment = style.HorizontalAlignment ?? source.HorizontalAlignment,
            VerticalAlignment = style.VerticalAlignment ?? source.VerticalAlignment,
        };
        if (element.MinWidth is { } minWidth && element.MaxWidth is { } maxWidth && minWidth > maxWidth
            || element.MinHeight is { } minHeight && element.MaxHeight is { } maxHeight && minHeight > maxHeight)
            throw new XsrUiThemeStyleException(entity, "A computed style conflicts with its host minimum/maximum bounds.");
        XsrUiStackPanel? sourceStack = _tree.GetComponent<XsrUiStackPanel>(entity);
        XsrUiStackPanel? stack = sourceStack is null ? null : new(style.Direction ?? sourceStack.Direction)
        {
            Spacing = style.Spacing ?? sourceStack.Spacing,
            StretchLastChild = sourceStack.StretchLastChild,
        };
        XsrUiVisualStyle sourceVisual = _tree.GetComponent<XsrUiVisualStyle>(entity) ?? new();
        XsrUiVisualStyle visual = new()
        {
            Background = style.Background ?? sourceVisual.Background,
            Foreground = style.Foreground ?? sourceVisual.Foreground,
            Border = style.Border ?? sourceVisual.Border,
            Hover = style.Hover ?? sourceVisual.Hover,
            Surface = style.Surface ?? sourceVisual.Surface,
            Opacity = style.Opacity ?? sourceVisual.Opacity,
            CornerRadius = style.CornerRadius ?? sourceVisual.CornerRadius,
            BorderWidth = style.BorderWidth ?? sourceVisual.BorderWidth,
            BlurRadius = style.BlurRadius ?? sourceVisual.BlurRadius,
            FontSize = style.FontSize ?? sourceVisual.FontSize,
            FontWeight = style.FontWeight ?? sourceVisual.FontWeight,
            TextAlignment = style.TextAlignment ?? sourceVisual.TextAlignment,
            WrapText = style.WrapText ?? sourceVisual.WrapText,
            HoverExpand = sourceVisual.HoverExpand,
            NavigationLayout = sourceVisual.NavigationLayout,
        };
        return new(key, style, element, stack, visual);
    }

    private void ValidateComputedStyle(XsrUiEntityId entity, XsrUiComputedStyle style)
    {
        foreach (double? value in new double?[] { style.Width, style.Height, style.MinWidth, style.MaxWidth,
            style.MinHeight, style.MaxHeight, style.Weight, style.Spacing, style.CornerRadius, style.BorderWidth,
            style.BlurRadius, style.FontSize, style.VirtualItemWidth, style.VirtualItemHeight, style.VirtualItemSpacing })
            if (value is { } number && (!double.IsFinite(number) || number < 0 || number > 1_000_000))
                throw new XsrUiThemeStyleException(entity, "A computed style dimension is outside the finite renderer budget.");
        foreach (double? value in new double?[] { style.Left, style.Top, style.Right, style.Bottom })
            if (value is { } number && (!double.IsFinite(number) || Math.Abs(number) > 1_000_000))
                throw new XsrUiThemeStyleException(entity, "A computed position is outside the finite renderer budget.");
        if (style.HorizontalAlignment is { } horizontal && !Enum.IsDefined(horizontal)
            || style.VerticalAlignment is { } vertical && !Enum.IsDefined(vertical)
            || style.Direction is { } direction && !Enum.IsDefined(direction)
            || style.Surface is { } surface && !Enum.IsDefined(surface)
            || style.TextAlignment is { } alignment && !Enum.IsDefined(alignment)
            || style.Position is { } position && !Enum.IsDefined(position))
            throw new XsrUiThemeStyleException(entity, "A computed style enum is outside its finite vocabulary.");
        if (style.MinWidth is { } styleMinWidth && style.MaxWidth is { } styleMaxWidth && styleMinWidth > styleMaxWidth
            || style.MinHeight is { } styleMinHeight && style.MaxHeight is { } styleMaxHeight && styleMinHeight > styleMaxHeight)
            throw new XsrUiThemeStyleException(entity, "A computed style minimum exceeds its maximum.");
        if (style.FontSize is < 1 or > 512)
            throw new XsrUiThemeStyleException(entity, "A computed font size exceeds the shared native layout budget.");
        if (style.FontFamily is { } font && (string.IsNullOrWhiteSpace(font) || font.Length > 4096)
            || style.ImageSource is { } source && (string.IsNullOrWhiteSpace(source) || source.Length > 4096))
            throw new XsrUiThemeStyleException(entity, "A computed resource identity is invalid.");
        if (style.Opacity is { } opacity && (!double.IsFinite(opacity) || opacity < 0 || opacity > 1)
            || style.FontWeight is { } weight && (!double.IsFinite(weight) || weight < 100 || weight > 900))
            throw new XsrUiThemeStyleException(entity, "A computed visual value is outside its range.");
        foreach (XsrUiThickness? box in new[] { style.Margin, style.Padding })
            if (box is { } thickness && new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom }
                .Any(value => !double.IsFinite(value) || Math.Abs(value) > 1_000_000))
                throw new XsrUiThemeStyleException(entity, "A computed box value exceeds its finite budget.");
        if (style.Padding is { } padding && (padding.Left < 0 || padding.Top < 0 || padding.Right < 0 || padding.Bottom < 0))
            throw new XsrUiThemeStyleException(entity, "A computed padding cannot be negative.");
        if (style.Shadow is { } shadow && (!double.IsFinite(shadow.OffsetX) || !double.IsFinite(shadow.OffsetY)
            || !double.IsFinite(shadow.BlurRadius) || shadow.BlurRadius < 0 || shadow.BlurRadius > 4096))
            throw new XsrUiThemeStyleException(entity, "A computed shadow exceeds its finite budget.");
        if (style.BackgroundGradient is { } gradient)
        {
            if (!double.IsFinite(gradient.Angle) || gradient.Stops.Count is < 2 or > 32)
                throw new XsrUiThemeStyleException(entity, "A computed gradient exceeds its finite budget.");
            double previous = -1;
            foreach (XsrUiGradientStop stop in gradient.Stops)
            {
                if (!double.IsFinite(stop.Position) || stop.Position < previous || stop.Position is < 0 or > 1)
                    throw new XsrUiThemeStyleException(entity, "A computed gradient stop is invalid.");
                previous = stop.Position;
            }
        }
        if (style.BackgroundImage is { } image && (string.IsNullOrWhiteSpace(image.ResourceId)
            || image.ResourceId.Length > 4096 || image.Fit is not ("cover" or "contain" or "stretch" or "tile")
            || !double.IsFinite(image.Opacity) || image.Opacity is < 0 or > 1))
            throw new XsrUiThemeStyleException(entity, "A computed background image is invalid.");
        if (style.Transform is { } transform && (!double.IsFinite(transform.TranslateX) || !double.IsFinite(transform.TranslateY)
            || !double.IsFinite(transform.ScaleX) || !double.IsFinite(transform.ScaleY) || !double.IsFinite(transform.Rotation)
            || transform.ScaleX <= 0 || transform.ScaleY <= 0 || transform.ScaleX > 100 || transform.ScaleY > 100))
            throw new XsrUiThemeStyleException(entity, "A computed transform must be finite and invertible.");
        if (style.Grid is { } grid)
        {
            if (grid.Columns.Count is < 1 or > 64 || grid.Rows?.Count > 4096 || grid.ColumnGap < 0 || grid.RowGap < 0
                || !double.IsFinite(grid.ColumnGap) || !double.IsFinite(grid.RowGap))
                throw new XsrUiThemeStyleException(entity, "A computed grid exceeds its track budget.");
            foreach (XsrUiGridTrack track in grid.Columns.Concat(grid.Rows ?? []))
                if (!Enum.IsDefined(track.Unit) || !double.IsFinite(track.Value) || track.Value < 0
                    || track.Unit == XsrUiGridUnit.Fraction && track.Value == 0)
                    throw new XsrUiThemeStyleException(entity, "A computed grid track is invalid.");
        }
        if (style.GridColumn is < 0 or > 63 || style.GridRow is < 0 or > 4095 || style.GridColumnSpan is < 1 or > 64 || style.GridRowSpan is < 1 or > 4096)
            throw new XsrUiThemeStyleException(entity, "A computed grid placement is invalid.");
        if (style.VirtualItemColumns is < 1 or > 64 || style.VirtualItemHeight is <= 0)
            throw new XsrUiThemeStyleException(entity, "A virtual metric is outside its finite budget.");
        XsrUiVirtualLayoutContract? virtualContract = _tree.GetComponent<XsrUiVirtualLayoutContract>(entity);
        if (virtualContract is null && (style.VirtualItemWidth is not null || style.VirtualItemHeight is not null
            || style.VirtualItemSpacing is not null || style.VirtualItemColumns is not null))
            throw new XsrUiThemeStyleException(entity, "Virtual item properties require a registered host layout contract.");
        if (style.VirtualItemColumns is { } columns && virtualContract is not null
            && (virtualContract.Kind == XsrUiVirtualLayoutKind.List && columns != 1 || virtualContract.Kind == XsrUiVirtualLayoutKind.Waterfall && columns > 12))
            throw new XsrUiThemeStyleException(entity, "A virtual column override exceeds the admitted host layout kind.");
        if (virtualContract is { SupportsThemeMetrics: false } && (style.VirtualItemWidth is not null
                || style.VirtualItemHeight is not null || style.VirtualItemSpacing is not null || style.VirtualItemColumns is not null))
            throw new XsrUiThemeStyleException(entity, "This host virtual layout has not admitted theme metrics.");
        if (style.Animations is { } animations)
        {
            if (animations.Count > 16) throw new XsrUiThemeStyleException(entity, "A node exceeds its style animation budget.");
            foreach (XsrUiStyleAnimation animation in animations)
            {
                if (!Enum.IsDefined(animation.Property) || animation.Duration <= TimeSpan.Zero
                    || animation.Duration > TimeSpan.FromHours(1) || animation.Keyframes.Count is < 2 or > 64)
                    throw new XsrUiThemeStyleException(entity, "A style animation is outside its finite budget.");
                double previous = -1;
                foreach (XsrUiStyleKeyframe frame in animation.Keyframes)
                {
                    if (!double.IsFinite(frame.Position) || frame.Position < 0 || frame.Position > 1 || frame.Position <= previous
                        || !double.IsFinite(frame.Value) || Math.Abs(frame.Value) > 1_000_000
                        || animation.Property == XsrUiStyleAnimationProperty.Opacity && frame.Value is < 0 or > 1
                        || animation.Property is XsrUiStyleAnimationProperty.ScaleX or XsrUiStyleAnimationProperty.ScaleY
                            && frame.Value is <= 0 or > 100
                        || animation.Property is XsrUiStyleAnimationProperty.CornerRadius or XsrUiStyleAnimationProperty.BlurRadius
                            && frame.Value is < 0 or > 4096) throw new XsrUiThemeStyleException(entity, "A style keyframe is invalid.");
                    previous = frame.Position;
                }
                if (animation.Keyframes[0].Position != 0 || animation.Keyframes[^1].Position != 1)
                    throw new XsrUiThemeStyleException(entity, "A style animation must cover its complete interval.");
            }
        }
    }

    private XsrUiComputedStyle ApplyHostStylePolicy(XsrUiEntityId entity, XsrUiComputedStyle style)
    {
        if (_tree.GetComponent<XsrUiVirtualLayoutContract>(entity) is null
            && _tree.GetComponent<XsrUiVirtualGeometry>(entity) is null) return style;
        // Host virtual extents and anchors have higher priority; validate the raw program first.
        IReadOnlyList<XsrUiStyleAnimation>? animations = style.Animations;
        if (animations?.Any(animation => IsStyleGeometryAnimation(animation.Property)) == true)
            animations = animations.Where(animation => !IsStyleGeometryAnimation(animation.Property)).ToArray();
        return style with
        {
            Width = null,
            Height = null,
            MinWidth = null,
            MaxWidth = null,
            MinHeight = null,
            MaxHeight = null,
            Weight = null,
            Margin = null,
            Padding = null,
            HorizontalAlignment = null,
            VerticalAlignment = null,
            Direction = null,
            Spacing = null,
            Grid = null,
            Order = null,
            Position = null,
            Left = null,
            Top = null,
            Right = null,
            Bottom = null,
            GridColumn = null,
            GridRow = null,
            GridColumnSpan = null,
            GridRowSpan = null,
            Transform = null,
            Animations = animations,
        };
    }

    private static bool IsStyleGeometryAnimation(XsrUiStyleAnimationProperty property) =>
        property is XsrUiStyleAnimationProperty.TranslateX or XsrUiStyleAnimationProperty.TranslateY
            or XsrUiStyleAnimationProperty.ScaleX or XsrUiStyleAnimationProperty.ScaleY or XsrUiStyleAnimationProperty.Rotation;

    private sealed record ThemeStyleEntry(ThemeStyleKey Key, XsrUiComputedStyle Style,
        XsrUiElement Element, XsrUiStackPanel? Stack, XsrUiVisualStyle Visual);
    private readonly record struct ThemeElementFacts(double? Width, double? Height, double? MinWidth,
        double? MaxWidth, double? MinHeight, double? MaxHeight, double Weight, XsrUiThickness Margin,
        XsrUiThickness Padding, XsrUiAlignment HorizontalAlignment, XsrUiAlignment VerticalAlignment);
    private readonly record struct ThemeStyleKey(long Revision, XsrUiThemeEnvironment Environment,
        XsrUiSize Viewport, XsrUiColorScheme ColorScheme, long FontRevision, XsrUiStyleIdentity Identity, int ParentSignature,
        XsrUiThemeInteractionState State, ThemeElementFacts Element, XsrUiOrientation? Direction,
        double Spacing, bool StretchLastChild, XsrUiVisualStyleSnapshot Visual);
}
