using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.UI.Next;
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
        var group = FormGroup(_sections, "SettingsStorageDirectories", "目录位置");
        if (_storageLocations is not { } locations)
        {
            Text(group, _storageLocationsUnavailable ? "目录信息暂不可用，请重新打开此页。"
                : _queries.TryResolve(StorageLocationsContract.Query, out _) ? "正在读取目录信息…" : "目录信息尚未接入。", 12, Muted, 30);
            return;
        }
        StoragePathRow(group, "Settings", "启动器设置", [locations.SettingsDirectory]);
        StoragePathRow(group, "Logs", "运行日志", [locations.LogDirectory]);
        StoragePathRow(group, "MinecraftLibrary", "游戏目录", locations.MinecraftLibraryDirectories);
        StoragePathRow(group, "ActiveMinecraft", "当前游戏目录", locations.ActiveMinecraftDirectory is { } root ? [root] : [], "尚未选择游戏目录");
        StoragePathRow(group, "Instances", "版本目录", locations.InstancesDirectory is { } instances ? [instances] : [], "尚未选择游戏目录");
        StoragePathRow(group, "SelectedInstance", "当前版本目录", locations.SelectedInstanceDirectory is { } instance ? [instance] : [], "尚未选择版本");
        StoragePathRow(group, "JavaRuntimes", "托管 Java 目录", locations.JavaRuntimeDirectories);
        StoragePathRow(group, "Cache", "下载缓存", locations.CacheDirectory is { } cache ? [cache] : []);
    }

    private void StoragePathRow(XsrUiEntityId parent, string key, string label, IReadOnlyList<string> paths, string absent = "未配置")
    {
        var row = Stack(parent, "SettingsStoragePath." + key, XsrUiOrientation.Horizontal, 16);
        var element = _shell.Tree.GetComponent<XsrUiElement>(row)!;
        element.Padding = new(0, 9, 0, 9);
        var caption = Text(row, label, 13, Ink, 26, 500);
        var captionElement = _shell.Tree.GetComponent<XsrUiElement>(caption)!;
        captionElement.Width = 136; captionElement.Height = null;
        var captionText = _shell.Tree.GetComponent<XsrUiText>(caption)!;
        captionText.MaxLines = 0; captionText.TrimOverflow = false;
        _shell.Tree.GetComponent<XsrUiVisualStyle>(caption)!.WrapText = true;
        var values = Stack(row, "SettingsStorageValue." + key, XsrUiOrientation.Vertical, 5);
        _shell.Tree.GetComponent<XsrUiElement>(values)!.Weight = 1;
        foreach (string path in paths.Count == 0 ? [absent] : paths)
        {
            var text = Text(values, path, 12, Muted, 42);
            _shell.Tree.GetComponent<XsrUiElement>(text)!.Height = null;
            _shell.Tree.GetComponent<XsrUiText>(text)!.MaxLines = 0;
            _shell.Tree.GetComponent<XsrUiText>(text)!.TrimOverflow = false;
            var style = _shell.Tree.GetComponent<XsrUiVisualStyle>(text)!;
            style.WrapText = true; style.TextAlignment = XsrUiTextAlignment.End;
            if (paths.Count > 0) DesktopLiteralText.Preserve(_shell.Tree, text);
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
