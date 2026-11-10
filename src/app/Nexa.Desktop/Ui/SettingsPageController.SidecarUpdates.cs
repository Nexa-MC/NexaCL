using System.Globalization;
using Nexa.Services.Updates;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private ISidecarUpdateControl? _sidecarUpdates;
    private Task? _sidecarUpdateWork, _sidecarProgressDelay;
    private SidecarUpdateSnapshot? _sidecarSnapshot;
    private Action? _restartForSidecar;
    private Func<IReadOnlyDictionary<string, SidecarUpdateRegistration>>? _readUpdateRegistrations;
    private Dictionary<string, SidecarUpdateRegistration> _updateRegistrations = new(StringComparer.Ordinal);
    private string? _sidecarOfferChannel, _sidecarCheckChannel;
    private bool _sidecarChecking;
    private SidecarUpdateItem[] SidecarItems => (_sidecarSnapshot?.Items ?? [])
        .Concat(_updateRegistrations.Where(pair => _sidecarSnapshot?.Items.Any(item => item.Id == pair.Key) != true)
            .Select(pair => new SidecarUpdateItem(pair.Key, pair.Value.RuntimeVersion, null, "installed", false)))
        .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    private bool CanInstallSidecar(SidecarUpdateItem item) => item.CanInstall && _sidecarOfferChannel is "alpha" or "beta" or "stable" && _sidecarOfferChannel == _updateQuery?.Channel;
    private readonly Dictionary<string, XsrUiEntityId> _sidecarUpdateRows = new(StringComparer.Ordinal);
    internal void ConfigureSidecarUpdates(ISidecarUpdateControl service, Action restart,
        Func<IReadOnlyDictionary<string, SidecarUpdateRegistration>>? registrations = null)
    {
        _sidecarUpdates = service; _restartForSidecar = restart; _sidecarSnapshot = service.Snapshot;
        _readUpdateRegistrations = registrations;
    }
    private string SidecarUpdateStatusText()
    {
        string phase = _sidecarSnapshot?.Phase ?? "initial";
        if (phase == "initial" && SidecarItems.Length == 0) phase = "none";
        return CustomCopy("sidecarUpdate." + phase);
    }
    private void CheckSidecarUpdates()
    {
        if (_sidecarUpdates is null || _sidecarUpdateWork is not null) return;
        _sidecarChecking = true; _sidecarOfferChannel = null;
        _sidecarCheckChannel = _updateQuery?.Channel is "stable" or "beta" ? _updateQuery.Channel : "alpha";
        _sidecarUpdateWork = _sidecarUpdates.CheckAsync(_sidecarCheckChannel, _updateStop.Token);
        WakeOnPlatformCompletion(_sidecarUpdateWork);
    }
    private void UpdateSidecarUpdates()
    {
        if (_sidecarUpdates is null) return;
        bool completed = false;
        if (_readUpdateRegistrations is not null && _visible && _selected == "advanced")
        {
            var registrations = _readUpdateRegistrations();
            if (registrations.Count != _updateRegistrations.Count || registrations.Any(pair => !_updateRegistrations.TryGetValue(pair.Key, out var old) || old != pair.Value))
            { _updateRegistrations = registrations.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal); completed = true; }
        }
        if (_sidecarUpdateWork is { IsCompleted: true } work)
        {
            _sidecarUpdateWork = null; completed = true;
            if (_sidecarChecking)
            {
                _sidecarOfferChannel = work.IsCompletedSuccessfully ? _sidecarCheckChannel : null;
                _sidecarChecking = false;
            }
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
        if (UpdateCenterBusy) return true;
        var item = SidecarItems.FirstOrDefault(i => i.Id == id);
        if (intent.Command.Value == "ui.settings.sidecar.restart" && item?.Phase == "restart") _restartForSidecar?.Invoke();
        else if (intent.Command.Value == "ui.settings.sidecar.install" && item is not null && CanInstallSidecar(item) && _sidecarUpdateWork is null && _sidecarUpdates is not null)
        { _sidecarUpdateWork = _sidecarUpdates.InstallAsync(id, _updateStop.Token); WakeOnPlatformCompletion(_sidecarUpdateWork); RefreshUpdateCard(); }
        return true;
    }
    private void RefreshSidecarUpdateRows(XsrUiEntityId slot)
    {
        var items = SidecarItems;
        foreach (string id in _sidecarUpdateRows.Keys.Where(id => !items.Any(item => item.Id == id)).ToArray())
        { if (_shell.Tree.IsAlive(_sidecarUpdateRows[id])) _dynamic.Remove(_sidecarUpdateRows[id]); _sidecarUpdateRows.Remove(id); }
        foreach (var item in items)
        {
            var data = new Dictionary<string, string>
            {
                ["id"] = item.Id,
                ["name"] = _updateRegistrations.TryGetValue(item.Id, out var registration) ? registration.DisplayName : item.Id,
                ["current"] = registration?.RuntimeVersion ?? (item.CurrentVersion.Length == 0 ? CustomCopy("update.unknown") : item.CurrentVersion),
                ["available"] = item.AvailableVersion ?? "",
                ["hasOffer"] = Flag(item.AvailableVersion is not null && (_sidecarOfferChannel == _updateQuery?.Channel || item.Phase is "downloading" or "verifying" or "restart" or "failed")),
                ["status"] = _queuedSidecarUpdates.Contains(item.Id) ? CustomCopy("update.queued")
                    : _sidecarChecking ? CustomCopy("update.checking")
                    : _sidecarSnapshot?.Phase == "failed" && item.Phase is "installed" or "current" or "available" or "host-required" ? CustomCopy("update.checkFailed")
                    : _sidecarOfferChannel != _updateQuery?.Channel && item.Phase is "current" or "available" or "host-required" ? CustomCopy("update.notChecked")
                    : CustomCopy("sidecarUpdate.item." + item.Phase),
                ["busy"] = Flag(item.Phase is "downloading" or "verifying"),
                ["progress"] = item.Progress.ToString("P0", CultureInfo.CurrentCulture),
                ["canInstall"] = Flag(CanInstallSidecar(item) && !UpdateCenterBusy),
                ["install"] = Flag(CanInstallSidecar(item)),
                ["restart"] = Flag(item.Phase == "restart"),
                ["canRestart"] = Flag(!UpdateCenterBusy),
                ["action"] = CustomCopy(item.Phase == "failed" ? "sidecarUpdate.retry" : "sidecarUpdate.install"),
            };
            var document = DesktopMarkup.Dynamic("SettingsCustomSidecarUpdate.d.nxml", data, _shell.Renderer.LocalizeText);
            if (_sidecarUpdateRows.TryGetValue(item.Id, out var row) && _shell.Tree.IsAlive(row)) _dynamic.Reload(row, document);
            else _sidecarUpdateRows[item.Id] = _dynamic.Append(slot, document);
        }
        _dynamic.Reorder(slot, items.Select(item => _sidecarUpdateRows[item.Id]).ToArray());
    }
}
