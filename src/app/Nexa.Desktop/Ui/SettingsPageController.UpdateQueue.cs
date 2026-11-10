using Nexa.Services.Updates;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly Queue<string> _queuedSidecarUpdates = new();
    private (string Version, string Channel)? _queuedLauncherUpdate;
    private bool _updateBatchActive;
    private bool UpdateCenterBusy => _updateBatchActive || AutomaticUpdateInProgress || _sidecarUpdateWork is not null || _updateReading is not null;
    private bool CanInstallLauncher => _automaticUpdates is not null && _automaticStatus?.CanInstall == true
        && _updateOffer is not null && _updateOfferChannel is "alpha" or "beta" or "stable" && _updateOfferChannel == _updateQuery?.Channel && !UpdateNeedsRestart;

    private void StartAllUpdates()
    {
        if (UpdateCenterBusy) return;
        foreach (var item in SidecarItems.Where(CanInstallSidecar)) _queuedSidecarUpdates.Enqueue(item.Id);
        if (CanInstallLauncher) _queuedLauncherUpdate = (_updateOffer!.Version, _updateOfferChannel!);
        _updateBatchActive = _queuedSidecarUpdates.Count > 0 || _queuedLauncherUpdate is not null;
        AdvanceUpdateQueue(); RefreshUpdateCard();
    }

    private void AdvanceUpdateQueue()
    {
        if (!_updateBatchActive || AutomaticUpdateInProgress || _sidecarUpdateWork is not null) return;
        while (_queuedSidecarUpdates.TryDequeue(out string? id))
        {
            if (_sidecarUpdates is null || !SidecarItems.Any(item => item.Id == id && CanInstallSidecar(item))) continue;
            _sidecarUpdateWork = InstallQueuedSidecarAsync(_sidecarUpdates, id, _updateStop.Token);
            WakeOnPlatformCompletion(_sidecarUpdateWork); RefreshUpdateCard(); return;
        }
        if (_queuedLauncherUpdate is { } launcher && _automaticUpdates is not null)
        {
            _queuedLauncherUpdate = null; _automaticRecoveryAttempted = true;
            _automaticInstalling = InstallQueuedLauncherAsync(_automaticUpdates, launcher.Version, launcher.Channel, _updateStop.Token);
            WakeOnPlatformCompletion(_automaticInstalling); RefreshUpdateCard(); return;
        }
        _updateBatchActive = false; RefreshUpdateCard();
    }

    // Async wrappers also turn a provider's synchronous exception into an observable failed item.
    private static async Task InstallQueuedSidecarAsync(ISidecarUpdateControl service, string id, CancellationToken token) =>
        await service.InstallAsync(id, token).ConfigureAwait(false);
    private static async Task<AutomaticUpdateStatus> InstallQueuedLauncherAsync(IAutomaticUpdateControl service, string version, string channel, CancellationToken token) =>
        await service.InstallAsync(version, channel, token).ConfigureAwait(false);

    private void RetireUpdateQueue()
    { _queuedSidecarUpdates.Clear(); _queuedLauncherUpdate = null; _updateBatchActive = false; }

    private void RestartAllUpdates()
    {
        if (UpdateCenterBusy) return;
        if (UpdateNeedsRestart) _restartForUpdate?.Invoke();
        else if (SidecarItems.Any(item => item.Phase == "restart")) _restartForSidecar?.Invoke();
    }
}
