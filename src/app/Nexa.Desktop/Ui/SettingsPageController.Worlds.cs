using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    internal Func<Task<string?>>? SelectDataPackFileAsync { get; set; }
    private Task<string?>? _worldImportPicker;
    private (string Instance, string World, string Revision) _worldImportTarget;
    private Task<XsrResult<InstanceWorldMetadata>>? _worldMetadataRead;
    private (string Instance, string World) _worldMetadataTarget;
    private bool _worldMetadataFailed;
    private CancellationTokenSource? _worldMetadataStop;
    private XsrUiEntityId _worldDetailsRoot, _worldDetailsSlot;
    private readonly Dictionary<string, XsrUiEntityId> _worldPackViews = [];

    private void BuildWorldDetails(XsrUiEntityId parent, InstanceContentEntry item)
    {
        _worldDetailsSlot = parent;
        var target = (_instance!, item.Name);
        if (_worldMetadataTarget != target)
        {
            CancelWorldDetails(); _worldMetadataTarget = target;
        }
        var world = item.World;
        if (world is null && !_worldMetadataFailed && _worldMetadataRead is null && _queries.TryResolve(InstanceWorldContract.Read, out var route))
        {
            _worldMetadataStop = new();
            _worldMetadataRead = _queries.QueryAsync<InstanceWorldMetadataQuery, InstanceWorldMetadata>(route, new(_instance!, item.Name), cancellationToken: _worldMetadataStop.Token).AsTask();
            WakeOnPlatformCompletion(_worldMetadataRead);
        }
        var nodes = WorldView(parent, ref _worldDetailsRoot, "Details", new()
        {
            ["reading"] = Flag(world is null && _worldMetadataRead is not null),
            ["unreadable"] = Flag(world is null && _worldMetadataRead is null),
            ["hasWorld"] = Flag(world is not null),
            ["name"] = world?.LevelName ?? "",
            ["hasDataVersion"] = Flag(world?.DataVersion is not null),
            ["dataVersion"] = world?.DataVersion is { } dataVersion ? WorldNumber(dataVersion) : "",
            ["hasPlayed"] = Flag(world?.LastPlayed is not null),
            ["played"] = world?.LastPlayed?.ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.CurrentCulture) ?? "",
            ["hasSeed"] = Flag(world?.Seed is not null),
            ["seed"] = world?.Seed is { } seed ? WorldNumber(seed) : "",
            ["hasMode"] = Flag(world?.GameMode is not null),
            ["mode"] = world?.GameMode is { } mode ? WorldTerms.Value.ContainsKey("mode." + WorldNumber(mode)) ? WorldTerm("mode." + WorldNumber(mode)) : WorldNumber(mode) : "",
            ["hasDifficulty"] = Flag(world?.Difficulty is not null),
            ["difficulty"] = world?.Difficulty is { } difficulty ? WorldTerms.Value.ContainsKey("difficulty." + WorldNumber(difficulty)) ? WorldTerm("difficulty." + WorldNumber(difficulty)) : WorldNumber(difficulty) : "",
            ["hardcore"] = WorldTerm(world?.Hardcore == true ? "yes" : "no"),
            ["incomplete"] = Flag(world is { SizeComplete: false }),
            ["locked"] = Flag(world?.Locked == true),
            ["unlocked"] = Flag(world is { Locked: false }),
            ["backup"] = Flag(world is not null && _commands.TryResolve(InstanceWorldContract.Backup, out _)),
            ["import"] = Flag(world is { Locked: false, DataPacksSupported: true } && SelectDataPackFileAsync is not null),
            ["unsupported"] = Flag(world is { DataPacksSupported: false }),
            ["supported"] = Flag(world?.DataPacksSupported == true),
            ["emptyPacks"] = Flag(world is { DataPacksSupported: true, DataPacks.Count: 0 })
        });
        BuildWorldHealth(nodes["WorldDetails.Health"], item);
        if (world is null) return;
        string instance = _instance!, name = item.Name;
        BindWorldAction(nodes, WorldKey(world.Locked ? "Unlock" : "Lock"), () => DispatchWorld(InstanceWorldContract.SetLock,
            new InstanceWorldLockCommand(instance, name, !world.Locked, world.Revision)));
        BindWorldAction(nodes, WorldKey("Backup"), () => DispatchWorld(InstanceWorldContract.Backup, new InstanceWorldBackupCommand(instance, name, world.Revision)));
        BindWorldAction(nodes, WorldKey("Copy"), () => DispatchWorld(InstanceWorldContract.Copy,
            new InstanceWorldCopyCommand(instance, name, name + "-copy-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture), world.Revision)));
        BindWorldAction(nodes, WorldKey("Import"), () =>
        {
            if (_worldImportPicker is { IsCompleted: false } || _managementWrite is not null || SelectDataPackFileAsync is null) return;
            _worldImportTarget = (instance, name, world.Revision); _worldImportPicker = SelectDataPackFileAsync(); WakeOnPlatformCompletion(_worldImportPicker);
        });
        BuildWorldSnapshots(nodes["WorldDetails.Snapshots"], item);
        var packs = world.DataPacksSupported ? world.DataPacks.Select((pack, index) => (Pack: pack, Key: "WorldPack." + InstanceToolIdentity(pack.Id + "\0" + pack.TrashName + "\0" + WorldNumber(index)))).ToArray() : [];
        RetireWorldRows(_worldPackViews, packs.Select(pack => pack.Key));
        foreach (var (pack, key) in packs)
        {
            var row = WorldRow(nodes["WorldDetails.Packs"], _worldPackViews, key, "DataPack", new()
            {
                ["key"] = key,
                ["name"] = pack.Name,
                ["state"] = WorldTerm(pack.Trashed ? "pack.trashed" : pack.Enabled ? "pack.enabled" : "pack.disabled"),
                ["restore"] = key + ".Restore",
                ["enable"] = key + ".Enable",
                ["disable"] = key + ".Disable",
                ["remove"] = key + ".Remove",
                ["canRestore"] = Flag(!world.Locked && pack.Trashed),
                ["canEnable"] = Flag(!world.Locked && !pack.Trashed && pack.Id != "vanilla" && !pack.Enabled),
                ["canDisable"] = Flag(!world.Locked && !pack.Trashed && pack.Id != "vanilla" && pack.Enabled),
                ["canRemove"] = Flag(!world.Locked && !pack.Trashed && !pack.BuiltIn && !pack.Enabled)
            });
            BindWorldAction(row, key + ".Restore", () => DispatchWorld(InstanceWorldContract.RestoreDataPack, new InstanceWorldDataPackRestoreCommand(instance, name, pack.TrashName, world.Revision)));
            foreach (string action in new[] { ".Enable", ".Disable" }) BindWorldAction(row, key + action, () => DispatchWorld(InstanceWorldContract.SetDataPackEnabled, new InstanceWorldDataPackCommand(instance, name, pack.Id, !pack.Enabled, world.Revision)));
            BindWorldAction(row, key + ".Remove", () => DispatchWorld(InstanceWorldContract.RemoveDataPack, new InstanceWorldDataPackRemoveCommand(instance, name, pack.Name, world.Revision)));
        }
        _dynamic.Reorder(nodes["WorldDetails.Packs"], packs.Select(pack => _worldPackViews[pack.Key]).ToArray());
    }
    private void CancelWorldDetails()
    {
        _worldMetadataStop?.Cancel(); _worldMetadataStop?.Dispose(); _worldMetadataStop = null;
        ObserveRetiredInstanceToolRead(_worldMetadataRead); ObserveRetiredInstanceToolRead(_worldImportPicker);
        _worldMetadataRead = null; _worldImportPicker = null; _worldMetadataTarget = default; _worldImportTarget = default; _worldMetadataFailed = false;
    }
    private void RefreshWorldDetails()
    {
        if (_visible && _selected == "saves" && _contentDetail is { } item && _shell.Tree.IsAlive(_worldDetailsSlot)) BuildWorldDetails(_worldDetailsSlot, item);
    }
    private void DispatchWorld<T>(XsrSemanticId id, T command) where T : notnull
    {
        if (_instance is null || _managementWrite is not null || !_commands.TryResolve(id, out var route)) return;
        _managementWriteInstance = _instance; _managementWrite = _commands.Dispatch(route, command).Completion; WakeOnPlatformCompletion(_managementWrite);
    }
    private void UpdateWorldPlatformAction()
    {
        UpdateWorldPresentation(); UpdateWorldHealth();
        if (_worldMetadataRead is { IsCompleted: true } reading)
        {
            _worldMetadataRead = null; _worldMetadataStop?.Dispose(); _worldMetadataStop = null; _worldMetadataFailed = !PendingQuery.Succeeded(reading);
            if (_instance == _worldMetadataTarget.Instance && _contentDetail?.Name == _worldMetadataTarget.World && _selected == "saves")
            {
                if (!_worldMetadataFailed) _contentDetail = _contentDetail with { World = reading.Result.Value };
                RefreshWorldDetails();
            }
        }
        if (_worldImportPicker is { IsCompleted: true } picker)
        {
            _worldImportPicker = null;
            if (picker.IsCompletedSuccessfully && picker.Result is { } source && _instance == _worldImportTarget.Instance && _contentDetail?.Name == _worldImportTarget.World)
                DispatchWorld(InstanceWorldContract.ImportDataPack, new InstanceWorldDataPackImportCommand(_worldImportTarget.Instance, _worldImportTarget.World, source, _worldImportTarget.Revision));
            else if (!picker.IsCompletedSuccessfully) { _ = picker.Exception; _feedback.Error(WorldTerm("world.import-error")); }
        }
    }
}
