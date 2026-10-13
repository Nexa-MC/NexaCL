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

internal sealed partial class AvaloniaUiSceneNodeControl : Control
{
    private const double CollapsedRailCenteringWidth = 50;
    private const double PillWidth = 5;
    private const double PillHeight = 20;
    private const double NavigationIconSize = 20;
    private const double NavigationIconTextGap = 8;
    private const double CapsuleCaptionIconGap = 6;

    /// <summary>
    /// Expanded rail rows keep the icon at the collapsed rail's centered position, so expanding
    /// never moves it sideways; the icon offset also clears the selection pill.
    /// </summary>
    private const double RailRowIconOffset = 14;

    private static readonly StyledProperty<double> HoverOpacityProperty =
        AvaloniaProperty.Register<AvaloniaUiSceneNodeControl, double>(nameof(HoverOpacity));

    private static readonly StyledProperty<double> PillScaleProperty =
        AvaloniaProperty.Register<AvaloniaUiSceneNodeControl, double>(nameof(PillScale));

    private static readonly StyledProperty<double> EnterProgressProperty =
        AvaloniaProperty.Register<AvaloniaUiSceneNodeControl, double>(
            nameof(EnterProgress),
            defaultValue: 1);

    private static readonly StyledProperty<double> OverlayProgressProperty =
        AvaloniaProperty.Register<AvaloniaUiSceneNodeControl, double>(
            nameof(OverlayProgress),
            defaultValue: 1);

    private readonly Action<XsrUiEntityId> _focusFromAutomation;
    private readonly Action<XsrUiEntityId> _invokeFromAutomation;
    private readonly Func<bool> _reducedMotion;
    private readonly ScaleTransform _pressScale = new(1, 1);
    private readonly ScaleTransform _enterScale = new(1, 1);
    private readonly ScaleTransform _overlayScale = new(1, 1);
    private readonly TranslateTransform _overlayTranslate = new();
    private readonly TranslateTransform _reflowTranslate = new();
    private readonly List<AvaloniaUiSceneNodeControl> _selectionItems = [];
    private XsrUiSceneNode _node;
    private AvaloniaUiSceneNodeControl? _selectionContainer;
    private bool _applied;
    private Rect? _localClip;
    internal int AppliedChanges { get; private set; }

    internal AvaloniaUiSceneNodeControl(
        Action<XsrUiEntityId> focusFromAutomation,
        Action<XsrUiEntityId> invokeFromAutomation,
        Func<bool> reducedMotion,
        AvaloniaUiTextInputActions? textInputActions = null,
        AvaloniaUiRasterPool? rasterPool = null)
    {
        _focusFromAutomation = focusFromAutomation ?? throw new ArgumentNullException(nameof(focusFromAutomation));
        _invokeFromAutomation = invokeFromAutomation ?? throw new ArgumentNullException(nameof(invokeFromAutomation));
        _reducedMotion = reducedMotion ?? throw new ArgumentNullException(nameof(reducedMotion));
        _rasterPool = rasterPool ?? AvaloniaUiRasterPool.Shared;
        IsHitTestVisible = false;
        UseLayoutRounding = false;
        FocusAdorner = null;
        ClipToBounds = true;
        RenderTransform = new TransformGroup
        {
            Children = { _overlayScale, _enterScale, _pressScale, _overlayTranslate, _reflowTranslate },
        };
        RenderTransformOrigin = RelativePoint.Center;
        InitializeTextInput(textInputActions);
    }

    private double HoverOpacity
    {
        get => GetValue(HoverOpacityProperty);
        set => SetValue(HoverOpacityProperty, value);
    }

    private double PillScale
    {
        get => GetValue(PillScaleProperty);
        set => SetValue(PillScaleProperty, value);
    }

    private double EnterProgress
    {
        get => GetValue(EnterProgressProperty);
        set => SetValue(EnterProgressProperty, value);
    }

    private double OverlayProgress
    {
        get => GetValue(OverlayProgressProperty);
        set => SetValue(OverlayProgressProperty, value);
    }

    internal double PresentedHoverOpacity => HoverOpacity;

    internal double PresentedPillScale => PillScale;

    internal double PresentedOverlayProgress => OverlayProgress;

    internal double PresentedOverlayTranslationY => _overlayTranslate.Y;

    internal double PresentedEnterProgress => EnterProgress;

    internal double PresentedPillExpand => _node.CapsuleExpansionProgress;

    internal XsrUiSceneNode Node => _node;

    internal AvaloniaUiSceneNodeControl? SelectionContainer => _selectionContainer;

    internal IReadOnlyList<AvaloniaUiSceneNodeControl> SelectionItems => _selectionItems;

    private bool _lastReducedMotion;

    public void Apply(XsrUiSceneNode node, XsrUiSize? viewport = null)
    {
        bool reducedMotion = _reducedMotion();
        bool motionPolicyChanged = _lastReducedMotion != reducedMotion;
        if (_applied && _node == node && _rasterViewport == viewport && !_presentationReleased && !motionPolicyChanged)
        {
            if (_rasterCapacityBlocked) UpdateRaster(node.RasterImage, node.Rect.Width, node.Rect.Height);
            return;
        }
        _presentationReleased = false;
        _lastReducedMotion = reducedMotion;
        _rasterViewport = viewport;
        XsrUiSceneNode previous = _node;
        _node = node;
        ApplyToggle(previous, node);
        Opacity = node.PresentationOpacity;
        UpdateRaster(node.RasterImage, node.Rect.Width, node.Rect.Height);
        UpdateTextInput(previous.TextInput, node.TextInput);
        if (previous.IsFocused != node.IsFocused) ResetCaret();
        IsEnabled = node.IsEnabled;
        Focusable = node.IsFocusable;
        Rect? localClip = node.ClipRect is { } clip
            ? new Rect(clip.X - node.Rect.X, clip.Y - node.Rect.Y, clip.Width, clip.Height) : null;
        if (!_applied || localClip != _localClip)
        {
            _localClip = localClip;
            Clip = localClip is { } rectangle ? new RectangleGeometry(rectangle) : null;
        }
        string name = node.Label ?? node.Text ?? node.Role.ToString();
        string previousName = previous.Label ?? previous.Text ?? previous.Role.ToString();
        if (!_applied || name != previousName || node.Entity != previous.Entity || node.Role != previous.Role
            || node.IsClickable != previous.IsClickable || node.IsSelected != previous.IsSelected
            || node.LiveSetting != previous.LiveSetting || node.IsAccessible != previous.IsAccessible
            || node.IsFocusable != previous.IsFocusable)
        {
            AutomationProperties.SetName(this, name);
            AutomationProperties.SetAutomationId(this, string.Create(
                CultureInfo.InvariantCulture,
                $"xsr-{node.Entity.Index}-{node.Entity.Generation}"));
            AutomationProperties.SetControlTypeOverride(this, ControlTypeFor(node.Role, node.IsClickable));
            AutomationProperties.SetHelpText(this, node.IsSelected ? "selected" : node.Role.ToString());
            AutomationProperties.SetItemStatus(this, node.Role == XsrUiSemanticRole.Status
                ? node.Label ?? node.Text ?? "Status"
                : string.Empty);
            AutomationProperties.SetLiveSetting(this, node.LiveSetting switch
            {
                XsrUiLiveSetting.Polite => AutomationLiveSetting.Polite,
                XsrUiLiveSetting.Assertive => AutomationLiveSetting.Assertive,
                _ => AutomationLiveSetting.Off,
            });
            AutomationProperties.SetIsControlElementOverride(this,
                node.IsAccessible && (node.HasRole || node.IsFocusable || node.IsClickable));
            AutomationProperties.SetAccessibilityView(this,
                node.IsAccessible && (node.HasRole || node.IsFocusable || node.IsClickable)
                    ? AccessibilityView.Content : AccessibilityView.Raw);
        }
        if (_applied && name != previousName && ControlAutomationPeer.FromElement(this) is { } namePeer)
            namePeer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previousName, name);

        if (!_applied || previous.IsSelected != node.IsSelected)
        {
            if (node.Role == XsrUiSemanticRole.NavigationItem)
            {
                AnimateFact(
                    PillScaleProperty,
                    node.IsSelected ? 1 : 0,
                    node.IsSelected
                        ? AvaloniaMotionTokens.SelectionInMilliseconds
                        : AvaloniaMotionTokens.SelectionOutMilliseconds,
                    node.IsSelected ? null : AvaloniaUiMotion.EaseIn);
            }

            if (ControlAutomationPeer.FromElement(this) is { } peer)
            {
                peer.RaisePropertyChangedEvent(
                    SelectionItemPatternIdentifiers.IsSelectedProperty,
                    previous.IsSelected,
                    node.IsSelected);
            }
        }

        if (!_applied || previous.IsHovered != node.IsHovered || previous.IsFocusVisible != node.IsFocusVisible
            || previous.IsEnabled != node.IsEnabled)
        {
            if (!node.IsEnabled)
            {
                // Disabled nodes never expand or light up.
                AnimateFact(HoverOpacityProperty, 0, AvaloniaMotionTokens.HoverOutMilliseconds);
            }
            else if (node.VisualStyle.HoverExpand)
            {
                // Expansion geometry comes from the scene, never a second paint-only width.
                AnimateFact(
                    HoverOpacityProperty,
                    0,
                    AvaloniaMotionTokens.HoverOutMilliseconds);
            }
            else
            {
                AnimateFact(
                    HoverOpacityProperty,
                    node.IsHovered ? 1 : 0,
                    node.IsHovered
                        ? AvaloniaMotionTokens.HoverMilliseconds
                        : AvaloniaMotionTokens.HoverOutMilliseconds);
            }
        }

        if (!_applied || previous.IsPressed != node.IsPressed || previous.IsEnabled != node.IsEnabled
            || previous.Rect.Width != node.Rect.Width || previous.Rect.Height != node.Rect.Height || motionPolicyChanged)
        {
            double scale = InteractionScale(node, _reducedMotion());
            SetPressScale(scale);
        }

        ApplyOverlayPresentation(previous, node);

        bool visualChanged = !_applied || (previous with
        { Label = node.Label, Depth = node.Depth, LiveSetting = node.LiveSetting, IsAccessible = node.IsAccessible }) != node;
        _applied = true;
        AppliedChanges++;
        if (visualChanged) InvalidateVisual();
    }

    internal void ResetSelectionRelationships()
    {
        _selectionContainer = null;
        _selectionItems.Clear();
    }

    internal void SetSelectionContainer(AvaloniaUiSceneNodeControl selectionContainer)
    {
        _selectionContainer = selectionContainer ?? throw new ArgumentNullException(nameof(selectionContainer));
    }

    internal void AddSelectionItem(AvaloniaUiSceneNodeControl item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _selectionItems.Add(item);
    }

    internal void FocusFromAutomation() => _focusFromAutomation(Node.Entity);

    internal void InvokeFromAutomation()
    {
        if (Node.IsEnabled && Node.IsClickable) _invokeFromAutomation(Node.Entity);
    }
}
