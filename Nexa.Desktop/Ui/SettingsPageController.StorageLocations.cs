using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private StorageLocationsSnapshot? _storageLocations;
    private Task<XsrResult<StorageLocationsSnapshot>>? _storageLocationsRead;
    private CancellationTokenSource? _storageLocationsStop;
    private long _storageLocationsRequestRevision = -1;
    private bool _storageLocationsUnavailable;
    private XsrUiEntityId _storageDirectoriesPanel, _storageDirectoryBodyView;
    private string? _storageDirectoryBodyTemplate;
    private readonly Dictionary<string, List<XsrUiEntityId>> _storageDirectoryPathViews = new(StringComparer.Ordinal);

    private void BuildStorageLocations()
    {
        _storageDirectoryBodyView = default; _storageDirectoryBodyTemplate = null; _storageDirectoryPathViews.Clear();
        _storageDirectoriesPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsStorageDirectories.d.nxml"));
        RefreshStorageLocationsPresentation();
    }

    private void RefreshStorageLocationsPresentation()
    {
        if (!_shell.Tree.IsAlive(_storageDirectoriesPanel) || _instanceDirectory is not null || _selected != "storage") return;
        var body = CatalogNodes(_storageDirectoriesPanel)["SettingsStorageDirectories.Body"];
        string template = _storageLocations is not null ? "SettingsStoragePaths.d.nxml" : _storageLocationsUnavailable ? "SettingsStorageUnavailable.d.nxml"
            : _queries.TryResolve(StorageLocationsContract.Query, out _) ? "SettingsStorageLoading.d.nxml" : "SettingsStorageUnregistered.d.nxml";
        var fragment = DesktopMarkup.Dynamic(template);
        bool hadPaths = _storageDirectoryBodyTemplate == "SettingsStoragePaths.d.nxml", hasPaths = _storageLocations is not null;
        if (_shell.Tree.IsAlive(_storageDirectoryBodyView) && hadPaths == hasPaths) _dynamic.Reload(_storageDirectoryBodyView, fragment);
        else
        {
            _storageDirectoryBodyView = _dynamic.Replace(body, fragment); _storageDirectoryPathViews.Clear();
        }
        _storageDirectoryBodyTemplate = template;
        if (_storageLocations is not { } locations) return;
        RefreshStorageDirectoryPaths("Settings", [locations.SettingsDirectory]);
        RefreshStorageDirectoryPaths("Logs", [locations.LogDirectory]);
        RefreshStorageDirectoryPaths("MinecraftLibrary", locations.MinecraftLibraryDirectories.ToArray());
        RefreshStorageDirectoryPaths("ActiveMinecraft", locations.ActiveMinecraftDirectory is { } game ? [game] : [], "SettingsStorageNoGame.d.nxml");
        RefreshStorageDirectoryPaths("Instances", locations.InstancesDirectory is { } instances ? [instances] : [], "SettingsStorageNoGame.d.nxml");
        RefreshStorageDirectoryPaths("SelectedInstance", locations.SelectedInstanceDirectory is { } instance ? [instance] : [], "SettingsStorageNoInstance.d.nxml");
        RefreshStorageDirectoryPaths("JavaRuntimes", locations.JavaRuntimeDirectories.ToArray());
        RefreshStorageDirectoryPaths("Cache", locations.CacheDirectory is { } cache ? [cache] : []);
    }

    private void RefreshStorageDirectoryPaths(string key, string[] paths, string absent = "SettingsStorageUnconfigured.d.nxml")
    {
        if (!_storageDirectoryPathViews.TryGetValue(key, out var rows)) _storageDirectoryPathViews[key] = rows = [];
        int count = Math.Max(1, paths.Length);
        while (rows.Count > count) { _dynamic.Remove(rows[^1]); rows.RemoveAt(rows.Count - 1); }
        var slot = CatalogNodes(_storageDirectoryBodyView)["SettingsStorageValue." + key];
        for (int index = 0; index < count; index++)
        {
            var fragment = paths.Length == 0 ? DesktopMarkup.Dynamic(absent)
                : DesktopMarkup.Dynamic("SettingsStoragePath.d.nxml", new Dictionary<string, string> { ["path"] = paths[index] });
            if (index < rows.Count) _dynamic.Reload(rows[index], fragment);
            else rows.Add(_dynamic.Append(slot, fragment));
        }
    }

    private void UpdateStorageLocations()
    {
        if (!_visible || _instanceDirectory is not null || _selected != "storage") return;
        long currentRevision = _store.TryResolve(MinecraftLibraryContract.StateKey, out var libraryState)
            ? _store.Read<MinecraftLibrarySnapshot>(libraryState).Value?.Revision ?? 0 : 0;
        bool changedScope = _storageLocations is { } previous && previous.LibraryRevision != currentRevision
            || (_storageLocationsRead is not null || _storageLocationsUnavailable) && _storageLocationsRequestRevision != currentRevision;
        if (changedScope)
        {
            CancelStorageLocations();
            // The former root is no longer a current fact, even while the next read is pending.
            _storageLocations = null;
            _storageRebuildPending = true;
        }
        if (_storageLocationsRead is null && !_storageLocationsUnavailable
            && (_storageLocations is null || _storageLocations.LibraryRevision != currentRevision)
            && _queries.TryResolve(StorageLocationsContract.Query, out var route))
        {
            _storageLocationsStop = new();
            _storageLocationsRequestRevision = currentRevision;
            _storageLocationsRead = _queries.QueryAsync<StorageLocationsQuery, StorageLocationsSnapshot>(route, new(),
                cancellationToken: _storageLocationsStop.Token).AsTask();
            ObserveTransfer(_storageLocationsRead);
        }
        if (_storageLocationsRead is not { IsCompleted: true } reading) return;
        _storageLocationsRead = null;
        _storageLocationsStop?.Dispose(); _storageLocationsStop = null;
        if (PendingQuery.Succeeded(reading))
        {
            if (reading.Result.Value!.LibraryRevision != currentRevision) return;
            _storageLocations = reading.Result.Value;
        }
        else if (!reading.IsCanceled) _storageLocationsUnavailable = true;
        _storageRebuildPending = true;
    }

    private void CancelStorageLocations()
    {
        _storageLocationsStop?.Cancel(); _storageLocationsStop?.Dispose(); _storageLocationsStop = null;
        _storageLocationsRead = null; _storageLocationsUnavailable = false;
        _storageLocationsRequestRevision = -1;
    }
}
