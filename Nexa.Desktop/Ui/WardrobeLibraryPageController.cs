using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Services.Accounts;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Public catalogue presentation. Accounts owns catalogue, texture and provider I/O.</summary>
internal sealed class WardrobeLibraryPageController : IDisposable
{
    private static readonly XsrSemanticId CardAction = XsrSemanticId.Parse("ui.wardrobe.library.action");
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _workspace;
    private readonly Dictionary<string,string> _presentation=DesktopWardrobeText.Defaults(library:true);
    private Dictionary<string,string> _applied=[];
    private readonly List<XsrUiEntityId> _gridRows=[];
    private readonly Dictionary<string,XsrUiEntityId> _siteRows=[];
    private string _copyStamp="";
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrQueryRouter _queries;
    private readonly XsrCommandRouter _commands;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly Dictionary<string, XsrUiEntityId> _nodes = [];
    private readonly Dictionary<XsrUiEntityId, Action> _actions = [];
    private readonly ConcurrentQueue<DesktopUiIntent> _pending = new();
    private readonly List<CardVisual> _cards = [];
    private readonly List<ImageRead> _images = [];
    private CancellationTokenSource _lifetime = new(), _catalogStop = new(), _imageStop = new();
    private Task<XsrResult<IReadOnlyList<WardrobeCatalogSite>>>? _sitesReading;
    private Task<XsrResult<WardrobeCatalogPage>>? _catalogReading;
    private Task<XsrResult<AccountWardrobeSnapshot>>? _accountReading;
    private Task<XsrResult>? _writing;
    private WardrobeCatalogSite[] _sites = [];
    private WardrobeCatalogSite? _site;
    private WardrobeCatalogPage? _catalog;
    private AccountWardrobeSnapshot? _account;
    private AccountWardrobeIdentity? _lastAdmittedIdentity;
    private LaunchProfileView _accountReadProfile;
    private long _accountReadSelectionEvents, _accountReadRosterRevision;
    private WardrobeCatalogQuery _filter = new();
    private Action<Uri>? _openUrl;
    private Action? _back;
    private bool _visible, _started, _accountLoaded, _accountFailed, _disposed, _effecting;
    private volatile bool _dirty = true;
    private long _wake, _selectionEvents, _rosterEvents, _seenSelectionEvents, _seenRosterEvents;
    private string? _observedIdentity, _notice;
    private string _siteStatus = Raw("copy.9d0d37af1ea8"), _emptyTitle = Raw("copy.31e1252e5216"), _emptyMessage = Raw("copy.7b00c3d7e6bf");
    private int _columns = 1;
    private double _railWidth, _scrollOffset = double.NaN;
    private XsrUiSize _viewport;
    private bool _siteStatusLiteral;
    internal const int MaximumVisibleCards = 64;
    private static double CardHeight=>Required(DesktopWardrobeText.Metric("library-grid").Height);
    private static double RowSpacing=>DesktopWardrobeText.Metric("library-grid").Spacing;
    private sealed class CardVisual(WardrobeCatalogItem item, XsrUiEntityId container, XsrUiEntityId preview)
    {
        internal WardrobeCatalogItem Item { get; } = item;
        internal XsrUiEntityId Container { get; } = container;
        internal XsrUiEntityId Preview { get; } = preview;
        internal XsrUiEntityId ViewButton { get; set; }
        internal WardrobePlayerView View { get; set; } = item.Kind == WardrobeCatalogKind.Cape ? WardrobePlayerView.Back : WardrobePlayerView.Isometric;
        internal AccountWardrobeResolvedTextures? Loaded { get; set; }
        internal bool Attempted { get; set; }
        internal Dictionary<string,string> Projection=[];
    }
    private sealed record ImageRead(CardVisual Card, CancellationTokenSource Stop, Task<XsrResult<AccountWardrobeResolvedTextures>> Read);

    internal WardrobeLibraryPageController(XsrUiShell shell, DesktopUiIntentSink intents,
        XsrQueryRouter queries, XsrCommandRouter commands, XsrStateStore store, DesktopFeedbackService feedback)
    {
        _shell = shell; _intents = intents; _queries = queries; _commands = commands; _store = store; _feedback = feedback;
        _dynamic=new(shell.Tree,store);
        Page=NxmlUiLoader.LoadDetached(DesktopMarkup.Load("WardrobeLibraryPage.nxml"),shell.Tree,store);
        _workspace=_dynamic.Append(shell.Tree.Children(Page).Single(),DesktopMarkup.Dynamic("WardrobeLibraryWorkspace.d.nxml",_presentation));
        shell.Tree.Walk(_workspace,entity=>{string name=shell.Tree.Name(entity);if(name.Length>0)_nodes[name]=entity;return true;});
        _intents.IntentEmitted += OnIntent;
        shell.Renderer.FramePreparing += OnFrame;
        _store.Changed += OnStateChanged;
    }

    internal XsrUiEntityId Page { get; }
    internal void ConfigureOpenUrl(Action<Uri> openUrl) => _openUrl = openUrl;
    internal void ConfigureBack(Action back) => _back = back;
    private bool IsCurrent => !_disposed && _shell.Tree.IsAlive(Page) && _shell.Stage.Navigation.Current == Page;

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (IsCurrent && !_effecting && args.Intent.Command.Value.StartsWith("ui.wardrobe.library.", StringComparison.Ordinal))
            _pending.Enqueue(args.Intent);
    }
    private void OnStateChanged(XsrStateChange change)
    {
        if (change.SemanticId == AccountStateContract.SelectedKey) Interlocked.Increment(ref _selectionEvents);
        if (change.SemanticId == AccountStateContract.ProfilesKey) Interlocked.Increment(ref _rosterEvents);
    }
    private string? CurrentIdentity()
    {
        return IdentityText(CurrentProfile());
    }
    private LaunchProfileView CurrentProfile()
    {
        int index = _store.Read<int>(_store.Resolve(AccountStateContract.SelectedKey)).Value;
        return _store.ReadCollection<LaunchProfileView>(_store.Resolve(AccountStateContract.ProfilesKey)).Items.FirstOrDefault(p => p.Index == index);
    }
    private static string? IdentityText(LaunchProfileView profile) => string.IsNullOrWhiteSpace(profile.Username) ? null
        : $"{profile.Index}\n{profile.Kind}\n{profile.Uuid}\n{profile.AuthServer}\n{profile.Username}";

    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed) return;
        string copy=_shell.Renderer.LocalizeText(Raw("read-account"));if(copy!=_copyStamp){_copyStamp=copy;_dirty=true;}
        if (!IsCurrent) { if (_visible) Retire(); _visible = false; return; }
        long selections = Volatile.Read(ref _selectionEvents), rosters = Volatile.Read(ref _rosterEvents);
        double offset = _shell.Tree.GetComponent<XsrUiScroll>(_nodes["WardrobeLibraryBody"])!.OffsetY;
        if (_visible && (_accountLoaded || _accountReading is not null) && !_dirty && _pending.IsEmpty && _viewport == _shell.Renderer.Viewport && _scrollOffset == offset
            && selections == _seenSelectionEvents && rosters == _seenRosterEvents
            && _sitesReading is not { IsCompleted: true } && _catalogReading is not { IsCompleted: true }
            && _accountReading is not { IsCompleted: true } && _writing is not { IsCompleted: true }
            && !_images.Any(image => image.Read.IsCompleted)
            && (_filter.Keyword.Length == 0 || !string.IsNullOrWhiteSpace(SearchDraft()))) return;
        LaunchProfileView? pendingPrincipal = _accountReading is not null ? _accountReadProfile : _writing is not null ? _account?.Profile : null;
        bool principalChanged = rosters != _seenRosterEvents && pendingPrincipal is { } original
            && !SamePrincipal(CurrentProfile(), original);
        bool contextChanged = !_visible || selections != _seenSelectionEvents || principalChanged
            || rosters != _seenRosterEvents && _accountReading is null && _writing is null;
        if (contextChanged)
        {
            Retire(); _observedIdentity = CurrentIdentity(); _visible = true;
            _seenSelectionEvents = selections; _seenRosterEvents = rosters;
        }
        UpdateResponsiveLayout();
        if (!_started) { _started = true; BeginSites(); }
        if (!_accountLoaded && _accountReading is null) BeginAccountRead();
        ConsumeReplies();
        while (_pending.TryDequeue(out var intent))
        {
            if (!IsCurrent) break;
            Handle(intent);
            if (!IsCurrent) return;
        }
        // Clearing a committed search restores the unfiltered catalogue without another click.
        if (_filter.Keyword.Length > 0 && string.IsNullOrWhiteSpace(SearchDraft()) && _writing is null)
            Search();
        ConsumeImages();
        Present();
        _scrollOffset = _shell.Tree.GetComponent<XsrUiScroll>(_nodes["WardrobeLibraryBody"])!.OffsetY;
    }

    private void BeginSites()
    {
        _dirty = true;
        if (!_queries.TryResolve(WardrobeCatalogContract.Sites, out var route))
        { SetEmpty(Raw("copy.c34ebcb0dd6f"), Raw("copy.9c6126383f6d")); return; }
        _sitesReading = _queries.QueryAsync<WardrobeCatalogSitesQuery, IReadOnlyList<WardrobeCatalogSite>>(route, new(), cancellationToken: _lifetime.Token).AsTask();
        Wake(_sitesReading, _lifetime.Token);
    }
    private void BeginAccountRead()
    {
        _dirty = true;
        if (_observedIdentity is null || !_queries.TryResolve(AccountWardrobeContract.Read, out var route)) { _accountLoaded = true; return; }
        _accountReadSelectionEvents = Volatile.Read(ref _selectionEvents);
        _accountReadProfile = CurrentProfile();
        _accountReadRosterRevision = _store.ReadCollection<LaunchProfileView>(_store.Resolve(AccountStateContract.ProfilesKey)).Revision;
        _accountReading = _queries.QueryAsync<AccountWardrobeQuery, AccountWardrobeSnapshot>(route, new(), cancellationToken: _lifetime.Token).AsTask();
        Wake(_accountReading, _lifetime.Token);
    }
    private void Reload(bool force = false)
    {
        _catalogStop.Cancel(); _catalogStop.Dispose(); _catalogStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _catalogReading = null; _catalog = null; ClearCards(); _dirty = true;
        if (_site is null) { SetEmpty(Raw("copy.c34ebcb0dd6f"), Raw("copy.9c6126383f6d")); return; }
        _filter = _filter with { SiteId = _site.Id, ForceRefresh = force };
        _siteStatus = Raw("copy.9d0d37af1ea8");
        _siteStatusLiteral = false;
        if (!_queries.TryResolve(WardrobeCatalogContract.Read, out var route))
        { SetEmpty(Raw("copy.7ae745517690"), Raw("copy.5028fc56dfe0")); return; }
        _catalogReading = _queries.QueryAsync<WardrobeCatalogQuery, WardrobeCatalogPage>(route, _filter, cancellationToken: _catalogStop.Token).AsTask();
        Wake(_catalogReading, _catalogStop.Token);
    }
    private void ConsumeReplies()
    {
        if (_sitesReading is { IsCompleted: true } sites)
        {
            _sitesReading = null; _dirty = true;
            if (Succeeded(sites))
            {
                _sites = sites.Result.Value.Take(16).ToArray();
                _site = _sites.FirstOrDefault(s => s.Id == _filter.SiteId) ?? (_sites.Length > 0 ? _sites[0] : null);
                if (_site?.SupportsCapes != true) _filter = _filter with { Kind = WardrobeCatalogKind.Skin };
                BuildSites(); Reload();
            }
            else SetEmpty(Raw("copy.c34ebcb0dd6f"), Raw("copy.f7d7864c571f"));
        }
        if (_accountReading is { IsCompleted: true } account)
        {
            _accountReading = null; _accountLoaded = true; _dirty = true;
            _accountFailed = !Succeeded(account);
            if (Succeeded(account))
            {
                var reply = account.Result.Value;
                if (AdmitRead(reply)) _account = reply;
                else { _account = null; _accountFailed = true; }
            }
            // A read can refresh credentials even if the next provider operation fails.
            _seenRosterEvents = Volatile.Read(ref _rosterEvents);
        }
        if (_catalogReading is { IsCompleted: true } catalog)
        {
            _catalogReading = null; _dirty = true;
            if (Succeeded(catalog) && _site is { } site && catalog.Result.Value.SiteId == site.Id && catalog.Result.Value.Page == _filter.Page
                && catalog.Result.Value.Items.Count <= MaximumVisibleCards)
            {
                _catalog = catalog.Result.Value;
                _siteStatus=CatalogueCaption(_catalog);
                _siteStatusLiteral = true;
                _emptyTitle = Raw("copy.31e1252e5216"); _emptyMessage = Raw("copy.7b00c3d7e6bf");
                BuildCards();
            }
            else SetEmpty(Raw("copy.7ae745517690"), Raw("copy.a3b8c4ddae92"));
        }
        if (_writing is { IsCompleted: true } writing)
        {
            _writing = null; _dirty = true;
            _notice = writing.IsCompletedSuccessfully && writing.Result.IsSuccess ? Raw("copy.9fa5be79e728")
                : writing.IsCompletedSuccessfully ? writing.Result.Error?.Message ?? Raw("copy.0a69a3749a9e") : Raw("copy.d3ff07c74807");
            if (writing.IsCompletedSuccessfully && writing.Result.IsSuccess) _feedback.Info(_notice);
            // Always refresh admission before a retry, including provider failure after token rotation.
            _account = null; _accountLoaded = false; _seenRosterEvents = Volatile.Read(ref _rosterEvents);
            ClearImages();
            BeginAccountRead();
        }
    }

    private bool MatchesCurrentAccount(AccountWardrobeSnapshot account)
    {
        var profile = CurrentProfile();
        return profile == account.Profile && profile.Index == account.Identity.Index && profile.Kind == account.Identity.Kind && profile.Uuid == account.Identity.Uuid
            && IdentityText(profile) == _observedIdentity;
    }
    private bool AdmitRead(AccountWardrobeSnapshot account)
    {
        var roster = _store.ReadCollection<LaunchProfileView>(_store.Resolve(AccountStateContract.ProfilesKey));
        var profile = CurrentProfile();
        if (_accountReadSelectionEvents != Volatile.Read(ref _selectionEvents) || !SamePrincipal(profile, _accountReadProfile)
            || profile != account.Profile || profile.Index != account.Identity.Index
            || profile.Kind != account.Identity.Kind || profile.Uuid != account.Identity.Uuid) return false;
        if (_lastAdmittedIdentity is { } previous && (account.Identity.SelectionGeneration != previous.SelectionGeneration
            || account.Identity.RosterGeneration < previous.RosterGeneration)) return false;
        if (profile.Username != _accountReadProfile.Username
            && (roster.Revision <= _accountReadRosterRevision || (profile with { Username = _accountReadProfile.Username }) != _accountReadProfile
                || _lastAdmittedIdentity is { } earlier && account.Identity.RosterGeneration <= earlier.RosterGeneration)) return false;
        // A provider refresh can publish a new username for this admitted principal. The
        // returned view must equal the roster, and selection changes (including ABA) retire it.
        if (_accountReadSelectionEvents != Volatile.Read(ref _selectionEvents) || CurrentProfile() != profile
            || _store.ReadCollection<LaunchProfileView>(_store.Resolve(AccountStateContract.ProfilesKey)).Revision != roster.Revision) return false;
        _observedIdentity = IdentityText(profile); _lastAdmittedIdentity = account.Identity;
        return true;
    }
    private static bool SamePrincipal(LaunchProfileView current, LaunchProfileView captured) => current.Index == captured.Index
        && current.Kind == captured.Kind && current.Uuid == captured.Uuid && current.AuthServer == captured.AuthServer;
    private void Handle(DesktopUiIntent intent)
    {
        if (!Authorized(intent.Source, intent.Command)) return;
        string command = intent.Command.Value;
        if (intent.Command == CardAction)
        {
            if (_actions.TryGetValue(intent.Source, out var action)) action();
            return;
        }
        string? key = command switch
        {
            "ui.wardrobe.library.back" => "WardrobeLibraryBack",
            "ui.wardrobe.library.docs" => "WardrobeLibraryDocs",
            "ui.wardrobe.library.site" => "WardrobeLibraryOpenSite",
            "ui.wardrobe.library.refresh" => "WardrobeLibraryRefresh",
            "ui.wardrobe.library.search" => "WardrobeLibrarySearchButton",
            "ui.wardrobe.library.skin" => "WardrobeLibrarySkin",
            "ui.wardrobe.library.cape" => "WardrobeLibraryCape",
            "ui.wardrobe.library.time" => "WardrobeLibraryTime",
            "ui.wardrobe.library.likes" => "WardrobeLibraryLikes",
            "ui.wardrobe.library.previous" => "WardrobeLibraryPrevious",
            "ui.wardrobe.library.next" => "WardrobeLibraryNext",
            "ui.wardrobe.library.manage" => "WardrobeLibraryManage",
            _ => null
        };
        if (key is null || intent.Source != _nodes[key]) return;
        _dirty = true;
        if (command == "ui.wardrobe.library.back") { _back?.Invoke(); return; }
        if (command == "ui.wardrobe.library.docs") { Open(_site?.DocumentationUri); return; }
        if (command == "ui.wardrobe.library.site") { Open(_site?.BaseUri); return; }
        if (command == "ui.wardrobe.library.manage") { Open(ManageUri()); return; }
        if (_writing is not null) return;
        if (command == "ui.wardrobe.library.refresh")
        {
            _notice = null;
            _account = null; _accountLoaded = false; _accountFailed = false;
            if (_sites.Length == 0) BeginSites(); else Reload(true);
        }
        else if (command == "ui.wardrobe.library.search") Search();
        else if (command is "ui.wardrobe.library.skin" or "ui.wardrobe.library.cape")
        {
            var kind = command.EndsWith("cape", StringComparison.Ordinal) ? WardrobeCatalogKind.Cape : WardrobeCatalogKind.Skin;
            if (kind == WardrobeCatalogKind.Cape && _site?.SupportsCapes != true || kind == _filter.Kind) return;
            _filter = _filter with { Kind = kind, Page = 1 }; Reload();
        }
        else if (command is "ui.wardrobe.library.time" or "ui.wardrobe.library.likes")
        {
            var order = command.EndsWith("likes", StringComparison.Ordinal) ? WardrobeCatalogOrder.Likes : WardrobeCatalogOrder.Time;
            if (order == _filter.Order) return;
            _filter = _filter with { Order = order, Page = 1 }; Reload();
        }
        else if (command == "ui.wardrobe.library.previous" && _catalog?.HasPreviousPage == true)
        { _filter = _filter with { Page = Math.Max(1, _filter.Page - 1) }; Reload(); }
        else if (command == "ui.wardrobe.library.next" && _catalog?.HasNextPage == true)
        { _filter = _filter with { Page = _filter.Page + 1 }; Reload(); }
    }
    private void Search()
    {
        string keyword = SearchDraft().Trim();
        if (keyword == _filter.Keyword) return;
        _filter = _filter with { Keyword = keyword, Page = 1 }; Reload();
    }
    private string SearchDraft() => _shell.Tree.GetComponent<XsrUiTextInput>(_nodes["WardrobeLibrarySearch"])!.ReadDraft();
    private bool Authorized(XsrUiEntityId source, XsrSemanticId command)
    {
        if (!IsCurrent || !_shell.Tree.IsAlive(source) || _shell.Tree.GetComponent<XsrUiInput>(source) is not { Enabled: true, Clickable: true }
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(source)?.Command != command) return false;
        for (var node = source; node.IsAssigned && _shell.Tree.IsAlive(node); node = _shell.Tree.Parent(node))
        {
            if (_shell.Tree.GetComponent<XsrUiElement>(node)?.IsVisible == false) return false;
            if (node == Page) return true;
        }
        return false;
    }
    private void Apply(WardrobeCatalogItem item)
    {
        if (_writing is not null || _account is not { } account || _site is not { } site || !MatchesCurrentAccount(account)
            || !CanApply(item) || _catalog?.Items.Contains(item) != true) return;
        if (!_commands.TryResolve(AccountWardrobeContract.ApplyPublic, out var route)) { _notice = Raw("copy.57a831d60896"); _dirty = true; return; }
        _writing = _commands.Dispatch(route, new AccountWardrobeApplyPublicCommand(account.Identity, site.Id, item.TextureId,
            item.Kind == WardrobeCatalogKind.Cape ? AccountWardrobeTextureKind.Cape : AccountWardrobeTextureKind.Skin), cancellationToken: _lifetime.Token).Completion;
        _notice = Raw("copy.cf41ca6935be"); _dirty = true; Wake(_writing, _lifetime.Token);
    }
    private bool CanApply(WardrobeCatalogItem item) => _account is { } account && _writing is null
        && (item.Kind == WardrobeCatalogKind.Skin ? account.CanUploadSkin
            && account.Identity.Kind is LaunchProfileKind.Microsoft or LaunchProfileKind.LittleSkin or LaunchProfileKind.NCloud
            : account.Identity.Kind == LaunchProfileKind.LittleSkin && account.CanChooseCape);
    private bool ShowApply(WardrobeCatalogItem item) => item.Kind != WardrobeCatalogKind.Cape
        || _account?.Identity.Kind is not (LaunchProfileKind.Microsoft or LaunchProfileKind.NCloud);
    private Uri? ManageUri() => _account?.Identity.Kind == LaunchProfileKind.ThirdParty
        && Uri.TryCreate(_account.ManageUri, UriKind.Absolute, out var uri) && SafeBrowserUri(uri) ? uri : null;
    private static bool SafeBrowserUri(Uri uri) => uri.IsAbsoluteUri && (uri.Scheme is "https" or "http") && uri.UserInfo.Length == 0;
    private void Open(Uri? uri)
    {
        if (uri is null || !SafeBrowserUri(uri) || _openUrl is null || _effecting) return;
        _effecting = true;
        try { _openUrl(uri); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (IsCurrent) { _notice = Raw("copy.5f57d692389c"); _dirty = true; } }
        finally { _effecting = false; }
    }
    private void SetEmpty(string title, string message)
    {
        _emptyTitle = title; _emptyMessage = message; _siteStatus = title == Raw("copy.7ae745517690") ? Raw("copy.46c8ec44cca3") : message; _siteStatusLiteral = false; _dirty = true;
    }

    private void BuildSites()
    {
        var desired=_sites.Select(site=>site.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var pair in _siteRows.ToArray())if(!desired.Contains(pair.Key)){RemoveFragment(pair.Value);_siteRows.Remove(pair.Key);}
        List<XsrUiEntityId> order=[];
        foreach(var site in _sites)
        {
            var data=new Dictionary<string,string>{["key"]="WardrobeLibrarySite."+SafeSuffix(site.Id),["caption"]=Format("Site",("name",site.DisplayName),("host",site.BaseUri.Host)),
                ["selected"]=Flag(_site==site),["variant"]=Raw(_site==site?"site-selected-class":"site-idle-class")};
            if(!_siteRows.TryGetValue(site.Id,out var row)){row=_dynamic.Append(_nodes["WardrobeLibrarySites"],DesktopMarkup.Dynamic("WardrobeLibrarySite.d.nxml",data));_siteRows[site.Id]=row;}
            else _dynamic.Reload(row,DesktopMarkup.Dynamic("WardrobeLibrarySite.d.nxml",data));
            _actions[row]=()=>
            {
                if(_writing is not null||_site==site)return;
                _site=site;_filter=_filter with{SiteId=site.Id,Page=1,Kind=site.SupportsCapes?_filter.Kind:WardrobeCatalogKind.Skin};BuildSites();Reload();
            };
            order.Add(row);
        }
        _dynamic.Reorder(_nodes["WardrobeLibrarySites"],order);
    }
    private void BuildCards()
    {
        ClearCards();if(_catalog is null)return;
        foreach(var item in _catalog.Items)
        {
            int rowIndex=_cards.Count/_columns;EnsureRows(rowIndex+1);
            CreateCard(_gridRows[rowIndex],item);
        }
        var scroll=_shell.Tree.GetComponent<XsrUiScroll>(_nodes["WardrobeLibraryBody"]);
        if(scroll is not null){scroll.OffsetX=0;scroll.OffsetY=0;}_dirty=true;
    }
    private void CreateCard(XsrUiEntityId parent,WardrobeCatalogItem item)
    {
        var data=CardProjection(item,item.Kind==WardrobeCatalogKind.Cape?WardrobePlayerView.Back:WardrobePlayerView.Isometric);
        var card=_dynamic.Append(parent,DesktopMarkup.Dynamic("WardrobeLibraryCard.d.nxml",data));
        _shell.Tree.Walk(card,node=>{_nodes[_shell.Tree.Name(node)]=node;return true;});
        string prefix=data["prefix"];
        var visual=new CardVisual(item,card,_nodes[prefix+".Preview"]){ViewButton=_nodes[prefix+".View"],Projection=data};_cards.Add(visual);PresentImage(visual);
        _actions[visual.ViewButton]=()=>{visual.View=(WardrobePlayerView)(((int)visual.View+1)%7);PresentImage(visual);_dirty=true;};
        _actions[_nodes[prefix+".Apply"]]=()=>Apply(item);
        _actions[_nodes[prefix+".Details"]]=()=>{if(_site is{}site&&item.DetailsUri.Host.Equals(site.BaseUri.Host,StringComparison.OrdinalIgnoreCase))Open(item.DetailsUri);};
    }
    private Dictionary<string,string> CardProjection(WardrobeCatalogItem item,WardrobePlayerView view)=>ProjectCard(_shell,item,view,ShowApply(item),CanApply(item));
    internal static Dictionary<string,string> ProjectCard(XsrUiShell shell,WardrobeCatalogItem item,WardrobePlayerView view,bool showApply,bool canApply)
    {
        string Format(string name,params(string Key,object Value)[] values)=>DesktopWardrobeText.Format(name,values.ToDictionary(pair=>pair.Key,pair=>Convert.ToString(pair.Value,CultureInfo.CurrentCulture)??""),shell.Renderer.LocalizeText);
        string hd=item.IsHighDefinition?Raw("hd-marker"):"";
        string metadata=item.Kind==WardrobeCatalogKind.Cape?Format("CapeMetadata",("likes",item.Likes),("hd",hd))
            :Format("SkinMetadata",("likes",item.Likes),("model",item.Model=="alex"?Raw("slim-model"):Raw("classic-model")),("hd",hd));
        string prefix="WardrobeLibraryCard."+item.TextureId.ToString(CultureInfo.InvariantCulture);
        return new(){["prefix"]=prefix,["body_key"]=prefix+".Body",["preview_key"]=prefix+".Preview",["name_key"]=prefix+".Name",["uploader_key"]=prefix+".Uploader",["metadata_key"]=prefix+".Metadata",["view_key"]=prefix+".View",["apply_key"]=prefix+".Apply",["details_key"]=prefix+".Details", ["name"]=item.Name,
            ["uploader"]=Format("Uploader",("uploader",string.IsNullOrWhiteSpace(item.Uploader)?shell.Renderer.LocalizeText(Raw("unknown-user")):item.Uploader)),
            ["metadata"]=metadata,["view"]=Format("View",("view",shell.Renderer.LocalizeText(ViewLabel(view)))),
            ["apply"]=shell.Renderer.LocalizeText(Raw(item.Kind==WardrobeCatalogKind.Cape?"use-cape":"use-skin")),
            ["apply_visible"]=Flag(showApply),["apply_enabled"]=Flag(canApply)};
    }
    private void ProjectCards()
    {
        foreach(var card in _cards)
        {
            var data=CardProjection(card.Item,card.View);
            if(Same(data,card.Projection))continue;
            _dynamic.Reload(card.Container,DesktopMarkup.Dynamic("WardrobeLibraryCard.d.nxml",data));card.Projection=data;
        }
    }
    private void EnsureRows(int count)
    {
        while(_gridRows.Count<count)_gridRows.Add(_dynamic.Append(_nodes["WardrobeLibraryItems"],DesktopMarkup.Dynamic("WardrobeLibraryRow.d.nxml",
            new Dictionary<string,string>{["key"]="WardrobeLibraryCardRow."+_gridRows.Count.ToString(CultureInfo.InvariantCulture)})));
    }
    private void ReflowCards()
    {
        int count=(_cards.Count+_columns-1)/_columns;EnsureRows(count);
        for(int i=0;i<_cards.Count;i++)_dynamic.Move(_cards[i].Container,_gridRows[i/_columns]);
        for(int i=0;i<count;i++)_dynamic.Reorder(_gridRows[i],_cards.Skip(i*_columns).Take(_columns).Select(card=>card.Container).ToArray());
        while(_gridRows.Count>count){RemoveFragment(_gridRows[^1]);_gridRows.RemoveAt(_gridRows.Count-1);}
        _dynamic.Reorder(_nodes["WardrobeLibraryItems"],_gridRows);
    }
    private void UpdateResponsiveLayout()
    {
        if(_viewport==_shell.Renderer.Viewport)return;
        _viewport=_shell.Renderer.Viewport;var view=DesktopWardrobeText.Metric("library-view");var content=DesktopWardrobeText.Metric("library-content");
        bool compact=_viewport.Height<=Required(view.Height);var layout=compact?DesktopWardrobeText.Metric("library-compact"):content;
        _presentation["WardrobeLibraryContent_padding"]=Thickness(layout.Padding);_presentation["WardrobeLibraryContent_spacing"]=Number(layout.Spacing);
        Visible("WardrobeLibraryHelp",!compact);
        var rail=DesktopWardrobeText.Metric("library-rail");_railWidth=_viewport.Width>=Required(view.MaxWidth)?Required(rail.MaxWidth):_viewport.Width<=Required(view.MinWidth)?Required(rail.MinWidth):Required(rail.Width);
        _presentation["WardrobeLibraryRail_width"]=Number(_railWidth-rail.Padding.Horizontal);
        var padding=_shell.Tree.GetComponent<XsrUiElement>(_shell.Content)!.Padding;
        double available=Math.Min(Required(content.MaxWidth),Math.Max(Required(content.MinWidth),_viewport.Width-view.Margin.Left-padding.Horizontal-_railWidth))-layout.Padding.Horizontal;
        var grid=DesktopWardrobeText.Metric("library-grid");int columns=Math.Clamp((int)((available+grid.Spacing)/Required(grid.Width)),(int)Required(grid.MinWidth),(int)Required(grid.MaxWidth));
        if(_columns!=columns){_columns=columns;ReflowCards();}_dirty=true;
    }
    private void Present()
    {
        if (!_dirty) return; _dirty = false;
        Text("WardrobeLibrarySiteName", _site?.DisplayName ?? Raw("copy.f242886e6a3f"), literal: _site is not null);
        Text("WardrobeLibrarySiteStatus",_catalog is{}catalog?CatalogueCaption(catalog):_siteStatus,literal:_siteStatusLiteral);
        Text("WardrobeLibraryPageNumber",Format("Page",("page",_filter.Page)),literal:true);
        Text("WardrobeLibraryAccountNotice", _account is { } account ? account.Identity.Kind switch
        {
            LaunchProfileKind.Offline => Raw("copy.409efd13530d"),
            LaunchProfileKind.ThirdParty => Raw("copy.0c01aef694ac"),
            LaunchProfileKind.NCloud => account.CanUploadSkin ? Raw("copy.f3c2deaebe84") : account.UnavailableReason ?? Raw("copy.e745c0d43198"),
            LaunchProfileKind.Microsoft => Raw("copy.91b8270f3266"),
            _ => account.UnavailableReason ?? Raw("copy.09301ef42513")
        } : _accountReading is not null ? Raw("read-account")
            : _accountFailed ? Raw("copy.c185a9b3b40f") : Raw("copy.df5f107f6bca"));
        Text("WardrobeLibraryOperationStatus", _notice ?? ""); Visible("WardrobeLibraryOperationStatus",!string.IsNullOrWhiteSpace(_notice));
        Visible("WardrobeLibraryManage",ManageUri() is not null);
        bool loading = _sitesReading is not null || _catalogReading is not null;
        Visible("WardrobeLibraryLoading",loading);
        Visible("WardrobeLibraryItems",!loading && _catalog?.Items.Count > 0);
        Visible("WardrobeLibraryEmpty",!loading && _catalog?.Items.Count is not > 0);
        Text("WardrobeLibraryEmptyTitle", _emptyTitle); Text("WardrobeLibraryEmptyMessage", _emptyMessage);
        bool idle = _writing is null;
        Enable("WardrobeLibraryBack", _back is not null);
        Enable("WardrobeLibraryDocs", _site is not null && _openUrl is not null);
        Enable("WardrobeLibraryOpenSite", _site is not null && _openUrl is not null);
        Enable("WardrobeLibraryManage", ManageUri() is not null && _openUrl is not null);
        Enable("WardrobeLibraryRefresh", idle && !loading);
        foreach (string key in new[] { "WardrobeLibrarySearchButton", "WardrobeLibrarySkin", "WardrobeLibraryCape", "WardrobeLibraryTime", "WardrobeLibraryLikes" }) Enable(key, idle && _site is not null);
        Visible("WardrobeLibraryCape",_site?.SupportsCapes == true);
        Enable("WardrobeLibraryPrevious", idle && !loading && _catalog?.HasPreviousPage == true);
        Enable("WardrobeLibraryNext", idle && !loading && _catalog?.HasNextPage == true);
        Select("WardrobeLibrarySkin", _filter.Kind == WardrobeCatalogKind.Skin); Select("WardrobeLibraryCape", _filter.Kind == WardrobeCatalogKind.Cape);
        Select("WardrobeLibraryTime", _filter.Order == WardrobeCatalogOrder.Time); Select("WardrobeLibraryLikes", _filter.Order == WardrobeCatalogOrder.Likes);
        ProjectCards();BuildSites();ReloadWorkspace();
        _shell.Tree.MarkDirty(Page,XsrUiDirtyKinds.Paint);
    }

    private void Retire()
    {
        _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        _catalogStop.Cancel(); _catalogStop.Dispose(); _catalogStop = new();
        _sitesReading = null; _catalogReading = null; _accountReading = null; _writing = null;
        _account = null; _lastAdmittedIdentity = null; _catalog = null; _started = false; _accountLoaded = false; _accountFailed = false; _notice = null; _dirty = true;
        _siteStatus = Raw("copy.9d0d37af1ea8"); _siteStatusLiteral = false; _scrollOffset = double.NaN;
        while (_pending.TryDequeue(out _)) { }
        ClearCards();
    }
    private void ClearCards(){ClearImages();Clear(_nodes["WardrobeLibraryItems"]);_gridRows.Clear();_cards.Clear();}
    private void ConsumeImages()
    {
        if (_cards.Count == 0 || _site is null) return;
        double offset = _shell.Tree.GetComponent<XsrUiScroll>(_nodes["WardrobeLibraryBody"])!.OffsetY;
        double height = _shell.Renderer.TryGetScrollSnapshot(_nodes["WardrobeLibraryBody"], out var snapshot)
            ? snapshot.ViewportHeight : CardHeight;
        int firstRow = Math.Clamp((int)(offset / (CardHeight + RowSpacing)), 0, (_cards.Count - 1) / _columns);
        int lastRow = Math.Max(firstRow, (int)Math.Ceiling((offset + Math.Max(1, height)) / (CardHeight + RowSpacing)) - 1);
        var visible = _cards.Skip(firstRow * _columns).Take((lastRow - firstRow + 1) * _columns).ToHashSet();
        for (int index = _images.Count - 1; index >= 0; index--)
        {
            var image = _images[index];
            if (!visible.Contains(image.Card))
            {
                image.Stop.Cancel(); image.Stop.Dispose(); _images.RemoveAt(index); image.Card.Attempted = false;
                continue;
            }
            if (!image.Read.IsCompleted) continue;
            _images.RemoveAt(index);
            image.Stop.Dispose();
            if (!Succeeded(image.Read) || !_shell.Tree.IsAlive(image.Card.Preview)) continue;
            image.Card.Loaded = image.Read.Result.Value; PresentImage(image.Card);
            _shell.Tree.MarkDirty(image.Card.Preview, XsrUiDirtyKinds.Paint);
        }
        foreach (var card in _cards)
        {
            if (visible.Contains(card)) continue;
            card.Attempted = false;
            if (card.Loaded is null) continue;
            card.Loaded = null; PresentImage(card); _shell.Tree.MarkDirty(card.Preview, XsrUiDirtyKinds.Paint);
        }
        if (!_queries.TryResolve(WardrobeCatalogContract.Preview, out var route)) return;
        foreach (var card in visible.OrderBy(card => _cards.IndexOf(card)))
        {
            if (_images.Count >= 4) break;
            if (card.Attempted || !_shell.Tree.IsAlive(card.Preview)) continue;
            card.Attempted = true;
            var stop = CancellationTokenSource.CreateLinkedTokenSource(_imageStop.Token);
            var query = new WardrobeCatalogPreviewQuery(_site.Id, card.Item.TextureId, card.Item.Kind);
            var read = _queries.QueryAsync<WardrobeCatalogPreviewQuery, AccountWardrobeResolvedTextures>(route, query, cancellationToken: stop.Token).AsTask();
            _images.Add(new(card, stop, read)); Wake(read, stop.Token);
        }
    }
    private void PresentImage(CardVisual card) => _shell.Tree.GetComponent<XsrUiImage>(card.Preview)!.Raster
        = WardrobePlayerPresentation.Player(card.Loaded?.Skin, card.Loaded?.Cape, card.Item.Model == "alex", card.View);
    private static string ViewLabel(WardrobePlayerView view) => view switch
    { WardrobePlayerView.Isometric => Raw("copy.bc295ff5696c"), WardrobePlayerView.Front => Raw("copy.740f5e8102e0"), WardrobePlayerView.Back => Raw("copy.2d270b6ce44a"), WardrobePlayerView.Left => Raw("copy.6a24b14f33c7"), WardrobePlayerView.Right => Raw("copy.1cf1d4d0b24b"), WardrobePlayerView.Top => Raw("copy.6f2a4a02e60e"), _ => Raw("copy.3f049887991b") };
    private void ClearImages()
    {
        _imageStop.Cancel(); _imageStop.Dispose(); _imageStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        foreach (var image in _images) image.Stop.Dispose();
        _images.Clear();
        foreach (var card in _cards) { card.Loaded = null; card.Attempted = false; if (_shell.Tree.IsAlive(card.Preview)) PresentImage(card); }
    }
    private void Clear(XsrUiEntityId parent)
    {
        foreach (var child in _shell.Tree.Children(parent).ToArray())
        {
            _shell.Tree.Walk(child, entity =>
            {
                _actions.Remove(entity);
                string name = _shell.Tree.Name(entity); if (_nodes.TryGetValue(name, out var mapped) && mapped == entity) _nodes.Remove(name);
                return true;
            });
            _dynamic.Remove(child);
        }
    }
    private void Wake(Task task, CancellationToken token) => _ = task.ContinueWith(completed =>
    {
        _ = completed.Exception;
        if (_disposed || token.IsCancellationRequested) return;
        _dirty = true;
        try { _store.Publish(_store.Resolve(WardrobePresentationState.WakeKey), Interlocked.Increment(ref _wake)); }
        catch (ObjectDisposedException) { }
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private static bool Succeeded<T>(Task<XsrResult<T>> task) => task.IsCompletedSuccessfully && task.Result.IsSuccess;
    private static string Raw(string key)=>DesktopWardrobeText.Raw(key);
    private string Format(string name,params(string Key,object Value)[] values)=>DesktopWardrobeText.Format(name,values.ToDictionary(pair=>pair.Key,pair=>Convert.ToString(pair.Value,CultureInfo.CurrentCulture)??""),_shell.Renderer.LocalizeText);
    private string CatalogueCaption(WardrobeCatalogPage page)=>Format("Catalogue",("page",page.Page),("count",page.Items.Count),
        ("server",string.IsNullOrWhiteSpace(page.ServerVersion)?"":Format("Server",("version",page.ServerVersion))));
    private static double Required(double? value)=>value is >0&&double.IsFinite(value.Value)?value.Value:throw new NxmlCompileException("Incomplete wardrobe library viewport metric.");
    private static string SafeSuffix(string value)=>value.Length<=90&&!value.Any(char.IsWhiteSpace)&&!value.Contains('{')&&!value.Contains('}')?value:"h"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Number(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    private static string Flag(bool value)=>value?"true":"false";
    private static string Thickness(XsrUiThickness value)=>string.Join(",",Number(value.Left),Number(value.Top),Number(value.Right),Number(value.Bottom));
    private static bool Same(Dictionary<string,string>a,Dictionary<string,string>b)=>a.Count==b.Count&&a.All(pair=>b.TryGetValue(pair.Key,out var value)&&value==pair.Value);
    private void RemoveFragment(XsrUiEntityId entity)
    {
        _shell.Tree.Walk(entity,node=>{_actions.Remove(node);string name=_shell.Tree.Name(node);if(_nodes.TryGetValue(name,out var mapped)&&mapped==node)_nodes.Remove(name);return true;});
        _dynamic.Remove(entity);
    }
    private void Text(string name,string value,bool literal=false)=>_presentation[name+"_text"]=literal?value:_shell.Renderer.LocalizeText(value);
    private void Enable(string name,bool enabled)=>_presentation[name+"_enabled"]=Flag(enabled);
    private void Visible(string name,bool visible)=>_presentation[name+"_visible"]=Flag(visible);
    private void Select(string name,bool selected){_presentation[name+"_selected"]=Flag(selected);_presentation[name+"_class"]=DesktopWardrobeText.Defaults(library:true)[name+"_class"]+(selected?Raw("selected-class-suffix"):"");}
    private void ReloadWorkspace(){if(Same(_presentation,_applied))return;_dynamic.Reload(_workspace,DesktopMarkup.Dynamic("WardrobeLibraryWorkspace.d.nxml",_presentation));_applied=new(_presentation);}
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _intents.IntentEmitted -= OnIntent; _shell.Renderer.FramePreparing -= OnFrame; _store.Changed -= OnStateChanged;
        Retire(); _lifetime.Dispose(); _catalogStop.Dispose(); _imageStop.Dispose();
        _dynamic.Remove(_workspace);if(_shell.Tree.IsAlive(Page))_shell.Tree.Destroy(Page);
    }
}
