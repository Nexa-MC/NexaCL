using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private ResourceModpackOnline? _modpackOnline;
    private ResourceModpackPreview? _modpackOnlinePreview;
    private Task<XsrResult<ResourceModpackOnline>>? _modpackOnlineRead;
    private Task<XsrResult<ResourceModpackPreview>>? _modpackOnlinePreviewRead;
    private CancellationTokenSource? _modpackOnlineStop;
    private ResourceModpackPreviewQuery? _modpackOnlinePreviewQuery;
    private readonly Dictionary<string, XsrUiEntityId> _modpackOnlineRows = [];
    private bool _modpackOnlineStarted;
    private string? _modpackOnlineError;

    private void InvalidateModpackPreview() { _modpackPreview = null; _modpackOnlinePreview = null; _modpackOnlinePreviewQuery = null; }
    private void CancelModpackOnline()
    {
        _modpackOnlineStop?.Cancel(); _modpackOnlineStop?.Dispose(); _modpackOnlineStop = null;
        ObserveRetiredInstanceToolRead(_modpackOnlineRead); ObserveRetiredInstanceToolRead(_modpackOnlinePreviewRead);
        _modpackOnlineRead = null; _modpackOnlinePreviewRead = null; _modpackOnline = null; _modpackOnlinePreview = null;
        _modpackOnlinePreviewQuery = null; _modpackOnlineStarted = false; _modpackOnlineError = null;
        RetireWorldRows(_modpackOnlineRows, []);
    }
    private void StartModpackOnlineRead(bool refresh)
    {
        if (_instance is null || _resourceQueries?.TryResolve(ResourceModpackContract.Online, out var route) != true) return;
        CaptureModpackUpdateDrafts();
        _modpackOnlineStop?.Cancel(); _modpackOnlineStop?.Dispose(); _modpackOnlineStop = new();
        ObserveRetiredInstanceToolRead(_modpackOnlineRead); ObserveRetiredInstanceToolRead(_modpackOnlinePreviewRead);
        _modpackOnlinePreviewRead = null; _modpackOnlineError = null; _modpackOnlineStarted = true;
        if (refresh) InvalidateModpackPreview();
        var query = new ResourceModpackOnlineQuery(_instance, string.IsNullOrWhiteSpace(_modpackOriginal) ? null : _modpackOriginal.Trim(), Refresh: refresh);
        _modpackOnlineRead = _resourceQueries.QueryAsync<ResourceModpackOnlineQuery, ResourceModpackOnline>(route, query,
            cancellationToken: _modpackOnlineStop.Token).AsTask(); WakeOnPlatformCompletion(_modpackOnlineRead);
    }
    private void UpdateModpackOnline()
    {
        if (!_modpackOnlineStarted) StartModpackOnlineRead(false);
        if (_modpackOnlineRead is { IsCompleted: true } read)
        {
            _modpackOnlineRead = null;
            if (read.IsCompletedSuccessfully && read.Result.IsSuccess && read.Result.Value!.Request.InstanceDirectory == _instance)
                _modpackOnline = read.Result.Value;
            else
            {
                _modpackOnlineError = read.IsCompletedSuccessfully ? read.Result.Error?.Message : read.Exception?.GetBaseException().Message;
                if (_modpackOnline is { } retained) _modpackOnline = retained with { IsStale = true, UpdateVersion = null };
            }
        }
        if (_modpackOnlinePreviewRead is { IsCompleted: true } prepared)
        {
            _modpackOnlinePreviewRead = null;
            if (prepared.IsCompletedSuccessfully && prepared.Result.IsSuccess && _modpackOnlinePreviewQuery is { } selected && prepared.Result.Value!.Request == selected
                && selected.Instance.InstanceDirectory == _instance && _modpackOptional == selected.IncludeOptional
                && _modpackReplace == selected.ReplaceModifiedMods)
            {
                _modpackOnlinePreview = prepared.Result.Value; _modpackPreview = prepared.Result.Value.Installation;
                ShowModpackPreview(_modpackPreview);
            }
            else _modpackUpdateStatus = prepared.IsCompletedSuccessfully ? prepared.Result.Error?.Message ?? WorldTerm("modpack.update.changed")
                : prepared.Exception?.GetBaseException().Message ?? WorldTerm("modpack.update.failed");
        }
    }
    private string ModpackOnlineText()
    {
        if (_modpackOnline is not { } info) return _modpackOnlineRead is not null ? WorldTerm("modpack.online.reading")
            : _modpackOnlineError ?? WorldTerm("modpack.online.unavailable");
        string text = info.Project is { } project ? WorldFormat("ModpackOnlineInfo", new()
        {
            ["name"] = project.DisplayName,
            ["source"] = project.SourceLabel,
            ["version"] = info.InstalledVersion?.Number ?? WorldTerm("unrecorded"),
            ["description"] = project.DisplayDescription.Length > 1200 ? project.DisplayDescription[..1200] : project.DisplayDescription
        }) : "";
        string? notice = _modpackOnlineError ?? info.Notice;
        if (notice is not null) text += "\n" + notice;
        if (info.IsStale) text += "\n" + WorldTerm("modpack.online.stale");
        else if (info.Project is not null && info.InstalledVersion is not null && info.Versions.Count == 0 && string.IsNullOrWhiteSpace(info.Notice)) text += "\n" + WorldTerm("modpack.online.latest");
        return text;
    }
    private void BuildModpackOnline(Dictionary<string, XsrUiEntityId> nodes, bool enabled)
    {
        BindWorldAction(nodes, "ModpackUpdate.OnlineRefresh", () => { if (_modpackOnlineRead is null && enabled) StartModpackOnlineRead(true); });
        BindWorldAction(nodes, "ModpackUpdate.OnlineProject", () =>
        {
            if (_modpackOnline?.Project is { } project && Uri.TryCreate(project.Website, UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0) _openResourceLink?.Invoke(uri);
        });
        var versions = _modpackOnline?.Versions.Take(20).ToArray() ?? [];
        string Key(ResourceVersion version) => InstanceToolIdentity(version.Provider + ":" + version.ProjectId + ":" + version.Id);
        RetireWorldRows(_modpackOnlineRows, versions.Select(Key));
        foreach (var version in versions)
        {
            string key = Key(version), action = "ModpackOnline.Preview." + key;
            var row = WorldRow(nodes["ModpackUpdate.OnlineVersions"], _modpackOnlineRows, key, "ModpackOnlineVersion", new()
            {
                ["name"] = version.Name,
                ["details"] = WorldFormat("ModpackOnlineVersionLabel", new() { ["version"] = version.Number, ["games"] = string.Join(", ", version.Games), ["channel"] = version.Channel }),
                ["action"] = action,
                ["enabled"] = Flag(enabled && _modpackOnline is { IsStale: false } && _modpackOnlineRead is null)
            });
            BindWorldAction(row, action, () =>
            {
                CaptureModpackUpdateDrafts();
                if (!enabled || _modpackOnline is not { IsStale: false, InstalledArchiveSha256: { } installed } online
                    || _modpackOnlinePreviewRead is not null || _resourceQueries?.TryResolve(ResourceModpackContract.Preview, out var route) != true) return;
                InvalidateModpackPreview();
                _modpackOnlinePreviewQuery = new(online.Request with { OriginalArchivePath = string.IsNullOrWhiteSpace(_modpackOriginal) ? online.OriginalArchivePath : _modpackOriginal.Trim() },
                    new(version.Provider, version.ProjectId), version.Id, installed, _modpackOptional, _modpackReplace);
                _modpackOnlineStop ??= new();
                _modpackOnlinePreviewRead = _resourceQueries.QueryAsync<ResourceModpackPreviewQuery, ResourceModpackPreview>(route,
                    _modpackOnlinePreviewQuery, cancellationToken: _modpackOnlineStop.Token).AsTask();
                _modpackUpdateStatus = WorldTerm("modpack.online.preparing"); WakeOnPlatformCompletion(_modpackOnlinePreviewRead);
            });
        }
        _dynamic.Reorder(nodes["ModpackUpdate.OnlineVersions"], versions.Select(version => _modpackOnlineRows[Key(version)]).ToArray());
    }
}
