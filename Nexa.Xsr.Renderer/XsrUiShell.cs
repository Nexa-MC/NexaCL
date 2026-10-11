using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer;

/// <summary>
/// Selects the supported product shell presentation. Additional styles are deferred.
/// </summary>
public enum XsrUiShellStyle
{
    Experimental = 0,
}

/// <summary>
/// One stable destination in the product's primary navigation.
/// </summary>
public sealed class XsrUiShellNavigationItem
{
    public XsrUiShellNavigationItem(string id, string label, string icon)
        : this(XsrSemanticId.Parse(id), label, icon)
    {
    }

    public XsrUiShellNavigationItem(
        XsrSemanticId id,
        string label,
        string icon,
        XsrSemanticId? command = null)
    {
        if (!id.IsAssigned)
        {
            throw new ArgumentException("A navigation item requires an assigned semantic ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        if (command is { IsAssigned: false })
        {
            throw new ArgumentException("A navigation command must be assigned.", nameof(command));
        }

        Id = id;
        Label = label;
        Icon = icon;
        Command = command ?? XsrSemanticId.Parse($"ui.{id.Value}");
    }

    public XsrSemanticId Id { get; }

    public string Label { get; }

    public string Icon { get; }

    public XsrSemanticId Command { get; }
}

/// <summary>
/// Options for the framework-neutral shell composition.
/// </summary>
public sealed class XsrUiShellOptions
{
    public XsrUiShellStyle Style { get; init; } = XsrUiShellStyle.Experimental;

    public string Title { get; init; } = "NexaCL";

    public string Version { get; init; } = "2.0.0.alpha.6";

    public IReadOnlyList<XsrUiShellNavigationItem>? NavigationItems { get; init; }

    public XsrSemanticId? InitialNavigationId { get; init; }
}

/// <summary>
/// Backend-neutral material tokens for the product shell.
/// </summary>
public readonly record struct XsrUiShellPalette(
    XsrUiColor WindowBackground,
    XsrUiColor TitleBarBackground,
    XsrUiColor TitleBarText,
    XsrUiColor NavigationBackground,
    XsrUiColor ContentBackground,
    XsrUiColor SurfaceBorder,
    XsrUiColor PrimaryText,
    XsrUiColor SecondaryText,
    XsrUiColor Accent,
    XsrUiColor NavigationHover,
    XsrUiColor SelectedNavigationText,
    XsrUiColor NavigationIcon,
    XsrUiSurfaceKind TitleBarSurface,
    XsrUiSurfaceKind NavigationSurface,
    XsrUiSurfaceKind ContentSurface,
    double CornerRadius,
    double BlurRadius,
    double BorderWidth)
{
    public static XsrUiShellPalette For(XsrUiShellStyle style) => style switch
    {
        // The Experimental style mirrors the legacy experimental base plate: a solid accent title
        // bar with white text over a light window, an icon rail, and a soft accent hover tint.
        XsrUiShellStyle.Experimental => new(
            WindowBackground: new XsrUiColor(251, 251, 251),
            TitleBarBackground: new XsrUiColor(19, 112, 243),
            TitleBarText: new XsrUiColor(255, 255, 255),
            NavigationBackground: new XsrUiColor(243, 247, 252),
            ContentBackground: new XsrUiColor(251, 251, 251),
            SurfaceBorder: new XsrUiColor(224, 234, 253),
            PrimaryText: new XsrUiColor(52, 61, 74),
            SecondaryText: new XsrUiColor(122, 138, 153),
            Accent: new XsrUiColor(19, 112, 243),
            NavigationHover: new XsrUiColor(213, 230, 253),
            SelectedNavigationText: new XsrUiColor(11, 91, 203),
            NavigationIcon: new XsrUiColor(52, 61, 74),
            TitleBarSurface: XsrUiSurfaceKind.Solid,
            NavigationSurface: XsrUiSurfaceKind.Solid,
            ContentSurface: XsrUiSurfaceKind.Solid,
            CornerRadius: XsrUiCornerRadii.Surface,
            BlurRadius: 0,
            BorderWidth: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown shell style."),
    };
}

/// <summary>
/// Stable semantic IDs used by the shell itself.
/// </summary>
public static class XsrUiShellIds
{
    public static readonly XsrSemanticId NavigationSelect = XsrSemanticId.Parse("ui.navigation.select");

    public static readonly XsrSemanticId NavigationExpand = XsrSemanticId.Parse("ui.navigation.expand");

    public static readonly XsrSemanticId WindowMinimize = XsrSemanticId.Parse("ui.window.minimize");

    public static readonly XsrSemanticId WindowMaximize = XsrSemanticId.Parse("ui.window.maximize");

    public static readonly XsrSemanticId WindowClose = XsrSemanticId.Parse("ui.window.close");
}

/// <summary>
/// Event data for a primary-navigation selection change.
/// </summary>
public sealed class XsrUiShellNavigationChangedEventArgs(
    XsrSemanticId previous,
    XsrSemanticId current) : EventArgs
{
    public XsrSemanticId Previous { get; } = previous;

    public XsrSemanticId Current { get; } = current;
}

/// <summary>
/// Handles returned by a PXML shell template. The template describes structure; XsrUiShell adds
/// selection, palette, and intent behavior without coupling UI.Next to the PXML compiler.
/// </summary>
public sealed class XsrUiShellTemplate
{
    public XsrUiShellTemplate(
        XsrUiTree tree,
        XsrUiEntityId root,
        XsrUiEntityId titleBar,
        XsrUiEntityId body,
        XsrUiEntityId navigation,
        XsrUiEntityId content,
        IReadOnlyList<XsrUiShellNavigationItem> navigationItems,
        IReadOnlyDictionary<XsrSemanticId, XsrUiEntityId> navigationEntities)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(navigationItems);
        ArgumentNullException.ThrowIfNull(navigationEntities);
        if (!tree.IsAlive(root)
            || !tree.IsAlive(titleBar)
            || !tree.IsAlive(body)
            || !tree.IsAlive(navigation)
            || !tree.IsAlive(content))
        {
            throw new ArgumentException("A shell template can contain only live entities.", nameof(tree));
        }

        if (!tree.Parent(titleBar).Equals(root)
            || !tree.Parent(body).Equals(root)
            || !tree.Parent(navigation).Equals(body)
            || !tree.Parent(content).Equals(body))
        {
            throw new ArgumentException("A shell template has an invalid chrome hierarchy.", nameof(tree));
        }

        if (navigationItems.Count == 0 || navigationItems.Count != navigationEntities.Count)
        {
            throw new ArgumentException("A shell template requires one entity for every navigation item.", nameof(navigationItems));
        }

        Tree = tree;
        Root = root;
        TitleBar = titleBar;
        Body = body;
        Navigation = navigation;
        Content = content;
        NavigationItems = [.. navigationItems];
        NavigationEntities = new Dictionary<XsrSemanticId, XsrUiEntityId>(navigationEntities);
        foreach (XsrUiShellNavigationItem item in NavigationItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!NavigationEntities.TryGetValue(item.Id, out XsrUiEntityId entity)
                || !tree.IsAlive(entity)
                || !tree.Parent(entity).Equals(navigation))
            {
                throw new ArgumentException(
                    $"Navigation item '{item.Id}' is not attached to the template navigation rail.",
                    nameof(navigationEntities));
            }
        }
    }

    public XsrUiTree Tree { get; }

    public XsrUiEntityId Root { get; }

    public XsrUiEntityId TitleBar { get; }

    public XsrUiEntityId Body { get; }

    public XsrUiEntityId Navigation { get; }

    public XsrUiEntityId Content { get; }

    public IReadOnlyList<XsrUiShellNavigationItem> NavigationItems { get; }

    public IReadOnlyDictionary<XsrSemanticId, XsrUiEntityId> NavigationEntities { get; }
}

/// <summary>
/// Shared product chrome: title bar, primary navigation, and content host. The shell is a
/// framework-neutral UI.Next tree so tests and non-Avalonia hosts see the same semantics and
/// layout. Avalonia is only a presentation edge over this contract.
/// </summary>
public sealed partial class XsrUiShell
{
    /// <summary>The canonical title bar height in logical pixels.</summary>
    public const double TitleBarHeight = 52;

    /// <summary>The collapsed icon-rail width in logical pixels.</summary>
    public const double CollapsedRailWidth = 48;

    /// <summary>The expanded rail width in logical pixels.</summary>
    public const double ExpandedRailWidth = 120;

    /// <summary>The canonical navigation item height in logical pixels.</summary>
    public const double NavigationItemHeight = 42;

    private static readonly XsrUiShellNavigationItem[] BuiltInNavigationItems =
    [
        new("navigation.launch", "启动", "lucide/play"),
        new("navigation.download", "安装", "lucide/package-plus"),
        new("navigation.community", "资源", "lucide/blocks"),
        new("navigation.settings", "设置", "lucide/settings"),
    ];

    public static IReadOnlyList<XsrUiShellNavigationItem> DefaultNavigationItems =>
        [.. BuiltInNavigationItems];

    private readonly Dictionary<XsrSemanticId, XsrUiEntityId> _navigationEntities = [];
    private readonly Dictionary<XsrUiEntityId, XsrSemanticId> _navigationIds = [];
    private readonly IXsrUiIntentSink? _externalIntentSink;
    private XsrUiEntityId _navigationToggle;

    private readonly XsrStateStore _windowStateStore;
    private double _trafficLightPadding, _windowContentInset;
    public void PublishWindowActivity(bool active, bool minimized)
    {
        if (!_windowStateStore.TryResolve(XsrUiShellWindowState.Activity, out var id)) return;
        XsrUiWindowActivity activity = new(active, minimized);
        if (_windowStateStore.Read<XsrUiWindowActivity>(id).Value != activity) _windowStateStore.Publish(id, activity);
    }
    public void PublishWindowMetrics(double trafficLightPadding, bool fullscreen, double contentInset = 0)
    {
        double padding = fullscreen ? 0 : Math.Clamp(trafficLightPadding, 0, 300);
        if (_windowStateStore.TryResolve(XsrUiShellWindowState.TrafficLightPadding, out var safeArea)
            && _windowStateStore.Read<double>(safeArea).Value != padding)
            _windowStateStore.Publish(safeArea, padding);
        if (_windowStateStore.TryResolve(XsrUiShellWindowState.Fullscreen, out var full)
            && _windowStateStore.Read<bool>(full).Value != fullscreen)
            _windowStateStore.Publish(full, fullscreen);
        double inset = fullscreen ? 0 : Math.Clamp(contentInset, 0, 32);
        if (_windowStateStore.TryResolve(XsrUiShellWindowState.ContentInset, out var insetState)
            && _windowStateStore.Read<double>(insetState).Value != inset)
            _windowStateStore.Publish(insetState, inset);
        if (_windowContentInset != inset)
        {
            _windowContentInset = inset;
            Tree.GetComponent<XsrUiElement>(Root)!.Padding = XsrUiThickness.Uniform(inset);
            Tree.MarkDirty(Root, XsrUiDirtyKinds.Layout);
        }
        if (_trafficLightPadding == padding) return;
        _trafficLightPadding = padding;
        var layout = Tree.GetComponent<XsrUiElement>(TitleBar)!;
        layout.Padding = new(padding, layout.Padding.Top, layout.Padding.Right, layout.Padding.Bottom);
        Tree.MarkDirty(TitleBar, XsrUiDirtyKinds.Layout);
    }

    public event EventHandler<XsrUiShellNavigationChangedEventArgs>? NavigationChanged;

    public event EventHandler? NavigationExpandedChanged;

    public event EventHandler? StyleChanged;

    public XsrUiTree Tree { get; }

    /// <summary>
    /// The optional host-store observer bound to this shell's render tree. A native backend uses
    /// its render request signal only to schedule a frame; the renderer remains the sole drain
    /// point.
    /// </summary>
    public XsrUiStateBridge? StateBridge { get; }

    public XsrUiStage Stage { get; }

    public XsrUiRenderer Renderer => Stage.Renderer;

    public XsrUiEntityId Root { get; }

    public XsrUiEntityId TitleBar { get; }

    public XsrUiEntityId Body { get; }

    public XsrUiEntityId Navigation { get; }

    public XsrUiEntityId Content { get; }

    /// <summary>The shell-owned rail expand/collapse affordance.</summary>
    public XsrUiEntityId NavigationToggle => _navigationToggle;

    public IReadOnlyList<XsrUiShellNavigationItem> NavigationItems { get; }

    public string Title { get; }

    public string Version { get; }

    public IReadOnlyDictionary<XsrSemanticId, XsrUiEntityId> NavigationEntities => _navigationEntities;

    public XsrSemanticId SelectedNavigationId { get; private set; }

    public XsrUiShellStyle Style { get; private set; }

    public XsrUiShellPalette Palette { get; private set; }

    /// <summary>Whether the navigation rail currently shows item labels beside the icons.</summary>
    public bool IsNavigationExpanded { get; private set; }

    /// <summary>
    /// Changes the presentation palette while preserving the semantic tree and current route.
    /// </summary>
    public void SetStyle(XsrUiShellStyle style)
    {
        if (Style == style)
        {
            return;
        }

        XsrUiShellPalette palette = XsrUiShellPalette.For(style);
        Style = style;
        Palette = palette;
        ApplyPalette();
        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects one primary navigation destination. Returns false for an unknown ID.
    /// </summary>
    public bool Select(XsrSemanticId id)
    {
        if (!_navigationEntities.TryGetValue(id, out XsrUiEntityId entity))
        {
            return false;
        }

        XsrSemanticId previous = SelectedNavigationId;
        if (previous == id)
        {
            return true;
        }

        if (_navigationEntities.TryGetValue(previous, out XsrUiEntityId previousEntity))
        {
            SetSelection(previousEntity, selected: false);
        }

        SetSelection(entity, selected: true);
        SelectedNavigationId = id;
        NavigationChanged?.Invoke(
            this,
            new XsrUiShellNavigationChangedEventArgs(previous, id));
        return true;
    }

    /// <summary>
    /// Selects a navigation destination by its renderer entity handle.
    /// </summary>
    public bool Select(XsrUiEntityId entity) =>
        _navigationIds.TryGetValue(entity, out XsrSemanticId id) && Select(id);

    /// <summary>
    /// Expands or collapses the navigation rail. Expansion is ephemeral presentation mechanics
    /// owned by the shell; it never becomes product state.
    /// </summary>
    public void SetNavigationExpanded(bool expanded)
    {
        if (IsNavigationExpanded == expanded)
        {
            return;
        }

        IsNavigationExpanded = expanded;
        if (Renderer.EffectiveReducedMotion)
        {
            // Reduced motion is a presentation contract: skip the geometry interpolation and
            // commit the final rail width immediately.
            SetRailPresentationProgress(expanded ? 1 : 0);
        }

        ApplyNavigationExpansion();
        NavigationExpandedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Flips the navigation rail between its icon rail and expanded forms.</summary>
    public void ToggleNavigationExpanded() => SetNavigationExpanded(!IsNavigationExpanded);

    /// <summary>
    /// The ephemeral presentation progress of the rail: 0 is fully collapsed, 1 fully expanded.
    /// This is renderer-local presentation state, not product truth — the semantic target is
    /// <see cref="IsNavigationExpanded"/>. The committed rail geometry derives from it, so the
    /// scene, the hit test, and the drawn frame always share one geometry while it plays.
    /// </summary>
    public double RailPresentationProgress { get; private set; }

    /// <summary>
    /// Sets the rail presentation progress and re-commits the rail geometry. Presentation
    /// drivers (the backend motion clock) call this once per frame; it is a no-op at rest.
    /// </summary>
    public void SetRailPresentationProgress(double value)
    {
        double clamped = Math.Clamp(value, 0, 1);
        if (RailPresentationProgress == clamped)
        {
            return;
        }

        RailPresentationProgress = clamped;
        ApplyRailPresentation();
    }

    /// <summary>
    /// Maps presented spring progress linearly to committed geometry. Easing belongs to the
    /// presentation clock, so applying another curve here would distort velocity on reversal.
    /// </summary>
    public static double RailWidthFor(double progress)
    {
        double clamped = Math.Clamp(progress, 0, 1);
        return CollapsedRailWidth + ((ExpandedRailWidth - CollapsedRailWidth) * clamped);
    }

    /// <summary>
    /// Runs the deterministic UI.Next layout pass for one viewport.
    /// </summary>
    public XsrUiScene Render(XsrUiSize viewport)
    {
        // Navigation fades as one prepared page in the backend. Do not manufacture
        // per-leaf motion: it delays controls and reanimates lists when revisiting them.
        Renderer.Viewport = viewport;
        return Renderer.Render();
    }

    /// <summary>Insets the first rail item from the title bar, mirroring the legacy rail.</summary>
    public const double RailTopInset = 10;

    /// <summary>Insets the rail toggle from the bottom edge.</summary>
    public const double RailBottomInset = 12;

    /// <summary>The collapse label shown beside the rail toggle while expanded.</summary>
    public const string NavigationToggleCollapseLabel = "收起";

    /// <summary>The title-bar product-title text size in logical pixels.</summary>
    public const double TitleFontSize = 17;

    /// <summary>Secondary title-bar text size (for example a version label).</summary>
    public const double TitleSecondaryFontSize = 12;

    /// <summary>The title-bar product-title weight on the 100..900 scale.</summary>
    public const double TitleFontWeight = 600;

    private sealed class ShellIntentSink(XsrUiShell owner) : IXsrUiIntentSink
    {
        public void Emit(XsrSemanticId command, XsrUiEntityId source, XsrCorrelationId correlationId)
        {
            _ = owner.Select(source);
            if (command == XsrUiShellIds.NavigationExpand)
            {
                owner.ToggleNavigationExpanded();
            }

            owner._externalIntentSink?.Emit(command, source, correlationId);
        }
    }
}

/// <summary>
/// Convenience entry point for composition roots that want the default shell contract.
/// </summary>
public static class XsrUiShellComposer
{
    public static XsrUiShell Compose(
        XsrStateStore state,
        XsrUiShellOptions? options = null,
        IXsrUiIntentSink? intentSink = null,
        XsrUiStateBridge? stateBridge = null) =>
        new(state, options, intentSink, stateBridge);

    public static XsrUiShell Compose(
        XsrStateStore state,
        XsrUiShellTemplate template,
        XsrUiShellOptions? options = null,
        IXsrUiIntentSink? intentSink = null,
        XsrUiStateBridge? stateBridge = null) =>
        new(state, template, options, intentSink, stateBridge);
}
