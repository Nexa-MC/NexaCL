using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Accounts;
using Nexa.Services.Logging;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal interface IAccountUiEffects
{
    Task OpenAuthorization(Uri uri);
    Task CopyCode(string code);
    Task<string?> PickProfiles();
}

internal sealed class NativeAccountUiEffects(AvaloniaUiPlatformActions actions) : IAccountUiEffects
{
    // Shell execution can briefly block while Windows resolves the registered browser. Keep that
    // work off the render thread so an authorization transition cannot stall pointer presentation.
    public Task OpenAuthorization(Uri uri) => Task.Run(() => actions.OpenHttpsUri(uri));
    public Task CopyCode(string code) => actions.CopyTextAsync(code);
    public Task<string?> PickProfiles() => actions.PickJsonFileAsync();
}

/// <summary>Render-thread NXML form projection and typed intent adapter. Auth workers own no tree.</summary>
internal sealed class AccountFormController : IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrCommandRouter _commands;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly IAccountUiEffects? _effects;
    private readonly ILogWriter? _log;
    private readonly UiProjectionSignal _projections;
    private readonly Dictionary<string, XsrUiEntityId> _entities = [];
    private readonly Dictionary<XsrUiEntityId, string> _importRows = [];
    private readonly Dictionary<XsrUiEntityId, string> _characterRows = [];
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _form;
    private string _copyStamp = "";
    private long _characterGeneration = -1;
    private readonly CancellationTokenSource _lifetime = new();
    private long _importsRevision = -1, _charactersRevision = -1, _seenCompletion, _viewEpoch;
    private long _presentedChallengeGeneration = -1;
    private string _appliedPath = string.Empty;
    private string? _pendingFocus;
    private XsrUiEntityId _returnFocus;
    private readonly XsrUiEntityId _fallbackFocus;
    private bool _keyboardFocus, _disposed;
    private AccountLoginProvider _provider;

    public AccountFormController(XsrUiShell shell, DesktopUiIntentSink intents, XsrCommandRouter commands,
        XsrStateStore store, XsrUiEntityId accountBody, DesktopFeedbackService feedback,
        IAccountUiEffects? effects = null, ILogWriter? log = null)
    {
        _shell = shell; _intents = intents; _commands = commands; _store = store;
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _effects = effects;
        _log = log;
        XsrUiEntityId fallback = default;
        shell.Tree.Walk(shell.Tree.Parent(accountBody), entity =>
        {
            if (shell.Tree.Name(entity) == "AccountAdd") fallback = entity;
            if (shell.Tree.Name(entity) is "AccountBack" or "AccountSwitch") _entities[shell.Tree.Name(entity)] = entity;
            return true;
        });
        _fallbackFocus = fallback;
        _dynamic = new(shell.Tree, store);
        XsrUiEntityId slot = default;
        shell.Tree.Walk(accountBody, entity => { if (shell.Tree.Name(entity) == "AccountFormSlot") slot = entity; return true; });
        _form = _dynamic.Append(slot, DesktopMarkup.Dynamic("AccountFormWorkspace.d.nxml"));
        shell.Tree.Walk(_form, entity => { _entities[shell.Tree.Name(entity)] = entity; return true; });
        _projections = new UiProjectionSignal(_store, _intents);
        _intents.IntentEmitted += OnIntent;
        _shell.Renderer.FramePreparing += OnFrame;
        _ = Dispatch(AccountOnboardingRoutes.DiscoverImports, new AccountDiscoverImportsCommand());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _intents.IntentEmitted -= OnIntent;
        _shell.Renderer.FramePreparing -= OnFrame;
        _projections.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        ClearDrafts();
        _dynamic.Remove(_form);
        _importRows.Clear(); _characterRows.Clear();
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs e)
    {
        string command = e.Intent.Command.ToString();
        if (_disposed || e.Intent.Source.IsAssigned && command.StartsWith("ui.account.", StringComparison.Ordinal)
            && !Admitted(e.Intent.Source, e.Intent.Command, command is "ui.account.add" or "ui.account.import" or "ui.account.back")) return;
        if (command == "ui.account.add" || command == "ui.account.import")
        {
            _returnFocus = e.Intent.Source;
            _keyboardFocus = IsKeyboard(e.Intent.Source);
            if (command == "ui.account.add" && !HasMicrosoftProfile)
            {
                _provider = AccountLoginProvider.Microsoft;
                Open("device");
                Submit();
            }
            else Open(command == "ui.account.add" ? "providers" : "import");
        }
        else if (command.StartsWith("ui.navigation.", StringComparison.Ordinal) && command != "ui.navigation.expand")
        {
            if (ReadBool("open")) Close(restoreFocus: false);
        }
        else if (command == "ui.account.back")
        {
            if (!ReadBool("open")) _intents.Emit(XsrSemanticId.Parse("ui.account.dismiss"), e.Intent.Source, XsrCorrelationId.Create());
            else if (ReadText("mode") is "providers" or "import" || !HasMicrosoftProfile) Close();
            else Open("providers");
        }
        else if (!ReadBool("open")) return;
        else if (command == "ui.account.close") Close();
        else if (command == "ui.account.cancel")
            _ = Dispatch(AccountOnboardingRoutes.Cancel, new AccountLoginCancelCommand(Snapshot.Generation));
        else if (command is "ui.account.microsoft" or "ui.account.littleskin" or "ui.account.third-party" or "ui.account.offline")
        {
            _provider = command switch
            {
                "ui.account.microsoft" => AccountLoginProvider.Microsoft,
                "ui.account.littleskin" => AccountLoginProvider.LittleSkin,
                "ui.account.third-party" => AccountLoginProvider.ThirdParty,
                _ => AccountLoginProvider.Offline,
            };
            Open(_provider is AccountLoginProvider.Microsoft or AccountLoginProvider.LittleSkin ? "device"
                : _provider == AccountLoginProvider.Offline ? "offline" : "third-party");
            if (_provider is AccountLoginProvider.Microsoft or AccountLoginProvider.LittleSkin) Submit();
        }
        else if (command == "ui.account.submit")
        {
            if (ReadBool("failed") && ReadText("mode") != "device") Open(ReadText("mode"));
            else Submit();
        }
        else if (command == "ui.account.browse") _ = PickProfiles();
        else if (command == "ui.account.authorize")
        {
            _ = OpenAuthorization(Snapshot);
        }
        else if (command == "ui.account.copy-code") _ = CopyCode(Snapshot.UserCode, showSuccess: true);
        else if (command == "ui.account.choice")
        {
            if (_importRows.TryGetValue(e.Intent.Source, out string? path)) Publish("import-path", path);
            else if (_characterRows.TryGetValue(e.Intent.Source, out string? uuid)
                && Snapshot.Phase == AccountLoginPhase.ChoosingProfile && Snapshot.Generation == _characterGeneration)
                _ = Dispatch(AccountOnboardingRoutes.ChooseCharacter, new AccountChooseCharacterCommand(Snapshot.Generation, uuid));
        }
    }

    private void Open(string mode)
    {
        if (Snapshot.IsBusy) _ = Dispatch(AccountOnboardingRoutes.Cancel, new AccountLoginCancelCommand(Snapshot.Generation));
        Interlocked.Increment(ref _viewEpoch);
        ClearDrafts();
        Publish("open", true); Publish("mode", mode);
        _seenCompletion = Snapshot.Generation;
        _pendingFocus = mode switch { "offline" => "OfflineName", "third-party" => "AuthServer", "import" => "ImportPath", "device" => "AccountBack", _ => "ProviderMicrosoft" };
        if (mode == "import") _ = Dispatch(AccountOnboardingRoutes.DiscoverImports, new AccountDiscoverImportsCommand());
    }

    private void Close(bool restoreFocus = true)
    {
        if (Snapshot.IsBusy) _ = Dispatch(AccountOnboardingRoutes.Cancel, new AccountLoginCancelCommand(Snapshot.Generation));
        Interlocked.Increment(ref _viewEpoch);
        Publish("open", false);
        _store.Publish(_store.Resolve(LaunchPageState.AccountAddVisibleKey), true);
        ClearDrafts();
        if (restoreFocus) RestoreFocus();
    }

    private void Submit()
    {
        if (Snapshot.IsBusy) return;
        if (ReadText("mode") == "import")
        {
            _ = Dispatch(AccountOnboardingRoutes.Import, new AccountImportCommand(Draft("ImportPath")));
            return;
        }
        AccountLoginStartCommand command = new(_provider,
            _provider == AccountLoginProvider.Offline ? Draft("OfflineName") : Draft("AuthUsername"),
            Draft("AuthServer"), Draft("AuthPassword"));
        _shell.Renderer.SetTextInputValue(_entities["AuthPassword"], string.Empty);
        _ = Dispatch(AccountOnboardingRoutes.Start, command);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        string copy = Copy("account.title.providers"); bool copyChanged = copy != _copyStamp; _copyStamp = copy;
        if (!_projections.Consume() && _pendingFocus is null && !copyChanged) return;
        AccountLoginSnapshot snapshot = Snapshot;
        bool open = ReadBool("open");
        if (open && snapshot.Generation > _seenCompletion && snapshot.Phase == AccountLoginPhase.Completed)
        {
            _seenCompletion = snapshot.Generation;
            _feedback.Success(snapshot.Message);
            Publish("open", false);
            _store.Publish(_store.Resolve(LaunchPageState.AccountAddVisibleKey), true);
            _store.Publish(_store.Resolve(LaunchPageState.AccountPickerKey), false);
            ClearDrafts();
            RestoreFocus();
            open = false;
        }
        bool failed = open && snapshot.Generation > _seenCompletion
            && snapshot.Phase is AccountLoginPhase.Failed or AccountLoginPhase.Cancelled;
        Publish("failed", failed);
        bool preparing = snapshot.Phase is AccountLoginPhase.Starting or AccountLoginPhase.Saving;
        Publish("loading", open && preparing);
        string mode = ReadText("mode");
        foreach (string name in new[] { "providers", "offline", "third-party", "import", "device" })
            Publish(name, open && mode == name && !snapshot.IsBusy && !failed);
        Publish("title", mode switch
        {
            "offline" => Copy("account.title.offline"),
            "third-party" => Copy("account.title.third-party"),
            "import" => Copy("account.title.import"),
            "device" when _provider == AccountLoginProvider.LittleSkin => Copy("account.title.littleskin"),
            "device" => Copy("account.title.microsoft"),
            _ => Copy("account.title.providers"),
        });
        Publish("status", mode == "providers" || snapshot.Generation <= _seenCompletion ? string.Empty : snapshot.Message);
        Publish("status-visible", !preparing && !failed && !string.IsNullOrWhiteSpace(ReadText("status")));
        Publish("user-code", snapshot.UserCode);
        Publish("busy", open && snapshot.IsBusy);
        Publish("code", open && snapshot.Phase == AccountLoginPhase.AwaitingAuthorization);
        Publish("characters", open && snapshot.Phase == AccountLoginPhase.ChoosingProfile);
        Publish("submit", open && mode != "providers" && !snapshot.IsBusy);
        Publish("submit-label", mode == "import" ? Copy("account.submit.import") : mode == "offline" ? Copy("account.submit.offline") : mode == "device" ? Copy("account.submit.device") : Copy("account.submit.default"));
        if (open && mode == "device" && snapshot.Phase == AccountLoginPhase.AwaitingAuthorization
            && snapshot.Generation != _presentedChallengeGeneration)
        {
            // State can invalidate many frames while polling. Generation is the service-owned
            // identity of this public challenge, so browser/clipboard effects occur exactly once.
            _presentedChallengeGeneration = snapshot.Generation;
            _ = OpenAuthorization(snapshot);
            _ = CopyCode(snapshot.UserCode, showSuccess: false);
        }
        XsrCollectionSnapshot<AccountImportCandidate> imports = _store.ReadCollection<AccountImportCandidate>(_store.Resolve(AccountOnboardingState.Imports));
        if (imports.Revision != _importsRevision || copyChanged)
        {
            BuildRows("ImportCandidates", _importRows, imports.Items.Select(item => (item.DisplayPath, Copy("account.import.choice"), item.DisplayPath)));
            _importsRevision = imports.Revision;
        }
        XsrCollectionSnapshot<AccountCharacterChoice> characters = _store.ReadCollection<AccountCharacterChoice>(_store.Resolve(AccountOnboardingState.Characters));
        if (characters.Revision != _charactersRevision || copyChanged || _characterGeneration != snapshot.Generation)
        {
            BuildRows("CharacterChoices", _characterRows, characters.Items.Select(item => (item.Uuid, item.Username, DesktopBootstrapText.Format("Character", new Dictionary<string, string> { ["name"] = item.Username }, _shell.Renderer.LocalizeText))));
            _charactersRevision = characters.Revision;
            _characterGeneration = snapshot.Generation;
        }
        string path = ReadText("import-path");
        if (path != _appliedPath)
        {
            _appliedPath = path;
            _shell.Renderer.SetTextInputValue(_entities["ImportPath"], path);
        }
        if (open && _pendingFocus is { } focus)
        {
            if (_shell.Renderer.Focus(_entities[focus], _keyboardFocus)) _pendingFocus = null;
        }
    }

    private void BuildRows(string host, Dictionary<XsrUiEntityId, string> destinations,
        IEnumerable<(string Value, string Text, string Label)> entries)
    {
        var items = entries.GroupBy(item => item.Value, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var desired = new HashSet<string>(items.Select(item => item.Value), StringComparer.Ordinal);
        foreach (var pair in destinations.ToArray())
            if (!desired.Contains(pair.Value)) { _dynamic.Remove(pair.Key); destinations.Remove(pair.Key); }
        List<XsrUiEntityId> order = [];
        foreach (var item in items)
        {
            var row = destinations.FirstOrDefault(pair => pair.Value == item.Value).Key;
            var data = new Dictionary<string, string> { ["key"] = host + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Value))).ToLowerInvariant(), ["text"] = item.Text, ["label"] = item.Label };
            var fragment = DesktopMarkup.Dynamic("AccountFormChoice.d.nxml", data);
            if (row.IsAssigned) _dynamic.Reload(row, fragment);
            else { row = _dynamic.Append(_entities[host], fragment); destinations[row] = item.Value; }
            order.Add(row);
        }
        _dynamic.Reorder(_entities[host], order);
    }
    private string Copy(string key) => _shell.Renderer.LocalizeText(DesktopBootstrapText.Raw(key));
    private bool Admitted(XsrUiEntityId source, XsrSemanticId command, bool allowHeader)
    {
        if (!_shell.Tree.IsAlive(source) || _shell.Tree.GetComponent<XsrUiInput>(source) is not { Enabled: true } input
            || input.BoundEnabled.IsAssigned && _store.ReadAppliedValue(input.BoundEnabled) is not true
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(source)?.Command != command) return false;
        bool owned = false;
        for (var node = source; node.IsAssigned; node = _shell.Tree.Parent(node))
        {
            var box = _shell.Tree.GetComponent<XsrUiElement>(node);
            if (box is not null && (box.BoundVisibility.IsAssigned ? _store.ReadAppliedValue(box.BoundVisibility) is not true : !box.IsVisible)) return false;
            if (node == _form) owned = true;
            if (node == _shell.Stage.Navigation.Current) return owned || allowHeader;
        }
        return false;
    }

    private async Task Dispatch<T>(XsrSemanticId route, T command) where T : notnull
    {
        if (!_commands.TryResolve(route, out XsrCommandId id)) { _feedback.Error(Copy("account.error.unregistered")); return; }
        XsrResult result = await _commands.Dispatch(id, command, cancellationToken: _lifetime.Token).Completion.ConfigureAwait(false);
        if (!_disposed && !result.IsSuccess) _feedback.Error(Copy("account.error.session"));
    }

    private async Task PickProfiles()
    {
        long epoch = Interlocked.Read(ref _viewEpoch);
        try
        {
            string? path = await RequireEffects().PickProfiles().ConfigureAwait(false);
            if (!_disposed && epoch == Interlocked.Read(ref _viewEpoch) && path is not null) Publish("import-path", path);
        }
        catch (Exception) { if (!_disposed) _feedback.Warn(Copy("account.error.picker")); }
    }

    private async Task OpenAuthorization(AccountLoginSnapshot snapshot)
    {
        string address = snapshot.VerificationUri;
        if (!AccountVerificationContract.IsVerificationUri(_provider, address))
        {
            if (!_disposed) _feedback.Warn(Copy("account.error.address"));
            return;
        }

        try { await RequireEffects().OpenAuthorization(new Uri(address)).ConfigureAwait(false); }
        catch (Exception) { if (!_disposed) _feedback.Warn(Copy("account.error.browser")); }
    }

    private async Task CopyCode(string code, bool showSuccess)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            if (!_disposed) _feedback.Warn(Copy("account.error.code"));
            return;
        }

        try
        {
            _log?.Debug("AccountOnboarding",
                $"Device authorization code copy started provider={_provider} mode={(showSuccess ? "manual" : "automatic")}");
            await RequireEffects().CopyCode(code).ConfigureAwait(false);
            _log?.Info("AccountOnboarding",
                $"Device authorization code copied provider={_provider} mode={(showSuccess ? "manual" : "automatic")}");
            if (showSuccess && !_disposed) _feedback.Success(Copy("account.code.copied"));
        }
        catch (Exception error)
        {
            _log?.Error(
                "AccountOnboarding",
                $"Device authorization code copy failed provider={_provider} mode={(showSuccess ? "manual" : "automatic")}",
                ExceptionDiagnostics.Describe(error));
            if (!_disposed) _feedback.Warn(Copy("account.error.clipboard"));
        }
    }

    private IAccountUiEffects RequireEffects() => _effects ?? throw new InvalidOperationException("Native actions unavailable.");
    private bool HasMicrosoftProfile => _store.ReadCollection<LaunchProfileView>(
        _store.Resolve(AccountStateContract.ProfilesKey)).Items.Any(profile => profile.Kind == LaunchProfileKind.Microsoft);
    private AccountLoginSnapshot Snapshot => _store.Read<AccountLoginSnapshot>(_store.Resolve(AccountOnboardingState.Login)).Value
        ?? new(0, AccountLoginPhase.Idle, "");
    private string Draft(string key) => _shell.Tree.GetComponent<XsrUiTextInput>(_entities[key])!.ReadDraft();
    private bool IsKeyboard(XsrUiEntityId entity) => entity.IsAssigned && _shell.Tree.GetComponent<XsrUiInput>(entity)?.IsFocusVisible == true;
    private bool ReadBool(string name) => _store.ReadAppliedValue(_store.Resolve(AccountFormState.Key(name))) is true;
    private string ReadText(string name) => _store.ReadAppliedValue(_store.Resolve(AccountFormState.Key(name))) as string ?? string.Empty;
    private void Publish<T>(string name, T value)
    {
        XsrStateId id = _store.Resolve(AccountFormState.Key(name));
        if (!Equals(_store.ReadAppliedValue(id), value)) _store.Publish(id, value);
    }
    private void ClearDrafts()
    {
        foreach (string key in new[] { "OfflineName", "AuthServer", "AuthUsername", "AuthPassword", "ImportPath" })
            _shell.Renderer.SetTextInputValue(_entities[key], string.Empty);
        _appliedPath = string.Empty;
        Publish("import-path", string.Empty);
        _pendingFocus = null;
    }
    private void RestoreFocus()
    {
        if (!_returnFocus.IsAssigned || !_shell.Renderer.Focus(_returnFocus, _keyboardFocus))
            if (_fallbackFocus.IsAssigned) _shell.Renderer.Focus(_fallbackFocus, _keyboardFocus);
    }
}
