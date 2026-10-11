using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private StorageLocationsSnapshot? _storageLocations;
    private Task<XsrResult<StorageLocationsSnapshot>>? _storageLocationsRead;
    private CancellationTokenSource? _storageLocationsStop;
    private long _storageLocationsRequestRevision = -1;
    private bool _storageLocationsUnavailable;

    private void BuildStorageLocations()
    {
        var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsStorageDirectories.d.nxml"));
        var body = CatalogNodes(root)["SettingsStorageDirectories.Body"];
        if (_storageLocations is not { } locations)
        {
            string template = _storageLocationsUnavailable ? "SettingsStorageUnavailable.d.nxml"
                : _queries.TryResolve(StorageLocationsContract.Query, out _) ? "SettingsStorageLoading.d.nxml" : "SettingsStorageUnregistered.d.nxml";
            _dynamic.Append(body, DesktopMarkup.Dynamic(template));
            return;
        }
        var rows = _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsStoragePaths.d.nxml"));
        var nodes = CatalogNodes(rows);
        Paths("Settings", [locations.SettingsDirectory]);
        Paths("Logs", [locations.LogDirectory]);
        Paths("MinecraftLibrary", locations.MinecraftLibraryDirectories);
        Paths("ActiveMinecraft", locations.ActiveMinecraftDirectory is { } game ? [game] : [], "SettingsStorageNoGame.d.nxml");
        Paths("Instances", locations.InstancesDirectory is { } instances ? [instances] : [], "SettingsStorageNoGame.d.nxml");
        Paths("SelectedInstance", locations.SelectedInstanceDirectory is { } instance ? [instance] : [], "SettingsStorageNoInstance.d.nxml");
        Paths("JavaRuntimes", locations.JavaRuntimeDirectories);
        Paths("Cache", locations.CacheDirectory is { } cache ? [cache] : []);

        void Paths(string key, IReadOnlyList<string> paths, string absent = "SettingsStorageUnconfigured.d.nxml")
        {
            var slot = nodes["SettingsStorageValue." + key];
            if (paths.Count == 0) _dynamic.Append(slot, DesktopMarkup.Dynamic(absent));
            else foreach (string path in paths)
                _dynamic.Append(slot, DesktopMarkup.Dynamic("SettingsStoragePath.d.nxml", new Dictionary<string, string> { ["path"] = path }));
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
