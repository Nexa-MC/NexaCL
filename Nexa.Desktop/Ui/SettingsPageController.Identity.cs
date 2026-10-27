using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId IdentityAction = XsrSemanticId.Parse("ui.settings.instance-identity.action");
    private readonly Dictionary<XsrUiEntityId, Action> _identityActions = [];
    private readonly Dictionary<string, (XsrUiEntityId Input, string Original, string Presented)> _identityInputs = new(StringComparer.Ordinal);
    private InstanceIdentitySnapshot? _identity;
    private Task<XsrResult<InstanceIdentitySnapshot>>? _identityRead;
    private Task<XsrResult>? _identityWrite;
    private string? _identityInstance;
    private XsrUiEntityId _identityGroup, _identityBody, _identityHistory, _identityNoteActions;
    private bool _identityBodyReady;
    private readonly Dictionary<int, XsrUiEntityId> _identityNoteRows = [];
    private readonly List<XsrUiEntityId> _identityMismatchRows = [];
    private string? _identityControlsRevision;
    private string? _identityWriteInstance;
    private bool _identityStarred, _identityOfflineAllowed, _identityIsolation;
    internal Action<string>? LaunchManagementInstance { get; set; }
    internal Action<string>? ModifyManagementInstance { get; set; }
    private bool _identityFailed;
    private CancellationTokenSource? _identityStop;
    private readonly List<string> _identityNotes = [];
    private readonly List<(XsrUiEntityId Input, int Index)> _identityNoteInputs = [];
    private XsrUiEntityId _identityNotesGroup;
    private int _identityNotesPage;
    private sealed record IdentityDraftSnapshot(string Instance, string Revision, string Section,
        Dictionary<string, string> Values, string[] Notes, int NotesPage, bool Starred, bool Isolation, bool OfflineAllowed);

    private IdentityDraftSnapshot? CaptureIdentityDraft(bool navigating)
    {
        if (navigating || _identity is null || _instance is null || _identityInstance != _instance
            || _selected is not ("overview" or "servers") || !_shell.Tree.IsAlive(_identityGroup)) return null;
        CaptureIdentityNotes();
        var values = _identityInputs.Where(pair => _shell.Tree.IsAlive(pair.Value.Input)).ToDictionary(pair => pair.Key, pair =>
        {
            string draft = _shell.Tree.GetComponent<XsrUiTextInput>(pair.Value.Input)!.ReadDraft();
            return draft == pair.Value.Presented ? pair.Value.Original : draft;
        }, StringComparer.Ordinal);
        return new(_instance, _identity.Revision, _selected, values, _identityNotes.ToArray(), _identityNotesPage,
            _identityStarred, _identityIsolation, _identityOfflineAllowed);
    }

    private void RestoreIdentityDraft(IdentityDraftSnapshot? draft, string? focus)
    {
        if (draft is null || _identity is null || draft.Instance != _instance || draft.Revision != _identity.Revision
            || draft.Section != _selected || !_shell.Tree.IsAlive(_identityGroup)) return;
        foreach (var pair in draft.Values)
        {
            if (!_identityInputs.TryGetValue(pair.Key, out var control)) continue;
            _shell.Renderer.SetTextInputValue(control.Input, pair.Value);
            _identityInputs[pair.Key] = (control.Input, pair.Value, _shell.Tree.GetComponent<XsrUiTextInput>(control.Input)!.ReadDraft());
        }
        if (_selected == "overview")
        {
            _identityStarred = draft.Starred; _identityIsolation = draft.Isolation;
            ReloadIdentityPresentation();
            _identityNotes.Clear(); _identityNotes.AddRange(draft.Notes); _identityNotesPage = draft.NotesPage;
            if (_shell.Tree.IsAlive(_identityNotesGroup))
            {
                BuildIdentityNoteRows();
                foreach (var line in _identityNoteInputs) _shell.Renderer.SetTextInputValue(line.Input, _identityNotes[line.Index]);
            }
        }
        else
        {
            _identityOfflineAllowed = draft.OfflineAllowed;
            ReloadIdentityPresentation();
        }
        if (focus is not null) _shell.Tree.Walk(_identityGroup, entity =>
        { if (_shell.Tree.Name(entity) == focus) _shell.Renderer.Focus(entity, showIndicator: false); return true; });
    }

    private void ResetIdentityControls()
    {
        _identityGroup = _identityBody = _identityNotesGroup = _identityHistory = _identityNoteActions = default;
        _identityActions.Clear(); _identityInputs.Clear(); _identityNoteInputs.Clear();
        _identityNoteRows.Clear(); _identityMismatchRows.Clear(); _identityControlsRevision = null;
    }

    private string IdentityTemplate => _selected == "overview" ? "SettingsCustomIdentityOverview.d.nxml" : "SettingsCustomIdentityServer.d.nxml";

    private void BuildInstanceIdentity()
    {
        if (_instance is null || _selected is not ("overview" or "servers")) return;
        _identityGroup = _dynamic.Append(_sections, DesktopMarkup.Dynamic(_selected == "overview" ? "SettingsCustomIdentityWorkspaceOverview.d.nxml" : "SettingsCustomIdentityWorkspaceServer.d.nxml"));
        BuildIdentityControls();
    }

    private Dictionary<string, string> IdentityProjection() => new()
    {
        ["loadingWeight"] = LoadingWeight(_identity is null && !_identityFailed),
        ["loading"] = Flag(_identity is null && !_identityFailed),
        ["failed"] = Flag(_identity is null && _identityFailed),
        ["ready"] = Flag(_identity is not null),
        ["enabled"] = Flag(_identityWrite is null),
        ["refreshKey"] = ContentKey("refresh"),
        ["managementEnabled"] = Flag(_managementWrite is null),
        ["loader"] = _identity?.Loader ?? "",
        ["java"] = _identity?.JavaPreference ?? "",
        ["launchCount"] = _identity?.LaunchCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        ["stableIdentity"] = _identity is null ? "" : _identity.Identity.Length == 0 ? CustomCopy("identity.generated") : _identity.Identity,
        ["canLaunch"] = Flag(LaunchManagementInstance is not null),
        ["canModify"] = Flag(ModifyManagementInstance is not null),
        ["icon"] = Flag(_identity?.Icon is not null),
        ["iconNotice"] = _identity?.IconNotice ?? "",
        ["hasIconNotice"] = Flag(_identity?.IconNotice.Length > 0),
        ["isolationLabel"] = CustomCopy(_identityIsolation ? "identity.isolated" : "identity.shared"),
        ["starLabel"] = CustomCopy(_identityStarred ? "identity.starred" : "identity.unstarred"),
        ["offlineLabel"] = CustomCopy(_identityOfflineAllowed ? "identity.offline" : "identity.online"),
        ["authentication"] = CustomCopy(_identity?.AuthenticationLocked == true ? "identity.locked" : "identity.editable"),
        ["authenticationEnabled"] = Flag(_identity?.AuthenticationLocked != true),
        ["undeclared"] = Flag(_identity?.EnvironmentRequirementsDeclared != true),
        ["matched"] = Flag(_identity?.EnvironmentRequirementsDeclared == true && _identity.EnvironmentMismatches.Count == 0),
        ["mismatched"] = Flag(_identity?.EnvironmentRequirementsDeclared == true && _identity.EnvironmentMismatches.Count > 0),
    };

    private void ReloadIdentityPresentation()
    {
        if (!_shell.Tree.IsAlive(_identityGroup)) return;
        bool ready = _identity is not null;
        var fragment = DesktopMarkup.Dynamic(ready ? IdentityTemplate : "SettingsCustomIdentityLoading.d.nxml", IdentityProjection(), _shell.Renderer.LocalizeText);
        if (_shell.Tree.IsAlive(_identityBody) && _identityBodyReady == ready) _dynamic.Reload(_identityBody, fragment);
        else
        {
            _identityBody = _dynamic.Replace(CatalogNodes(_identityGroup)["InstanceIdentity.Body"], fragment);
            _identityHistory = _identityNotesGroup = _identityNoteActions = default;
            _identityInputs.Clear(); _identityNoteInputs.Clear(); _identityNoteRows.Clear(); _identityMismatchRows.Clear();
        }
        _identityBodyReady = ready;
    }

    private void BuildIdentityControls()
    {
        if (!_shell.Tree.IsAlive(_identityGroup)) return;
        bool initialize = _identity is not null && _identityControlsRevision != _identity.Revision;
        if (initialize)
        {
            _identityStarred = _identity!.Fields.Starred; _identityIsolation = _identity.Fields.InstanceIsolation;
            _identityOfflineAllowed = _identity.Server.OfflineLaunchAllowed;
        }
        ReloadIdentityPresentation();
        var nodes = CatalogNodes(_identityGroup);
        _identityActions.Clear();
        if (_identity is null)
        {
            _identityActions[nodes["InstanceIdentityRetry"]] = () => { _identityFailed = false; StartIdentityRead(); BuildIdentityControls(); };
            return;
        }
        _identityActions[nodes["InstanceIdentitySave"]] = SaveIdentity;
        _identityActions[nodes["InstanceIdentityReload"]] = () =>
        { _identity = null; _identityControlsRevision = null; CancelIdentityRead(); StartIdentityRead(); BuildIdentityControls(); };
        var fields = _identity.Fields; var server = _identity.Server;
        Dictionary<string, string> values;
        if (_selected == "overview")
        {
            BindContentAction(nodes, ContentKey("refresh"), RefreshManagement);
            values = new()
            {
                ["name"] = fields.DisplayName,
                ["description"] = fields.Description,
                ["icon"] = fields.IconPath,
                ["tags"] = string.Join(", ", fields.Tags),
                ["group"] = fields.Group,
                ["custom-info"] = fields.CustomInfo,
                ["modpack-project"] = fields.ModpackProject,
                ["modpack-version"] = fields.ModpackVersion
            };
            if (LaunchManagementInstance is not null) _identityActions[nodes["InstanceOverviewLaunch"]] = () => LaunchManagementInstance(_instance!);
            if (ModifyManagementInstance is not null) _identityActions[nodes["InstanceOverviewModify"]] = () => ModifyManagementInstance(_instance!);
            _identityActions[nodes["InstanceIdentityIsolation"]] = () => { _identityIsolation = !_identityIsolation; ReloadIdentityPresentation(); };
            _identityActions[nodes["InstanceIdentityStar"]] = () => { _identityStarred = !_identityStarred; ReloadIdentityPresentation(); };
            if (initialize && _identity.Icon is { } icon)
                _shell.Tree.SetComponent(nodes["InstanceIdentityIconPreview"], new XsrUiRasterImage(icon, []) { FitToBounds = true, AspectRatio = (double)icon.Width / icon.Height });
            if (initialize)
            {
                _dynamic.Clear(nodes["InstanceNotes.Rows"]); _identityNoteRows.Clear();
                _identityNotes.Clear(); _identityNotes.AddRange(fields.Notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
                _identityNotesPage = 0;
            }
            _identityNotesGroup = nodes["InstanceNotes.Rows"];
            BuildIdentityNoteRows();
            BuildObservedInstanceHistory(nodes["InstanceIdentity.History"]);
        }
        else
        {
            values = new()
            {
                ["login"] = server.LoginRequirement.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["auth"] = server.AuthServerAddress,
                ["register"] = server.AuthRegisterAddress,
                ["auth-name"] = server.AuthServerDisplayName,
                ["default-server"] = server.DefaultServer,
                ["game-version"] = server.ExpectedGameVersion,
                ["loader"] = server.ExpectedLoader,
                ["mods"] = string.Join(", ", server.RequiredMods)
            };
            _identityActions[nodes["InstanceOfflinePolicy"]] = () => { _identityOfflineAllowed = !_identityOfflineAllowed; ReloadIdentityPresentation(); };
            var mismatches = _identity.EnvironmentMismatches.Take(128).ToArray();
            while (_identityMismatchRows.Count > mismatches.Length)
            { _dynamic.Remove(_identityMismatchRows[^1]); _identityMismatchRows.RemoveAt(_identityMismatchRows.Count - 1); }
            for (int index = 0; index < mismatches.Length; index++)
            {
                var fragment = DesktopMarkup.Dynamic("SettingsCustomIdentityMismatch.d.nxml", new Dictionary<string, string>
                { ["key"] = "InstanceIdentity.Mismatch." + index, ["text"] = mismatches[index] });
                if (index < _identityMismatchRows.Count) _dynamic.Reload(_identityMismatchRows[index], fragment);
                else _identityMismatchRows.Add(_dynamic.Append(nodes["InstanceIdentity.Mismatches"], fragment));
            }
        }
        foreach (var (key, value) in values)
        {
            var input = nodes["InstanceIdentityInput." + key];
            if (!initialize && _identityInputs.ContainsKey(key)) continue;
            _shell.Renderer.SetTextInputValue(input, value);
            _identityInputs[key] = (input, value, _shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft());
        }
        _identityControlsRevision = _identity.Revision;
    }

    private void CaptureIdentityNotes()
    {
        foreach (var line in _identityNoteInputs)
            if (_shell.Tree.IsAlive(line.Input)) _identityNotes[line.Index] = _shell.Tree.GetComponent<XsrUiTextInput>(line.Input)!.ReadDraft();
    }

    private void BuildIdentityNoteRows()
    {
        foreach (var key in _identityActions.Keys.Where(entity => _shell.Tree.Name(entity).StartsWith("InstanceNote", StringComparison.Ordinal)).ToArray()) _identityActions.Remove(key);
        _identityNoteInputs.Clear(); _identityNotesPage = Math.Clamp(_identityNotesPage, 0, (_identityNotes.Count - 1) / 32);
        int first = _identityNotesPage * 32, last = Math.Min(_identityNotes.Count, first + 32);
        foreach (int index in _identityNoteRows.Keys.Where(index => index < first || index >= last).ToArray())
        { _dynamic.Remove(_identityNoteRows[index]); _identityNoteRows.Remove(index); }
        for (int index = first; index < last; index++)
        {
            var fragment = DesktopMarkup.Dynamic("SettingsCustomIdentityNote.d.nxml", new Dictionary<string, string>
            { ["input"] = "InstanceNoteInput." + index, ["index"] = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }, _shell.Renderer.LocalizeText);
            if (!_identityNoteRows.TryGetValue(index, out var input))
            {
                input = _dynamic.Append(_identityNotesGroup, fragment); _identityNoteRows[index] = input;
                _shell.Renderer.SetTextInputValue(input, _identityNotes[index]);
            }
            else _dynamic.Reload(input, fragment);
            _identityNoteInputs.Add((input, index));
        }
        var actionsFragment = DesktopMarkup.Dynamic("SettingsCustomIdentityNoteActions.d.nxml", new Dictionary<string, string>
        {
            ["enabled"] = Flag(_identityWrite is null),
            ["pages"] = Flag(_identityNotes.Count > 32),
            ["page"] = (_identityNotesPage + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["total"] = ((_identityNotes.Count - 1) / 32 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, _shell.Renderer.LocalizeText);
        if (_shell.Tree.IsAlive(_identityNoteActions)) _dynamic.Reload(_identityNoteActions, actionsFragment);
        else _identityNoteActions = _dynamic.Append(CatalogNodes(_identityGroup)["InstanceNotes.Actions"], actionsFragment);
        var nodes = CatalogNodes(_identityNoteActions);
        _identityActions[nodes["InstanceNoteAdd"]] = () =>
        { if (_identityNotes.Count >= 8193) return; CaptureIdentityNotes(); _identityNotes.Add(""); _identityNotesPage = (_identityNotes.Count - 1) / 32; BuildIdentityNoteRows(); };
        if (_identityNotes.Count > 32)
        {
            _identityActions[nodes["InstanceNotePrevious"]] = () => { CaptureIdentityNotes(); _identityNotesPage--; BuildIdentityNoteRows(); };
            _identityActions[nodes["InstanceNoteNext"]] = () => { CaptureIdentityNotes(); _identityNotesPage++; BuildIdentityNoteRows(); };
        }
    }

    private void StartIdentityRead()
    {
        if (_instance is not null && _queries.TryResolve(InstanceIdentityContract.Query, out var route))
        {
            _identityStop?.Cancel(); _identityStop?.Dispose(); _identityStop = new();
            _identityRead = _queries.QueryAsync<InstanceIdentityQuery, InstanceIdentitySnapshot>(route, new(_instance), cancellationToken: _identityStop.Token).AsTask();
            WakeOnPlatformCompletion(_identityRead);
        }
    }

    private void CancelIdentityRead()
    { _identityStop?.Cancel(); _identityStop?.Dispose(); _identityStop = null; _identityRead = null; _identityFailed = false; }

    private void UpdateIdentity()
    {
        if (_instance is null || _selected is not ("overview" or "servers")) return;
        if (_identityInstance != _instance)
        { CancelIdentityRead(); _identityInstance = _instance; _identity = null; }
        if (_identityWrite is { IsCompleted: true } written)
        {
            _identityWrite = null;
            if (_identityWriteInstance != _instance) { _ = written.Exception; return; }
            if (written.IsCompletedSuccessfully && written.Result.IsSuccess)
            { _feedback.Success(CustomCopy("identity.saved")); _identity = null; _identityRead = null; StartIdentityRead(); RefreshManagement(); ManagementChanged?.Invoke(); }
            else _feedback.Error(CustomFormat("IdentitySaveFailure", new() { ["error"] = written.IsCompletedSuccessfully ? written.Result.Error?.Message ?? "" : CustomCopy("identity.permissions") }), localizeMessage: false);
        }
        if (_identity is null && _identityRead is null && !_identityFailed) StartIdentityRead();
        if (_identityRead is not { IsCompleted: true } reading)
        {
            if (_selected == "overview" && _identity is not null && _shell.Tree.IsAlive(_identityGroup))
                BuildObservedInstanceHistory(CatalogNodes(_identityGroup)["InstanceIdentity.History"]);
            return;
        }
        _identityRead = null;
        if (PendingQuery.Succeeded(reading)) { _identity = reading.Result.Value; BuildIdentityControls(); }
        else { _identityFailed = true; _feedback.Error(CustomCopy("identity.readFailed")); BuildIdentityControls(); }
    }

    private void SaveIdentity()
    {
        if (_identity is null || _identityWrite is not null || _instance is null || !_commands.TryResolve(InstanceIdentityContract.Save, out var route)) return;
        string Draft(string key, string fallback) => _identityInputs.TryGetValue(key, out var item)
            ? _shell.Tree.GetComponent<XsrUiTextInput>(item.Input)!.ReadDraft() is { } value && value != item.Presented ? value : item.Original : fallback;
        static string[] Parts(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var fields = _identity.Fields;
        var server = _identity.Server;
        if (_selected == "overview")
        {
            CaptureIdentityNotes();
            fields = fields with
            {
                DisplayName = Draft("name", fields.DisplayName),
                Description = Draft("description", fields.Description),
                IconPath = Draft("icon", fields.IconPath),
                Tags = Parts(Draft("tags", string.Join(',', fields.Tags))),
                Group = Draft("group", fields.Group),
                CustomInfo = Draft("custom-info", fields.CustomInfo),
                Starred = _identityStarred,
                InstanceIsolation = _identityIsolation,
                ModpackProject = Draft("modpack-project", fields.ModpackProject),
                ModpackVersion = Draft("modpack-version", fields.ModpackVersion),
                Notes = string.Join('\n', _identityNotes)
            };
        }
        else
        {
            if (!int.TryParse(Draft("login", server.LoginRequirement.ToString(System.Globalization.CultureInfo.InvariantCulture)), out int login)) { _feedback.Error(CustomCopy("identity.invalidLogin")); return; }
            server = server with
            {
                LoginRequirement = login,
                AuthServerAddress = Draft("auth", server.AuthServerAddress),
                AuthRegisterAddress = Draft("register", server.AuthRegisterAddress),
                AuthServerDisplayName = Draft("auth-name", server.AuthServerDisplayName),
                DefaultServer = Draft("default-server", server.DefaultServer),
                ExpectedGameVersion = Draft("game-version", server.ExpectedGameVersion),
                ExpectedLoader = Draft("loader", server.ExpectedLoader),
                RequiredMods = Parts(Draft("mods", string.Join(',', server.RequiredMods))),
                OfflineLaunchAllowed = _identityOfflineAllowed
            };
        }
        _identityWriteInstance = _instance;
        _identityWrite = _commands.Dispatch(route, new InstanceIdentitySaveCommand(_instance, _identity.Revision, fields, server)).Completion;
        WakeOnPlatformCompletion(_identityWrite);
        ReloadIdentityPresentation();
    }
}
