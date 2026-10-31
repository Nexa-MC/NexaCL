using Nexa.Core;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private Task<XsrResult<InstanceOfflineReadinessReport>>? _offlineRead;
    private Task<XsrResult>? _offlinePrepare;
    private string? _offlinePrepareInstance;
    private CancellationTokenSource? _offlineStop;
    private InstanceOfflineReadinessReport? _offlineReport;
    private string? _offlineInstance;
    private long _offlineSettingsRevision;
    private string? _offlineError;
    private XsrUiEntityId _offlineRoot;
    private string? _offlinePresentationLanguage;
    private Func<string, string>? _offlinePresentationLocalizer;
    private long _offlineTextRevision;
    private readonly Dictionary<string, (XsrUiEntityId Root, string Identity)> _offlineRows = [];

    private void BuildOfflineReadiness(InstanceManagementSnapshot snapshot)
    {
        if (_selected is not ("overview" or "recovery") || !_queries.TryResolve(InstanceOfflineReadinessContract.Query, out _)) return;
        if (!PathIdentity.Comparer.Equals(_offlineInstance, snapshot.InstanceDirectory))
        { CancelOfflineReadiness(); _offlineInstance = snapshot.InstanceDirectory; _offlineReport = null; _offlineError = null; }
        if (!_shell.Tree.IsAlive(_offlineRoot) || _shell.Tree.Parent(_offlineRoot) != _sections)
        {
            _offlineRows.Clear();
            _offlineRoot = AppendInstanceTool(_sections, "SettingsInstanceOffline.d.nxml", OfflineReadinessData());
        }
        BindInstanceToolAction(CatalogNodes(_offlineRoot), "InstanceOfflineReadiness.Check", BeginOfflineReadiness);
        BindInstanceToolAction(CatalogNodes(_offlineRoot), "InstanceOfflineReadiness.Prepare", BeginOfflinePreparation);
        ProjectOfflineReadiness();
    }
    private Dictionary<string, string> OfflineReadinessData()
    {
        var report = _offlineReport;
        bool busy = _offlinePrepare is not null || _offlineRead is not null;
        return new()
        {
            ["canCheck"] = Flag(_offlineRead is null && _offlinePrepare is null),
            ["canPrepare"] = Flag(_offlineRead is null && _offlinePrepare is null && report is { Ready: false }
                && _commands.TryResolve(MachineCapabilityStateContract.RemediationCommand, out _)),
            ["busy"] = Flag(busy),
            ["idle"] = Flag(!busy),
            ["hasError"] = Flag(!busy && _offlineError is not null),
            ["error"] = _offlineError is null ? "" : InstanceToolTerm(_offlineError),
            ["hasReport"] = Flag(!busy && report is not null),
            ["result"] = report is null ? "" : InstanceToolTerm(report.Ready ? "offline.ready" : report.Complete ? "offline.not-ready" : "offline.incomplete"),
            ["java"] = report?.JavaCompatible == true ? InstanceToolFormat("OfflineJava", new() { ["version"] = report.JavaVersion ?? "" })
                : report?.JavaCompatible == false ? InstanceToolTerm("java.missing") : InstanceToolTerm("java.unknown"),
            ["verified"] = InstanceNumber(report?.Artifacts.Count(item => item.State == OfflineArtifactState.Verified) ?? 0),
            ["more"] = Flag(report?.Artifacts.Count(item => item.State != OfflineArtifactState.Verified) > 20),
            ["language"] = InstanceNumber(_offlineTextRevision),
        };
    }
    private void ProjectOfflineReadiness()
    {
        if (!_shell.Tree.IsAlive(_offlineRoot)) return;
        string language = DesktopResourceText.Language(_store);
        if (_offlinePresentationLanguage != language || !ReferenceEquals(_offlinePresentationLocalizer, _shell.Renderer.TextLocalizer))
            _offlineTextRevision++;
        var data = OfflineReadinessData();
        var candidates = new List<(string Key, string Identity, Dictionary<string, string> Data)>();
        int index = 0;
        foreach (var fact in _offlineReport?.Artifacts.Where(item => item.State != OfflineArtifactState.Verified).Take(20) ?? [])
        {
            string key = "InstanceOfflineReadiness.Fact." + InstanceNumber(index++);
            candidates.Add((key, InstanceToolIdentity(fact.Category + "\n" + fact.RelativePath), new()
            {
                ["row"] = key,
                ["state"] = InstanceToolTerm(fact.State switch
                {
                    OfflineArtifactState.Missing => "artifact.missing",
                    OfflineArtifactState.Corrupt => "artifact.corrupt",
                    OfflineArtifactState.PresentUnverified => "artifact.unverified",
                    _ => "artifact.unavailable"
                }),
                ["path"] = fact.RelativePath,
                ["reason"] = fact.ReasonCode,
                ["language"] = InstanceNumber(_offlineTextRevision),
            }));
        }
        _ = DesktopMarkup.Dynamic("SettingsInstanceOffline.d.nxml", data, _shell.Renderer.LocalizeText);
        foreach (var candidate in candidates)
            _ = DesktopMarkup.Dynamic("SettingsInstanceOfflineRow.d.nxml", candidate.Data, _shell.Renderer.LocalizeText);
        ReloadInstanceTool(_offlineRoot, data);
        var rows = CatalogNodes(_offlineRoot)["InstanceOfflineReadiness.Rows"];
        var wanted = candidates.Select(candidate => candidate.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _offlineRows.Keys.Where(key => !wanted.Contains(key)).ToArray())
        {
            var row = _offlineRows[key].Root;
            _dynamic.Remove(row); _instanceToolViews.Remove(row); _offlineRows.Remove(key);
        }
        foreach (var candidate in candidates)
        {
            if (_offlineRows.TryGetValue(candidate.Key, out var current)
                && (current.Identity != candidate.Identity || !_shell.Tree.IsAlive(current.Root)))
            {
                _dynamic.Remove(current.Root); _instanceToolViews.Remove(current.Root); _offlineRows.Remove(candidate.Key);
            }
            if (_offlineRows.TryGetValue(candidate.Key, out current)) ReloadInstanceTool(current.Root, candidate.Data);
            else _offlineRows[candidate.Key] =
                (AppendInstanceTool(rows, "SettingsInstanceOfflineRow.d.nxml", candidate.Data), candidate.Identity);
        }
        _dynamic.Reorder(rows, candidates.Select(candidate => _offlineRows[candidate.Key].Root).ToArray());
        _offlinePresentationLanguage = language; _offlinePresentationLocalizer = _shell.Renderer.TextLocalizer;
    }
    private void BeginOfflineReadiness()
    {
        if (_instance is not { } instance || _selected is not ("overview" or "recovery") || _offlineRead is not null || _offlinePrepare is not null
            || !_queries.TryResolve(InstanceOfflineReadinessContract.Query, out var route)) return;
        CancelOfflineReadiness(); _offlineInstance = instance; _offlineError = null; _offlineReport = null; _offlineStop = new();
        _offlineSettingsRevision = _store.Read<long>(_revisionId).Value;
        _offlineRead = _queries.QueryAsync<InstanceOfflineReadinessQuery, InstanceOfflineReadinessReport>(route, new(instance), cancellationToken: _offlineStop.Token).AsTask();
        WakeOnPlatformCompletion(_offlineRead); ProjectOfflineReadiness();
    }
    private void BeginOfflinePreparation()
    {
        if (_instance is not { } instance || _selected is not ("overview" or "recovery")
            || _offlineRead is not null || _offlinePrepare is not null || _offlineReport is not { Ready: false } report
            || !PathIdentity.Comparer.Equals(report.InstanceDirectory, instance)
            || !_commands.TryResolve(MachineCapabilityStateContract.RemediationCommand, out var route)) return;
        _offlineError = null;
        _offlinePrepareInstance = instance;
        _offlinePrepare = PrepareOfflineAsync(instance, report.JavaCompatible != true, route);
        WakeOnPlatformCompletion(_offlinePrepare);
        ProjectOfflineReadiness();
    }
    private async Task<XsrResult> PrepareOfflineAsync(string instance, bool needsJava, XsrCommandId route)
    {
        var arguments = new Dictionary<string, string> { ["instanceDirectory"] = instance };
        var result = await _commands.Dispatch(route,
            new RemediationRequest("remediation.game.repair_files", arguments, Confirmed: true)).Completion.ConfigureAwait(false);
        if (!result.IsSuccess || !needsJava) return result;
        return await _commands.Dispatch(route,
            new RemediationRequest("remediation.java.download", arguments, Confirmed: true)).Completion.ConfigureAwait(false);
    }
    private void UpdateOfflineReadiness()
    {
        if (_offlinePrepare is { IsCompleted: true } prepared)
        {
            _offlinePrepare = null;
            if (_visible && _selected is "overview" or "recovery" && PathIdentity.Comparer.Equals(_instance, _offlinePrepareInstance))
            {
                if (prepared.IsCompletedSuccessfully && prepared.Result.IsSuccess) BeginOfflineReadiness();
                else
                {
                    _offlineError = "offline.prepare-failed";
                    if (prepared.IsCompletedSuccessfully && prepared.Result.Error is { } error) _feedback.Error(error.Message);
                    ProjectOfflineReadiness();
                }
            }
            _ = prepared.Exception;
        }
        if (!_visible || _selected is not ("overview" or "recovery") || !PathIdentity.Comparer.Equals(_instance, _offlineInstance))
        { CancelOfflineReadiness(); return; }
        if (_offlinePresentationLanguage != DesktopResourceText.Language(_store)
            || !ReferenceEquals(_offlinePresentationLocalizer, _shell.Renderer.TextLocalizer)) ProjectOfflineReadiness();
        if (_offlineRead is not { IsCompleted: true } completed)
        {
            if (_offlinePrepare is null && _offlineReport is not null && _store.Read<long>(_revisionId).Value != _offlineSettingsRevision)
            { _offlineReport = null; _offlineError = "offline.stale"; ProjectOfflineReadiness(); }
            return;
        }
        _offlineRead = null; _offlineStop?.Dispose(); _offlineStop = null;
        if (_store.Read<long>(_revisionId).Value != _offlineSettingsRevision) { _offlineReport = null; _offlineError = "offline.stale"; }
        else if (PendingQuery.Succeeded(completed) && PathIdentity.Comparer.Equals(completed.Result.Value!.InstanceDirectory, _instance))
        { _offlineReport = completed.Result.Value; _offlineError = null; }
        else if (!completed.IsCanceled) _offlineError = "offline.failed";
        ProjectOfflineReadiness();
    }
    private void CancelOfflineReadiness()
    {
        ObserveRetiredInstanceToolRead(_offlinePrepare);
        _offlineStop?.Cancel(); _offlineStop?.Dispose(); _offlineStop = null;
        ObserveRetiredInstanceToolRead(_offlineRead); _offlineRead = null;
    }
}
