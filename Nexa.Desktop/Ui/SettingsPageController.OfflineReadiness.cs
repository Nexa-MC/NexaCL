using Nexa.Core;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private Task<XsrResult<InstanceOfflineReadinessReport>>? _offlineRead;
    private CancellationTokenSource? _offlineStop;
    private InstanceOfflineReadinessReport? _offlineReport;
    private string? _offlineInstance;
    private long _offlineSettingsRevision;
    private string? _offlineError;
    private XsrUiEntityId _offlineRoot;

    private void BuildOfflineReadiness(InstanceManagementSnapshot snapshot)
    {
        if (_selected is not ("overview" or "recovery") || !_queries.TryResolve(InstanceOfflineReadinessContract.Query, out _)) return;
        if (!PathIdentity.Comparer.Equals(_offlineInstance, snapshot.InstanceDirectory))
        { CancelOfflineReadiness(); _offlineInstance = snapshot.InstanceDirectory; _offlineReport = null; _offlineError = null; }
        _offlineRoot = AppendInstanceTool(_sections, "SettingsInstanceOffline.d.nxml", OfflineReadinessData());
        BindInstanceToolAction(CatalogNodes(_offlineRoot), "InstanceOfflineReadiness.Check", BeginOfflineReadiness);
        ProjectOfflineReadiness();
    }
    private Dictionary<string, string> OfflineReadinessData()
    {
        var report = _offlineReport;
        return new()
        {
            ["canCheck"] = Flag(_offlineRead is null),
            ["pending"] = Flag(_offlineRead is not null),
            ["hasError"] = Flag(_offlineError is not null),
            ["error"] = _offlineError is null ? "" : InstanceToolTerm(_offlineError),
            ["hasReport"] = Flag(report is not null),
            ["result"] = report is null ? "" : InstanceToolTerm(report.Ready ? "offline.ready" : report.Complete ? "offline.not-ready" : "offline.incomplete"),
            ["java"] = report?.JavaCompatible == true ? InstanceToolFormat("OfflineJava", new() { ["version"] = report.JavaVersion ?? "" })
                : report?.JavaCompatible == false ? InstanceToolTerm("java.missing") : InstanceToolTerm("java.unknown"),
            ["verified"] = InstanceNumber(report?.Artifacts.Count(item => item.State == OfflineArtifactState.Verified) ?? 0),
            ["more"] = Flag(report?.Artifacts.Count(item => item.State != OfflineArtifactState.Verified) > 20)
        };
    }
    private void ProjectOfflineReadiness()
    {
        if (!_shell.Tree.IsAlive(_offlineRoot)) return;
        ReloadInstanceTool(_offlineRoot, OfflineReadinessData());
        var rows = CatalogNodes(_offlineRoot)["InstanceOfflineReadiness.Rows"];
        _dynamic.Clear(rows);
        if (_offlineReport is not { } report) return;
        int index = 0;
        foreach (var fact in report.Artifacts.Where(item => item.State != OfflineArtifactState.Verified).Take(20))
            _dynamic.Append(rows, DesktopMarkup.Dynamic("SettingsInstanceOfflineRow.d.nxml", new Dictionary<string, string>
            {
                ["row"] = "InstanceOfflineReadiness.Fact." + InstanceNumber(index++),
                ["state"] = InstanceToolTerm(fact.State switch
                {
                    OfflineArtifactState.Missing => "artifact.missing", OfflineArtifactState.Corrupt => "artifact.corrupt",
                    OfflineArtifactState.PresentUnverified => "artifact.unverified", _ => "artifact.unavailable"
                }),
                ["path"] = fact.RelativePath, ["reason"] = fact.ReasonCode
            }, _shell.Renderer.LocalizeText));
    }
    private void BeginOfflineReadiness()
    {
        if (_instance is not { } instance || _selected is not ("overview" or "recovery") || _offlineRead is not null
            || !_queries.TryResolve(InstanceOfflineReadinessContract.Query, out var route)) return;
        CancelOfflineReadiness(); _offlineInstance = instance; _offlineError = null; _offlineReport = null; _offlineStop = new();
        _offlineSettingsRevision = _store.Read<long>(_revisionId).Value;
        _offlineRead = _queries.QueryAsync<InstanceOfflineReadinessQuery, InstanceOfflineReadinessReport>(route, new(instance), cancellationToken: _offlineStop.Token).AsTask();
        WakeOnPlatformCompletion(_offlineRead); ProjectOfflineReadiness();
    }
    private void UpdateOfflineReadiness()
    {
        if (!_visible || _selected is not ("overview" or "recovery") || !PathIdentity.Comparer.Equals(_instance, _offlineInstance))
        { CancelOfflineReadiness(); return; }
        if (_offlineRead is not { IsCompleted: true } completed)
        {
            if (_offlineReport is not null && _store.Read<long>(_revisionId).Value != _offlineSettingsRevision)
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
        _offlineStop?.Cancel(); _offlineStop?.Dispose(); _offlineStop = null;
        ObserveRetiredInstanceToolRead(_offlineRead); _offlineRead = null;
    }
}
