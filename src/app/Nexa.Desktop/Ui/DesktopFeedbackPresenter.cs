using System.Collections.Concurrent;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>
/// Render-thread projection of <see cref="DesktopFeedbackService"/> into the shared renderer
/// stage. Worker/timer callbacks only publish a wake revision; NXML entities are reconciled at
/// <see cref="XsrUiRenderer.FramePreparing"/>.
/// </summary>
internal sealed class DesktopFeedbackPresenter : IDisposable
{
    private static readonly TimeSpan ExitSettle = TimeSpan.FromMilliseconds(360);
    private static readonly XsrSemanticId NotificationDismiss =
        XsrSemanticId.Parse("ui.feedback.notification.dismiss");
    private static readonly XsrSemanticId NotificationOpen =
        XsrSemanticId.Parse("ui.feedback.notification.open");
    private static readonly XsrSemanticId DialogAccept =
        XsrSemanticId.Parse("ui.feedback.dialog.accept");
    private static readonly XsrSemanticId DialogCancel =
        XsrSemanticId.Parse("ui.feedback.dialog.cancel");

    private sealed class PresentedNotification(
        DesktopNotification value,
        XsrUiEntityId root,
        XsrUiEntityId close,
        XsrUiOverlayMotion motion)
    {
        public DesktopNotification Value { get; } = value;
        public XsrUiEntityId Root { get; } = root;
        public XsrUiEntityId Close { get; } = close;
        public XsrUiOverlayMotion Motion { get; } = motion;
        public bool IsClosing { get; set; }
        public ITimer? Cleanup { get; set; }
    }

    private sealed class PresentedDialog(
        DesktopDialog value,
        XsrUiEntityId root,
        XsrUiEntityId card,
        XsrUiEntityId accept,
        XsrUiEntityId cancel,
        XsrUiOverlayMotion scrimMotion,
        XsrUiOverlayMotion cardMotion,
        XsrUiEntityId previousFocus,
        bool previousFocusVisible)
    {
        public DesktopDialog Value { get; set; } = value;
        public XsrUiEntityId Root { get; } = root;
        public XsrUiEntityId Card { get; } = card;
        public XsrUiEntityId Accept { get; } = accept;
        public XsrUiEntityId Cancel { get; } = cancel;
        public XsrUiOverlayMotion ScrimMotion { get; } = scrimMotion;
        public XsrUiOverlayMotion CardMotion { get; } = cardMotion;
        public XsrUiEntityId PreviousFocus { get; } = previousFocus;
        public bool PreviousFocusVisible { get; } = previousFocusVisible;
        public bool IsClosing { get; set; }
        public ITimer? Cleanup { get; set; }
    }

    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly DesktopFeedbackService _service;
    private readonly XsrStateStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _notificationLayer, _dialogLayer;
    private readonly XsrUiThickness _notificationBaseMargin;
    private (XsrUiThickness Margin, bool Visible)? _hostProjection;
    private string _copyStamp = "";
    private bool _copyChanged;
    private readonly XsrUiEntityId _notificationHost;
    private readonly Dictionary<Guid, PresentedNotification> _notifications = [];
    private readonly Dictionary<XsrUiEntityId, Guid> _notificationRoots = [];
    private readonly ConcurrentQueue<Guid> _notificationCleanup = new();
    private readonly ConcurrentQueue<Guid> _dialogCleanup = new();
    private readonly XsrStateId _wakeState;
    private PresentedDialog? _dialog;
    private long _wakeRevision;
    private bool _disposed;

    public DesktopFeedbackPresenter(
        XsrUiShell shell,
        DesktopUiIntentSink intents,
        DesktopFeedbackService service,
        XsrStateStore store,
        TimeProvider? timeProvider = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dynamic = new(shell.Tree, store);
        _notificationLayer = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("FeedbackNotifications.nxml"), shell.Tree, store);
        _dialogLayer = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("FeedbackDialogs.nxml"), shell.Tree, store);
        _notificationBaseMargin = DesktopMarkup.Load("FeedbackHostLayout.nxml").Root.Margin;
        _notificationHost = _dynamic.Append(_notificationLayer, HostFragment(_notificationBaseMargin, visible: false));
        _wakeState = store.Resolve(DesktopFeedbackState.Revision);
        shell.Stage.Show(_notificationLayer);

        _service.Changed += OnServiceChanged;
        _intents.IntentEmitted += OnIntent;
        _shell.Renderer.FramePreparing += OnFramePreparing;
        RequestFrame();
    }

    internal XsrUiEntityId NotificationHost => _notificationHost;

    internal int PresentedNotificationCount => _notifications.Count;

    internal XsrUiEntityId PresentedDialogRoot => _dialog?.Root ?? default;

    private void OnServiceChanged(object? sender, EventArgs e) => RequestFrame();

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
            // Host teardown won the race with a timer callback.
        }
    }

    private void OnFramePreparing(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        string stamp = _shell.Renderer.LocalizeText(DesktopOverlayText.Raw("common.close"));
        _copyChanged = stamp != _copyStamp; _copyStamp = stamp;
        DrainCleanup();
        UpdateNotificationPlacement();
        DesktopFeedbackSnapshot snapshot = _service.Snapshot();
        ReconcileNotifications(snapshot.Notifications);
        ReconcileDialog(snapshot.Dialog);
    }

    private void UpdateNotificationPlacement() => SetNotificationHostVisible(_notifications.Count > 0);

    private static NxmlHostIr HostFragment(XsrUiThickness margin, bool visible) => DesktopMarkup.Dynamic("FeedbackNotificationHost.d.nxml",
        new Dictionary<string, string>
        {
            ["margin"] = string.Join(',', new[] { margin.Left, margin.Top, margin.Right, margin.Bottom }
            .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ["visible"] = Flag(visible)
        });

    private void SetNotificationHostVisible(bool visible)
    {
        var margin = _notificationBaseMargin with { Left = XsrUiShell.RailWidthFor(_shell.RailPresentationProgress) + _notificationBaseMargin.Left };
        if (_hostProjection == (margin, visible)) return;
        _dynamic.Reload(_notificationHost, HostFragment(margin, visible));
        _hostProjection = (margin, visible);
    }

    private void ReconcileNotifications(IReadOnlyList<DesktopNotification> requested)
    {
        HashSet<Guid> requestedIds = [.. requested.Select(notification => notification.Id)];
        foreach (PresentedNotification current in _notifications.Values.ToArray())
        {
            if (!requestedIds.Contains(current.Value.Id) && !current.IsClosing)
            {
                BeginNotificationClose(current);
            }
        }

        bool added = false;
        foreach (DesktopNotification notification in requested)
        {
            if (_notifications.TryGetValue(notification.Id, out var current))
            {
                if (_copyChanged && !current.IsClosing) _dynamic.Reload(current.Root, NotificationFragment(notification, enabled: true));
                continue;
            }

            PresentedNotification presented = CreateNotification(notification);
            _notifications.Add(notification.Id, presented);
            _notificationRoots.Add(presented.Root, notification.Id);
            added = true;
        }

        SetNotificationHostVisible(_notifications.Count > 0);

        if (added && _shell.Tree.GetComponent<XsrUiScroll>(_notificationHost) is { } scroll)
        {
            // New notifications belong at the visible bottom edge; users can still wheel back
            // through older permanent errors when the bounded stack overflows.
            scroll.OffsetY = double.PositiveInfinity;
            _shell.Tree.MarkDirty(_notificationHost, XsrUiDirtyKinds.Layout);
        }
    }

    private NxmlHostIr NotificationFragment(DesktopNotification notification, bool enabled)
    {
        string style = notification.Level switch
        {
            DesktopNotificationLevel.Info => "info",
            DesktopNotificationLevel.Warn => "warn",
            DesktopNotificationLevel.Error => "error",
            _ => throw new ArgumentOutOfRangeException(nameof(notification)),
        };
        string level = _shell.Renderer.LocalizeText(DesktopOverlayText.Raw("notification." + style));
        string message = notification.LocalizeMessage ? _shell.Renderer.LocalizeText(notification.Message) : notification.Message;
        return DesktopMarkup.Dynamic("FeedbackNotification.d.nxml", new Dictionary<string, string>
        {
            ["key"] = "notification:" + notification.Id.ToString("N"),
            ["level"] = level,
            ["message"] = message,
            ["icon"] = notification.IsSuccess ? "lucide/circle-check" : DesktopOverlayText.Raw("notification.icon." + style),
            ["enabled"] = Flag(enabled),
            ["urgency"] = notification.Level == DesktopNotificationLevel.Info ? "Polite" : "Assertive",
            ["label"] = DesktopOverlayText.Format("NotificationLabel", new() { ["level"] = level, ["message"] = message }, _shell.Renderer.LocalizeText),
            ["close-label"] = DesktopOverlayText.Format("NotificationClose", new() { ["level"] = level }, _shell.Renderer.LocalizeText),
            ["style"] = "layout-Notification-Notification feedback-notice-" + style,
            ["accent-style"] = "layout-Notification-NotificationAccent feedback-accent-" + style,
            ["icon-style"] = "layout-Notification-NotificationIcon feedback-icon-" + style,
            ["level-style"] = "layout-Notification-NotificationLevel feedback-level-" + style,
            ["close-style"] = "layout-Notification-NotificationClose feedback-close-" + style,
        });
    }

    private PresentedNotification CreateNotification(DesktopNotification notification)
    {
        var root = _dynamic.Append(_notificationHost, NotificationFragment(notification, enabled: true));
        var entities = Index(root);
        return new(notification, root, entities["NotificationClose"], _shell.Tree.GetComponent<XsrUiOverlayMotion>(root)!);
    }

    private void BeginNotificationClose(PresentedNotification notification)
    {
        notification.IsClosing = true;
        notification.Motion.IsClosing = true;
        _dynamic.Reload(notification.Root, NotificationFragment(notification.Value, enabled: false));
        _shell.Tree.MarkDirty(notification.Root, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        _shell.Tree.MarkDirty(notification.Close, XsrUiDirtyKinds.Paint);
        if (_shell.Renderer.ReducedMotion)
        {
            FinalizeNotification(notification.Value.Id);
            return;
        }

        notification.Cleanup = _timeProvider.CreateTimer(
            static state =>
            {
                (DesktopFeedbackPresenter owner, Guid id) = ((DesktopFeedbackPresenter, Guid))state!;
                owner._notificationCleanup.Enqueue(id);
                owner.RequestFrame();
            },
            (this, notification.Value.Id),
            ExitSettle,
            Timeout.InfiniteTimeSpan);
    }

    private void FinalizeNotification(Guid id)
    {
        if (!_notifications.Remove(id, out PresentedNotification? notification))
        {
            return;
        }

        notification.Cleanup?.Dispose();
        _notificationRoots.Remove(notification.Root);
        if (_shell.Tree.IsAlive(notification.Root))
        {
            _dynamic.Remove(notification.Root);
        }
        SetNotificationHostVisible(_notifications.Count > 0);
    }

    private void ReconcileDialog(DesktopDialog? requested)
    {
        if (requested is null)
        {
            if (_dialog is { IsClosing: false } current)
            {
                BeginDialogClose(current);
            }
            return;
        }

        if (_dialog is null)
        {
            _dialog = CreateDialog(requested);
            return;
        }

        if (_dialog.Value.Id != requested.Id)
        {
            FinalizeDialog(_dialog.Value.Id);
            _dialog = CreateDialog(requested);
            return;
        }

        if (!_dialog.IsClosing && (_dialog.Value != requested || _copyChanged))
        {
            bool changedBody = _dialog.Value.Message != requested.Message;
            UpdateDialog(_dialog, requested, changedBody);
            _dialog.Value = requested;
        }
    }

    private NxmlHostIr DialogFragment(DesktopDialog dialog, bool enabled)
    {
        string title = dialog.LocalizeTitle ? _shell.Renderer.LocalizeText(dialog.Title) : dialog.Title;
        string message = dialog.LocalizeMessage ? _shell.Renderer.LocalizeText(dialog.Message) : dialog.Message;
        bool alternate = dialog.Alternate is not null && !string.IsNullOrWhiteSpace(dialog.AlternateLabel);
        return DesktopMarkup.Dynamic("FeedbackDialog.d.nxml", new Dictionary<string, string>
        {
            ["loading"] = Flag(dialog.IsLoading),
            ["title"] = title,
            ["message"] = message,
            ["enabled"] = Flag(enabled),
            ["narration"] = DesktopOverlayText.Format("DialogNarration", new() { ["title"] = title, ["message"] = message }),
            ["accept"] = _shell.Renderer.LocalizeText(dialog.AcceptLabel),
            ["cancel"] = _shell.Renderer.LocalizeText(dialog.CancelLabel ?? ""),
            ["alternate"] = _shell.Renderer.LocalizeText(dialog.AlternateLabel ?? ""),
            ["has-cancel"] = Flag(!string.IsNullOrWhiteSpace(dialog.CancelLabel)),
            ["has-alternate"] = Flag(alternate),
            ["action-alignment"] = alternate ? "Stretch" : "End",
        });
    }

    private PresentedDialog CreateDialog(DesktopDialog dialog)
    {
        var root = _dynamic.Append(_dialogLayer, DialogFragment(dialog, enabled: true));
        var entities = Index(root);
        var previousFocus = _shell.Renderer.Focused;
        bool focusVisible = _shell.Tree.IsAlive(previousFocus)
            && _shell.Tree.GetComponent<XsrUiInput>(previousFocus)?.IsFocusVisible == true;
        var presented = new PresentedDialog(dialog, root, entities["DialogCard"], entities["DialogAccept"], entities["DialogCancel"],
            _shell.Tree.GetComponent<XsrUiOverlayMotion>(root)!, _shell.Tree.GetComponent<XsrUiOverlayMotion>(entities["DialogCard"])!,
            previousFocus, focusVisible);
        _shell.Stage.Show(_dialogLayer, modal: true);
        _shell.Renderer.Focus(presented.Accept, focusVisible);
        return presented;
    }

    private void UpdateDialog(PresentedDialog presented, DesktopDialog dialog, bool changedBody = false)
    {
        if (changedBody && _shell.Tree.GetComponent<XsrUiScroll>(Index(presented.Root)["DialogMessageViewport"]) is { } scroll)
        {
            scroll.OffsetY = 0;
            _shell.Tree.MarkDirty(Index(presented.Root)["DialogMessageViewport"], XsrUiDirtyKinds.Layout);
        }
        _dynamic.Reload(presented.Root, DialogFragment(dialog, enabled: !presented.IsClosing));
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private void BeginDialogClose(PresentedDialog dialog)
    {
        dialog.IsClosing = true;
        dialog.ScrimMotion.IsClosing = true;
        dialog.CardMotion.IsClosing = true;
        _dynamic.Reload(dialog.Root, DialogFragment(dialog.Value, enabled: false));
        _shell.Tree.MarkDirty(dialog.Root, XsrUiDirtyKinds.Paint);
        _shell.Tree.MarkDirty(dialog.Card, XsrUiDirtyKinds.Paint);
        if (_shell.Renderer.ReducedMotion)
        {
            FinalizeDialog(dialog.Value.Id);
            return;
        }

        dialog.Cleanup = _timeProvider.CreateTimer(
            static state =>
            {
                (DesktopFeedbackPresenter owner, Guid id) = ((DesktopFeedbackPresenter, Guid))state!;
                owner._dialogCleanup.Enqueue(id);
                owner.RequestFrame();
            },
            (this, dialog.Value.Id),
            ExitSettle,
            Timeout.InfiniteTimeSpan);
    }

    private void FinalizeDialog(Guid id)
    {
        PresentedDialog? dialog = _dialog;
        if (dialog is null || dialog.Value.Id != id)
        {
            return;
        }

        _dialog = null;
        dialog.Cleanup?.Dispose();
        if (_shell.Tree.IsAlive(dialog.Root))
        {
            _shell.Stage.Dismiss(_dialogLayer);
            _dynamic.Remove(dialog.Root);
        }
        if (dialog.PreviousFocus.IsAssigned && _shell.Tree.IsAlive(dialog.PreviousFocus))
        {
            _shell.Renderer.Focus(dialog.PreviousFocus, dialog.PreviousFocusVisible);
        }
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.Intent.Command == NotificationDismiss)
        {
            if (IsEnabledCommand(e.Intent.Source, NotificationDismiss) && TryResolveNotification(e.Intent.Source, out Guid id))
            {
                _service.DismissNotification(id);
            }
            return;
        }

        if (e.Intent.Command == NotificationOpen)
        {
            if (IsEnabledCommand(e.Intent.Source, NotificationOpen) && TryResolveNotification(e.Intent.Source, out Guid id)
                && _notifications.TryGetValue(id, out PresentedNotification? notification))
            {
                if (notification.IsClosing) return;
                string style = notification.Value.Level.ToString().ToLowerInvariant();
                string level = _shell.Renderer.LocalizeText(DesktopOverlayText.Raw("notification." + style));
                _service.ShowMessageDialog("notification.detail." + id.ToString("N"),
                    DesktopOverlayText.Format("NotificationTitle", new() { ["level"] = level }, _shell.Renderer.LocalizeText),
                    notification.Value.Message, localizeMessage: notification.Value.LocalizeMessage, localizeTitle: false);
            }
            return;
        }

        if (_dialog is { IsClosing: false } dialog)
        {
            if (!_shell.Tree.IsAlive(e.Intent.Source)) return;
            var entities = Index(dialog.Root);
            if (e.Intent.Command.Value == "ui.feedback.dialog.alternate"
                && IsEnabledCommand(e.Intent.Source, e.Intent.Command) && e.Intent.Source == entities["DialogAlternate"] && dialog.Value.Alternate is not null
                && _shell.Tree.GetComponent<XsrUiElement>(e.Intent.Source)?.IsVisible != false)
            { _service.InvokeDialogAlternate(dialog.Value.Id); return; }
            if (e.Intent.Command == DialogAccept && e.Intent.Source == dialog.Accept && IsEnabledCommand(e.Intent.Source, DialogAccept))
            {
                _service.ResolveDialog(dialog.Value.Id, accepted: true);
            }
            else if (e.Intent.Command == DialogCancel && ((e.Intent.Source == dialog.Cancel && IsEnabledCommand(e.Intent.Source, DialogCancel)) || e.Intent.Source == dialog.Root))
            {
                _service.ResolveDialog(dialog.Value.Id, accepted: false);
            }
        }
    }

    private void DrainCleanup()
    {
        while (_notificationCleanup.TryDequeue(out Guid notificationId))
        {
            FinalizeNotification(notificationId);
        }
        while (_dialogCleanup.TryDequeue(out Guid dialogId))
        {
            FinalizeDialog(dialogId);
        }
    }

    private Dictionary<string, XsrUiEntityId> Index(XsrUiEntityId root)
    {
        Dictionary<string, XsrUiEntityId> result = [];
        _shell.Tree.Walk(root, entity =>
        {
            string name = _shell.Tree.Name(entity);
            if (name.Length > 0)
            {
                result[name] = entity;
            }
            return true;
        });
        return result;
    }

    private bool IsEnabledCommand(XsrUiEntityId source, XsrSemanticId command)
    {
        if (!_shell.Tree.IsAlive(source) || _shell.Tree.GetComponent<XsrUiInput>(source)?.Enabled != true
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(source)?.Command != command) return false;
        for (var entity = source; entity.IsAssigned && _shell.Tree.IsAlive(entity); entity = _shell.Tree.Parent(entity))
            if (_shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible == false) return false;
        return true;
    }

    private bool TryResolveNotification(XsrUiEntityId source, out Guid id)
    {
        XsrUiEntityId current = source;
        while (current.IsAssigned && _shell.Tree.IsAlive(current))
        {
            if (_notificationRoots.TryGetValue(current, out id))
                return _notifications.TryGetValue(id, out var notification) && !notification.IsClosing;
            current = _shell.Tree.Parent(current);
        }

        id = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.Changed -= OnServiceChanged;
        _intents.IntentEmitted -= OnIntent;
        _shell.Renderer.FramePreparing -= OnFramePreparing;
        foreach (PresentedNotification notification in _notifications.Values)
        {
            notification.Cleanup?.Dispose();
            if (_shell.Tree.IsAlive(notification.Root))
            {
                _dynamic.Remove(notification.Root);
            }
        }
        _notifications.Clear();
        _notificationRoots.Clear();
        if (_dialog is { } dialog)
        {
            dialog.Cleanup?.Dispose();
            if (_shell.Tree.IsAlive(dialog.Root))
            {
                _shell.Stage.Dismiss(_dialogLayer);
                _dynamic.Remove(dialog.Root);
            }
            _dialog = null;
        }
        if (_shell.Tree.IsAlive(_notificationHost))
        {
            _dynamic.Clear(_notificationHost);
            _shell.Stage.Dismiss(_notificationLayer);
            _shell.Tree.Destroy(_notificationLayer);
        }
        if (_shell.Tree.IsAlive(_dialogLayer)) { _dynamic.Clear(_dialogLayer); _shell.Tree.Destroy(_dialogLayer); }
    }
}
