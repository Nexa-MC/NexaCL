using System.Collections.Concurrent;
using System.Globalization;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Services.Composition;
using Nexa.Services.Tasks;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>
/// Owns the task center page: opening it from the bubble (or anywhere), reconciling one card
/// per task entry at frame time, and dispatching card intents through the typed task routes.
/// The page is pushed onto the shell navigation stack, so the shared back affordance pops it
/// and the bubble reclaims the corner as soon as the page leaves the stage.
/// </summary>
internal sealed class TaskCenterController : IDisposable
{
    private const string PageResource = "Ui.TaskCenterPage.nxml";

    private static readonly XsrSemanticId DetailsCommand = XsrSemanticId.Parse("ui.tasks.card.details");
    private readonly ConcurrentQueue<string> _detailToggles = new();
    private static readonly XsrSemanticId OpenCommand = XsrSemanticId.Parse("ui.tasks.open");
    private static readonly XsrSemanticId CardCancelCommand = XsrSemanticId.Parse("ui.tasks.card.cancel");
    private static readonly XsrSemanticId CardDismissCommand = XsrSemanticId.Parse("ui.tasks.card.dismiss");
    private static readonly XsrSemanticId ClearFinishedCommand = XsrSemanticId.Parse("ui.tasks.clear-finished");

    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrCommandRouter _commands;
    private readonly XsrStateStore _store;
    private readonly DesktopTaskBubblePresenter _bubble;
    private readonly XsrStateId _entriesId;
    private readonly XsrStateId _summaryId;
    private readonly XsrUiEntityId _page;
    private readonly Dictionary<string, XsrUiEntityId> _entities;
    private readonly NxmlDynamicContent _dynamic;
    private XsrUiEntityId _workspace;
    private (string Summary, bool Empty, bool Finished)? _header;
    private readonly Dictionary<string, PresentedCard> _cards = [];
    private readonly ConcurrentQueue<(string TaskId, XsrSemanticId Command)> _dispatches = [];
    private bool _dispatchedThisFrame;
    private bool _staged;
    private bool _disposed;

    private sealed class PresentedCard(XsrUiEntityId root, XsrUiEntityId action, XsrUiEntityId steps)
    {
        public bool Expanded { get; set; }
        public TaskCenterEntry? Value { get; set; }
        public XsrUiEntityId Root { get; } = root;
        public XsrUiEntityId Action { get; } = action;
        public XsrUiEntityId Steps { get; } = steps;
        public List<XsrUiEntityId> StepRows { get; } = [];
    }

    public TaskCenterController(
        XsrUiShell shell,
        DesktopUiIntentSink intents,
        XsrCommandRouter commands,
        XsrStateStore store,
        DesktopTaskBubblePresenter bubble)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _bubble = bubble ?? throw new ArgumentNullException(nameof(bubble));
        _entriesId = store.Resolve(TaskCenterStateContract.EntriesKey);
        _summaryId = store.Resolve(TaskCenterStateContract.SummaryKey);
        _dynamic = new(shell.Tree, store);
        (_page, _entities) = LoadPage();

        _intents.IntentEmitted += OnIntentEmitted;
        _shell.Renderer.FramePreparing += OnFramePreparing;
    }

    internal XsrUiEntityId Page => _page;

    internal bool IsStaged => _staged;

    internal XsrUiEntityId CardAction(string taskId) =>
        _cards.TryGetValue(taskId, out PresentedCard? card) ? card.Action : default;

    private (XsrUiEntityId Page, Dictionary<string, XsrUiEntityId> Entities) LoadPage()
    {
        XsrUiEntityId page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load(PageResource), _shell.Tree, _store);
        var slot = _shell.Tree.Children(page).Single();
        _workspace = _dynamic.Append(slot, Workspace("暂无任务", empty: true, finished: false));
        Dictionary<string, XsrUiEntityId> entities = [];
        _shell.Tree.Walk(page, entity =>
        {
            string key = _shell.Tree.Name(entity);
            if (key.Length > 0)
            {
                entities[key] = entity;
            }

            return true;
        });
        _shell.Tree.SetComponent(entities["TaskCenterList"], new XsrUiScrollGesture());
        return (page, entities);
    }

    private void OnIntentEmitted(object? sender, DesktopUiIntentEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.Intent.Command == OpenCommand)
        {
            Open();
            return;
        }

        if (e.Intent.Command == ClearFinishedCommand)
        {
            Dispatch(TaskCenterRoutes.ClearFinished, new TaskCenterClearCommand());
            return;
        }

        if (e.Intent.Command != CardCancelCommand && e.Intent.Command != CardDismissCommand && e.Intent.Command != DetailsCommand)
        {
            return;
        }

        // The card carries the entry identity: walk from the source button to its card root.
        XsrUiEntityId ancestor = e.Intent.Source;
        while (ancestor.IsAssigned && _shell.Tree.IsAlive(ancestor))
        {
            if (TryFindCardByRoot(ancestor, out PresentedCard? card) && card is not null)
            {
                if (e.Intent.Command == DetailsCommand)
                { _detailToggles.Enqueue(card.Value!.TaskId); return; }
                if (e.Intent.Command == CardCancelCommand)
                {
                    Dispatch(TaskCenterRoutes.Cancel, new TaskCenterCancelCommand(card.Value!.TaskId));
                }
                else
                {
                    Dispatch(TaskCenterRoutes.Dismiss, new TaskCenterDismissCommand(card.Value!.TaskId));
                }

                return;
            }

            ancestor = _shell.Tree.Parent(ancestor);
        }
    }

    private bool TryFindCardByRoot(XsrUiEntityId entity, out PresentedCard? card)
    {
        foreach (PresentedCard candidate in _cards.Values)
        {
            if (candidate.Root.Equals(entity))
            {
                card = candidate;
                return true;
            }
        }

        card = null;
        return false;
    }

    private void Open()
    {
        if (_staged || _disposed)
        {
            return;
        }

        _shell.Stage.Navigation.Push(_page);
        _staged = true;
        _bubble.SetPageVisible(true);
        _shell.Renderer.Focus(_entities["TaskCenterClear"]);
    }

    private void Dispatch<TCommand>(XsrSemanticId route, TCommand command) where TCommand : notnull
    {
        // Intents arrive off the render thread; route them at the next frame alongside the
        // card reconciliation so cancel/dismiss always lands with a fresh view.
        string taskId = command switch
        {
            TaskCenterCancelCommand cancel => cancel.TaskId,
            TaskCenterDismissCommand dismiss => dismiss.TaskId,
            _ => string.Empty,
        };
        _dispatches.Enqueue((taskId, route));
        _dispatchedThisFrame = false;
    }

    private void OnFramePreparing(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        while (_detailToggles.TryDequeue(out string? taskId))
            if (_cards.TryGetValue(taskId, out var card) && card.Value is { } entry)
            { card.Expanded = !card.Expanded; card.Value = null; UpdateCard(card, entry); }
        DrainDispatches();
        TrackStagedState();
        TaskCenterSummary summary =
            (TaskCenterSummary?)_store.ReadAppliedValue(_summaryId) ?? new TaskCenterSummary(0, 0, 0, 0, 0);
        XsrCollectionSnapshot<TaskCenterEntry> entries = _store.ReadCollection<TaskCenterEntry>(_entriesId);
        ReconcileHeader(summary, entries);
        if (_staged)
        {
            ReconcileCards(entries.Items);
        }
    }

    private void DrainDispatches()
    {
        if (_dispatchedThisFrame)
        {
            return;
        }

        _dispatchedThisFrame = true;
        while (_dispatches.TryDequeue(out (string TaskId, XsrSemanticId Command) pending))
        {
            if (pending.Command == TaskCenterRoutes.Cancel)
            {
                if (_commands.TryResolve(TaskCenterRoutes.Cancel, out XsrCommandId cancelId))
                {
                    _ = _commands.Dispatch(cancelId, new TaskCenterCancelCommand(pending.TaskId));
                }
            }
            else if (pending.Command == TaskCenterRoutes.Dismiss)
            {
                if (_commands.TryResolve(TaskCenterRoutes.Dismiss, out XsrCommandId dismissId))
                {
                    _ = _commands.Dispatch(dismissId, new TaskCenterDismissCommand(pending.TaskId));
                }
            }
            else if (pending.Command == TaskCenterRoutes.ClearFinished)
            {
                if (_commands.TryResolve(TaskCenterRoutes.ClearFinished, out XsrCommandId clearId))
                {
                    _ = _commands.Dispatch(clearId, new TaskCenterClearCommand());
                }
            }
        }
    }

    private void TrackStagedState()
    {
        bool staged = _shell.Stage.Navigation.Current.Equals(_page);
        if (staged == _staged)
        {
            return;
        }

        _staged = staged;
        _bubble.SetPageVisible(staged);
        if (!staged)
        {
            // The page left the stage (back or destination switch); drop its cards so the
            // next open starts from the current entries.
            foreach (PresentedCard card in _cards.Values)
            {
                if (_shell.Tree.IsAlive(card.Root))
                {
                    _dynamic.Remove(card.Root);
                }
            }

            _cards.Clear();
        }
    }

    private void ReconcileHeader(TaskCenterSummary summary, XsrCollectionSnapshot<TaskCenterEntry> entries)
    {
        string header;
        if (summary.VisibleCount == 0)
        {
            header = "暂无任务";
        }
        else if (summary.ActiveCount == 0)
        {
            header = entries.Items.Any(static entry => entry.State == TaskCenterEntryState.Failed)
                ? "有任务未成功"
                : "没有进行中的任务";
        }
        else
        {
            header = string.Create(
                CultureInfo.CurrentCulture,
                $"{summary.ActiveCount} 个任务进行中 · {(int)Math.Round(summary.Progress * 100d)}%");
            if (summary.SpeedBytesPerSecond > 0)
            {
                header += $" · {FormatSpeed(summary.SpeedBytesPerSecond)}";
            }

            if (summary.RemainingFiles > 0)
            {
                header += $" · 剩余 {summary.RemainingFiles} 个文件";
            }
        }

        var projection = (header, summary.VisibleCount == 0, entries.Items.Any(static entry => entry.IsTerminal));
        if (_header == projection) return;
        _dynamic.Reload(_workspace, Workspace(projection.header, projection.Item2, projection.Item3));
        _header = projection;
    }

    private static NxmlHostIr Workspace(string summary, bool empty, bool finished) =>
        DesktopMarkup.Dynamic("TaskCenterWorkspace.d.nxml", new Dictionary<string, string>
        {
            ["summary"] = summary,
            ["empty"] = Flag(empty),
            ["has-tasks"] = Flag(!empty),
            ["has-finished"] = Flag(finished),
        });


    private void ReconcileCards(IReadOnlyList<TaskCenterEntry> entries)
    {
        HashSet<string> requested = [.. entries.Select(static entry => entry.TaskId)];
        foreach (KeyValuePair<string, PresentedCard> pair in _cards.ToArray())
        {
            if (!requested.Contains(pair.Key) && _shell.Tree.IsAlive(pair.Value.Root))
            {
                _dynamic.Remove(pair.Value.Root);
                _cards.Remove(pair.Key);
            }
        }

        foreach (TaskCenterEntry entry in entries)
        {
            if (!_cards.TryGetValue(entry.TaskId, out PresentedCard? card))
            {
                card = CreateCard(entry);
                _cards.Add(entry.TaskId, card);
            }

            UpdateCard(card, entry);
        }

        // Keep active work reachable, preserving the collection order within each group.
        var list = _entities["TaskCenterList"];
        var ordered = entries.OrderBy(static entry => entry.IsTerminal)
            .Select(entry => _cards[entry.TaskId].Root).ToArray();
        if (!_shell.Tree.Children(list).SequenceEqual(ordered))
            foreach (var root in ordered) _shell.Tree.Attach(root, list);
    }

    private PresentedCard CreateCard(TaskCenterEntry entry)
    {
        var root = _dynamic.Append(_entities["TaskCenterList"], Card(entry, expanded: false));
        Dictionary<string, XsrUiEntityId> entities = [];
        _shell.Tree.Walk(root, entity => { entities[_shell.Tree.Name(entity)] = entity; return true; });
        return new(root, entities["TaskCardAction"], entities["TaskCardSteps"]);
    }

    private void UpdateCard(PresentedCard card, TaskCenterEntry entry)
    {
        if (card.Value == entry) return;
        _dynamic.Reload(card.Root, Card(entry, card.Expanded));
        card.Value = entry;
        ReconcileSteps(card, entry);
    }

    private static string Flag(bool value) => value ? "true" : "false";
    private static string StateStyle(TaskCenterEntryState state) => state switch
    {
        TaskCenterEntryState.Finished => "finished",
        TaskCenterEntryState.Failed => "failed",
        TaskCenterEntryState.Canceled or TaskCenterEntryState.Paused => "canceled",
        _ => "running",
    };

    private static NxmlHostIr Card(TaskCenterEntry entry, bool expanded)
    {
        string style = StateStyle(entry.State);
        bool hasSteps = entry.Steps is { Count: > 1 };
        return DesktopMarkup.Dynamic("TaskCard.d.nxml", new Dictionary<string, string>
        {
            ["key"] = $"task-card:{entry.TaskId}",
            ["title"] = entry.Title,
            ["stage"] = entry.IsTerminal ? entry.Detail : $"{entry.Stage} · {entry.Detail}",
            ["icon"] = entry.State switch
            {
                TaskCenterEntryState.Finished => "lucide/circle-check",
                TaskCenterEntryState.Failed => "lucide/circle-x",
                TaskCenterEntryState.Canceled => "lucide/circle-minus",
                TaskCenterEntryState.Paused => "lucide/pause",
                _ => "lucide/loader-circle",
            },
            ["icon-style"] = "layout-TaskCard-TaskCardIcon task-" + style,
            ["percent-style"] = "layout-TaskCard-TaskCardPercent task-" + style,
            ["fill-style"] = "layout-TaskCard-TaskCardFill task-fill-" + style,
            ["percent"] = entry.State switch
            {
                TaskCenterEntryState.Finished => "已完成",
                TaskCenterEntryState.Failed => "失败",
                TaskCenterEntryState.Canceled => "已取消",
                TaskCenterEntryState.Paused => "已暂停",
                _ => $"{(int)Math.Round(Math.Clamp(entry.Progress, 0d, 1d) * 100d)}%",
            },
            ["progress"] = Math.Clamp(entry.Progress, 0d, 1d).ToString(CultureInfo.InvariantCulture),
            ["active"] = Flag(!entry.IsTerminal),
            ["show-meta"] = Flag(entry.TotalFiles > 0 || entry.SpeedBytesPerSecond > 0 && !entry.IsTerminal),
            ["files"] = entry.TotalFiles > 0 ? string.Create(CultureInfo.CurrentCulture, $"{entry.CompletedFiles}/{entry.TotalFiles} 个文件") : "",
            ["speed"] = entry.SpeedBytesPerSecond > 0 && !entry.IsTerminal ? FormatSpeed(entry.SpeedBytesPerSecond) : "",
            ["error"] = entry.ErrorMessage ?? "",
            ["show-error"] = Flag(entry.State == TaskCenterEntryState.Failed && !string.IsNullOrEmpty(entry.ErrorMessage)),
            ["action-label"] = entry.IsTerminal ? "移除任务" : "取消任务",
            ["action"] = entry.IsTerminal ? "ui.tasks.card.dismiss" : "ui.tasks.card.cancel",
            ["show-action"] = Flag(entry.IsTerminal || entry.CanCancel),
            ["has-steps"] = Flag(hasSteps),
            ["expanded"] = Flag(hasSteps && expanded),
            ["details"] = expanded ? "收起步骤" : "查看步骤",
            ["details-label"] = expanded ? "收起任务步骤" : "展开任务步骤",
        });
    }

    private void ReconcileSteps(PresentedCard card, TaskCenterEntry entry)
    {
        IReadOnlyList<TaskCenterStep> steps = entry.Steps ?? [];
        int count = steps.Count > 1 && card.Expanded ? steps.Count : 0;
        if (card.StepRows.Count != count)
        {
            _dynamic.Clear(card.Steps); card.StepRows.Clear();
            for (int i = 0; i < count; i++) card.StepRows.Add(_dynamic.Append(card.Steps, Step(entry.TaskId, steps[i], i)));
        }
        else for (int i = 0; i < count; i++) _dynamic.Reload(card.StepRows[i], Step(entry.TaskId, steps[i], i));
    }

    private static NxmlHostIr Step(string taskId, TaskCenterStep step, int index) =>
        DesktopMarkup.Dynamic("TaskStep.d.nxml", new Dictionary<string, string>
        {
            ["key"] = $"task-card-step:{taskId}:{index}",
            ["style"] = "task-step task-" + StateStyle(step.State),
            ["text"] = step.State switch
            {
                TaskCenterEntryState.Finished => $"✓ {step.Name}",
                TaskCenterEntryState.Running => $"● {step.Name} · {step.Detail}",
                TaskCenterEntryState.Failed => $"✕ {step.Name} · {step.Detail}",
                _ => $"· {step.Name}",
            },
        });

    internal static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 B/s";
        }

        double value = bytesPerSecond;
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{value.ToString(unit == 0 ? "F0" : "F1", CultureInfo.CurrentCulture)} {units[unit]}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _intents.IntentEmitted -= OnIntentEmitted;
        _shell.Renderer.FramePreparing -= OnFramePreparing;
        foreach (PresentedCard card in _cards.Values)
        {
            if (_shell.Tree.IsAlive(card.Root))
            {
                _dynamic.Remove(card.Root);
            }
        }

        _cards.Clear();
        if (_shell.Tree.IsAlive(_page))
        {
            _shell.Tree.Destroy(_page);
        }
    }
}
