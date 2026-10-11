using Nexa.Services.Tasks;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Desktop-owned presentation cells for the task bubble.</summary>
internal static class TaskBubbleState
{
    internal const string OwnerName = "Nexa.Desktop.TaskBubble";

    /// <summary>Wake revision: timer and controller threads ask for a frame through this cell.</summary>
    public static readonly XsrSemanticId Revision = XsrSemanticId.Parse("ui.tasks.bubble.revision");

    public static void DeclareState(XsrStateStoreBuilder builder) => builder.Cell<long>(Revision, OwnerName);
}

/// <summary>
/// Render-thread projection of the task center summary into the bottom-right progress
/// bubble. The compact shell overlay shows task count and a fine progress track, and yields
/// while the task page is open. All tree mutations happen at
/// <see cref="XsrUiRenderer.FramePreparing"/>; worker threads only publish the wake cell.
/// </summary>
internal sealed class DesktopTaskBubblePresenter : IDisposable
{
    private static readonly TimeSpan ExitSettle = TimeSpan.FromMilliseconds(360);

    private readonly XsrUiShell _shell;
    private readonly XsrStateStore _store;
    private readonly XsrStateId _summaryId;
    private readonly XsrStateId _wakeState;
    private readonly XsrUiEntityId _root;
    private readonly XsrUiEntityId _fill;
    private readonly DesktopDynamicLayer _layer;
    private readonly XsrUiEntityId _launch;
    private readonly XsrUiOverlayMotion _motion;
    private readonly TimeProvider _timeProvider;
    private long _wakeRevision;
    private volatile bool _exitPending;
    private ITimer? _exitTimer;
    private bool _pageVisible;
    private bool _closing;
    private (string Caption, string Label, bool Details, double Progress)? _projection;
    private bool _disposed;

    public DesktopTaskBubblePresenter(XsrUiShell shell, XsrStateStore store, TimeProvider? timeProvider = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _summaryId = store.Resolve(TaskCenterStateContract.SummaryKey);
        _wakeState = store.Resolve(TaskBubbleState.Revision);

        _layer = new(shell, store, "ShellBubbleLayer.nxml");
        _root = _layer.Append("TaskBubble.d.nxml", Data("任务", "打开任务中心", false, 0));
        DesktopBubbleLayout.Register(shell, _root, 1);
        _motion = shell.Tree.GetComponent<XsrUiOverlayMotion>(_root)!;
        XsrUiEntityId fill = default;
        shell.Tree.Walk(_root, entity => { if (shell.Tree.Name(entity) == "task-bubble-fill") fill = entity; return true; });
        _fill = fill;
        _launch = _layer.Append("LaunchBubble.d.nxml");
        DesktopBubbleLayout.Register(shell, _launch, 0);
        _shell.Renderer.FramePreparing += OnFramePreparing;
    }

    private static Dictionary<string, string> Data(string caption, string label, bool details, double progress) => new()
    {
        ["caption"] = caption,
        ["label"] = label,
        ["details"] = details ? "true" : "false",
        ["progress"] = progress.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    internal XsrUiEntityId Root => _root;

    internal XsrUiEntityId FillEntity => _fill;

    /// <summary>Called by the task center controller: the bubble yields while the page is open.</summary>
    public void SetPageVisible(bool visible)
    {
        if (_pageVisible == visible)
        {
            return;
        }

        _pageVisible = visible;
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_disposed)
        {
            return;
        }

        long revision = Interlocked.Increment(ref _wakeRevision);
        try
        {
            _store.Publish(_wakeState, revision);
        }
        catch (ObjectDisposedException)
        {
            // Host teardown won the race with the controller thread.
        }
    }

    private void OnFramePreparing(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_exitPending)
        {
            _exitPending = false;
            _closing = false;
            _motion.IsClosing = false;
            SetVisible(false);
            SetEnabled(true);
        }

        TaskCenterSummary summary =
            (TaskCenterSummary?)_store.ReadAppliedValue(_summaryId) ?? new TaskCenterSummary(0, 0, 0, 0, 0);
        Reconcile(summary);
        DesktopBubbleLayout.Arrange(_shell);
    }

    private void Reconcile(TaskCenterSummary summary)
    {
        bool wanted = summary.VisibleCount > 0 && !_pageVisible;
        bool launching = _store.TryResolve(LaunchPageState.LaunchingVisibleKey, out var launchState)
            && _store.ReadAppliedValue(launchState) is true;
        DesktopBubbleLayout.SetVisible(_shell, _store, _launch, launching);
        var rootInput = _shell.Tree.GetComponent<XsrUiInput>(_root)!;
        bool expanded = wanted && (rootInput.IsHovered || rootInput.IsFocused);
        bool showDetails = expanded && (rootInput.CapsuleExpansionProgress > .95 || _shell.Renderer.ReducedMotion);
        if (wanted && _closing)
        {
            _exitTimer?.Dispose(); _exitTimer = null;
            _exitPending = false; _closing = false; _motion.IsClosing = false;
            SetEnabled(true);
            _shell.Tree.MarkDirty(_root, XsrUiDirtyKinds.Paint);
        }
        string caption = summary.ActiveCount > 0 ? $"{summary.ActiveCount} 项任务 · {(int)Math.Round(summary.Progress * 100)}%" : $"{summary.VisibleCount} 项任务记录";
        if (wanted && !_closing && !IsVisible(_root))
        {
            SetVisible(true);
        }
        else if (!wanted && IsVisible(_root) && !_closing)
        {
            BeginClose();
        }

        double target = summary.ActiveCount > 0 ? Math.Clamp(summary.Progress, 0d, 1d) : 1d;
        int percent = (int)Math.Round(target * 100d);
        string label = summary.ActiveCount > 0 ? $"任务进行中 {percent}%，打开任务中心" : "查看任务记录，打开任务中心";
        var projection = (caption, label, showDetails, target);
        if (_projection != projection)
        {
            _layer.Reload(_root, "TaskBubble.d.nxml", Data(caption, label, showDetails, target));
            _projection = projection;
        }
    }

    private void BeginClose()
    {
        _closing = true;
        _motion.IsClosing = true;
        SetEnabled(false);
        _shell.Tree.MarkDirty(_root, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        if (_shell.Renderer.ReducedMotion)
        {
            _exitPending = false;
            _closing = false;
            _motion.IsClosing = false;
            SetVisible(false);
            SetEnabled(true);
            return;
        }

        _exitTimer?.Dispose();
        _exitTimer = _timeProvider.CreateTimer(
            static state =>
            {
                DesktopTaskBubblePresenter owner = (DesktopTaskBubblePresenter)state!;
                owner._exitPending = true;
                owner.RequestFrame();
            },
            this,
            ExitSettle,
            Timeout.InfiniteTimeSpan);
    }

    private void SetVisible(bool visible) => DesktopElementVisibility.Set(_shell.Tree, _root, visible);

    private void SetEnabled(bool enabled)
    {
        if (_shell.Tree.GetComponent<XsrUiInput>(_root) is { } input)
        {
            input.Enabled = enabled;
        }
    }

    private bool IsVisible(XsrUiEntityId entity) =>
        _shell.Tree.GetComponent<XsrUiElement>(entity)!.IsVisible;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shell.Renderer.FramePreparing -= OnFramePreparing;
        _exitTimer?.Dispose();
        _layer.Dispose();
    }
}
