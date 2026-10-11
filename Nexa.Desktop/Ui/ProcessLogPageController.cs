using Nexa.Xsr.Renderer.Nxml;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal static class ProcessLogPresentationState
{
    internal static readonly XsrSemanticId Wake = XsrSemanticId.Parse("ui.process.log.wake");
    internal static void DeclareState(XsrStateStoreBuilder builder) => builder.Cell<long>(Wake, "Nexa.Desktop.ProcessLogs");
}

/// <summary>Bounded presentation of typed, redacted process output; native copy is an explicit effect.</summary>
internal sealed class ProcessLogPageController : IDisposable
{
    internal const int PageSize = 24;
    private readonly XsrUiShell _shell;
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _workspace;
    private readonly Dictionary<string, string> _view;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrQueryRouter _queries;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly Func<string, Task>? _copy;
    private readonly Dictionary<string, XsrUiEntityId> _entities = [];
    private readonly Dictionary<XsrUiEntityId, long> _rows = [];
    private readonly HashSet<long> _selected = [];
    private CancellationTokenSource? _readStop;
    private Task<XsrResult<MinecraftProcessOutputSnapshot>>? _reading;
    private Task _pending = Task.CompletedTask;
    private MinecraftProcessOutputSnapshot? _snapshot;
    private MinecraftProcessOutputEntry[] _filtered = [];
    private Guid _session;
    private string _search = "", _error = "", _copyStamp = "";
    private MinecraftProcessOutputChannel? _stream;
    private int _page;
    private long _generation, _wakeRevision, _sessionRevision = -1;
    private bool _visible;
    private volatile bool _dirty = true, _disposed, _copying;

    internal ProcessLogPageController(XsrUiShell shell, DesktopUiIntentSink intents, XsrQueryRouter queries,
        XsrStateStore store, DesktopFeedbackService feedback, Func<string, Task>? copy = null)
    {
        _shell = shell; _intents = intents; _queries = queries; _store = store; _feedback = feedback; _copy = copy;
        _view = InitialView();
        _dynamic = new(shell.Tree, store);
        Page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("ProcessLogPage.nxml"), shell.Tree, store);
        _workspace = _dynamic.Append(shell.Tree.Children(Page).Single(), DesktopMarkup.Dynamic("ProcessLogWorkspace.d.nxml", _view));
        shell.Tree.Walk(Page, entity => { _entities[shell.Tree.Name(entity)] = entity; return true; });
        shell.Renderer.FramePreparing += OnFrame;
        intents.IntentEmitted += OnIntent;
    }

    internal XsrUiEntityId Page { get; }
    internal Task WaitUntilIdle() => _pending;

    internal void Leave()
    {
        RetireRead(); _snapshot = null; _filtered = []; _selected.Clear(); ClearRows(); _visible = false;
    }

    internal void Open(Guid session)
    {
        RetireRead();
        _session = session; _snapshot = null; _selected.Clear(); _page = 0; _stream = null; _search = "";
        _shell.Renderer.SetTextInputValue(_entities["ProcessLogSearch"], "");
        _visible = true;
        Read();
    }

    private MinecraftProcessSnapshot[] Sessions()
    {
        if (!_store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var id)) return [];
        return _store.ReadCollection<MinecraftProcessSnapshot>(id).Items.OrderBy(item => item.StartedAt).ToArray();
    }

    private void Read()
    {
        RetireRead();
        _error = ""; _dirty = true;
        if (!_queries.TryResolve(MinecraftProcessOutputContract.Query, out var route))
        { _error = DesktopPresentationText.Raw("log.unsupported"); return; }
        long generation = _generation;
        Guid session = _session;
        _readStop = new();
        CancellationToken token = _readStop.Token;
        _reading = Task.Run(async () =>
        {
            try
            {
                return await _queries.QueryAsync<MinecraftProcessOutputQuery, MinecraftProcessOutputSnapshot>(route,
                    new(session), cancellationToken: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { return XsrResult.Failure<MinecraftProcessOutputSnapshot>(MinecraftErrors.InvalidRequest(DesktopPresentationText.Raw("log.canceled"))); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { return XsrResult.Failure<MinecraftProcessOutputSnapshot>(MinecraftErrors.InvalidRequest(DesktopPresentationText.Raw("log.unavailable"))); }
        });
        _pending = _reading;
        WakeOnCompletion(_reading, generation);
    }

    private void WakeOnCompletion(Task task, long generation)
    {
        _ = task.ContinueWith(_ =>
        {
            if (_disposed || generation != Interlocked.Read(ref _generation)) return;
            try { _store.Publish(_store.Resolve(ProcessLogPresentationState.Wake), Interlocked.Increment(ref _wakeRevision)); }
            catch (ObjectDisposedException) { }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void RetireRead()
    {
        Interlocked.Increment(ref _generation);
        _readStop?.Cancel(); _readStop?.Dispose(); _readStop = null; _reading = null;
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed) return;
        bool visible = _shell.Stage.Navigation.Current == Page;
        if (_visible && !visible)
        {
            Leave();
        }
        else if (!_visible && visible) Read();
        _visible = visible;
        if (!visible) return;
        string stamp = LogTerm("task.empty");
        if (_copyStamp != stamp) { _copyStamp = stamp; _dirty = true; }
        string draft = _shell.Tree.GetComponent<XsrUiTextInput>(_entities["ProcessLogSearch"])!.ReadDraft().Trim();
        if (draft != _search) { _search = draft; _page = 0; _dirty = true; }
        if (_store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var sessionsId))
        {
            long revision = _store.ReadCollection<MinecraftProcessSnapshot>(sessionsId).Revision;
            if (revision != _sessionRevision)
            {
                _sessionRevision = revision; _dirty = true;
                if (_snapshot is not null && !_store.ReadCollection<MinecraftProcessSnapshot>(sessionsId).Items.Any(item => item.SessionId == _session))
                {
                    RetireRead(); _snapshot = null; _selected.Clear();
                    _error = DesktopPresentationText.Raw("log.expired");
                }
            }
        }
        if (_reading is { IsCompleted: true } reading)
        {
            _reading = null; _readStop?.Dispose(); _readStop = null;
            bool completed = new PendingQuery<MinecraftProcessOutputSnapshot>(reading).TryRead(out var result);
            // A malformed provider cannot silently substitute another session's output.
            if (completed && result!.IsSuccess && result.Value!.SessionId == _session)
            {
                _snapshot = result.Value;
                _selected.IntersectWith(_snapshot.Entries.Select(entry => entry.Sequence));
            }
            else
            {
                _snapshot = null; _selected.Clear();
                _error = !completed ? DesktopPresentationText.Raw("log.unavailable") : result!.Error?.Message ?? DesktopPresentationText.Raw("log.mismatch");
            }
            _dirty = true;
        }
        if (_dirty) { _dirty = false; Project(); }
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (_disposed || _shell.Stage.Navigation.Current != Page) return;
        string command = args.Intent.Command.Value;
        if (command == "ui.page.back" || command.StartsWith("ui.navigation.", StringComparison.Ordinal))
        { RetireRead(); return; }
        if (!command.StartsWith("ui.process.log.", StringComparison.Ordinal)) return;
        if (command == "ui.process.log.select" && _rows.TryGetValue(args.Intent.Source, out var sequence))
        { if (!_selected.Add(sequence)) _selected.Remove(sequence); }
        else if (command == "ui.process.log.refresh") Read();
        else if (command == "ui.process.log.previous-session") SwitchSession(-1);
        else if (command == "ui.process.log.next-session") SwitchSession(1);
        else if (command == "ui.process.log.previous") _page = Math.Max(0, _page - 1);
        else if (command == "ui.process.log.next") _page++;
        else if (command is "ui.process.log.all" or "ui.process.log.stdout" or "ui.process.log.stderr")
        {
            _stream = command.EndsWith(".stdout", StringComparison.Ordinal) ? MinecraftProcessOutputChannel.Stdout
                : command.EndsWith(".stderr", StringComparison.Ordinal) ? MinecraftProcessOutputChannel.Stderr : null;
            _page = 0;
        }
        else if (command == "ui.process.log.clear-selection") _selected.Clear();
        else if (command is "ui.process.log.copy-selected" or "ui.process.log.copy-page")
        {
            if (_copying || _snapshot is null) return;
            var entries = command == "ui.process.log.copy-selected"
                ? _snapshot.Entries.Where(entry => _selected.Contains(entry.Sequence)).ToArray()
                : _filtered.Skip(_page * PageSize).Take(PageSize).ToArray();
            if (entries.Length == 0) return;
            _copying = true;
            _pending = CopyAsync(string.Join(Environment.NewLine, entries.Select(Display)), _generation);
            WakeOnCompletion(_pending, _generation);
        }
        _dirty = true;
    }

    private void SwitchSession(int direction)
    {
        var sessions = Sessions();
        int index = sessions.ToList().FindIndex(item => item.SessionId == _session);
        int next = index + direction;
        if (next >= 0 && next < sessions.Length) Open(sessions[next].SessionId);
    }

    private async Task CopyAsync(string text, long generation)
    {
        try
        {
            if (_copy is null) throw new InvalidOperationException(DesktopPresentationText.Raw("log.clipboard"));
            await _copy(text).ConfigureAwait(false);
            if (!_disposed && generation == Interlocked.Read(ref _generation)) _feedback.Info(LogTerm("log.copied"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            if (!_disposed && generation == Interlocked.Read(ref _generation))
                _feedback.Error(error is InvalidOperationException && error.Message == DesktopPresentationText.Raw("log.clipboard")
                    ? LogTerm("log.copy-unavailable") : LogTerm("log.copy-failed"));
        }
        finally { _copying = false; _dirty = true; }
    }

    private static string Display(MinecraftProcessOutputEntry entry) =>
        DesktopPresentationText.Format(entry.Stream == MinecraftProcessOutputChannel.Stdout ? "LogStdout" : "LogStderr",
            new() { ["text"] = entry.Text });

    private void Project()
    {
        var sessions = Sessions();
        int index = sessions.ToList().FindIndex(item => item.SessionId == _session);
        var session = index >= 0 ? sessions[index] : null;
        var state = session?.State ?? _snapshot?.State;
        string lifecycle = LogTerm(state is MinecraftProcessState.Created or MinecraftProcessState.Running ? "log.running"
            : state == MinecraftProcessState.Cancelled ? "log.cancelled" : state == MinecraftProcessState.Failed ? "log.failed" : "log.exited");
        var sessionData = new Dictionary<string, string>
        { ["instance"] = session?.InstanceId ?? _snapshot?.InstanceId ?? LogTerm("log.session"), ["lifecycle"] = lifecycle };
        if (session is not null) sessionData["time"] = session.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
        Text("ProcessLogSession", LogFormat(session is null ? "LogSession" : "LogSessionTime", sessionData));
        Enable("ProcessLogPreviousSession", index > 0);
        Enable("ProcessLogNextSession", index >= 0 && index + 1 < sessions.Length);
        Enable("ProcessLogRefresh", _reading is null);
        _filtered = (_snapshot?.Entries ?? []).Where(entry => (!_stream.HasValue || entry.Stream == _stream)
            && (_search.Length == 0 || entry.Text.Contains(_search, StringComparison.OrdinalIgnoreCase))).ToArray();
        int pages = Math.Max(1, (_filtered.Length + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        Text("ProcessLogPageNumber", LogFormat("LogPage", new() { ["page"] = LogNumber(_page + 1), ["pages"] = LogNumber(pages) }));
        string status = _reading is not null ? LogTerm("log.reading")
            : _error.Length > 0 ? _shell.Renderer.LocalizeText(_error) : LogFormat("LogStatus", new()
            { ["count"] = LogNumber(_filtered.Length), ["selected"] = LogNumber(_selected.Count) });
        if (_reading is null && _error.Length == 0 && _snapshot?.DroppedEntries > 0)
            status = LogFormat("LogDropped", new() { ["status"] = status, ["dropped"] = LogNumber(_snapshot.DroppedEntries) });
        Text("ProcessLogStatus", status);
        string empty = _error.Length > 0 || _reading is not null ? ""
            : _snapshot?.Entries.Count == 0 ? LogTerm("log.empty") : _filtered.Length == 0 ? LogTerm("log.no-match") : "";
        Text("ProcessLogEmpty", empty);
        _view["ProcessLogEmpty.visible"] = Flag(empty.Length > 0);
        Enable("ProcessLogPrevious", _page > 0);
        Enable("ProcessLogNext", _page + 1 < pages);
        Enable("ProcessLogClearSelection", _selected.Count > 0);
        Enable("ProcessLogCopySelected", _selected.Count > 0 && !_copying);
        Enable("ProcessLogCopyPage", _filtered.Length > 0 && !_copying);
        foreach (var choice in new[] { ("ProcessLogAll", (MinecraftProcessOutputChannel?)null),
            ("ProcessLogStdout", (MinecraftProcessOutputChannel?)MinecraftProcessOutputChannel.Stdout),
            ("ProcessLogStderr", (MinecraftProcessOutputChannel?)MinecraftProcessOutputChannel.Stderr) })
            _view[choice.Item1 + ".selected"] = Flag(_stream == choice.Item2);
        _dynamic.Reload(_workspace, DesktopMarkup.Dynamic("ProcessLogWorkspace.d.nxml", _view));
        var entries = _filtered.Skip(_page * PageSize).Take(PageSize).ToArray();
        if (!_rows.Values.SequenceEqual(entries.Select(entry => entry.Sequence))) ClearRows();
        foreach (var entry in entries)
        {
            var row = _rows.FirstOrDefault(pair => pair.Value == entry.Sequence).Key;
            var fragment = DesktopMarkup.Dynamic("ProcessLogRow.d.nxml", new Dictionary<string, string>
            {
                ["key"] = "ProcessLogEntry:" + entry.Sequence,
                ["text"] = Display(entry),
                ["selected"] = Flag(_selected.Contains(entry.Sequence)),
                ["style"] = _selected.Contains(entry.Sequence) ? "process-log-row process-log-row-selected" : "process-log-row",
            });
            if (!row.IsAssigned)
            {
                row = _dynamic.Append(_entities["ProcessLogRows"], fragment);
                _rows[row] = entry.Sequence;
            }
            else _dynamic.Reload(row, fragment);
        }
    }

    private void ClearRows()
    {
        _dynamic.Clear(_entities["ProcessLogRows"]);
        _rows.Clear();
        _shell.Tree.GetComponent<XsrUiScroll>(_entities["ProcessLogRows"])!.OffsetY = 0;
    }

    private static string Flag(bool value) => value ? "true" : "false";
    private void Text(string key, string text) => _view[key] = text;
    private void Enable(string key, bool enabled) => _view[key + ".enabled"] = Flag(enabled);
    private Dictionary<string, string> InitialView()
    {
        var values = new Dictionary<string, string>
        {
            ["ProcessLogSession"] = LogTerm("log.title"),
            ["ProcessLogPageNumber"] = LogFormat("LogPage", new() { ["page"] = "1", ["pages"] = "1" }),
            ["ProcessLogStatus"] = LogTerm("log.reading"),
            ["ProcessLogEmpty"] = "",
            ["ProcessLogEmpty.visible"] = "false",
            ["ProcessLogAll.selected"] = "true",
            ["ProcessLogStdout.selected"] = "false",
            ["ProcessLogStderr.selected"] = "false",
        };
        foreach (string key in new[] { "ProcessLogPreviousSession", "ProcessLogNextSession", "ProcessLogRefresh", "ProcessLogPrevious",
            "ProcessLogNext", "ProcessLogClearSelection", "ProcessLogCopySelected", "ProcessLogCopyPage" }) values[key + ".enabled"] = "false";
        return values;
    }

    private string LogTerm(string key) => DesktopPresentationText.Term(key, _shell.Renderer.LocalizeText);
    private string LogFormat(string template, Dictionary<string, string> data)
        => DesktopPresentationText.Format(template, data, _shell.Renderer.LocalizeText);
    private static string LogNumber(long value) => value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; RetireRead();
        _intents.IntentEmitted -= OnIntent; _shell.Renderer.FramePreparing -= OnFrame;
        _shell.Tree.Destroy(Page); _snapshot = null; _filtered = []; _selected.Clear(); _rows.Clear();
    }
}
