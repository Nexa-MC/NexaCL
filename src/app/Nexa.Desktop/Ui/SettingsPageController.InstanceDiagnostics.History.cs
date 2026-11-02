using Nexa.Services.Logging;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private Task<XsrResult<IReadOnlyList<DurableDiagnosticEntry>>>? _instanceHistoryRead;
    private IReadOnlyList<DurableDiagnosticEntry>? _instanceHistory;
    private CancellationTokenSource? _instanceHistoryStop;
    private string? _instanceHistoryError;
    private int _instanceLaunchHistoryPage, _instanceCrashHistoryPage;
    private XsrUiEntityId _instanceHistoryRoot;

    private void CancelInstanceDiagnosticHistory()
    {
        _instanceHistoryStop?.Cancel(); _instanceHistoryStop?.Dispose(); _instanceHistoryStop = null;
        if (_instanceHistoryRead is { } retired)
            _ = retired.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _instanceHistoryRead = null; _instanceHistory = null; _instanceHistoryError = null;
        _instanceLaunchHistoryPage = _instanceCrashHistoryPage = 0;
        _instanceOperationPage = 0; _instanceOperationView = false;
    }
    private void EnsureInstanceDiagnosticHistoryRead()
    {
        if (_instanceHistory is not null || _instanceHistoryRead is not null || _instanceHistoryError is not null) return;
        if (_queries.TryResolve(DiagnosticWorkspaceContract.InstanceHistory, out var route))
        {
            _instanceHistoryStop = new(); var query = new DurableInstanceDiagnosticHistoryQuery(_instance!); var token = _instanceHistoryStop.Token;
            _instanceHistoryRead = Task.Run(async () => await _queries.QueryAsync<DurableInstanceDiagnosticHistoryQuery, IReadOnlyList<DurableDiagnosticEntry>>(
                route, query, cancellationToken: token).ConfigureAwait(false), token);
            WakeOnPlatformCompletion(_instanceHistoryRead);
        }
        else _instanceHistoryError = "history.missing";
        ProjectInstanceHistory();
    }
    private void BuildInstanceDiagnosticHistory()
    {
        _instanceHistoryRoot = AppendInstanceDiagnostic(_sections, "SettingsInstanceHistory.d.nxml", InstanceHistoryData());
        var nodes = CatalogNodes(_instanceHistoryRoot);
        foreach (string kind in new[] { "Launch", "Crash", "Operation" })
            BindInstanceDiagnosticAction(nodes, "InstanceDiagnostics." + kind + "History.Next", () =>
            {
                if (kind == "Operation") _instanceOperationPage++;
                else if (kind == "Crash") _instanceCrashHistoryPage++;
                else _instanceLaunchHistoryPage++;
                ProjectInstanceHistory(kind);
            });
        ProjectInstanceHistory();
    }
    private Dictionary<string, string> InstanceHistoryData()
    {
        var result = new Dictionary<string, string>
        {
            ["pending"] = Flag(_instanceHistoryRead is not null || _instanceHistory is null && _instanceHistoryError is null),
            ["hasError"] = Flag(_instanceHistoryError is not null),
            ["error"] = _instanceHistoryError is null ? "" : InstanceDiagnosticTerm(_instanceHistoryError),
            ["showStatus"] = Flag(_instanceHistory is null),
        };
        foreach (string kind in new[] { "Launch", "Crash", "Operation" })
        {
            int count = InstanceHistoryEntries(kind).Length;
            int pages = Math.Max(1, (count + 11) / 12);
            int requested = kind == "Launch" ? _instanceLaunchHistoryPage : kind == "Crash" ? _instanceCrashHistoryPage : _instanceOperationPage;
            int page = requested % pages;
            if (kind == "Launch") _instanceLaunchHistoryPage = page;
            else if (kind == "Crash") _instanceCrashHistoryPage = page; else _instanceOperationPage = page;
            result[kind + "Visible"] = Flag(_instanceHistory is not null && (kind == "Operation" ? _instanceOperationView : !_instanceOperationView));
            result[kind + "Paging"] = Flag(pages > 1);
            result[kind + "Page"] = InstanceNumber(page + 1);
            result[kind + "Pages"] = InstanceNumber(pages);
        }
        return result;
    }
    private DurableDiagnosticEntry[] InstanceHistoryEntries(string kind)
    {
        var entries = (_instanceHistory ?? []).Where(entry => entry.Instance is not null);
        if (kind == "Operation") return entries.OrderByDescending(entry => entry.Timestamp).ToArray();
        if (kind == "Crash") entries = entries.Where(entry => entry.Instance!.FailureCode is not null || entry.Instance.ExitCode is not null and not 0);
        return entries.GroupBy(entry => entry.Instance!.SessionId).Select(group => group.OrderByDescending(entry => entry.Timestamp).First())
            .OrderByDescending(entry => entry.Timestamp).ToArray();
    }
    private string InstanceDiagnosticOutcome(DiagnosticOperationOutcome outcome, bool operation = false) => InstanceDiagnosticTerm(outcome switch
    {
        DiagnosticOperationOutcome.Started => operation ? "outcome.started" : "outcome.running",
        DiagnosticOperationOutcome.Entered => operation ? "outcome.entered" : "outcome.running",
        DiagnosticOperationOutcome.Completed => "outcome.completed",
        DiagnosticOperationOutcome.Cancelled => "outcome.cancelled",
        DiagnosticOperationOutcome.Unfinished => "outcome.unfinished",
        _ => "outcome.failed"
    });
    private void ProjectInstanceHistory(string? only = null)
    {
        if (!_shell.Tree.IsAlive(_instanceHistoryRoot)) return;
        ReloadInstanceDiagnostic(_instanceHistoryRoot, InstanceHistoryData());
        var nodes = CatalogNodes(_instanceHistoryRoot);
        foreach (string kind in new[] { "Launch", "Crash", "Operation" })
        {
            if (only is not null && only != kind) continue;
            var rows = nodes["InstanceDiagnostics." + kind + "History.Rows"];
            _dynamic.Clear(rows);
            if (_instanceHistory is null || (kind == "Operation" ? !_instanceOperationView : _instanceOperationView)) continue;
            var entries = InstanceHistoryEntries(kind);
            int page = kind == "Launch" ? _instanceLaunchHistoryPage : kind == "Crash" ? _instanceCrashHistoryPage : _instanceOperationPage;
            if (entries.Length == 0) _dynamic.Append(rows, DesktopMarkup.Dynamic("SettingsInstance" + kind + "Empty.d.nxml"));
            if (kind == "Operation") { ProjectInstanceOperationRows(rows, entries, page); continue; }
            foreach (var entry in entries.Skip(page * 12).Take(12))
            {
                var instance = entry.Instance!;
                _dynamic.Append(rows, DesktopMarkup.Dynamic("SettingsInstanceHistoryRow.d.nxml", new Dictionary<string, string>
                {
                    ["row"] = "InstanceDiagnostics.History." + instance.SessionId.ToString("N"),
                    ["timestamp"] = InstanceTime(instance.StartedAt ?? entry.Timestamp),
                    ["HistorySession"] = instance.SessionId.ToString("N"),
                    ["HistoryResult"] = InstanceDiagnosticOutcome(entry.Outcome),
                    ["hasEnded"] = Flag(instance.EndedAt is not null),
                    ["HistoryEnded"] = instance.EndedAt is { } ended ? InstanceTime(ended) : "",
                    ["hasExit"] = Flag(instance.ExitCode is not null),
                    ["HistoryExit"] = instance.ExitCode is { } exit ? InstanceNumber(exit) : "",
                    ["hasFailure"] = Flag(instance.FailureCode is not null),
                    ["HistoryFailure"] = instance.FailureCode ?? "",
                    ["hasDuration"] = Flag(instance.LaunchDurationMilliseconds is not null),
                    ["HistoryLaunchDuration"] = instance.LaunchDurationMilliseconds is { } duration ? InstanceUnit("Milliseconds", InstanceNumber(duration)) : "",
                }));
            }
        }
    }
    private void UpdateInstanceDiagnostics()
    {
        if (!InstanceDiagnosticsVisible || !Nexa.Core.PathIdentity.Comparer.Equals(_instance, _instanceDiagnosticTarget))
        { if (_instanceDiagnosticTarget is not null) CancelInstanceDiagnostics(); return; }
        EnsureInstanceDiagnosticHistoryRead();
        if (_instanceHistoryRead is not { IsCompleted: true } read) return;
        _instanceHistoryRead = null; _instanceHistoryStop?.Dispose(); _instanceHistoryStop = null;
        if (PendingQuery.Succeeded(read)) _instanceHistory = read.Result.Value!.Take(1024).ToArray();
        else _instanceHistoryError = "history.failed";
        ProjectInstanceHistory();
    }
}
