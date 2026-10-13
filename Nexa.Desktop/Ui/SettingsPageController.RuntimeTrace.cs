using System.Globalization;
using Nexa.Services.Logging;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId RuntimeTraceAction = XsrSemanticId.Parse("ui.settings.diagnostics.trace");
    private readonly Dictionary<XsrUiEntityId, string> _runtimeTraceActions = [];
    private RuntimeTraceSnapshot? _runtimeTraceSnapshot;
    private Task<XsrResult<RuntimeTraceSnapshot>>? _runtimeTraceRead;
    private CancellationTokenSource? _runtimeTraceStop;
    private string _runtimeTraceFilter = "operations";
    private int _runtimeTracePage;
    private XsrUiEntityId _runtimeTraceBody, _runtimeTraceRoot;
    private bool RuntimeTraceVisible => !_disposed && _visible && _instanceDirectory is null && _developer && _selected == "privacy";
    private static bool IsRuntimeTraceIntent(XsrSemanticId command) => command == RuntimeTraceAction;
    private void ResetRuntimeTraceControls() { _runtimeTraceActions.Clear(); _runtimeTraceBody = _runtimeTraceRoot = default; }
    private void RetireRuntimeTrace()
    {
        _runtimeTraceStop?.Cancel(); _runtimeTraceStop?.Dispose(); _runtimeTraceStop = null;
        _runtimeTraceRead = null; _runtimeTraceSnapshot = null; _runtimeTracePage = 0;
        ResetRuntimeTraceControls();
    }
    private void BuildRuntimeTraceDiagnostics()
    {
        if (!RuntimeTraceVisible) { RetireRuntimeTrace(); return; }
        if (!_queries.TryResolve(RuntimeTraceContract.Read, out _)) return;
        _runtimeTraceRoot = AppendCatalog(_sections, "SettingsRuntimeTrace.d.nxml", new Dictionary<string, string> { ["enabled"] = Flag(_runtimeTraceRead is null) });
        var nodes = CatalogNodes(_runtimeTraceRoot);
        _runtimeTraceBody = nodes["RuntimeTrace.Rows"];
        foreach (var (key, action) in new[] { ("Capture", "refresh"), ("Operations", "operations"), ("State", "state"), ("Launch", "launch"), ("Next", "next") })
            _runtimeTraceActions[nodes["RuntimeTrace." + key]] = action;
        BuildRuntimeTraceRows();
    }
    private void UpdateRuntimeTrace()
    {
        if (!RuntimeTraceVisible) { RetireRuntimeTrace(); return; }
        if (_runtimeTraceRead is not { IsCompleted: true } read) return;
        _runtimeTraceRead = null; _runtimeTraceStop?.Dispose(); _runtimeTraceStop = null;
        if (PendingQuery.Succeeded(read)) _runtimeTraceSnapshot = read.Result.Value;
        else if (!read.IsCanceled) _feedback.Error(ProductPresentationText.Raw("settings.trace.captureFailed"));
        ReloadCatalog(_runtimeTraceRoot, ("enabled", "true"));
        BuildRuntimeTraceRows();
    }
    private void BuildRuntimeTraceRows()
    {
        if (!_shell.Tree.IsAlive(_runtimeTraceBody)) return;
        _dynamic.Clear(_runtimeTraceBody);
        if (_runtimeTraceRead is not null || _runtimeTraceSnapshot is not { } snapshot)
        {
            _dynamic.Append(_runtimeTraceBody, DesktopMarkup.Dynamic(_runtimeTraceRead is null ? "SettingsRuntimeTraceEmpty.d.nxml" : "SettingsRuntimeTraceLoading.d.nxml"));
            return;
        }
        var rows = FilterRuntimeTrace(snapshot, _runtimeTraceFilter);
        int pages = Math.Max(1, (rows.Count + 31) / 32);
        _runtimeTracePage = Math.Clamp(_runtimeTracePage, 0, pages - 1);
        _dynamic.Append(_runtimeTraceBody, DesktopMarkup.Dynamic("SettingsRuntimeTraceSummary.d.nxml", new Dictionary<string, string>
        {
            ["page"] = (_runtimeTracePage + 1).ToString(CultureInfo.InvariantCulture),
            ["pages"] = pages.ToString(CultureInfo.InvariantCulture),
            ["count"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["capacity"] = snapshot.Capacity.ToString(CultureInfo.InvariantCulture),
            ["dropped"] = snapshot.Dropped.ToString(CultureInfo.InvariantCulture)
        }, _shell.Renderer.LocalizeText));
        int index = 0;
        foreach (var entry in rows.Skip(_runtimeTracePage * 32).Take(32))
        {
            string row = "RuntimeTrace.Entry." + index++;
            _dynamic.Append(_runtimeTraceBody, DesktopMarkup.Dynamic("SettingsRuntimeTraceEntry.d.nxml", new Dictionary<string, string>
            {
                ["row"] = row,
                ["identityKey"] = "RuntimeTrace.Identity." + index,
                ["detailKey"] = "RuntimeTrace.Detail." + index,
                ["kind"] = entry.Kind,
                ["semantic"] = entry.SemanticId,
                ["detail"] = entry.Detail,
                ["correlation"] = entry.CorrelationId.ToString(),
                ["ticks"] = entry.MonotonicTimestamp.ToString(CultureInfo.InvariantCulture),
                ["result"] = RuntimeTerm("trace." + Flag(entry.IsSuccess))
            }, _shell.Renderer.LocalizeText));
        }
    }
    internal static IReadOnlyList<RuntimeTraceEntry> FilterRuntimeTrace(RuntimeTraceSnapshot snapshot, string filter)
        => Array.AsReadOnly(snapshot.Entries.Where(entry => filter switch
        {
            "state" => entry.Kind == "State",
            "launch" => entry.SemanticId.StartsWith("minecraft.", StringComparison.Ordinal)
                || entry.SemanticId.StartsWith("process.", StringComparison.Ordinal),
            _ => entry.Kind != "State",
        }).Take(256).ToArray());
    private void HandleRuntimeTraceIntent(DesktopUiIntent intent)
    {
        if (!RuntimeTraceVisible || _store.Read<long>(_revisionId).Value != _revision
            || intent.Command != RuntimeTraceAction || !_shell.Tree.IsAlive(intent.Source)
            || !_runtimeTraceActions.TryGetValue(intent.Source, out string? action)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true || _runtimeTraceRead is not null) return;
        if (action == "refresh" && _queries.TryResolve(RuntimeTraceContract.Read, out var route))
        {
            _runtimeTraceStop?.Dispose(); _runtimeTraceStop = new(); _runtimeTracePage = 0;
            _runtimeTraceRead = _queries.QueryAsync<RuntimeTraceQuery, RuntimeTraceSnapshot>(route, new(), cancellationToken: _runtimeTraceStop.Token).AsTask();
            WakeOnPlatformCompletion(_runtimeTraceRead); ReloadCatalog(_runtimeTraceRoot, ("enabled", "false")); BuildRuntimeTraceRows();
        }
        else if (action == "next") { _runtimeTracePage++; BuildRuntimeTraceRows(); }
        else if (action is "operations" or "state" or "launch") { _runtimeTraceFilter = action; _runtimeTracePage = 0; BuildRuntimeTraceRows(); }
    }
}
