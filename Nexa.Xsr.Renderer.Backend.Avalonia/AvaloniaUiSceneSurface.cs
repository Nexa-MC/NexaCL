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

/// <summary>
/// The Avalonia backend commit surface for one immutable UI.Next scene. It never reads UI tree
/// components or rebuilds shell layout: PXML/UI.Next own structure and geometry, while this
/// class owns final drawing, native input translation, and native accessibility properties.
/// Node-local presentation motion (hover, press, selection) animates between committed scene
/// facts from the currently presented value; structural geometry such as the rail expansion is
/// animated inside UI.Next itself, so the scene, the hit test, and the drawn frame always share
/// one geometry.
/// </summary>
public sealed partial class AvaloniaUiSceneSurface : Panel, IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly object _commitGate = new();
    private readonly Dictionary<XsrUiEntityId, AvaloniaUiSceneNodeControl> _controls = [];
    private readonly Dictionary<XsrUiEntityId, XsrUiSceneNode> _sceneNodes = [];
    private readonly Dictionary<Control, int> _childIndexes = [];
    private readonly List<AvaloniaUiSceneNodeControl> _outgoingControls = [];
    private readonly HashSet<XsrUiEntityId> _currentEntities = [];
    private readonly List<XsrUiEntityId> _retiredEntities = [];
    private readonly Dictionary<XsrUiEntityId, double> _capsuleTargets = [];
    private readonly Dictionary<XsrUiEntityId, double> _progressTargets = [];
    private readonly Dictionary<XsrUiEntityId, long> _pagerRevisions = [];
    internal XsrUiScene? _scene;

    /// <summary>The scene currently committed to this surface, for window-level overlays.</summary>
    internal XsrUiScene? DebugScene => _scene;

    private XsrUiEntityId _lastPageRoot;
    private XsrSemanticId _lastNavigation;
    private readonly Dictionary<XsrUiEntityId, string> _transitionKeys = [];
    private readonly Cursor _handCursor = new(StandardCursorType.Hand);
    private readonly Cursor _textCursor = new(StandardCursorType.Ibeam);
    private bool _commitQueued;
    private bool _applyingScene;
    private bool _disposed;
    private bool _rasterPresentationEnabled = true;
    private bool _initialFocusAssigned;
    private bool _pointerInside;
    private XsrUiPoint _lastPointerPoint;
    private XsrUiPointerCursor _presentedPointerCursor;

    public AvaloniaUiSceneSurface(XsrUiShell shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        UseLayoutRounding = false;
        Focusable = true;
        FocusAdorner = null;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        AutomationProperties.SetName(this, shell.Title);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Control);
        // A transparent brush makes the surface itself hit-testable. Without it the panel never
        // receives pointers (its scene-node children are deliberately not hit-testable), and
        // every click, hover, drag, and double-click would fall through to the window.
        Background = Brushes.Transparent;
        ClipToBounds = true;
        _shell.Tree.RenderInvalidated += OnTreeRenderInvalidated;
        _shell.StateBridge?.RenderRequested += OnStateRenderRequested;
        _shell.NavigationExpandedChanged += OnNavigationExpandedChanged;
        SizeChanged += (_, _) => RequestCommit();
        AttachedToVisualTree += (_, _) => RequestCommit();
    }

    /// <summary>The last immutable scene committed to native drawing controls.</summary>
    public XsrUiScene? Scene => _scene;

    internal XsrUiPointerCursor PresentedPointerCursor => _presentedPointerCursor;

    internal void SetRasterPresentationEnabled(bool enabled)
    {
        if (_disposed || _rasterPresentationEnabled == enabled) return;
        _rasterPresentationEnabled = enabled;
        foreach (AvaloniaUiSceneNodeControl control in _controls.Values) control.SetRasterPresentationEnabled(enabled);
        foreach (AvaloniaUiSceneNodeControl control in _outgoingControls) control.SetRasterPresentationEnabled(enabled);
        if (!enabled) AvaloniaUiRasterPool.Shared.TrimIdle();
    }

    internal bool TryGetPresentedEnterProgress(XsrUiEntityId entity, out double value)
    {
        value = 1;
        return _controls.TryGetValue(entity, out AvaloniaUiSceneNodeControl? control)
            && (value = control.PresentedEnterProgress) is >= 0 and <= 1;
    }

    /// <summary>
    /// Raised when a pointer press lands on the PXML title-bar surface rather than a clickable
    /// entity. The Window translates it into its native drag operation.
    /// </summary>
    public event EventHandler<PointerPressedEventArgs>? TitleBarDragRequested;

    /// <summary>
    /// Raised after an immutable UI.Next scene has been committed to this backend surface. Native
    /// window chrome consumes this event instead of reading shell palette or navigation state.
    /// </summary>
    public event EventHandler<AvaloniaUiSceneCommittedEventArgs>? SceneCommitted;

    /// <summary>
    /// Schedules one UI-thread scene commit. Repeated invalidations coalesce until that frame is
    /// rendered, including state publications from background service threads.
    /// </summary>
    public void RequestCommit()
    {
        lock (_commitGate)
        {
            if (_disposed || _commitQueued)
            {
                return;
            }

            _commitQueued = true;
        }

        Dispatcher.UIThread.Post(CommitScene);
    }

    /// <summary>
    /// Renders the UI.Next tree at the native surface size and applies that one scene to the
    /// native visual/accessibility bridge.
    /// </summary>
    public void CommitScene()
    {
        // Native focus loss during removal can synchronously commit text and reenter us.
        // Keep the current scene transaction intact; consume the new draft next frame.
        if (_applyingScene) { RequestCommit(); return; }
        _applyingScene = true;
        try { CommitSceneCore(); }
        finally { _applyingScene = false; }
    }

    private void CommitSceneCore()
    {
        lock (_commitGate)
        {
            if (_disposed)
            {
                return;
            }

            // Clear this before rendering. A state publication that occurs during the render
            // will then schedule a following frame instead of being silently lost.
            _commitQueued = false;
        }

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        XsrUiScene next = _shell.Render(new XsrUiSize(Bounds.Width, Bounds.Height));
        if (!_initialFocusAssigned)
        {
            XsrUiSceneNode initial = next.Nodes.FirstOrDefault(node => node.IsFocusable && node.IsFocused);
            if (!initial.Entity.IsAssigned) initial = next.Nodes.FirstOrDefault(node => node.IsFocusable && node.IsSelected);
            if (!initial.Entity.IsAssigned) initial = next.Nodes.FirstOrDefault(node => node.IsFocusable);
            if (initial.Entity.IsAssigned)
            {
                _initialFocusAssigned = true;
                if (!initial.IsFocused)
                {
                    _shell.Renderer.Focus(initial.Entity, showIndicator: false);
                    next = _shell.Render(new XsrUiSize(Bounds.Width, Bounds.Height));
                }
            }
        }
        if (_scene is null || _scene.Version != next.Version)
        {
            _scene = next;
            ApplyScene(next);
            AvaloniaUiRuntimeDiagnostics.SceneCommitted();
            SceneCommitted?.Invoke(this, new AvaloniaUiSceneCommittedEventArgs(next));
        }
        // Attachment can make native focus available without changing the scene version.
        SynchronizeNativeFocus(next);
    }

    public void Dispose()
    {
        lock (_commitGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        AvaloniaUiMotion.CancelAll(this);
        _contextMenu?.Close();
        _shell.Tree.RenderInvalidated -= OnTreeRenderInvalidated;
        _shell.NavigationExpandedChanged -= OnNavigationExpandedChanged;
        if (_shell.StateBridge is not null)
        {
            _shell.StateBridge.RenderRequested -= OnStateRenderRequested;
        }

        foreach (AvaloniaUiSceneNodeControl control in _controls.Values) control.ReleasePresentation();
        foreach (AvaloniaUiSceneNodeControl control in _outgoingControls) control.ReleasePresentation();
        _outgoingControls.Clear();
        _controls.Clear();
        AvaloniaUiRasterPool.Shared.TrimIdle();
        Children.Clear();
        Cursor = null;
        _handCursor.Dispose();
        _textCursor.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_scene is null)
        {
            return finalSize;
        }

        foreach ((XsrUiEntityId entity, AvaloniaUiSceneNodeControl control) in _controls)
        {
            if (TryGetNode(_scene, entity, out XsrUiSceneNode node))
            {
                control.Arrange(new Rect(node.Rect.X, node.Rect.Y, node.Rect.Width, node.Rect.Height));
            }
        }

        ArrangeOutgoingLayers();
        return finalSize;
    }

    private readonly Dictionary<XsrUiEntityId, long> _scrollRevisions = [];

    private readonly Dictionary<XsrUiEntityId, bool> _segmentTargets = [];
    private readonly Dictionary<XsrUiEntityId, XsrUiSegmentScrollSnapshot> _segmentScrollTargets = [];

    /// <summary>
    /// Scroll indicators paint in the adorner layer: each scene node's own children paint
    /// after the node, so a per-node draw is always covered by its rows, and inserting a
    /// surface child would fight ApplyScene's ownership of the Children order (dialog and
    /// notification z-order depends on it). The adorner layer sits above every child without
    /// touching Children.
    /// </summary>
}

/// <summary>Immutable scene data delivered to backend-native chrome after a commit.</summary>
public sealed class AvaloniaUiSceneCommittedEventArgs(XsrUiScene scene) : EventArgs
{
    public XsrUiScene Scene { get; } = scene ?? throw new ArgumentNullException(nameof(scene));
}
