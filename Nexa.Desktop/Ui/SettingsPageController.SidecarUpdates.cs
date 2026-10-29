using System.Globalization;
using Nexa.Services.Updates;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private ISidecarUpdateControl? _sidecarUpdates;
    private Task? _sidecarUpdateWork, _sidecarProgressDelay;
    private SidecarUpdateSnapshot? _sidecarSnapshot;
    private Action? _restartForSidecar;
    private readonly Dictionary<string, XsrUiEntityId> _sidecarUpdateRows = new(StringComparer.Ordinal);
    internal void ConfigureSidecarUpdates(ISidecarUpdateControl service, Action restart)
    { _sidecarUpdates = service; _restartForSidecar = restart; _sidecarSnapshot = service.Snapshot; }
    private string SidecarUpdateStatusText()
    {
        string phase = _sidecarSnapshot?.Phase ?? "initial";
        if (phase == "initial" && _sidecarSnapshot?.Items.Count == 0) phase = "none";
        return CustomCopy("sidecarUpdate." + phase);
    }
    private void CheckSidecarUpdates()
    {
        if (_sidecarUpdates is null || _sidecarUpdateWork is not null) return;
        _sidecarUpdateWork = _sidecarUpdates.CheckAsync(_updateQuery?.Channel is "stable" or "beta" ? _updateQuery.Channel : "alpha", _updateStop.Token);
        WakeOnPlatformCompletion(_sidecarUpdateWork);
    }
    private void UpdateSidecarUpdates()
    {
        if (_sidecarUpdates is null) return;
        bool completed = false;
        if (_sidecarUpdateWork is { IsCompleted: true } work)
        {
            _sidecarUpdateWork = null; completed = true;
            if (work.Exception is { } error) _updateFailure?.Invoke(error.InnerException ?? error);
        }
        var snapshot = _sidecarUpdates.Snapshot;
        if (completed || !ReferenceEquals(snapshot, _sidecarSnapshot))
        { _sidecarSnapshot = snapshot; if (_selected == "advanced") RefreshUpdateCard(); }
        if (_sidecarUpdateWork is not null && _sidecarProgressDelay is not { IsCompleted: false })
        { _sidecarProgressDelay = Task.Delay(500, _updateStop.Token); WakeOnPlatformCompletion(_sidecarProgressDelay); }
    }
    private bool HandleSidecarUpdateIntent(DesktopUiIntent intent)
    {
        if (intent.Command.Value is not ("ui.settings.sidecar.install" or "ui.settings.sidecar.restart")) return false;
        if (!_visible || _selected != "advanced" || !_shell.Tree.IsAlive(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(intent.Source)?.Command != intent.Command) return true;
        string id = _shell.Renderer.ReadIntentArgument(intent.Source);
        if (!_sidecarUpdateRows.TryGetValue(id, out var row) || !CatalogNodes(row).ContainsValue(intent.Source)) return true;
        var item = _sidecarSnapshot?.Items.FirstOrDefault(i => i.Id == id);
        if (intent.Command.Value == "ui.settings.sidecar.restart" && item?.Phase == "restart") _restartForSidecar?.Invoke();
        else if (item?.CanInstall == true && _sidecarUpdateWork is null && _sidecarUpdates is not null)
        { _sidecarUpdateWork = _sidecarUpdates.InstallAsync(id, _updateStop.Token); WakeOnPlatformCompletion(_sidecarUpdateWork); RefreshUpdateCard(); }
        return true;
    }
    private void RefreshSidecarUpdateRows(XsrUiEntityId slot)
    {
        var items = _sidecarSnapshot?.Items ?? [];
        foreach (string id in _sidecarUpdateRows.Keys.Where(id => !items.Any(item => item.Id == id)).ToArray())
        { if (_shell.Tree.IsAlive(_sidecarUpdateRows[id])) _dynamic.Remove(_sidecarUpdateRows[id]); _sidecarUpdateRows.Remove(id); }
        foreach (var item in items)
        {
            var data = new Dictionary<string, string>
            {
                ["id"] = item.Id,
                ["current"] = item.CurrentVersion.Length == 0 ? CustomCopy("update.unknown") : item.CurrentVersion,
                ["available"] = item.AvailableVersion ?? "",
                ["hasOffer"] = Flag(item.AvailableVersion is not null),
                ["status"] = CustomCopy("sidecarUpdate.item." + item.Phase),
                ["busy"] = Flag(item.Phase is "downloading" or "verifying"),
                ["progress"] = item.Progress.ToString("P0", CultureInfo.CurrentCulture),
                ["canInstall"] = Flag(item.CanInstall && _sidecarUpdateWork is null),
                ["install"] = Flag(item.CanInstall),
                ["restart"] = Flag(item.Phase == "restart"),
                ["action"] = CustomCopy(item.Phase == "failed" ? "sidecarUpdate.retry" : "sidecarUpdate.install"),
            };
            var document = DesktopMarkup.Dynamic("SettingsCustomSidecarUpdate.d.nxml", data, _shell.Renderer.LocalizeText);
            if (_sidecarUpdateRows.TryGetValue(item.Id, out var row) && _shell.Tree.IsAlive(row)) _dynamic.Reload(row, document);
            else _sidecarUpdateRows[item.Id] = _dynamic.Append(slot, document);
        }
    }
}
