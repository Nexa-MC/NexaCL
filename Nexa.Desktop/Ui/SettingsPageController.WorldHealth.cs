using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private Task<XsrResult<InstanceWorldHealth>>? _worldHealthRead;
    private InstanceWorldHealth? _worldHealth;
    private (string Instance, string World) _worldHealthTarget;
    private CancellationTokenSource? _worldHealthStop;
    private string? _worldHealthError;
    private XsrUiEntityId _worldHealthRoot;
    private readonly Dictionary<string, XsrUiEntityId> _worldHealthIssues = [];
    private void CancelWorldHealth()
    {
        _worldHealthStop?.Cancel(); _worldHealthStop?.Dispose(); _worldHealthStop = null;
        ObserveRetiredInstanceToolRead(_worldHealthRead);
        _worldHealthRead = null; _worldHealth = null; _worldHealthError = null; _worldHealthTarget = default;
    }
    private void BuildWorldHealth(XsrUiEntityId parent, InstanceContentEntry item)
    {
        var target = (Instance: _instance!, World: item.Name);
        if (_worldHealthTarget != target) { CancelWorldHealth(); _worldHealthTarget = target; }
        var health = _worldHealth;
        var nodes = WorldView(parent, ref _worldHealthRoot, "Health", new()
        {
            ["available"] = Flag(_queries.TryResolve(InstanceWorldHealthContract.Read, out _)),
            ["enabled"] = Flag(_worldHealthRead is null),
            ["checking"] = Flag(_worldHealthRead is not null),
            ["hasError"] = Flag(_worldHealthError is not null),
            ["error"] = _worldHealthError ?? "",
            ["hasHealth"] = Flag(health is not null),
            ["healthy"] = Flag(health?.Healthy == true),
            ["damaged"] = Flag(health is { Healthy: false, Complete: true }),
            ["incomplete"] = Flag(health is { Healthy: false, Complete: false }),
            ["regions"] = WorldNumber(health?.Regions ?? 0),
            ["chunks"] = WorldNumber(health?.Chunks ?? 0),
            ["more"] = Flag(health?.DetectedIssues > 10)
        });
        BindWorldAction(nodes, WorldKey("Health"), () =>
        {
            if (_worldHealthRead is not null || !_queries.TryResolve(InstanceWorldHealthContract.Read, out var route)) return;
            _worldHealth = null; _worldHealthError = null; _worldHealthStop?.Dispose(); _worldHealthStop = new();
            _worldHealthRead = _queries.QueryAsync<InstanceWorldHealthQuery, InstanceWorldHealth>(route, new(target.Instance, target.World), cancellationToken: _worldHealthStop.Token).AsTask();
            WakeOnPlatformCompletion(_worldHealthRead); RefreshWorldDetails();
        });
        var issues = (health?.Issues ?? []).Take(10).Select((issue, index) => (Issue: issue, Key: "WorldHealth.Issue." + WorldNumber(index))).ToArray();
        RetireWorldRows(_worldHealthIssues, issues.Select(issue => issue.Key));
        foreach (var (issue, key) in issues) WorldRow(nodes["WorldHealth.Issues"], _worldHealthIssues, key, "Issue", new() { ["key"] = key, ["issue"] = issue });
        _dynamic.Reorder(nodes["WorldHealth.Issues"], issues.Select(issue => _worldHealthIssues[issue.Key]).ToArray());
    }
    private void UpdateWorldHealth()
    {
        if (_worldHealthRead is not { IsCompleted: true } task) return;
        _worldHealthRead = null; _worldHealthStop?.Dispose(); _worldHealthStop = null;
        if (_instance != _worldHealthTarget.Instance || _contentDetail?.Name != _worldHealthTarget.World || _selected != "saves") return;
        if (PendingQuery.Succeeded(task)) _worldHealth = task.Result.Value;
        else _worldHealthError = WorldTerm("health.error");
        RefreshWorldDetails();
    }
}
