using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private XsrQueryRouter? _modpackQueries;
    private XsrCommandRouter? _modpackCommands;
    private Func<Task<string?>>? _pickModpack;
    private Task<string?>? _modpackPicking;
    private bool _modpackPickOriginal, _modpackOptional, _modpackReplace;
    private string _modpackArchive = "", _modpackOriginal = "", _modpackUpdateStatus = "";
    private XsrUiEntityId _modpackUpdateRoot, _modpackArchiveInput, _modpackOriginalInput, _modpackOverviewHost;
    private MinecraftModpackUpdatePreview? _modpackPreview;
    private Task<XsrResult<MinecraftModpackUpdatePreview>>? _modpackPreviewRead;
    private Task<XsrResult>? _modpackApplying;
    private CancellationTokenSource? _modpackUpdateStop;
    private string? _modpackUpdateInstance;
    private bool _modpackDraftsNeedReset;

    internal void ConfigureModpackUpdates(XsrQueryRouter queries, XsrCommandRouter commands, Func<Task<string?>>? picker = null)
    { _modpackQueries = queries; _modpackCommands = commands; _pickModpack = picker; }

    private void CancelModpackUpdate()
    {
        CancelModpackOnline();
        _modpackUpdateStop?.Cancel(); _modpackUpdateStop?.Dispose(); _modpackUpdateStop = null;
        ObserveRetiredInstanceToolRead(_modpackPreviewRead); ObserveRetiredInstanceToolRead(_modpackPicking);
        _modpackPreviewRead = null; _modpackPicking = null; _modpackPreview = null;
        _modpackApplying = null;
        _modpackUpdateInstance = null; _modpackArchive = _modpackOriginal = _modpackUpdateStatus = "";
        _modpackOptional = _modpackReplace = false;
        _modpackArchiveInput = _modpackOriginalInput = default;
        _modpackDraftsNeedReset = true;
    }

    private void CaptureModpackUpdateDrafts()
    {
        string Draft(XsrUiEntityId entity, string fallback) => _shell.Tree.IsAlive(entity)
            ? _shell.Tree.GetComponent<XsrUiTextInput>(entity)!.ReadDraft() : fallback;
        string archive = Draft(_modpackArchiveInput, _modpackArchive), original = Draft(_modpackOriginalInput, _modpackOriginal);
        if (archive != _modpackArchive || original != _modpackOriginal) InvalidateModpackPreview();
        _modpackArchive = archive; _modpackOriginal = original;
    }

    private MinecraftModpackUpdateQuery CurrentModpackUpdateQuery() => new(_instance!, _modpackArchive.Trim(),
        string.IsNullOrWhiteSpace(_modpackOriginal) ? null : _modpackOriginal.Trim(), _modpackOptional, _modpackReplace);

    private void UpdateModpackUpdate()
    {
        if (_selected != "overview" || _instance is null)
        { if (_modpackUpdateInstance is not null) CancelModpackUpdate(); return; }
        if (_modpackUpdateInstance != _instance) { CancelModpackUpdate(); _modpackUpdateInstance = _instance; }
        CaptureModpackUpdateDrafts();
        UpdateModpackOnline();
        if (_modpackApplying is { IsCompleted: true } applied)
        {
            _modpackApplying = null;
            _modpackUpdateStatus = applied.IsCompletedSuccessfully && applied.Result.IsSuccess ? WorldTerm("modpack.update.complete")
                : applied.IsCompletedSuccessfully ? applied.Result.Error?.Message ?? WorldTerm("modpack.update.failed") : WorldTerm("modpack.update.failed");
            if (applied.IsCompletedSuccessfully && applied.Result.IsSuccess) StartModpackOnlineRead(true);
        }
        if (_modpackPicking is { IsCompleted: true } picking)
        {
            _modpackPicking = null;
            if (picking.IsCompletedSuccessfully && picking.Result is { } path)
            {
                if (_modpackPickOriginal) { _modpackOriginal = path; _shell.Renderer.SetTextInputValue(_modpackOriginalInput, path); }
                else { _modpackArchive = path; _shell.Renderer.SetTextInputValue(_modpackArchiveInput, path); }
                InvalidateModpackPreview();
                if (_modpackPickOriginal) StartModpackOnlineRead(true);
            }
            else if (picking.IsFaulted) _modpackUpdateStatus = WorldFormat("ModpackUpdatePickError", new() { ["error"] = picking.Exception!.GetBaseException().Message });
        }
        if (_modpackPreviewRead is { IsCompleted: true } read)
        {
            _modpackPreviewRead = null;
            if (read.IsCompletedSuccessfully && read.Result.IsSuccess && read.Result.Value!.Request == CurrentModpackUpdateQuery())
            {
                _modpackPreview = read.Result.Value;
                ShowModpackPreview(_modpackPreview!);
            }
            else _modpackUpdateStatus = read.IsCompletedSuccessfully ? read.Result.Error?.Message ?? WorldTerm("modpack.update.changed") : read.Exception?.GetBaseException().Message ?? WorldTerm("modpack.update.canceled");
        }
        if (_management is { } snapshot) BuildModpackUpdate(snapshot);
    }

    private void ShowModpackPreview(MinecraftModpackUpdatePreview preview)
    {
        _modpackUpdateStatus = WorldFormat("ModpackUpdateSummary", new()
        {
            ["name"] = preview.Pack.Name,
            ["version"] = preview.Pack.Version,
            ["game"] = preview.Pack.Game,
            ["added"] = WorldNumber(preview.Added.Count),
            ["updated"] = WorldNumber(preview.Updated.Count),
            ["removed"] = WorldNumber(preview.Removed.Count),
            ["preserved"] = WorldNumber(preview.Preserved.Count)
        })
                    + string.Join('\n', preview.Added.Select(path => "+ " + path).Concat(preview.Updated.Select(path => "↻ " + path))
                        .Concat(preview.Removed.Select(path => "− " + path)).Concat(preview.Preserved.Select(path => WorldFormat("ModpackUpdatePreserved", new() { ["path"] = path }))).Take(50))
                    + (preview.Conflicts.Count > 0 ? "\n" + WorldTerm("modpack.update.conflicts") : "");
    }

    private void BuildModpackUpdate(Nexa.Services.Minecraft.Management.InstanceManagementSnapshot snapshot)
    {
        if (_modpackQueries?.TryResolve(MinecraftModpackUpdateContract.Preview, out _) != true || !_shell.Tree.IsAlive(_modpackOverviewHost)) return;
        CaptureModpackUpdateDrafts();
        bool initialize = _modpackDraftsNeedReset || !_shell.Tree.IsAlive(_modpackUpdateRoot) || _shell.Tree.Parent(_modpackUpdateRoot) != _modpackOverviewHost;
        bool enabled = _managementWrite is null && _modpackPreviewRead is null && _modpackPicking is null && _modpackOnlinePreviewRead is null;
        var nodes = WorldView(_modpackOverviewHost, ref _modpackUpdateRoot, "ModpackUpdate", new()
        {
            ["loading"] = Flag(_modpackPreviewRead is not null || _modpackOnlinePreviewRead is not null || _modpackOnlineRead is not null),
            ["picker"] = Flag(_pickModpack is not null),
            ["enabled"] = Flag(enabled),
            ["canApply"] = Flag(enabled && _modpackPreview is { Conflicts.Count: 0 }),
            ["optional"] = Flag(_modpackOptional),
            ["replace"] = Flag(_modpackReplace),
            ["online"] = Flag(_resourceQueries?.TryResolve(Nexa.Services.Resources.ResourceModpackContract.Online, out _) == true),
            ["onlineInfo"] = ModpackOnlineText(),
            ["onlineRefresh"] = Flag(enabled && _modpackOnlineRead is null),
            ["onlineProject"] = Flag(_modpackOnline?.Project is not null),
            ["status"] = _modpackPreviewRead is not null ? WorldTerm("modpack.update.reading") : _modpackUpdateStatus
        });
        _modpackArchiveInput = nodes["ModpackUpdate.Archive"]; _modpackOriginalInput = nodes["ModpackUpdate.Original"];
        if (initialize)
        {
            _modpackDraftsNeedReset = false;
            _shell.Renderer.SetTextInputValue(_modpackArchiveInput, _modpackArchive);
            _shell.Renderer.SetTextInputValue(_modpackOriginalInput, _modpackOriginal);
        }
        void Bind(string key, Action action) => BindWorldAction(nodes, key, action);
        void Pick(bool original)
        {
            if (!enabled || _pickModpack is null) return;
            _modpackPickOriginal = original; _modpackPicking = _pickModpack(); WakeOnPlatformCompletion(_modpackPicking);
        }
        Bind("ModpackUpdate.Pick", () => Pick(false)); Bind("ModpackUpdate.PickOriginal", () => Pick(true));
        Bind("ModpackUpdate.Optional", () => { _modpackOptional = !_modpackOptional; InvalidateModpackPreview(); });
        Bind("ModpackUpdate.Replace", () => { _modpackReplace = !_modpackReplace; InvalidateModpackPreview(); });
        Bind("ModpackUpdate.Preview", () =>
        {
            CaptureModpackUpdateDrafts();
            if (!enabled || _instance is null || !_modpackQueries.TryResolve(MinecraftModpackUpdateContract.Preview, out var route)) return;
            InvalidateModpackPreview(); _modpackUpdateStop?.Cancel(); _modpackUpdateStop?.Dispose(); _modpackUpdateStop = new();
            _modpackPreviewRead = _modpackQueries.QueryAsync<MinecraftModpackUpdateQuery, MinecraftModpackUpdatePreview>(route,
                CurrentModpackUpdateQuery(), cancellationToken: _modpackUpdateStop.Token).AsTask(); WakeOnPlatformCompletion(_modpackPreviewRead);
        });
        Bind("ModpackUpdate.Apply", () =>
        {
            CaptureModpackUpdateDrafts();
            if (!enabled || _modpackPreview is not { Conflicts.Count: 0 } preview || _instance != snapshot.InstanceDirectory) return;
            Task<XsrResult> applying;
            if (_modpackOnlinePreview is { } online && ReferenceEquals(online.Installation, preview))
            {
                if (_resourceCommands?.TryResolve(Nexa.Services.Resources.ResourceModpackContract.Update, out var onlineRoute) != true) return;
                applying = _resourceCommands.Dispatch(onlineRoute, new Nexa.Services.Resources.ResourceModpackUpdateCommand(online)).Completion;
            }
            else
            {
                if (_modpackCommands?.TryResolve(MinecraftModpackUpdateContract.Apply, out var route) != true) return;
                applying = _modpackCommands.Dispatch(route, new MinecraftModpackUpdateCommand(preview)).Completion;
            }
            _managementWriteInstance = _instance;
            _managementWrite = applying;
            _modpackApplying = _managementWrite;
            _modpackPreview = null; _modpackUpdateStatus = WorldTerm("modpack.update.queued"); WakeOnPlatformCompletion(_managementWrite);
        });
        BuildModpackOnline(nodes, enabled);
    }
}
