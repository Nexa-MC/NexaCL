using System.Collections.Concurrent;
using System.Globalization;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private (string? Instance, string? World) _worldSnapshotTarget;
    private IReadOnlyList<InstanceWorldSnapshot> _worldSnapshotRows = [];
    private Task<XsrResult<IReadOnlyList<InstanceWorldSnapshot>>>? _worldSnapshotRead;
    private Task<XsrResult<InstanceWorldSnapshotVerification>>? _worldSnapshotVerify;
    private Task<XsrResult>? _worldSnapshotWrite;
    private CancellationTokenSource? _worldSnapshotStop;
    private readonly ConcurrentQueue<(long Generation, bool Accepted, InstanceWorldSnapshotRestoreCommand Command)> _worldSnapshotDecisions = new();
    private Guid _worldSnapshotDialog;
    private long _worldSnapshotGeneration;
    private string? _worldSnapshotMessage;
    private bool WorldSnapshotBusy => _worldSnapshotRead is not null || _worldSnapshotVerify is not null || _worldSnapshotWrite is not null || _worldSnapshotDialog != Guid.Empty;

    private XsrUiEntityId _worldSnapshotsRoot;
    private readonly Dictionary<string, XsrUiEntityId> _worldSnapshotViews = [];
    private void BuildWorldSnapshots(XsrUiEntityId parent, InstanceContentEntry item)
    {
        if (item.World is not { } world || !_queries.TryResolve(InstanceWorldSnapshotContract.List, out _)) return;
        var target = (Instance: _instance, World: item.Name);
        if (_worldSnapshotTarget != target) { CancelWorldSnapshots(); _worldSnapshotTarget = target; }
        var nodes = WorldView(parent, ref _worldSnapshotsRoot, "Snapshots", new()
        {
            ["enabled"] = Flag(!WorldSnapshotBusy),
            ["busy"] = Flag(WorldSnapshotBusy),
            ["truncated"] = Flag(_worldSnapshotRows.Count > 16),
            ["hasMessage"] = Flag(_worldSnapshotMessage is not null),
            ["message"] = _worldSnapshotMessage ?? ""
        });
        BindWorldAction(nodes, WorldKey("SnapshotRead"), BeginWorldSnapshotRead);
        BindWorldAction(nodes, WorldKey("SnapshotCapture"), () => BeginWorldSnapshotWrite(InstanceWorldSnapshotContract.Capture,
            new InstanceWorldSnapshotCaptureCommand(target.Instance!, target.World, world.Revision)));
        var rows = _worldSnapshotRows.Take(16).ToArray();
        RetireWorldRows(_worldSnapshotViews, rows.Select(snapshot => snapshot.Identity));
        foreach (var snapshot in rows)
        {
            string key = "WorldSnapshots." + InstanceToolIdentity(snapshot.Identity);
            var row = WorldRow(nodes["WorldSnapshots.Rows"], _worldSnapshotViews, snapshot.Identity, "SnapshotRow", new()
            {
                ["key"] = key,
                ["created"] = snapshot.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                ["files"] = WorldNumber(snapshot.Files),
                ["bytes"] = WorldNumber(snapshot.Bytes),
                ["verify"] = key + ".Verify",
                ["restore"] = key + ".Restore",
                ["enabled"] = Flag(!WorldSnapshotBusy)
            });
            BindWorldAction(row, key + ".Verify", () => BeginWorldSnapshotVerify(snapshot.Identity));
            BindWorldAction(row, key + ".Restore", () => ConfirmWorldSnapshotRestore(snapshot, world.Revision));
        }
        _dynamic.Reorder(nodes["WorldSnapshots.Rows"], rows.Select(snapshot => _worldSnapshotViews[snapshot.Identity]).ToArray());
    }

    private bool WorldSnapshotTargetCurrent => _visible && _selected == "saves" && _instance == _worldSnapshotTarget.Instance
        && _contentDetail?.Name == _worldSnapshotTarget.World && _worldSnapshotTarget.Instance is not null;

    private void BeginWorldSnapshotRead()
    {
        if (!WorldSnapshotTargetCurrent || WorldSnapshotBusy || !_queries.TryResolve(InstanceWorldSnapshotContract.List, out var route)) return;
        _worldSnapshotStop ??= new();
        _worldSnapshotRead = _queries.QueryAsync<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(route,
            new(_worldSnapshotTarget.Instance!, _worldSnapshotTarget.World!), cancellationToken: _worldSnapshotStop.Token).AsTask();
        WakeOnPlatformCompletion(_worldSnapshotRead); RefreshWorldDetails();
    }

    private void BeginWorldSnapshotVerify(string identity)
    {
        if (!WorldSnapshotTargetCurrent || WorldSnapshotBusy || !_queries.TryResolve(InstanceWorldSnapshotContract.Verify, out var route)) return;
        _worldSnapshotStop ??= new();
        _worldSnapshotVerify = _queries.QueryAsync<InstanceWorldSnapshotVerifyQuery, InstanceWorldSnapshotVerification>(route,
            new(_worldSnapshotTarget.Instance!, _worldSnapshotTarget.World!, identity), cancellationToken: _worldSnapshotStop.Token).AsTask();
        WakeOnPlatformCompletion(_worldSnapshotVerify); RefreshWorldDetails();
    }

    private void BeginWorldSnapshotWrite<T>(XsrSemanticId semantic, T command) where T : notnull
    {
        if (!WorldSnapshotTargetCurrent || WorldSnapshotBusy || _managementWrite is not null || !_commands.TryResolve(semantic, out var route)) return;
        _worldSnapshotStop ??= new();
        _worldSnapshotWrite = _commands.Dispatch(route, command, cancellationToken: _worldSnapshotStop.Token).Completion;
        WakeOnPlatformCompletion(_worldSnapshotWrite); RefreshWorldDetails();
    }

    private void ConfirmWorldSnapshotRestore(InstanceWorldSnapshot snapshot, string revision)
    {
        if (!WorldSnapshotTargetCurrent || WorldSnapshotBusy || _managementWrite is not null) return;
        string name = "Snapshot-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + snapshot.Identity[..8];
        var command = new InstanceWorldSnapshotRestoreCommand(_worldSnapshotTarget.Instance!, _worldSnapshotTarget.World!, snapshot.Identity, name, revision);
        long generation = _worldSnapshotGeneration;
        string description = WorldFormat("SnapshotRestoreFormat", new() { ["files"] = WorldNumber(snapshot.Files), ["bytes"] = WorldNumber(snapshot.Bytes), ["name"] = name });
        _worldSnapshotDialog = _feedback.ShowDialog("settings.world-snapshot", WorldTerm("snapshot.restore-title"), description, WorldTerm("snapshot.restore-accept"), WorldTerm("return"),
            accepted => { if (Volatile.Read(ref _worldSnapshotGeneration) == generation) _worldSnapshotDecisions.Enqueue((generation, accepted, command)); }, localizeMessage: false);
        RefreshWorldDetails();
    }

    private void UpdateWorldSnapshots()
    {
        if (!WorldSnapshotTargetCurrent) { if (_worldSnapshotTarget.Instance is not null) CancelWorldSnapshots(); return; }
        bool changed = false;
        while (_worldSnapshotDecisions.TryDequeue(out var decision))
        {
            if (decision.Generation != _worldSnapshotGeneration) continue;
            _worldSnapshotDialog = default;
            if (decision.Accepted) BeginWorldSnapshotWrite(InstanceWorldSnapshotContract.Restore, decision.Command);
            changed = true;
        }
        if (_worldSnapshotRead is { IsCompleted: true } read)
        {
            _worldSnapshotRead = null; changed = true;
            if (PendingQuery.Succeeded(read)) { _worldSnapshotRows = read.Result.Value!; _worldSnapshotMessage = _worldSnapshotRows.Count == 0 ? WorldTerm("snapshot.empty") : null; }
            else if (!read.IsCanceled) _worldSnapshotMessage = WorldTerm("snapshot.read-error");
        }
        if (_worldSnapshotVerify is { IsCompleted: true } verify)
        {
            _worldSnapshotVerify = null; changed = true;
            if (PendingQuery.Succeeded(verify))
            { var report = verify.Result.Value!; _worldSnapshotMessage = report.Ready ? WorldTerm("snapshot.verified") : WorldFormat("SnapshotIssuesFormat", new() { ["paths"] = string.Join('\n', report.MissingOrCorruptPaths.Take(8)) }); }
            else if (!verify.IsCanceled) _worldSnapshotMessage = WorldTerm("snapshot.verify-error");
        }
        if (_worldSnapshotWrite is { IsCompleted: true } write)
        {
            _worldSnapshotWrite = null; changed = true;
            if (PendingQuery.Succeeded(write)) { _worldSnapshotMessage = WorldTerm("snapshot.complete"); BeginWorldSnapshotRead(); RefreshManagement(); ManagementChanged?.Invoke(); }
            else if (!write.IsCanceled) _worldSnapshotMessage = write.IsCompletedSuccessfully ? write.Result.Error?.Message ?? WorldTerm("snapshot.write-error") : WorldTerm("snapshot.write-error");
        }
        if (changed) RefreshWorldDetails();
    }

    private void CancelWorldSnapshots()
    {
        Interlocked.Increment(ref _worldSnapshotGeneration); _worldSnapshotStop?.Cancel(); _worldSnapshotStop?.Dispose(); _worldSnapshotStop = null;
        ObserveRetiredInstanceToolRead(_worldSnapshotRead); ObserveRetiredInstanceToolRead(_worldSnapshotVerify); ObserveRetiredInstanceToolRead(_worldSnapshotWrite);
        _worldSnapshotRead = null; _worldSnapshotVerify = null; _worldSnapshotWrite = null; _worldSnapshotRows = []; _worldSnapshotTarget = default; _worldSnapshotMessage = null;
        if (_worldSnapshotDialog != Guid.Empty) _feedback.DismissDialog(_worldSnapshotDialog); _worldSnapshotDialog = default;
        while (_worldSnapshotDecisions.TryDequeue(out _)) { }
    }
}
