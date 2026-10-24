using System.Globalization;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer;

/// <summary>
/// The UI.Next runtime: runs the deterministic measure/arrange layout pass over dirty subtrees,
/// produces the immutable render scene, and hands it to the backend commit boundary. It reads
/// state through <see cref="XsrStateStore"/> and emits intent through
/// <see cref="IXsrUiIntentSink"/>; it never resolves services or touches a backend type.
/// </summary>
public sealed partial class XsrUiRenderer
{
    private readonly XsrUiPresentation _tree;
    private long _presentationRevision;
    private readonly XsrStateStore _state;
    private readonly IXsrUiIntentSink? _sink;
    private readonly XsrUiStateBridge? _stateBridge;
    private XsrUiEntityId _hovered;
    private XsrUiPoint _hoverDecisionPoint = new(double.NegativeInfinity, double.NegativeInfinity);
    private XsrUiEntityId _pressed;
    private XsrUiEntityId _focused;
    private readonly Dictionary<int, XsrUiSize> _desiredSizes = [];
    private readonly Dictionary<int, XsrUiSize> _stackContentSizes = [];
    private readonly Dictionary<int, XsrUiSize> _measureConstraints = [];
    private readonly HashSet<int> _widthSensitiveMeasures = [];
    private readonly Dictionary<int, XsrUiRect> _paintRects = [];
    private readonly Dictionary<int, XsrUiRect> _arrangedSlots = [];
    private readonly HashSet<int> _measuredThisPass = [];
    private XsrUiScene? _scene;
    private long _sceneVersion;
    private XsrUiEntityId _root;
    private XsrUiSize _viewport = new(800, 600);
    private int _layoutVisits;

    public XsrUiRenderer(
        XsrUiTree tree,
        XsrStateStore state,
        IXsrUiIntentSink? sink = null,
        XsrUiStateBridge? stateBridge = null,
        TimeProvider? timeProvider = null)
    {
        _tree = (tree ?? throw new ArgumentNullException(nameof(tree))).Presentation;
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _sink = sink;
        _stateBridge = stateBridge;
        _gestureTime = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets or sets whether animation-heavy presentation should reduce motion. The flag is a
    /// presentation contract; backends and animation drivers must honor it.
    /// </summary>
    private bool _reducedMotion;
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            if (_reducedMotion == value) return;
            _reducedMotion = value;
            if (_root.IsAssigned && _tree.IsAlive(_root)) _tree.MarkDirty(_root, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        }
    }

    private bool _optionalMotionSuspended;
    /// <summary>Temporary host presentation policy, independent of the user's reduced-motion preference.</summary>
    public bool OptionalMotionSuspended
    {
        get => _optionalMotionSuspended;
        set
        {
            if (_optionalMotionSuspended == value) return;
            _optionalMotionSuspended = value;
            if (_root.IsAssigned && _tree.IsAlive(_root)) _tree.MarkDirty(_root, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        }
    }
    public bool EffectiveReducedMotion => ReducedMotion || OptionalMotionSuspended;

    /// <summary>
    /// Render-thread composition hook for materializing state-backed PXML templates. Runs before
    /// state dirt is drained and before layout; publishers must never call this hook themselves.
    /// </summary>
    public event EventHandler? FramePreparing;

    /// <summary>Runs after logical page projections so generic presentation layers see newly created nodes in the same frame.</summary>
    public event EventHandler? PresentationPreparing;

    /// <summary>Finite composition hook for committing already prepared theme candidates before host data projection.</summary>
    public event EventHandler? ThemePreparing;
    private bool _renderInProgress;

    /// <summary>
    /// Gets the currently focused entity, or an unassigned handle.
    /// </summary>
    public XsrUiEntityId Focused => _focused;

    private Func<string, string>? _textLocalizer;
    /// <summary>Optional presentation resolver. Assign again when its language changes.</summary>
    public Func<string, string>? TextLocalizer
    {
        get => _textLocalizer;
        set
        {
            _textLocalizer = value;
            // Navigation keeps detached pages alive. Drop their measurements as well so
            // returning to a cached page measures its text in the new language.
            _desiredSizes.Clear();
            _stackContentSizes.Clear();
            _measureConstraints.Clear();
            _widthSensitiveMeasures.Clear();
            if (_root.IsAssigned) _tree.Walk(_root, entity => { _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint); return true; });
        }
    }

    public string LocalizeText(string source) => _textLocalizer?.Invoke(source) ?? source;

    /// <summary>
    /// Gets or sets the size the root entity is arranged into.
    /// </summary>
    public XsrUiSize Viewport
    {
        get => _viewport;
        set
        {
            if (_root.IsAssigned && value != _viewport)
            {
                _tree.MarkDirty(_root, XsrUiDirtyKinds.Layout);
            }

            _viewport = value;
        }
    }

    /// <summary>
    /// Gets the version of the last produced scene; unchanged renders reuse the version.
    /// </summary>
    public long SceneVersion => _scene?.Version ?? 0;

    /// <summary>Reads advisory scroll geometry from the last produced scene without rendering.</summary>
    public bool TryGetScrollSnapshot(XsrUiEntityId entity, out XsrUiScrollSnapshot snapshot)
    {
        snapshot = default;
        if (_scene is null || !_tree.IsAlive(entity) || _tree.GetComponent<XsrUiScroll>(entity) is null)
            return false;
        for (int index = 0; index < _scene.Count; index++)
        {
            if (_scene[index].Entity == entity && _scene[index].Scroll is { } scroll)
            {
                snapshot = scroll;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets how many entities the last render measured. Layout touches only dirty subtrees.
    /// </summary>
    public int LastLayoutVisits { get; private set; }

    /// <summary>
    /// Sets the root entity of the rendered scene.
    /// </summary>
    public void SetRoot(XsrUiEntityId root)
    {
        if (!_tree.IsAlive(root))
        {
            throw new InvalidOperationException($"The renderer root '{root}' is not alive.");
        }

        _root = root;
        _tree.MarkDirty(root, XsrUiDirtyKinds.Structure);
    }

    /// <summary>
    /// Produces the current render scene. A clean tree returns the cached scene unchanged;
    /// any dirty subtree relayouts exactly that subtree before the scene rebuilds.
    /// </summary>
    public XsrUiScene Render()
    {
        if (_renderInProgress) throw new InvalidOperationException("A renderer frame cannot recursively render.");
        _renderInProgress = true;
        try
        {
            try { return RenderCore(); }
            catch (XsrUiThemeStyleException exception)
            {
                IXsrUiThemeStyleProgram? failedProgram = _themeProgram;
                long failedGeneration = ThemeActivationGeneration;
                ThemeStyleFailed?.Invoke(this, new(exception, failedGeneration, failedProgram?.Revision ?? 0));
                if (ReferenceEquals(failedProgram, _themeProgram) && failedGeneration == ThemeActivationGeneration) throw;
                // A synchronous coordinator restored a prepared appearance. One bounded retry only.
                return RenderCore();
            }
        }
        finally { _renderInProgress = false; }
    }

    private XsrUiScene RenderCore()
    {
        if (!_root.IsAssigned)
        {
            throw new InvalidOperationException("The renderer has no root entity.");
        }

        // The state bridge queues publisher-thread changes; the render thread drains them here,
        // so coalesced and derived state land in this frame's dirty set before the clean check.
        ThemePreparing?.Invoke(this, EventArgs.Empty);
        FramePreparing?.Invoke(this, EventArgs.Empty);
        _stateBridge?.DrainAndMark(_state);
        PresentationPreparing?.Invoke(this, EventArgs.Empty);
        _tree.PrepareFrame();
        if (_presentationRevision != _tree.Revision)
        {
            _presentationRevision = _tree.Revision;
            _desiredSizes.Clear(); _stackContentSizes.Clear(); _measureConstraints.Clear();
            _widthSensitiveMeasures.Clear(); _paintRects.Clear(); _arrangedSlots.Clear();
        }
        PrepareThemeFrame();

        if (_scene is not null && !_tree.HasDirtySubtree(_root))
        {
            return _scene;
        }

        _layoutVisits = 0;
        _measuredThisPass.Clear();
        if (SubtreeHasKind(_root, XsrUiDirtyKinds.Structure))
        {
            PruneDeadCacheEntries();
        }


        Layout(_root, new XsrUiRect(0, 0, _viewport.Width, _viewport.Height));

        // Hidden subtrees were not measured this frame. Their dirt is still acknowledged below,
        // so discard stale layout caches first: a later visibility change must measure new rows
        // rather than reuse the empty/old list's desired size and recycled entity rectangles.
        _tree.Walk(_root, entity =>
        {
            if (_tree.HasDirtyLayoutSubtree(entity) && !_measuredThisPass.Contains(entity.Index))
            {
                _desiredSizes.Remove(entity.Index);
                _stackContentSizes.Remove(entity.Index);
                _arrangedSlots.Remove(entity.Index);
                _paintRects.Remove(entity.Index);
            }
            return true;
        }, entity => _tree.HasDirtyLayoutSubtree(entity));

        RetireScrollGesture();
        XsrUiSceneNode[] nodes = CollectNodes(_root, depth: 0);
        RecordThemeAnimationScene(nodes);
        _sceneVersion++;
        _scene = new XsrUiScene(_sceneVersion, nodes, _outgoingLayers.ToArray());
        _themeSnapshotsInvalidated = false;
        _inputVisited.Clear();
        int inputCapacity = Math.Max(16, nodes.Length);
        if (_inputVisited.EnsureCapacity(0) > inputCapacity * 2L)
            _inputVisited.TrimExcess(inputCapacity);
        LastLayoutVisits = _layoutVisits;

        _tree.Walk(_root, entity =>
        {
            _tree.ClearDirty(entity);
            return true;
        }, entity => _tree.HasDirtySubtree(entity));

        _tree.FinishFrame(_root);
        return _scene;
    }

    private bool SubtreeHasKind(XsrUiEntityId root, XsrUiDirtyKinds kind)
    {
        bool found = false;
        _tree.Walk(
            root,
            entity =>
            {
                if (_tree.DirtyKinds(entity).HasFlag(kind))
                {
                    found = true;
                }

                return !found;
            },
            entity => _tree.HasDirtySubtree(entity));
        return found;
    }

    private void PruneDeadCacheEntries()
    {
        foreach (XsrUiEntityId entity in _themeGridMeasurements.Keys.Where(entity => !_tree.IsAlive(entity)).ToArray())
            _themeGridMeasurements.Remove(entity);
        foreach (int id in _desiredSizes.Keys.Where(id => !_tree.IsIndexAlive(id)).ToArray())
        {
            _ = _desiredSizes.Remove(id);
            _ = _measureConstraints.Remove(id);
            _ = _widthSensitiveMeasures.Remove(id);
        }

        foreach (int id in _paintRects.Keys.Where(id => !_tree.IsIndexAlive(id)).ToArray())
        {
            _ = _paintRects.Remove(id);
        }

        foreach (int id in _arrangedSlots.Keys.Where(id => !_tree.IsIndexAlive(id)).ToArray())
        {
            _ = _arrangedSlots.Remove(id);
        }

        foreach (int id in _stackContentSizes.Keys.Where(id => !_tree.IsIndexAlive(id)).ToArray())
        {
            _ = _stackContentSizes.Remove(id);
        }
    }

    /// <summary>Advances a transition group's scene geometry on the render thread.</summary>
    public void SetTransitionOffset(XsrUiEntityId entity, double offset)
    {
        if (!_tree.IsAlive(entity) || _tree.GetComponent<XsrUiTransition>(entity) is not { } transition) return;
        if (!double.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
        double value = EffectiveReducedMotion ? 0 : offset;
        if (transition.PresentedOffsetX == value) return;
        transition.PresentedOffsetX = value;
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }

    public double GetTransitionOffset(XsrUiEntityId entity) =>
        _tree.IsAlive(entity) ? _tree.GetComponent<XsrUiTransition>(entity)?.PresentedOffsetX ?? 0 : 0;

    public void SetTransitionOffsetY(XsrUiEntityId entity, double offset)
    {
        if (!_tree.IsAlive(entity) || _tree.GetComponent<XsrUiTransition>(entity) is not { } transition) return;
        if (!double.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
        double value = EffectiveReducedMotion ? 0 : offset;
        if (transition.PresentedOffsetY == value) return;
        transition.PresentedOffsetY = value;
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }

    public double GetTransitionOffsetY(XsrUiEntityId entity) =>
        _tree.IsAlive(entity) ? _tree.GetComponent<XsrUiTransition>(entity)?.PresentedOffsetY ?? 0 : 0;

    /// <summary>Advances local capsule geometry on the render thread, driven by a presentation clock.</summary>
    public void SetCapsulePresentationProgress(XsrUiEntityId entity, double progress)
    {
        if (!_tree.IsAlive(entity) || _tree.GetComponent<XsrUiInput>(entity) is not { } input
            || _tree.GetComponent<XsrUiVisualStyle>(entity)?.HoverExpand != true) return;
        double value = Math.Clamp(progress, 0, 1);
        if (input.CapsuleExpansionProgress == value) return;
        input.CapsuleExpansionProgress = value;
        _tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    private double? PresentedWidth(XsrUiEntityId entity, XsrUiElement? element, double availableWidth)
    {
        if (element?.Height is not { } collapsed
            || _tree.GetComponent<XsrUiVisualStyle>(entity)?.HoverExpand != true
            || _tree.GetComponent<XsrUiInput>(entity) is not { } input) return element?.Width;
        double expanded;
        if (element.Width is { } authoredWidth) expanded = authoredWidth;
        else
        {
            if (!IsScrollableCapsule(entity)) return null;
            expanded = CapsuleTextSize(entity).Width + CapsuleCaptionInset(collapsed);
            expanded = ConstrainDimension(Math.Max(collapsed, expanded), element.MinWidth, element.MaxWidth, 0);
            expanded = Math.Min(expanded, Math.Max(0, availableWidth - element.Margin.Horizontal - element.Padding.Horizontal));
        }
        if (EffectiveReducedMotion) input.CapsuleExpansionProgress = IsEnabled(input) && (input.IsHovered || input.IsFocusVisible) ? 1 : 0;
        return Math.Min(collapsed, expanded) + Math.Max(0, expanded - collapsed) * input.CapsuleExpansionProgress;
    }

    private void MarkInputDirty(XsrUiEntityId entity) => _tree.MarkDirty(entity,
        _tree.GetComponent<XsrUiVisualStyle>(entity)?.HoverExpand == true
            ? XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint : XsrUiDirtyKinds.Paint);

    internal bool IsVisible(XsrUiEntityId entity)
    {
        if (_tree.ReplacedHostElement(entity) is { } host && (!host.IsVisible
            || host.BoundVisibility.IsAssigned && _state.ReadAppliedValue(host.BoundVisibility) is not true)) return false;
        XsrUiElement? element = _tree.GetComponent<XsrUiElement>(entity);
        if (element?.BoundVisibility is { } bound && bound.IsAssigned)
        {
            return _state.ReadAppliedValue(bound) is bool visible && visible;
        }

        return element?.IsVisible ?? true;
    }

    private bool ParticipatesInStackFlow(XsrUiEntityId entity)
    {
        if (!IsVisible(entity) || _tree.GetComponent<XsrUiOverlayLayer>(entity) is not null || IsThemedOutOfFlow(entity))
        {
            return false;
        }

        // A notice leaves the stack at the start of its inverse exit, while its last arranged
        // rectangle remains available to the backend until the visual track settles. This lets
        // siblings retarget immediately instead of waiting for the cleanup timer.
        return _tree.GetComponent<XsrUiOverlayMotion>(entity) is not
        {
            Kind: XsrUiOverlayMotionKind.Notification,
            IsClosing: true,
        };
    }

    private readonly HashSet<int> _inputVisited = [];

    private readonly List<XsrUiOutgoingLayer> _outgoingLayers = [];
    private readonly List<XsrUiSceneNode> _collectedNodes = [];
    private readonly Dictionary<string, int> _entryOrders = [];
}
