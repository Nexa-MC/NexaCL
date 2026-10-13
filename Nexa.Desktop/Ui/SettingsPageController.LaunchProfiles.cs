using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId LaunchProfileAction = XsrSemanticId.Parse("ui.settings.launch-profile.action");
    private readonly Dictionary<XsrUiEntityId, Action> _launchProfileActions = [];
    private SettingsLaunchProfilesSnapshot? _launchProfiles;
    private Task<XsrResult<SettingsLaunchProfilesSnapshot>>? _launchProfilesReading;
    private string? _launchProfilesInstance;
    private string? _launchProfilesError, _launchProfileInputScope;
    private string? _launchProfilesReadingInstance;
    private readonly Dictionary<string, XsrUiEntityId> _launchProfileRows = new(StringComparer.Ordinal);
    private bool? _launchProfileAdmission;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string? Instance, string Error)> _launchProfileWriteErrors = new();
    private readonly Dictionary<string, string> _launchProfileDrafts = [];
    private readonly Dictionary<string, (XsrUiEntityId Entity, string Original)> _launchProfileDraftControls = [];
    private XsrUiEntityId _launchProfilesGroup, _launchProfileName, _launchMods, _launchResourcePacks, _launchShaders, _launchConfig;

    private void ResetLaunchProfileControls()
    {
        _launchProfilesGroup = _launchProfileName = _launchMods = _launchResourcePacks = _launchShaders = _launchConfig = default;
        _launchProfileActions.Clear(); _launchProfileDraftControls.Clear(); _launchProfileRows.Clear(); _launchProfileAdmission = null;
    }

    private void CaptureLaunchProfileDrafts(bool navigating = false)
    {
        if (navigating) { _launchProfileDrafts.Clear(); _launchProfileInputScope = null; return; }
        foreach (var control in _launchProfileDraftControls)
        {
            if (!_shell.Tree.IsAlive(control.Value.Entity)) continue;
            string value = _shell.Tree.GetComponent<XsrUiTextInput>(control.Value.Entity)!.ReadDraft();
            if (value == control.Value.Original) _launchProfileDrafts.Remove(control.Key);
            else _launchProfileDrafts[control.Key] = value;
        }
    }

    private Dictionary<string, string> LaunchProfileProjection()
    {
        var selected = _launchProfiles?.Profiles.FirstOrDefault(profile => profile.Id == _launchProfiles.SelectedProfileId);
        string layer = _launchProfiles?.TemporaryId is not null ? CustomCopy("profile.temporary")
            : selected is not null ? CustomFormat("ProfileLayer", new() { ["name"] = selected.Name }) : CustomCopy("profile.instance");
        return new()
        {
            ["loading"] = Flag(_launchProfiles is null && _launchProfilesError is null),
            ["failed"] = Flag(_launchProfilesError is not null),
            ["error"] = _launchProfilesError ?? "",
            ["ready"] = Flag(_launchProfiles is not null && _launchProfilesError is null),
            ["enabled"] = Flag(LaunchProfileWriteReady),
            ["retryEnabled"] = Flag(_writing is null),
            ["layer"] = layer,
            ["selected"] = Flag(selected is not null),
            ["temporary"] = Flag(_launchProfiles?.TemporaryId is not null),
            ["permanent"] = Flag(_launchProfiles?.TemporaryId is null),
        };
    }

    private void BuildLaunchProfiles()
    {
        if (_instance is null || _selected != "game") return;
        _launchProfilesGroup = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsCustomLaunchProfiles.d.nxml", LaunchProfileProjection()));
        BuildLaunchProfileControls();
    }

    private void BuildLaunchProfileControls()
    {
        if (!_shell.Tree.IsAlive(_launchProfilesGroup)) return;
        CaptureLaunchProfileDrafts();
        string scope = _instance + "\0" + _launchProfiles?.SelectedProfileId + "\0" + _launchProfiles?.TemporaryId;
        bool changedScope = _launchProfiles is not null && _launchProfileInputScope is not null && _launchProfileInputScope != scope;
        if (changedScope)
        {
            var previous = _launchProfilesGroup;
            _launchProfilesGroup = _dynamic.Append(_shell.Tree.Parent(previous), DesktopMarkup.Dynamic("SettingsCustomLaunchProfiles.d.nxml", LaunchProfileProjection()));
            _dynamic.Remove(previous); _launchProfileRows.Clear(); _launchProfileDraftControls.Clear(); _launchProfileDrafts.Clear();
        }
        else _dynamic.Reload(_launchProfilesGroup, DesktopMarkup.Dynamic("SettingsCustomLaunchProfiles.d.nxml", LaunchProfileProjection()));
        _launchProfileActions.Clear();
        var nodes = CatalogNodes(_launchProfilesGroup);
        _launchProfileActions[nodes["LaunchProfilesRetry"]] = () => { _launchProfilesError = null; _launchProfiles = null; BuildLaunchProfileControls(); };
        if (_launchProfiles is null || _launchProfilesError is not null) return;
        if (_launchProfileInputScope != scope) _launchProfileDrafts.Clear();
        _launchProfileInputScope = scope;
        var selected = _launchProfiles.Profiles.FirstOrDefault(profile => profile.Id == _launchProfiles.SelectedProfileId);
        _launchProfileActions[nodes["LaunchProfileBase"]] = () => SelectLaunchProfile(null);
        var live = _launchProfiles.Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _launchProfileRows.Keys.Where(id => !live.Contains(id)).ToArray())
        { _dynamic.Remove(_launchProfileRows[id]); _launchProfileRows.Remove(id); }
        foreach (var profile in _launchProfiles.Profiles)
        {
            var fragment = LaunchProfileRowProjection(profile);
            if (_launchProfileRows.TryGetValue(profile.Id, out var row)) _dynamic.Reload(row, fragment);
            else _launchProfileRows[profile.Id] = row = _dynamic.Append(nodes["LaunchProfileChoices"], fragment);
            var controls = CatalogNodes(row);
            _launchProfileActions[controls["LaunchProfileSelect." + profile.Id]] = () => SelectLaunchProfile(profile.Id);
            _launchProfileActions[controls["LaunchProfileDelete." + profile.Id]] = () => WriteLaunchProfile(SettingsLaunchProfileContract.Delete,
                new SettingsLaunchProfileDeleteCommand(_instance!, profile.Id, _launchProfiles!.Revision));
        }
        _dynamic.Reorder(nodes["LaunchProfileChoices"], _launchProfiles.Profiles.Select(profile => _launchProfileRows[profile.Id]).ToArray());
        MinecraftLaunchOverlay overlay = _launchProfiles.TemporaryId is not null ? _launchProfiles.TemporaryOverlay : selected?.Overlay ?? new();
        Dictionary<string, string> values = new()
        {
            ["LaunchProfileName"] = selected?.Name ?? CustomCopy("profile.new"),
            ["LaunchOverlayMods"] = overlay.ModsSource ?? "",
            ["LaunchOverlayResourcePacks"] = overlay.ResourcePacksSource ?? "",
            ["LaunchOverlayShaders"] = overlay.ShaderPacksSource ?? "",
            ["LaunchOverlayConfig"] = overlay.ConfigSource ?? ""
        };
        foreach (var (key, value) in values)
        {
            var input = nodes[key];
            string draft = _launchProfileDrafts.GetValueOrDefault(key, value);
            if (draft == value) _launchProfileDrafts.Remove(key);
            bool retainedDraft = _launchProfileDraftControls.TryGetValue(key, out var control) && control.Entity == input
                && (control.Original == value || _launchProfileDrafts.ContainsKey(key));
            if (!retainedDraft) _shell.Renderer.SetTextInputValue(input, draft);
            _launchProfileDraftControls[key] = (input, value);
        }
        _launchProfileName = nodes["LaunchProfileName"]; _launchMods = nodes["LaunchOverlayMods"];
        _launchResourcePacks = nodes["LaunchOverlayResourcePacks"]; _launchShaders = nodes["LaunchOverlayShaders"]; _launchConfig = nodes["LaunchOverlayConfig"];
        _launchProfileActions[nodes["LaunchProfileCreate"]] = () => SaveLaunchProfile(Guid.NewGuid().ToString("N"));
        if (selected is not null) _launchProfileActions[nodes["LaunchProfileSave"]] = () => SaveLaunchProfile(selected.Id);
        if (_launchProfiles.TemporaryId is null)
            _launchProfileActions[nodes["LaunchTemporaryBegin"]] = () => WriteLaunchProfile(SettingsLaunchProfileContract.BeginTemporary,
                new SettingsTemporaryLaunchBeginCommand(_instance!, Guid.NewGuid().ToString("N"), new Dictionary<string, SettingsOverride>(), ReadOverlay(), _launchProfiles!.Revision));
        else
        {
            _launchProfileActions[nodes["LaunchTemporarySave"]] = () => WriteLaunchProfile(SettingsLaunchProfileContract.BeginTemporary,
                new SettingsTemporaryLaunchBeginCommand(_instance!, _launchProfiles.TemporaryId!, _launchProfiles.TemporaryValues, ReadOverlay(), _launchProfiles.Revision));
            _launchProfileActions[nodes["LaunchTemporaryEnd"]] = () => WriteLaunchProfile(SettingsLaunchProfileContract.EndTemporary,
                new SettingsTemporaryLaunchEndCommand(_instance!, _launchProfiles.Revision));
        }
        _launchProfileAdmission = LaunchProfileWriteReady;
    }

    private Nexa.Xsr.Renderer.Nxml.NxmlHostIr LaunchProfileRowProjection(SettingsLaunchProfile profile)
        => DesktopMarkup.Dynamic("SettingsCustomLaunchProfileRow.d.nxml", new Dictionary<string, string>
        {
            ["row"] = "LaunchProfileChoice." + profile.Id,
            ["select"] = "LaunchProfileSelect." + profile.Id,
            ["delete"] = "LaunchProfileDelete." + profile.Id,
            ["enabled"] = Flag(LaunchProfileWriteReady),
            ["label"] = profile.Id == _launchProfiles?.SelectedProfileId ? CustomFormat("ProfileSelected", new() { ["name"] = profile.Name }) : profile.Name,
        });

    private bool LaunchProfileSnapshotReady => _instance is not null && _launchProfilesInstance == _instance
        && _selected == "game" && _launchProfilesReading is null && _launchProfilesError is null
        && _launchProfiles is { } profiles && profiles.Revision == _store.Read<long>(_revisionId).Value
        && _values?.Revision == profiles.Revision;

    private bool LaunchProfileWriteReady => _writing is null && LaunchProfileSnapshotReady;

    private bool LaunchProfileActionReady(XsrUiEntityId source) => _shell.Tree.IsAlive(source)
        && (LaunchProfileWriteReady || _shell.Tree.Name(source) == "LaunchProfilesRetry" && _writing is null);

    private void UpdateLaunchProfileAdmission()
    {
        bool available = LaunchProfileWriteReady;
        if (!_shell.Tree.IsAlive(_launchProfilesGroup) || _launchProfileAdmission == available) return;
        _dynamic.Reload(_launchProfilesGroup, DesktopMarkup.Dynamic("SettingsCustomLaunchProfiles.d.nxml", LaunchProfileProjection()));
        if (_launchProfiles is not null)
            foreach (var profile in _launchProfiles.Profiles)
                if (_launchProfileRows.TryGetValue(profile.Id, out var row)) _dynamic.Reload(row, LaunchProfileRowProjection(profile));
        _launchProfileAdmission = available;
    }

    private void HandleLaunchProfileIntent(DesktopUiIntent intent)
    {
        if (LaunchProfileActionReady(intent.Source)
            && _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled == true
            && _launchProfileActions.TryGetValue(intent.Source, out var action)) action();
    }

    private MinecraftLaunchOverlay ReadOverlay()
    {
        string? Source(XsrUiEntityId input)
        {
            string value = _shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        // Safe Launch stays a normal editable setting rather than an independent hidden toggle.
        return new(false, Source(_launchMods), Source(_launchResourcePacks), Source(_launchShaders), Source(_launchConfig));
    }

    private void SaveLaunchProfile(string id)
    {
        if (_values is null || _instance is null || _launchProfiles is null) return;
        var values = _values.Values.Where(value => SettingsPolicySchema.ByKey[value.Key].InstanceOverride && value.Key is not ("java.compatibility" or "instance.development-mode"))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        WriteLaunchProfile(SettingsLaunchProfileContract.Save, new SettingsLaunchProfileSaveCommand(_instance, id,
            _shell.Tree.GetComponent<XsrUiTextInput>(_launchProfileName)!.ReadDraft(), values, ReadOverlay(), _launchProfiles.Revision));
    }

    private void SelectLaunchProfile(string? id)
    {
        if (_instance is not null && _launchProfiles is not null)
            WriteLaunchProfile(SettingsLaunchProfileContract.Select, new SettingsLaunchProfileSelectCommand(_instance, id, _launchProfiles.Revision));
    }

    private void WriteLaunchProfile<T>(XsrSemanticId command, T payload) where T : notnull
    {
        if (!LaunchProfileWriteReady || !_commands.TryResolve(command, out var route)) return;
        string? instance = _instance;
        _writing = FinishAsync();
        WakeOnPlatformCompletion(_writing);
        async Task<XsrResult> FinishAsync()
        {
            var result = await _commands.Dispatch(route, payload).Completion.ConfigureAwait(false);
            if (!_disposed && !result.IsSuccess) _launchProfileWriteErrors.Enqueue((instance, result.Error?.Message ?? ""));
            return result;
        }
    }

    private void UpdateLaunchProfiles()
    {
        while (_launchProfileWriteErrors.TryDequeue(out var failed))
            if (failed.Instance == _instance) _feedback.Error(CustomFormat("ProfileSaveFailure", new() { ["error"] = failed.Error }), localizeMessage: false);
        if (_instance is null || _selected != "game") return;
        if (_launchProfilesInstance != _instance)
        {
            if (_launchProfilesReading is { } retired)
                _ = retired.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _launchProfiles = null; _launchProfilesReading = null; _launchProfilesError = null; _launchProfilesInstance = _instance;
        }
        long revision = _store.Read<long>(_revisionId).Value;
        if (_launchProfilesReading is null && _launchProfiles?.Revision != revision && _launchProfilesError is null)
        {
            if (!_queries.TryResolve(SettingsLaunchProfileContract.Query, out var route))
            { _launchProfilesError = CustomCopy("profile.unavailable"); BuildLaunchProfileControls(); return; }
            _launchProfilesReadingInstance = _instance;
            _launchProfilesReading = _queries.QueryAsync<SettingsLaunchProfilesQuery, SettingsLaunchProfilesSnapshot>(route, new(_instance), cancellationToken: _updateStop.Token).AsTask();
            WakeOnPlatformCompletion(_launchProfilesReading);
        }
        if (_launchProfilesReading is not { IsCompleted: true } reading)
        { UpdateLaunchProfileAdmission(); return; }
        _launchProfilesReading = null;
        if (_launchProfilesReadingInstance != _instance) { _ = reading.Exception; return; }
        if (PendingQuery.Succeeded(reading)) _launchProfiles = reading.Result.Value;
        else _launchProfilesError = CustomCopy("profile.readFailed");
        BuildLaunchProfileControls();
    }

    private async Task<XsrResult> SaveLaunchLayerAsync(SettingsMutation mutation)
    {
        if (_queries.TryResolve(SettingsLaunchProfileContract.Query, out _) && !LaunchProfileSnapshotReady)
            return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("settings.profile.refresh-pending"),
                CustomCopy("profile.refreshPending")));
        if (_launchProfiles?.TemporaryId is { } temporary && _instance is not null
            && _commands.TryResolve(SettingsLaunchProfileContract.BeginTemporary, out var temporaryRoute))
        {
            var values = new Dictionary<string, SettingsOverride>(_launchProfiles.TemporaryValues, StringComparer.Ordinal);
            if (mutation.Value.Mode == SettingsOverrideMode.Inherit) values.Remove(mutation.Key); else values[mutation.Key] = mutation.Value;
            return await _commands.Dispatch(temporaryRoute, new SettingsTemporaryLaunchBeginCommand(_instance, temporary, values,
                _launchProfiles.TemporaryOverlay, _launchProfiles.Revision)).Completion.ConfigureAwait(false);
        }
        if (_launchProfiles?.SelectedProfileId is { } profile)
            mutation = mutation with { Layer = SettingsLayer.Profile, ProfileId = profile };
        if (!_commands.TryResolve(SettingsPolicyContract.SetCommand, out var route))
            return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("settings.profile.unavailable"), CustomCopy("profile.routeUnavailable")));
        return await _commands.Dispatch(route, mutation).Completion.ConfigureAwait(false);
    }
}
