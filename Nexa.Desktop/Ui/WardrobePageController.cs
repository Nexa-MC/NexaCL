using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Accounts;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal static class WardrobePresentationState
{
    internal static readonly XsrSemanticId WakeKey = XsrSemanticId.Parse("ui.wardrobe.wake");
    internal static void DeclareState(XsrStateStoreBuilder builder) => builder.Cell<long>(WakeKey, "Nexa.Desktop.Wardrobe");
}

/// <summary>Render-thread projection of the dev wardrobe behavior, with admitted service intents.</summary>
internal sealed class WardrobePageController : IDisposable
{
    private sealed class CardVisual(AccountWardrobeCard card, XsrUiEntityId container, XsrUiEntityId body,
        XsrUiEntityId image, XsrUiEntityId details, XsrUiEntityId viewButton, XsrUiEntityId apply)
    {
        internal readonly AccountWardrobeCard Card = card;
        internal readonly XsrUiEntityId Container = container, Body = body, Image = image, Details = details, ViewButton = viewButton, Apply = apply;
        internal WardrobePlayerView View = card.Kind == AccountWardrobeTextureKind.Cape ? WardrobePlayerView.Back : WardrobePlayerView.Front;
        internal AccountWardrobeResolvedTextures? Loaded;
        internal AccountWardrobeResolvedTextures? Presented;
        internal WardrobePlayerView PresentedView;
        internal Dictionary<string, string> Projection = [];
    }
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _workspace;
    private readonly Dictionary<string, string> _presentation = DesktopWardrobeText.Defaults(library: false);
    private Dictionary<string, string> _applied = [];
    private string _copyStamp = "";
    private double _trackHeight = Required(DesktopWardrobeText.Metric("track").MinHeight);
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrQueryRouter _queries;
    private readonly XsrCommandRouter _commands;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly Dictionary<string, XsrUiEntityId> _nodes = [];
    private readonly Dictionary<string, HashSet<XsrUiEntityId>> _staticSources = [];
    private readonly Dictionary<XsrUiEntityId, CardVisual> _applySources = [], _viewSources = [];
    private readonly List<CardVisual> _skinCards = [], _capeCards = [];
    private readonly Dictionary<CardVisual, Task<XsrResult<AccountWardrobeResolvedTextures>>> _textures = [];
    private readonly HashSet<CardVisual> _textureAttempts = [];
    private readonly ConcurrentQueue<DesktopUiIntent> _pending = new();
    private CancellationTokenSource _stop = new();
    private Func<CancellationToken, Task<string?>>? _pickFile;
    private Action? _openLibrary;
    private Action<Uri>? _openUrl;
    private Task<string?>? _picking;
    private Task<XsrResult<AccountWardrobeSnapshot>>? _reading;
    private Task<XsrResult<AccountWardrobeSkinPreview>>? _previewing;
    private Task<XsrResult>? _writing;
    private AccountWardrobeSnapshot? _snapshot;
    private AccountWardrobeSkinPreview? _preview;
    private AccountWardrobeResolvedTextures? _presentedCurrent;
    private WardrobePlayerView _currentView = WardrobePlayerView.Front, _presentedCurrentView;
    private string? _observedIdentity;
    private LaunchProfileView? _readingProfile;
    private long _readingSelectionEpoch;
    private LaunchProfileView? _writingProfile;
    private long _writingSelectionEpoch;
    private bool _visible, _loaded, _slim, _disposed, _localOpen;
    private volatile bool _dirty = true;
    private long _wake, _selectionEvents, _seenSelectionEvents, _rosterEvents, _seenRosterEvents;
    private string _status = Raw("read-account");
    private string? _operationNotice;
    private XsrUiSize _viewport;
    private double _skinOffset, _capeOffset, _cardWidth = CardWidth(Required(DesktopWardrobeText.Metric("track").MinHeight)), _cardHeight = Required(DesktopWardrobeText.Metric("track").MinHeight), _trackWidth = Required(DesktopWardrobeText.Metric("track").MinWidth);
    private bool Busy => _reading is not null || _previewing is not null || _writing is not null || _picking is not null;
    internal const int CapePageSize = 12; // Bounded lazy image window; the horizontal track retains every card.

    internal WardrobePageController(XsrUiShell shell, DesktopUiIntentSink intents, XsrQueryRouter queries,
        XsrCommandRouter commands, XsrStateStore store, DesktopFeedbackService feedback)
    {
        _shell = shell; _intents = intents; _queries = queries; _commands = commands; _store = store; _feedback = feedback;
        _dynamic = new(shell.Tree, store);
        Page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("WardrobePage.nxml"), shell.Tree, store);
        var slot = shell.Tree.Children(Page).Single();
        _workspace = _dynamic.Append(slot, DesktopMarkup.Dynamic("WardrobeWorkspace.d.nxml", _presentation));
        shell.Tree.Walk(_workspace, entity =>
        {
            string name = shell.Tree.Name(entity); if (name.Length > 0) _nodes[name] = entity;
            if (shell.Tree.GetComponent<XsrUiCommandBinding>(entity) is { } binding)
            {
                if (!_staticSources.TryGetValue(binding.Command.Value, out var sources)) _staticSources[binding.Command.Value] = sources = [];
                sources.Add(entity);
            }
            return true;
        });
        _intents.IntentEmitted += OnIntent; shell.Renderer.FramePreparing += OnFrame; _store.Changed += OnStateChanged;
    }
    internal XsrUiEntityId Page { get; }
    internal XsrUiEntityId LibraryButton => _nodes["WardrobeLibrary"];
    internal void ConfigureFilePicker(Func<CancellationToken, Task<string?>> picker) => _pickFile = picker;
    internal void ConfigureLibrary(Action openLibrary) => _openLibrary = openLibrary;
    internal void ConfigureOpenUrl(Action<Uri> openUrl) => _openUrl = openUrl;

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (!_disposed && _shell.Stage.Navigation.Current == Page && args.Intent.Command.Value.StartsWith("ui.wardrobe.", StringComparison.Ordinal))
            _pending.Enqueue(args.Intent);
    }
    private void OnStateChanged(XsrStateChange change)
    {
        if (change.SemanticId == AccountStateContract.SelectedKey) Interlocked.Increment(ref _selectionEvents);
        if (change.SemanticId == AccountStateContract.ProfilesKey) Interlocked.Increment(ref _rosterEvents);
        if (change.SemanticId == AccountStateContract.ProfilesKey || change.SemanticId == AccountStateContract.SelectedKey
            || change.SemanticId == AccountSkinContract.SkinsKey) _dirty = true;
    }
    private LaunchProfileView CurrentProfile()
    {
        int selected = _store.Read<int>(_store.Resolve(AccountStateContract.SelectedKey)).Value;
        return _store.ReadCollection<LaunchProfileView>(_store.Resolve(AccountStateContract.ProfilesKey)).Items.FirstOrDefault(p => p.Index == selected);
    }
    private string? CurrentIdentity()
    {
        var profile = CurrentProfile();
        return string.IsNullOrWhiteSpace(profile.Username) ? null
            : $"{profile.Index}\n{profile.Kind}\n{profile.Uuid}\n{profile.AuthServer}\n{profile.Username}";
    }
    private void OnFrame(object? sender, EventArgs args)
    {
        string copy = _shell.Renderer.LocalizeText(Raw("read-account")); if (copy != _copyStamp) { _copyStamp = copy; _dirty = true; }
        if (_disposed) return;
        bool visible = _shell.Stage.Navigation.Current == Page;
        if (!visible) { if (_visible) { Retire(); } _visible = false; return; }
        double skinOffset = Scroll("WardrobeSkins").OffsetX, capeOffset = Scroll("WardrobeCapes").OffsetX;
        double trackWidth = _trackWidth;
        if (_shell.Renderer.TryGetScrollSnapshot(_nodes["WardrobeSkins"], out var skinTrack) && skinTrack.ViewportWidth > 0)
            trackWidth = skinTrack.ViewportWidth;
        else if (_shell.Renderer.TryGetScrollSnapshot(_nodes["WardrobeCapes"], out var capeTrack) && capeTrack.ViewportWidth > 0)
            trackWidth = capeTrack.ViewportWidth;
        if (_viewport != _shell.Renderer.Viewport || _skinOffset != skinOffset || _capeOffset != capeOffset || _trackWidth != trackWidth) _dirty = true;
        _trackWidth = trackWidth;
        // Budget both tracks from their parent viewport, never from card-driven content extent.
        // This keeps measurement stable while status rows appear and disappear.
        if (_viewport == _shell.Renderer.Viewport &&
            _shell.Renderer.TryGetScrollSnapshot(_nodes["WardrobeContent"], out var contentViewport))
        {
            var content = _shell.Tree.GetComponent<XsrUiElement>(_nodes["WardrobeContent"])!;
            double fixedHeight = content.Padding.Vertical;
            int visibleChildren = 0;
            foreach (var child in _shell.Tree.Children(_nodes["WardrobeContent"]))
            {
                var element = _shell.Tree.GetComponent<XsrUiElement>(child)!;
                if (!element.IsVisible) continue;
                visibleChildren++;
                if (child != _nodes["WardrobeSkinTrack"] && child != _nodes["WardrobeCapeTrack"])
                    fixedHeight += (element.Height ?? 0) + element.Padding.Vertical + element.Margin.Vertical;
            }
            fixedHeight += Math.Max(0, visibleChildren - 1) *
                _shell.Tree.GetComponent<XsrUiStackPanel>(_nodes["WardrobeContent"])!.Spacing;
            var metric = DesktopWardrobeText.Metric("track");
            double trackHeight = Math.Max(Required(metric.MinHeight), (contentViewport.ViewportHeight - fixedHeight) / 2);
            double height = Math.Clamp(trackHeight - metric.Margin.Top, Required(metric.MinHeight), Required(metric.MaxHeight));
            if (_trackHeight != trackHeight)
            {
                _trackHeight = trackHeight;
                foreach (string track in new[] { "WardrobeSkinTrack", "WardrobeCapeTrack" }) _presentation[track + "_height"] = Number(trackHeight);
                _dirty = true;
            }
            if (Math.Abs(height - _cardHeight) > .5)
            {
                _cardHeight = height;
                _cardWidth = CardWidth(height);
                SetCardMetrics();
                _dirty = true;
            }
        }
        if (_shell.Renderer.TryGetScrollSnapshot(_nodes["WardrobeProfileRail"], out var railViewport))
        {
            var rail = _shell.Tree.GetComponent<XsrUiElement>(_nodes["WardrobeProfileRail"])!;
            double reserved = rail.Padding.Vertical;
            int count = 0;
            foreach (var child in _shell.Tree.Children(_nodes["WardrobeProfileRail"]))
            {
                var element = _shell.Tree.GetComponent<XsrUiElement>(child)!;
                if (!element.IsVisible) continue;
                count++;
                if (child != _nodes["WardrobeCurrentCard"])
                    reserved += (element.Height ?? 0) + element.Padding.Vertical + element.Margin.Vertical;
            }
            reserved += Math.Max(0, count - 1) * DesktopWardrobeText.Metric("rail").Spacing;
            var card = _shell.Tree.GetComponent<XsrUiElement>(_nodes["WardrobeCurrentCard"])!;
            double height = Math.Max(Required(DesktopWardrobeText.Metric("current-card").MinHeight), railViewport.ViewportHeight - reserved - card.Padding.Vertical);
            if (card.Height != height)
            {
                _presentation["WardrobeCurrentCard_height"] = Number(height); _dirty = true;
            }
        }
        long selections = Volatile.Read(ref _selectionEvents), rosters = Volatile.Read(ref _rosterEvents);
        if (_visible && (_loaded || _observedIdentity is null) && !_dirty && _pending.IsEmpty
            && selections == _seenSelectionEvents && rosters == _seenRosterEvents
            && _picking is not { IsCompleted: true } && _reading is not { IsCompleted: true }
            && _previewing is not { IsCompleted: true } && _writing is not { IsCompleted: true }
            && !_textures.Values.Any(task => task.IsCompleted)) return;
        string? identity = CurrentIdentity();
        bool rosterChangedWhileIdle = rosters != _seenRosterEvents && _reading is null && _writing is null;
        bool admittedReadContext = _reading is not null && _readingProfile is { } readingProfile
            && selections == _readingSelectionEpoch && SamePrincipal(readingProfile, CurrentProfile());
        bool admittedWriteContext = _writing is not null && _writingProfile is { } writingProfile
            && selections == _writingSelectionEpoch && SamePrincipal(writingProfile, CurrentProfile());
        if (!_visible || identity != _observedIdentity && !admittedReadContext && !admittedWriteContext
            || selections != _seenSelectionEvents || rosterChangedWhileIdle)
        {
            Retire(); _observedIdentity = identity;
            _status = identity is null ? Raw("copy.df5f107f6bca") : Raw("read-account");
        }
        _visible = true; _seenSelectionEvents = selections; _seenRosterEvents = rosters;
        ApplyResponsiveLayout();
        if (identity is null) { Present(); return; }
        while (_pending.TryDequeue(out var intent))
        {
            Handle(intent);
            if (_disposed || _shell.Stage.Navigation.Current != Page) return;
        }
        if (_picking is { IsCompleted: true } picking)
        {
            _dirty = true; _picking = null;
            if (picking.IsCompletedSuccessfully && picking.Result is { } path)
            { _shell.Renderer.SetTextInputValue(_nodes["WardrobeSkinPath"], path); BeginPreview(); }
            else if (picking.IsFaulted) _status = Raw("copy.9547fec915a5");
        }
        if (!_loaded && _reading is null) BeginRead();
        if (_reading is { IsCompleted: true } reading)
        {
            _dirty = true; _reading = null; _loaded = true;
            if (Succeeded(reading) && MatchesPublishedProfile(reading.Result.Value))
            {
                _snapshot = reading.Result.Value;
                _status = _operationNotice ?? _snapshot.ProviderStatus ?? _snapshot.UnavailableReason ?? Raw("ready");
                BuildCards();
            }
            else
                _status = reading.IsCompletedSuccessfully && !reading.Result.IsSuccess
                    ? reading.Result.Error?.Message ?? Raw("copy.fc0b5b5c5528") : Raw("copy.259aaeffa337");
            // An admitted read can publish a provider rename before its final reply, including
            // a later failure. Consume that same-principal roster change without auto-retrying.
            if (_readingProfile is { } captured && SamePrincipal(captured, CurrentProfile())
                && Volatile.Read(ref _selectionEvents) == _readingSelectionEpoch)
                _observedIdentity = CurrentIdentity();
            _readingProfile = null;
            _seenRosterEvents = Volatile.Read(ref _rosterEvents);
        }
        if (_previewing is { IsCompleted: true } previewing)
        {
            _dirty = true; _previewing = null;
            if (Succeeded(previewing)) { _preview = previewing.Result.Value; _status = Raw("copy.00e98e4bc87c"); }
            else _status = previewing.IsCompletedSuccessfully ? previewing.Result.Error?.Message ?? Raw("copy.13135fbcc904") : Raw("copy.9a6d2ea3d613");
        }
        if (_writing is { IsCompleted: true } writing)
        {
            _dirty = true; _writing = null;
            bool currentWrite = _writingProfile is { } captured && SamePrincipal(captured, CurrentProfile())
                && Volatile.Read(ref _selectionEvents) == _writingSelectionEpoch;
            if (!currentWrite)
            {
                Retire(); _observedIdentity = CurrentIdentity(); _status = Raw("read-account");
            }
            else if (writing.IsCompletedSuccessfully && writing.Result.IsSuccess)
            {
                _operationNotice = null; _feedback.Info(Raw("copy.9fa5be79e728")); _loaded = false; _snapshot = null; _preview = null; _localOpen = false;
                _shell.Renderer.SetTextInputValue(_nodes["WardrobeSkinPath"], ""); _status = Raw("copy.1d165a5566db");
            }
            else
            {
                _operationNotice = _status = writing.IsCompletedSuccessfully ? writing.Result.Error?.Message ?? Raw("copy.0a69a3749a9e") : Raw("copy.d3ff07c74807");
                _loaded = false; _snapshot = null;
            }
            if (currentWrite) _observedIdentity = CurrentIdentity();
            _writingProfile = null;
            _seenRosterEvents = Volatile.Read(ref _rosterEvents);
        }
        PumpTextures(); Present();
    }
    private bool AdmittedSource(DesktopUiIntent intent)
    {
        if (!intent.Source.IsAssigned || !_shell.Tree.IsAlive(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source) is not { Enabled: true }
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(intent.Source)?.Command != intent.Command) return false;
        bool inLocal = false;
        for (var entity = intent.Source; entity.IsAssigned; entity = _shell.Tree.Parent(entity))
        {
            if (_shell.Tree.GetComponent<XsrUiElement>(entity) is { IsVisible: false }) return false;
            inLocal |= entity == _nodes["WardrobeLocalLayer"];
            if (entity == Page) return !_localOpen || inLocal;
        }
        return false;
    }
    private void Handle(DesktopUiIntent intent)
    {
        if (!AdmittedSource(intent)) return;
        string command = intent.Command.Value;
        bool knownStatic = _staticSources.TryGetValue(command, out var sources) && sources.Contains(intent.Source);
        bool knownView = command == "ui.wardrobe.view.card" && _viewSources.ContainsKey(intent.Source);
        bool knownApply = _applySources.TryGetValue(intent.Source, out var candidate)
            && command == (candidate.Card.Kind == AccountWardrobeTextureKind.Cape ? "ui.wardrobe.cape.choose" : "ui.wardrobe.skin.choose");
        if (!knownStatic && !knownApply && !knownView) return;
        _dirty = true;
        if (command is "ui.wardrobe.cancel" or "ui.wardrobe.local.close")
        {
            if (Busy) { Retire(); _observedIdentity = CurrentIdentity(); _loaded = true; _status = Raw("copy.eae1ff8ac1ab"); }
            else { _localOpen = false; _preview = null; _shell.Renderer.SetTextInputValue(_nodes["WardrobeSkinPath"], ""); }
            return;
        }
        if (command == "ui.wardrobe.refresh") { if (!Busy) { Retire(); _observedIdentity = CurrentIdentity(); } return; }
        if (command == "ui.wardrobe.view.current") { _currentView = WardrobePlayerPresentation.Next(_currentView); return; }
        if (command == "ui.wardrobe.view.card" && _viewSources.TryGetValue(intent.Source, out var viewed))
        { viewed.View = WardrobePlayerPresentation.Next(viewed.View); return; }
        if (Busy || _snapshot is not { } snapshot) return;
        _operationNotice = null;
        if (command == "ui.wardrobe.library" && _openLibrary is not null) { _openLibrary(); return; }
        if (command == "ui.wardrobe.manage" && SafeManageUri(snapshot.ManageUri) is { } uri && _openUrl is not null)
        {
            try { _openUrl(uri); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
            { _status = Raw("copy.d6f1c4f4be1a"); }
            return;
        }
        if (command is "ui.wardrobe.skin.previous" or "ui.wardrobe.skin.next" or "ui.wardrobe.cape.previous" or "ui.wardrobe.cape.next")
        {
            string track = command.Contains(".skin.", StringComparison.Ordinal) ? "WardrobeSkins" : "WardrobeCapes";
            var scroll = Scroll(track);
            scroll.OffsetX = Math.Max(0, scroll.OffsetX + (command.EndsWith("next", StringComparison.Ordinal) ? 1 : -1) * Math.Max(_cardWidth + TrackSpacing, _trackWidth - TrackSpacing));
            _shell.Tree.MarkDirty(_nodes[track], XsrUiDirtyKinds.Layout); return;
        }
        if (command is "ui.wardrobe.classic" or "ui.wardrobe.slim")
        { _slim = command.EndsWith("slim", StringComparison.Ordinal); _preview = null; _status = Raw("copy.46da263430ba"); }
        else if (command == "ui.wardrobe.browse")
        {
            if (snapshot.CanUploadSkin && _pickFile is not null)
            {
                _localOpen = true;
                try { _picking = _pickFile(_stop.Token); Wake(_picking); }
                catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
                { _status = Raw("copy.9547fec915a5"); }
            }
            else if (SafeManageUri(snapshot.ManageUri) is { } site && _openUrl is not null)
            {
                try { _openUrl(site); }
                catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
                { _status = Raw("copy.d6f1c4f4be1a"); }
            }
            else _status = snapshot.UnavailableReason ?? Raw("copy.67ee74e4b591");
        }
        else if (command == "ui.wardrobe.preview" && snapshot.CanUploadSkin) BeginPreview();
        else if (command == "ui.wardrobe.upload" && snapshot.CanUploadSkin && _preview is { } preview)
            Write(AccountWardrobeContract.UploadSkin, new AccountWardrobeUploadSkinCommand(snapshot.Identity,
                preview.Image.Bytes.ToArray(), preview.FileName, preview.IsSlim));
        else if (command == "ui.wardrobe.cape.clear" && snapshot.CanChooseCape)
            Write(AccountWardrobeContract.SetCape, new AccountWardrobeSetCapeCommand(snapshot.Identity, null));
        else if (command is "ui.wardrobe.skin.choose" or "ui.wardrobe.cape.choose"
            && _applySources.TryGetValue(intent.Source, out var chosen) && chosen.Card.CanApply && !chosen.Card.IsActive)
        {
            if (chosen.Card.Kind == AccountWardrobeTextureKind.Cape && !snapshot.CanChooseCape) return;
            if (chosen.Card.Kind == AccountWardrobeTextureKind.Cape && chosen.Card.CapeId is { } id)
                Write(AccountWardrobeContract.SetCape, new AccountWardrobeSetCapeCommand(snapshot.Identity, id));
            else Write(AccountWardrobeContract.ApplyCard, new AccountWardrobeApplyCardCommand(snapshot.Identity, chosen.Card.Id));
        }
    }
    private static Uri? SafeManageUri(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && !uri.IsLoopback ? uri : null;
    private void BeginRead()
    {
        _dirty = true; _status = _operationNotice ?? Raw("read-account");
        if (!_queries.TryResolve(AccountWardrobeContract.Read, out var route)) { _loaded = true; _status = Raw("copy.3fdfadaa192d"); return; }
        _readingProfile = CurrentProfile(); _readingSelectionEpoch = Volatile.Read(ref _selectionEvents);
        _reading = _queries.QueryAsync<AccountWardrobeQuery, AccountWardrobeSnapshot>(route, new(), cancellationToken: _stop.Token).AsTask(); Wake(_reading);
    }
    private bool MatchesPublishedProfile(AccountWardrobeSnapshot snapshot)
    {
        var published = CurrentProfile();
        return _readingProfile is { } captured && SamePrincipal(captured, published)
            && Volatile.Read(ref _selectionEvents) == _readingSelectionEpoch
            && snapshot.Profile == published && snapshot.Identity.Index == published.Index
            && snapshot.Identity.Uuid == published.Uuid && snapshot.Identity.Kind == published.Kind;
    }
    private static bool SamePrincipal(LaunchProfileView captured, LaunchProfileView published) =>
        !string.IsNullOrWhiteSpace(published.Username) && captured.Index == published.Index
        && captured.Uuid == published.Uuid && captured.Kind == published.Kind && captured.AuthServer == published.AuthServer;
    private void BeginPreview()
    {
        _preview = null;
        if (_snapshot is not { } snapshot || !_queries.TryResolve(AccountWardrobeContract.ValidateSkin, out var route)) return;
        string path = _shell.Tree.GetComponent<XsrUiTextInput>(_nodes["WardrobeSkinPath"])!.ReadDraft();
        _previewing = _queries.QueryAsync<AccountWardrobeSkinQuery, AccountWardrobeSkinPreview>(route,
            new(snapshot.Identity, path, _slim), cancellationToken: _stop.Token).AsTask(); Wake(_previewing); _status = Raw("copy.4d82b5ad78bd");
    }
    private void Write<T>(XsrSemanticId semantic, T command) where T : notnull
    {
        if (!_commands.TryResolve(semantic, out var route)) { _status = Raw("copy.57a831d60896"); return; }
        _writingProfile = CurrentProfile(); _writingSelectionEpoch = Volatile.Read(ref _selectionEvents);
        _writing = _commands.Dispatch(route, command, cancellationToken: _stop.Token).Completion; Wake(_writing); _status = Raw("copy.cf41ca6935be");
    }
    private void Wake(Task task)
    {
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            if (_disposed) return;
            _dirty = true;
            try { _store.Publish(_store.Resolve(WardrobePresentationState.WakeKey), Interlocked.Increment(ref _wake)); }
            catch (ObjectDisposedException) { }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private static bool Succeeded<T>(Task<XsrResult<T>> task) => task.IsCompletedSuccessfully && task.Result.IsSuccess;
    private void PumpTextures()
    {
        foreach (var (card, pending) in _textures.ToArray())
            if (pending.IsCompleted)
            {
                _textures.Remove(card);
                if (Succeeded(pending)) card.Loaded = pending.Result.Value;
                _dirty = true;
            }
        var visible = VisibleCards(_skinCards, Scroll("WardrobeSkins").OffsetX)
            .Concat(VisibleCards(_capeCards, Scroll("WardrobeCapes").OffsetX)).ToHashSet();
        foreach (var card in _skinCards.Concat(_capeCards))
            if (!visible.Contains(card) && !_textures.ContainsKey(card) && card.Loaded is not null)
            { card.Loaded = null; _textureAttempts.Remove(card); card.Presented = null; }
        if (_localOpen || Busy || _snapshot is not { } snapshot || !_queries.TryResolve(AccountWardrobeContract.ReadTexture, out var route)) return;
        foreach (var card in visible)
        {
            if (_textures.Count >= 3) break;
            if (!_textureAttempts.Add(card) || card.Card.Appearance.Skin is not null &&
                (card.Card.Appearance.CapeAddress is null || card.Card.Appearance.Cape is not null)) continue;
            var pending = _queries.QueryAsync<AccountWardrobeTextureQuery, AccountWardrobeResolvedTextures>(route,
                new(snapshot.Identity, CardId: card.Card.Id, Kind: card.Card.Kind), cancellationToken: _stop.Token).AsTask();
            _textures.Add(card, pending); Wake(pending);
        }
    }
    private IEnumerable<CardVisual> VisibleCards(List<CardVisual> cards, double offset) => cards
        .Skip(Math.Clamp((int)(offset / (_cardWidth + TrackSpacing)), 0, Math.Max(0, cards.Count - 1))).Take(CapePageSize);
    private void Present()
    {
        if (!_dirty) return; _dirty = false;
        _presentation["pageLoading"] = Flag(_reading is not null);
        var profile = _writing is not null && _writingProfile is { } captured && SamePrincipal(captured, CurrentProfile())
            ? CurrentProfile() : _snapshot?.Profile ?? CurrentProfile();
        Text("WardrobeIdentity", string.IsNullOrWhiteSpace(profile.Username) ? Raw("copy.7e7d4ca64386") : profile.Username, literal: true);
        var profileCaption = ProfileCaption(profile);
        Text("WardrobeProfileKind", profileCaption.Content, profileCaption.Literal);
        Text("WardrobeStatus", _status); Text("WardrobeLocalStatus", _status);
        Visible("WardrobeStatus", !_localOpen && _status != Raw("ready") && !string.IsNullOrWhiteSpace(_status));
        Text("WardrobeClassic", Format("Choice", ("marker", !_slim ? Raw("choice-marker") : ""), ("label", _shell.Renderer.LocalizeText(Raw("copy.fc09417885b9")))), literal: true); Text("WardrobeSlim", Format("Choice", ("marker", _slim ? Raw("choice-marker") : ""), ("label", _shell.Renderer.LocalizeText(Raw("copy.252d2d8a440b")))), literal: true);
        _presentation["WardrobeCurrentView_label"] = ViewCaption(_currentView);
        Text("WardrobeSkinCount", Format("Count", ("count", _skinCards.Count)), literal: true); Text("WardrobeCapeCount", Format("Count", ("count", _capeCards.Count)), literal: true);
        Text("WardrobeSkinsEmpty", _snapshot is not null
            ? Raw("copy.5621b9ced9f2")
            : _observedIdentity is null ? Raw("copy.df5f107f6bca")
            : !_loaded ? Raw("copy.aeaccff1924a") : Raw("copy.eb4a3e791478"));
        bool capeEmpty = _capeCards.Count == 0;
        Text("WardrobeCapeTitle", profile.Kind switch { LaunchProfileKind.Microsoft => Raw("copy.aa1c5c5541be"), LaunchProfileKind.LittleSkin => Raw("copy.e18491f56efd"), LaunchProfileKind.ThirdParty => Raw("copy.c293fca99b4b"), _ => Raw("copy.ff6519c3a91a") });
        Text("WardrobeCapeSubtitle", profile.Kind switch
        {
            LaunchProfileKind.Microsoft => Raw("copy.d2bbc702a031"),
            LaunchProfileKind.LittleSkin => Raw("copy.c11c5962da20"),
            LaunchProfileKind.ThirdParty => Raw("copy.11d9c550308f"),
            _ => Raw("copy.a95caa65232f")
        });
        var capeState = _snapshot?.CapeState ?? (!_loaded ? AccountWardrobeCapeState.Loading : AccountWardrobeCapeState.LoadFailed);
        Text("WardrobeCapeStatus", capeState switch
        {
            AccountWardrobeCapeState.Loading => Raw("copy.7629fe4d751d"),
            AccountWardrobeCapeState.LoadFailed => Raw("copy.c4483d5f9b3c"),
            AccountWardrobeCapeState.Unsupported => _snapshot?.UnavailableReason ?? Raw("copy.1719a7a417c1"),
            _ => profile.Kind switch
            {
                LaunchProfileKind.Microsoft => Raw("copy.277a40b70823"),
                LaunchProfileKind.LittleSkin => Raw("copy.fb8dbf7bd06e"),
                LaunchProfileKind.ThirdParty => Raw("copy.9b76bdbfc3d8"),
                _ => Raw("copy.4678e5d9760d")
            }
        });
        Visible("WardrobeCapeStatus", capeEmpty); Visible("WardrobeCapes", !capeEmpty);
        Visible("WardrobeSkinsEmpty", _skinCards.Count == 0); Visible("WardrobeSkins", _skinCards.Count > 0);
        Visible("WardrobeSkinPager", _skinCards.Count > 0); Visible("WardrobeLocalLayer", _localOpen);
        bool skin = !Busy && _snapshot?.CanUploadSkin == true, cape = !Busy && _snapshot?.CanChooseCape == true;
        foreach (string name in new[] { "WardrobeSkinPath", "WardrobeClassic", "WardrobeSlim", "WardrobePreview" }) Enable(name, skin);
        bool canManage = SafeManageUri(_snapshot?.ManageUri) is not null && _openUrl is not null;
        Enable("WardrobeBrowse", !Busy && _snapshot is not null && (skin && _pickFile is not null || canManage));
        Enable("WardrobeChooseFile", skin && _pickFile is not null); Enable("WardrobeUpload", skin && _preview is not null);
        Enable("WardrobeLibrary", !Busy && _snapshot is not null && _openLibrary is not null);
        Visible("WardrobeManage", canManage); Enable("WardrobeManage", !Busy && canManage);
        Enable("WardrobeCapeClear", cape); Visible("WardrobeCapeClear", _snapshot?.CanChooseCape == true);
        Enable("WardrobeRefresh", !Busy); Enable("WardrobeCancel", Busy); Visible("WardrobeCancel", Busy && !_localOpen);
        Enable("WardrobeLocalClose", true); Enable("WardrobeCurrentView", true);
        UpdateTrackButtons("WardrobeSkin", _skinCards.Count, Scroll("WardrobeSkins").OffsetX);
        UpdateTrackButtons("WardrobeCape", _capeCards.Count, Scroll("WardrobeCapes").OffsetX);
        var current = _snapshot?.Current ?? new AccountWardrobeResolvedTextures(profile.SkinAddress, null,
            LaunchProfilePresentation.Avatar(profile.Uuid ?? "") == "pcl/avatar/alex", CurrentSkin(profile), null);
        if (_presentedCurrent != current || _presentedCurrentView != _currentView)
        {
            _shell.Tree.GetComponent<XsrUiImage>(_nodes["WardrobePlayerPreview"])!.Raster = WardrobePlayerPresentation.Player(current.Skin, current.Cape, current.IsSlim, _currentView);
            _presentedCurrent = current; _presentedCurrentView = _currentView;
        }
        var head = _shell.Tree.GetComponent<XsrUiImage>(_nodes["WardrobeHeadPreview"])!;
        _presentation["WardrobeHeadPreview_source"] = LaunchProfilePresentation.Avatar(profile.Uuid ?? "");
        head.Raster = _preview is { } selected ? LaunchProfilePresentation.Head(selected.Image) : null;
        _shell.Tree.GetComponent<XsrUiImage>(_nodes["WardrobeTexturePreview"])!.Raster = _preview is { } draft
            ? new XsrUiRasterImage(draft.Image, []) { FitToBounds = true } : null;
        foreach (var card in _skinCards.Concat(_capeCards))
        {
            var textures = card.Loaded ?? card.Card.Appearance;
            if (card.Presented != textures || card.PresentedView != card.View)
            {
                _shell.Tree.GetComponent<XsrUiImage>(card.Image)!.Raster = WardrobePlayerPresentation.Player(textures.Skin, textures.Cape, textures.IsSlim, card.View);
                card.Presented = textures; card.PresentedView = card.View;
            }
            ProjectCard(card);
        }
        ReloadWorkspace();
        _shell.Tree.MarkDirty(Page, XsrUiDirtyKinds.Paint);
        _skinOffset = Scroll("WardrobeSkins").OffsetX; _capeOffset = Scroll("WardrobeCapes").OffsetX;
    }
    private Nexa.Core.Media.PngImage? CurrentSkin(LaunchProfileView profile) => _store.ReadCollection<AccountSkinSnapshot>(_store.Resolve(AccountSkinContract.SkinsKey)).Items
        .FirstOrDefault(s => s.ProfileKey == AccountSkinContract.ProfileKey(profile))?.Image;
    private (string Content, bool Literal) ProfileCaption(LaunchProfileView profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Username)) return (Raw("copy.e6d4f4247246"), false);
        if (profile.Kind == LaunchProfileKind.ThirdParty)
        {
            string server = profile.AuthServer.Trim();
            if (!server.Contains("://", StringComparison.Ordinal)) server = "https://" + server;
            if (Uri.TryCreate(server, UriKind.Absolute, out var address) && !string.IsNullOrEmpty(address.Host))
                return (Format("ThirdParty", ("host", address.Host)), true);
        }
        return !string.IsNullOrWhiteSpace(profile.Info)
            ? (profile.Info, true) : (LaunchProfilePresentation.Description(profile), false);
    }
    private static string ViewLabel(WardrobePlayerView view) => view switch
    { WardrobePlayerView.Isometric => Raw("copy.bc295ff5696c"), WardrobePlayerView.Front => Raw("copy.740f5e8102e0"), WardrobePlayerView.Back => Raw("copy.2d270b6ce44a"), WardrobePlayerView.Left => Raw("copy.6a24b14f33c7"), WardrobePlayerView.Right => Raw("copy.1cf1d4d0b24b"), WardrobePlayerView.Top => Raw("copy.6f2a4a02e60e"), _ => Raw("copy.3f049887991b") };
    private string ViewCaption(WardrobePlayerView view) => Format("View", ("view", _shell.Renderer.LocalizeText(ViewLabel(view))));
    private void UpdateTrackButtons(string prefix, int count, double offset)
    {
        Enable(prefix + "Previous", !Busy && offset > 0);
        Enable(prefix + "Next", !Busy && offset + _trackWidth + .5 < count * (_cardWidth + TrackSpacing) - TrackSpacing);
    }
    private void BuildCards()
    {
        ClearCards();
        foreach (var card in _snapshot?.Skins ?? []) AddCard(card, _nodes["WardrobeSkins"], _skinCards);
        var capes = _snapshot?.CapeCards ?? [];
        if (capes.Count == 0 && _snapshot is { } legacy && legacy.Capes.Count > 0)
            capes = legacy.Capes.Select(cape => new AccountWardrobeCard(cape.Id, cape.Name,
                cape.IsActive ? Raw("copy.5ddff4afab27") : legacy.Profile.Kind == LaunchProfileKind.Microsoft ? Raw("copy.e4df853a4197") : Raw("copy.54b19c79b4f1"),
                (legacy.Current ?? new(null, null, false, null, null)) with { CapeAddress = cape.TextureAddress, Cape = null },
                legacy.CanChooseCape && !cape.IsActive, CapeId: cape.Id)
            { Kind = AccountWardrobeTextureKind.Cape, IsActive = cape.IsActive }).ToArray();
        foreach (var card in capes) AddCard(card, _nodes["WardrobeCapes"], _capeCards);
        SetCardMetrics();
    }
    private void AddCard(AccountWardrobeCard card, XsrUiEntityId parent, List<CardVisual> cards)
    {
        var data = CardProjection(card, card.Kind == AccountWardrobeTextureKind.Cape ? WardrobePlayerView.Back : WardrobePlayerView.Front);
        var container = _dynamic.Append(parent, DesktopMarkup.Dynamic("WardrobeCard.d.nxml", data));
        var nodes = new Dictionary<string, XsrUiEntityId>();
        _shell.Tree.Walk(container, node => { nodes[_shell.Tree.Name(node)] = node; return true; });
        XsrUiEntityId apply = default;
        if (card.CanApply && !card.IsActive)
            apply = _dynamic.Append(nodes[data["apply_slot"]], DesktopMarkup.Dynamic("WardrobeCardApply.d.nxml", ApplyProjection(card)));
        var visual = new CardVisual(card, container, nodes[data["body"]], nodes[data["image"]], nodes[data["details"]], nodes[data["view"]], apply) { Projection = data };
        cards.Add(visual); _viewSources[visual.ViewButton] = visual; if (apply.IsAssigned) _applySources[apply] = visual;
    }
    private Dictionary<string, string> ApplyProjection(AccountWardrobeCard card)
    {
        bool cape = card.Kind == AccountWardrobeTextureKind.Cape;
        return new()
        {
            ["key"] = (cape ? "WardrobeCape." : "WardrobeSkinApply.") + SafeSuffix(card.Id),
            ["caption"] = _shell.Renderer.LocalizeText(Raw(cape ? "use-cape" : "use-skin")),
            ["command"] = cape ? "ui.wardrobe.cape.choose" : "ui.wardrobe.skin.choose",
            ["enabled"] = Flag(!Busy && !_localOpen && _snapshot is not null)
        };
    }
    private Dictionary<string, string> CardProjection(AccountWardrobeCard card, WardrobePlayerView view) => ProjectCard(_shell, card, view, _cardWidth, _cardHeight);
    internal static Dictionary<string, string> ProjectCard(XsrUiShell shell, AccountWardrobeCard card, WardrobePlayerView view, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw new ArgumentException("Invalid wardrobe card viewport.");
        string ViewCaption(WardrobePlayerView selected) => DesktopWardrobeText.Format("View", new Dictionary<string, string> { ["view"] = shell.Renderer.LocalizeText(ViewLabel(selected)) }, shell.Renderer.LocalizeText);
        string prefix = card.Kind == AccountWardrobeTextureKind.Cape ? "WardrobeCape" : "WardrobeSkin", suffix = SafeSuffix(card.Id);
        var axis = DesktopWardrobeText.Metric("card-axis"); bool horizontal = height < Required(axis.Height);
        var image = DesktopWardrobeText.Metric("horizontal-image"); var vertical = DesktopWardrobeText.Metric("vertical-image"); var labels = DesktopWardrobeText.Metric("card-labels");
        var aspect = DesktopWardrobeText.Metric("image-aspect"); double imageHeight = horizontal ? Math.Clamp(height - image.Margin.Top, Required(image.MinHeight), Required(image.MaxHeight))
            : Math.Max(Required(vertical.MinHeight), height - (card.CanApply && !card.IsActive ? vertical.Margin.Top : vertical.Margin.Bottom));
        double imageWidth = horizontal ? imageHeight * Required(aspect.Width) / Required(aspect.Height) : width - DesktopWardrobeText.Metric("card-body").Margin.Horizontal;
        return new()
        {
            ["container"] = prefix + "Card." + suffix,
            ["body"] = prefix + "Body." + suffix,
            ["image"] = prefix + "Preview." + suffix,
            ["details"] = prefix + "Details." + suffix,
            ["title_key"] = prefix + "Title." + suffix,
            ["source_key"] = prefix + "Source." + suffix,
            ["apply_slot"] = prefix + "ApplySlot." + suffix,
            ["view_layer"] = prefix + "ViewLayer." + suffix,
            ["view"] = prefix + "View." + suffix,
            ["width"] = Number(width),
            ["height"] = Number(height),
            ["orientation"] = horizontal ? "Horizontal" : "Vertical",
            ["image_width"] = Number(imageWidth),
            ["image_height"] = Number(imageHeight),
            ["image_weight"] = horizontal ? "0" : "1",
            ["image_alignment"] = horizontal ? "Center" : "Stretch",
            ["details_weight"] = horizontal ? "1" : "0",
            ["title_height"] = Number(Required(horizontal ? labels.MinHeight : labels.Height)),
            ["source_height"] = Number(Required(horizontal ? labels.MinWidth : labels.Width)),
            ["title"] = card.Title,
            ["source"] = shell.Renderer.LocalizeText(card.Source),
            ["view_label"] = ViewCaption(view),
            ["title_variant"] = Raw(horizontal ? "card-title-start" : "card-title-center"),
            ["source_variant"] = Raw(horizontal ? "card-source-start" : "card-source-center")
        };
    }
    private void ProjectCard(CardVisual card)
    {
        var data = CardProjection(card.Card, card.View);
        if (!Same(data, card.Projection)) { _dynamic.Reload(card.Container, DesktopMarkup.Dynamic("WardrobeCard.d.nxml", data)); card.Projection = data; }
        if (card.Apply.IsAssigned) _dynamic.Reload(card.Apply, DesktopMarkup.Dynamic("WardrobeCardApply.d.nxml", ApplyProjection(card.Card)));
    }
    private void ApplyResponsiveLayout()
    {
        if (_viewport == _shell.Renderer.Viewport) return;
        _viewport = _shell.Renderer.Viewport; var view = DesktopWardrobeText.Metric("view"); var railMetric = DesktopWardrobeText.Metric("rail");
        bool compact = _viewport.Height <= Required(view.Height);
        double rail = _viewport.Width >= Required(view.MaxWidth) ? Required(railMetric.MaxWidth) : _viewport.Width <= Required(view.MinWidth) ? Required(railMetric.MinWidth) : Required(railMetric.Width);
        var railPadding = (compact ? DesktopWardrobeText.Metric("rail-compact") : railMetric).Padding;
        var content = DesktopWardrobeText.Metric("content"); var contentHeight = compact ? DesktopWardrobeText.Metric("content-compact") : content;
        var edgeMetric = _viewport.Width >= Required(content.MaxWidth) ? DesktopWardrobeText.Metric("content-large") : _viewport.Width <= Required(content.MinWidth) ? DesktopWardrobeText.Metric("content-narrow") : content;
        double edge = edgeMetric.Padding.Left;
        _presentation["WardrobeRailSurface_width"] = Number(rail); _presentation["WardrobeProfileRail_width"] = Number(rail - railPadding.Horizontal);
        _presentation["WardrobeProfileRail_padding"] = Thickness(railPadding);
        _presentation["WardrobeContent_padding"] = Thickness(new(edge, contentHeight.Padding.Top, edge, contentHeight.Padding.Bottom));
        _presentation["WardrobeContent_spacing"] = Number(contentHeight.Spacing);
        _presentation["WardrobeCapeHeader_margin"] = Thickness(new(0, contentHeight.Spacing, 0, 0));
        foreach (var pair in new[] { ("WardrobeMainHeader", "header"), ("WardrobeStatus", "status"), ("WardrobeSkinSubtitle", "skin-subtitle"), ("WardrobeCapeSubtitle", "cape-subtitle") })
        {
            var metric = DesktopWardrobeText.Metric(pair.Item2); _presentation[pair.Item1 + "_height"] = Number(Required(compact ? metric.MinHeight : metric.Height));
        }
        var track = DesktopWardrobeText.Metric("track"); _trackWidth = Math.Max(Required(track.MinWidth), _viewport.Width - rail - edge * 2 - track.Margin.Left);
        var estimate = DesktopWardrobeText.Metric("estimate"); _cardHeight = Math.Clamp((_viewport.Height - Required(compact ? estimate.MinHeight : estimate.Height)) / 2 - track.Margin.Top, Required(track.MinHeight), Required(track.MaxHeight));
        _cardWidth = CardWidth(_cardHeight);
        var local = DesktopWardrobeText.Metric("local"); var padding = _shell.Tree.GetComponent<XsrUiElement>(_nodes["WardrobeLocalPanel"])!.Padding;
        _presentation["WardrobeLocalPanel_width"] = Number(Math.Min(Required(local.Width), Math.Max(Required(local.MinWidth), _viewport.Width - local.Margin.Horizontal)) - padding.Horizontal);
        _presentation["WardrobeLocalPanel_maxheight"] = Number(Math.Min(Required(local.Height), Math.Max(Required(local.MinHeight), _viewport.Height - local.Margin.Vertical)) - padding.Vertical);
        _dirty = true; SetCardMetrics();
    }
    private static double CardWidth(double height)
    {
        var axis = DesktopWardrobeText.Metric("card-axis"); var aspect = DesktopWardrobeText.Metric("image-aspect");
        if (height < Required(axis.Height))
        {
            var image = DesktopWardrobeText.Metric("horizontal-image"); var card = DesktopWardrobeText.Metric("horizontal-card");
            return Math.Clamp(Math.Clamp(height - image.Margin.Top, Required(image.MinHeight), Required(image.MaxHeight)) * Required(aspect.Width) / Required(aspect.Height) + card.Padding.Left, Required(card.MinWidth), Required(card.MaxWidth));
        }
        var vertical = DesktopWardrobeText.Metric("vertical-card");
        return Math.Clamp((height - vertical.Padding.Top) * Required(aspect.Width) / Required(aspect.Height) + vertical.Padding.Left, Required(vertical.MinWidth), Required(vertical.MaxWidth));
    }
    private void SetCardMetrics() { foreach (var card in _skinCards.Concat(_capeCards)) ProjectCard(card); }
    private double TrackSpacing => _shell.Tree.GetComponent<XsrUiStackPanel>(_nodes["WardrobeSkins"])!.Spacing;
    private static double Required(double? value) => value is > 0 && double.IsFinite(value.Value) ? value.Value : throw new NxmlCompileException("Incomplete wardrobe viewport metric.");
    private static string SafeSuffix(string value) => value.Length <= 90 && !value.Any(char.IsWhiteSpace) && !value.Contains('{') && !value.Contains('}') ? value : "h" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Flag(bool value) => value ? "true" : "false";
    private static string Thickness(XsrUiThickness value) => string.Join(",", Number(value.Left), Number(value.Top), Number(value.Right), Number(value.Bottom));
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private string Format(string name, params (string Key, object Value)[] values) => DesktopWardrobeText.Format(name, values.ToDictionary(pair => pair.Key, pair => Convert.ToString(pair.Value, CultureInfo.CurrentCulture) ?? ""), _shell.Renderer.LocalizeText);
    private static string Raw(string key) => DesktopWardrobeText.Raw(key);
    private void ReloadWorkspace() { if (Same(_presentation, _applied)) return; _dynamic.Reload(_workspace, DesktopMarkup.Dynamic("WardrobeWorkspace.d.nxml", _presentation)); _applied = new(_presentation); }
    private XsrUiScroll Scroll(string node) => _shell.Tree.GetComponent<XsrUiScroll>(_nodes[node])!;
    private void ClearCards()
    {
        foreach (string key in new[] { "WardrobeSkins", "WardrobeCapes" })
        {
            _dynamic.Clear(_nodes[key]);
            Scroll(key).OffsetX = 0;
        }
        _skinCards.Clear(); _capeCards.Clear(); _applySources.Clear(); _viewSources.Clear(); _textures.Clear(); _textureAttempts.Clear();
    }
    private void Retire()
    {
        _dirty = true; _stop.Cancel(); _stop.Dispose(); _stop = new();
        _picking = null; _reading = null; _readingProfile = null; _previewing = null; _writing = null; _writingProfile = null; _snapshot = null; _preview = null;
        _loaded = false; _slim = false; _localOpen = false; _operationNotice = null; _presentedCurrent = null; _currentView = WardrobePlayerView.Front;
        while (_pending.TryDequeue(out _)) { }
        _shell.Renderer.SetTextInputValue(_nodes["WardrobeSkinPath"], "");
        foreach (string name in new[] { "WardrobeHeadPreview", "WardrobeTexturePreview", "WardrobePlayerPreview" })
            _shell.Tree.GetComponent<XsrUiImage>(_nodes[name])!.Raster = null;
        ClearCards();
    }
    private void Text(string name, string content, bool literal = false) => _presentation[name + "_text"] = literal ? content : _shell.Renderer.LocalizeText(content);
    private void Enable(string name, bool enabled) => _presentation[name + "_enabled"] = Flag(enabled);
    private void Visible(string name, bool visible) => _presentation[name + "_visible"] = Flag(visible);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _intents.IntentEmitted -= OnIntent; _shell.Renderer.FramePreparing -= OnFrame; _store.Changed -= OnStateChanged;
        Retire(); _stop.Dispose(); _dynamic.Remove(_workspace);
    }
}
