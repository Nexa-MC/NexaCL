using Nexa.Services.Settings;
using Nexa.Services.Updates;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId CheckUpdate = XsrSemanticId.Parse("ui.settings.update.check");
    private static readonly XsrSemanticId DownloadUpdate = XsrSemanticId.Parse("ui.settings.update.download");
    private static readonly XsrSemanticId PortableUpdate = XsrSemanticId.Parse("ui.settings.update.portable");
    private static readonly XsrSemanticId UpdateNotes = XsrSemanticId.Parse("ui.settings.update.notes");
    private static readonly XsrSemanticId InstallUpdate = XsrSemanticId.Parse("ui.settings.update.install");
    private static readonly XsrSemanticId RestartUpdate = XsrSemanticId.Parse("ui.settings.update.restart");
    private static readonly XsrSemanticId RollbackUpdate = XsrSemanticId.Parse("ui.settings.update.rollback");
    private IAutomaticUpdateControl? _automaticUpdates;
    private Task<AutomaticUpdateStatus>? _automaticReading, _automaticInstalling;
    private AutomaticUpdateStatus? _automaticStatus;
    private Action? _restartForUpdate;
    private Task? _automaticRefreshDelay;
    private bool _automaticRecoveryAttempted;
    private bool _automaticDiscardReading;
    private bool UpdateNeedsRestart => _automaticStatus?.Phase is "complete" or "rolledback"
        && _automaticStatus.Version != _updateQuery?.CurrentVersion;
    private bool AutomaticUpdateInProgress => _automaticInstalling is not null
        || _automaticStatus?.Phase is "verifying" or "downloading" or "preparing" or "activating";
    private XsrQueryRouter? _updateQueries;
    private NexaUpdateQuery? _updateQuery;
    private NexaUpdateQuery? _buildUpdateQuery, _updateReadingQuery;
    private Task<XsrResult<SettingsEffectiveSnapshot>>? _updatePolicyReading;
    private long _updatePolicyRevision = -1;
    private bool _startupUpdateChecked;
    private CancellationTokenSource? _updateCheckStop;
    private string? _updateOfferChannel;
    private Action<Uri>? _openUpdateLink;
    private Task<XsrResult<NexaUpdateStatus>>? _updateReading;
    private NexaUpdateOffer? _updateOffer;
    private string _updateStatus = "";
    private XsrUiEntityId _updateCard;
    private readonly Dictionary<string, XsrUiEntityId> _updateActionViews = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _updateStop = new();

    private Action<Exception>? _updateFailure;

    internal void ConfigureUpdates(XsrQueryRouter queries, NexaUpdateQuery query, Action<Uri> open,
        IAutomaticUpdateControl? automatic = null, Action? restart = null, Action<Exception>? failure = null)
    {
        _updateQueries = queries; _updateQuery = _buildUpdateQuery = query; _openUpdateLink = open;
        _automaticUpdates = automatic; _restartForUpdate = restart; _updateFailure = failure;
        if (automatic is not null) { _automaticReading = automatic.ReadAsync(_updateStop.Token); WakeOnPlatformCompletion(_automaticReading); }
    }

    private static bool IsUpdateIntent(XsrSemanticId id) => id == CheckUpdate || id == DownloadUpdate || id == PortableUpdate || id == UpdateNotes
        || id == InstallUpdate || id == RestartUpdate || id == RollbackUpdate;
    private void HandleUpdateIntent(DesktopUiIntent intent)
    {
        if (!_visible || _selected != "advanced" || !_shell.Tree.IsAlive(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(intent.Source)?.Command != intent.Command
            || !_shell.Tree.IsAlive(_updateCard)) return;
        var nodes = CatalogNodes(_updateCard);
        if (intent.Source != nodes["SettingsCheckUpdate"] && !_updateActionViews.ContainsValue(intent.Source)) return;
        var id = intent.Command;
        if (id == RestartUpdate && UpdateNeedsRestart) { _restartForUpdate?.Invoke(); return; }
        if (id == InstallUpdate && !AutomaticUpdateInProgress && _automaticStatus?.CanInstall == true && _automaticUpdates is not null && _updateQuery is not null)
        {
            string? version = _updateOffer?.Version ?? _automaticStatus.Version;
            string? channel = _updateOffer is null ? _automaticStatus.Channel : _updateOfferChannel;
            if (version is null || channel is not ("alpha" or "beta" or "stable")) return;
            _automaticRecoveryAttempted = true;
            _automaticInstalling = _automaticUpdates.InstallAsync(version, channel, _updateStop.Token);
            WakeOnPlatformCompletion(_automaticInstalling); _updateStatus = CustomCopy("update.installing"); RefreshUpdateCard(); return;
        }
        if (id == RollbackUpdate && !AutomaticUpdateInProgress && _automaticStatus?.CanRollback == true && _automaticUpdates is not null)
        { _automaticRecoveryAttempted = true; _automaticInstalling = _automaticUpdates.RollbackAsync(_updateStop.Token); WakeOnPlatformCompletion(_automaticInstalling); return; }
        if (id == CheckUpdate && _updateReading is null && StartUpdateCheck())
        {
            RefreshUpdateCard();
        }
        else if (_updateOffer is { } offer && id != CheckUpdate)
        {
            string url = id == DownloadUpdate ? offer.InstallerUrl : id == PortableUpdate ? offer.PortableUrl : offer.ReleaseUrl;
            try { _openUpdateLink?.Invoke(new Uri(url)); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _feedback.Error(CustomCopy("update.openFailed")); }
        }
    }

    private void UpdateReleaseCheck()
    {
        UpdateDiscoveryPreferences();
        UpdateAutomaticState();
        if (_updateReading is not { IsCompleted: true } task) return;
        _updateReading = null;
        _updateCheckStop?.Dispose(); _updateCheckStop = null;
        if (_updateReadingQuery != _updateQuery) { _ = task.Exception; return; }
        _updateOffer = PendingQuery.Succeeded(task) ? task.Result.Value!.Offer : null;
        _updateOfferChannel = _updateOffer is null ? null : _updateReadingQuery?.Channel;
        _updateStatus = !PendingQuery.Succeeded(task) ? CustomCopy("update.checkFailed")
            : _updateOffer is null ? CustomCopy("update.noOffer") : CustomFormat("UpdateOffer", new() { ["version"] = _updateOffer.Version });
        if (_selected == "advanced") RefreshUpdateCard();
    }

    private bool StartUpdateCheck()
    {
        if (_updateQuery is null || _updateQueries?.TryResolve(NexaUpdateContract.Check, out var route) != true) return false;
        _updateCheckStop = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token);
        _updateReadingQuery = _updateQuery;
        _updateStatus = CustomCopy("update.checking");
        _updateReading = _updateQueries.QueryAsync<NexaUpdateQuery, NexaUpdateStatus>(route, _updateReadingQuery, cancellationToken: _updateCheckStop.Token).AsTask();
        WakeOnPlatformCompletion(_updateReading);
        return true;
    }

    private void UpdateDiscoveryPreferences()
    {
        if (_buildUpdateQuery is null || _instanceDirectory is not null) return;
        long revision = _store.Read<long>(_revisionId).Value;
        if (_updatePolicyReading is null && _updatePolicyRevision != revision && _queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var route))
        {
            _updatePolicyReading = _queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(route, new()).AsTask();
            WakeOnPlatformCompletion(_updatePolicyReading);
        }
        if (_updatePolicyReading is not { IsCompleted: true } reading) return;
        _updatePolicyReading = null;
        if (!PendingQuery.Succeeded(reading)) { _ = reading.Exception; _updatePolicyRevision = revision; return; }
        var snapshot = reading.Result.Value!;
        if (snapshot.Revision != revision) return;
        _updatePolicyRevision = snapshot.Revision;
        var policy = SettingsUpdatePolicy.FromSnapshot(snapshot, _buildUpdateQuery.Channel);
        var query = _buildUpdateQuery with { Channel = policy.Channel };
        bool changed = query != _updateQuery;
        if (changed)
        {
            _updateCheckStop?.Cancel(); _updateCheckStop?.Dispose(); _updateCheckStop = null;
            if (_updateReading is { } retired)
                _ = retired.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _updateReading = null; _updateReadingQuery = null;
            _updateQuery = query; _updateOffer = null; _updateOfferChannel = null;
            if (!AutomaticUpdateInProgress && !UpdateNeedsRestart)
                _updateStatus = CustomCopy("update.initial");
        }
        if (!_startupUpdateChecked)
        {
            _startupUpdateChecked = true;
            if (policy.AutomaticCheck) StartUpdateCheck();
        }
        if (changed && _visible && _selected == "advanced" && _catalog is not null) RefreshUpdateCard();
    }

    private void UpdateAutomaticState()
    {
        bool changed = false;
        if (_automaticInstalling is { IsCompleted: true } operation)
        {
            _automaticInstalling = null;
            // A progress read started before terminal publication must not replace the
            // operation's final status after it completes, including rollback.
            _automaticDiscardReading = _automaticReading is not null;
            if (operation.IsCompletedSuccessfully)
            {
                _automaticStatus = operation.Result;
                _updateStatus = CustomCopy(_automaticStatus.Phase == "rolledback" ? "update.rolledBack" : "update.complete");
                if (UpdateNeedsRestart) _updateStatus = CustomFormat("UpdateRestart", new() { ["status"] = _updateStatus });
            }
            else
            {
                if (operation.Exception is { } error) _updateFailure?.Invoke(error.InnerException ?? error);
                _updateStatus = CustomCopy("update.incomplete");
            }
            changed = true;
        }
        if (_automaticReading is { IsCompleted: true } reading)
        {
            _automaticReading = null;
            if (_automaticDiscardReading) { _ = reading.Exception; _automaticDiscardReading = false; }
            else if (reading.IsCompletedSuccessfully)
            {
                changed |= _automaticStatus != reading.Result;
                _automaticStatus = reading.Result;
                if (_automaticInstalling is null && _automaticStatus.Phase is "complete" or "rolledback")
                {
                    _updateStatus = CustomCopy(_automaticStatus.Phase == "rolledback" ? "update.rolledBack" : "update.complete");
                    if (UpdateNeedsRestart) _updateStatus = CustomFormat("UpdateRestart", new() { ["status"] = _updateStatus });
                }
                if (!_automaticRecoveryAttempted && _automaticInstalling is null && _automaticStatus.CanInstall && _automaticStatus.Version is { } version
                    && _automaticStatus.Channel is { } channel && _automaticStatus.Phase is "verifying" or "downloading" or "preparing" or "activating" or "paused")
                {
                    _automaticRecoveryAttempted = true;
                    _automaticInstalling = _automaticUpdates!.InstallAsync(version, channel, _updateStop.Token);
                    WakeOnPlatformCompletion(_automaticInstalling);
                }
            }
            else { _ = reading.Exception; _updateStatus = CustomCopy("update.unsupported"); changed = true; }
            if (AutomaticUpdateInProgress)
            {
                _automaticRefreshDelay = Task.Delay(500, _updateStop.Token);
                WakeOnPlatformCompletion(_automaticRefreshDelay);
            }
        }
        if (AutomaticUpdateInProgress && _automaticReading is null && _automaticRefreshDelay is not { IsCompleted: false })
        { _automaticRefreshDelay = null; _automaticReading = _automaticUpdates!.ReadAsync(_updateStop.Token); WakeOnPlatformCompletion(_automaticReading); }
        if (changed && _selected == "advanced") RefreshUpdateCard();
    }

    private Dictionary<string, string> UpdateCardProjection()
    {
        string? phase = AutomaticUpdateInProgress ? _automaticStatus?.Phase : null;
        return new()
        {
            ["version"] = _updateQuery?.CurrentVersion ?? "",
            ["busy"] = Flag(AutomaticUpdateInProgress || _updateReading is not null),
            ["canCheck"] = Flag(_updateReading is null),
            ["status"] = _updateStatus.Length == 0 ? CustomCopy("update.initial") : _updateStatus,
            ["downloading"] = Flag(phase == "downloading"),
            ["preparing"] = Flag(phase == "preparing"),
            ["activating"] = Flag(phase == "activating"),
            ["verifying"] = Flag(AutomaticUpdateInProgress && phase is not ("downloading" or "preparing" or "activating")),
        };
    }

    private void BuildUpdateCard()
    {
        _updateActionViews.Clear(); _updateCard = default;
        if (_updateQuery is null) return;
        _updateCard = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsCustomUpdate.d.nxml", UpdateCardProjection(), _shell.Renderer.LocalizeText));
        RefreshUpdateCard();
    }

    private void RefreshUpdateCard()
    {
        if (!_shell.Tree.IsAlive(_updateCard) || _selected != "advanced") return;
        _dynamic.Reload(_updateCard, DesktopMarkup.Dynamic("SettingsCustomUpdate.d.nxml", UpdateCardProjection(), _shell.Renderer.LocalizeText));
        var nodes = CatalogNodes(_updateCard); var slot = nodes["SettingsUpdateActions"];
        List<string> actions = [];
        if (_updateOffer is not null)
        {
            if (_automaticStatus?.CanInstall == true && !AutomaticUpdateInProgress && _updateQuery?.Channel is "alpha" or "beta" or "stable") actions.Add("Install");
            actions.AddRange(["Download", "Portable", "Notes"]);
        }
        if (UpdateNeedsRestart && _automaticInstalling is null) actions.Add("Restart");
        else if (_updateOffer is null && _automaticStatus?.Version is not null && _automaticStatus.CanInstall
            && _automaticStatus.Channel is "alpha" or "beta" or "stable"
            && _automaticStatus.Phase is not ("complete" or "rolledback") && _automaticInstalling is null) actions.Add("Resume");
        if (_automaticStatus?.CanRollback == true && !AutomaticUpdateInProgress) actions.Add("Rollback");
        foreach (string name in _updateActionViews.Keys.Where(name => !actions.Contains(name)).ToArray())
        { _dynamic.Remove(_updateActionViews[name]); _updateActionViews.Remove(name); }
        foreach (string name in actions)
            if (!_updateActionViews.ContainsKey(name)) _updateActionViews[name] = _dynamic.Append(slot, DesktopMarkup.Dynamic("SettingsCustomUpdate" + name + ".d.nxml"));
        _dynamic.Reorder(slot, actions.Select(name => _updateActionViews[name]).ToArray());
    }
}
