using System.Collections.Concurrent;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal static class ResourcesPresentationState
{
    internal static readonly XsrSemanticId Wake = XsrSemanticId.Parse("ui.resources.wake");
    internal static void DeclareState(XsrStateStoreBuilder builder) => builder.Cell<long>(Wake, "Nexa.Desktop.Resources");
}

/// <summary>Presentation-only catalog. Request replacement cancels and forgets the old query.</summary>
internal sealed partial class ResourcesPageController : IDisposable
{
    private static readonly XsrSemanticId Action = XsrSemanticId.Parse("ui.resources.action");
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrQueryRouter _queries;
    private readonly XsrStateStore _store;
    private readonly Nexa.Services.RegionalPolicy _region;
    private readonly Action<Uri> _open;
    private readonly DesktopFunctionPatches _functionPatches;
    private readonly DesktopSidecarSignals? _sidecarSignals;
    private readonly DesktopSidecarUiPatches? _sidecarUi;
    private long _captionRevision = -1;
    private long _moduleRevision = -1;
    private readonly Dictionary<XsrUiEntityId, Action> _actions = [];
    private readonly ConcurrentQueue<XsrUiEntityId> _pending = new();
    private readonly Dictionary<string, XsrUiEntityId> _entities = [];
    private readonly List<XsrUiEntityId> _listActions = [];
    private readonly List<XsrUiEntityId> _detailActions = [];
    private readonly List<(XsrUiEntityId Track, double Minimum)> _segments = [];
    private readonly XsrUiEntityId _search, _game, _loader, _status, _previous, _next, _detailBody;
    private CancellationTokenSource _stop = new();
    private CancellationTokenSource _contextStop = new();
    private CancellationTokenSource _iconStop = new();
    private CancellationTokenSource _translationStop = new();
    private XsrCommandRouter? _commands;
    private Func<Task<string?>>? _pickDownload;
    private Func<string?>? _modpackRoot;
    internal void ConfigureModpackRoot(Func<string?> root) => _modpackRoot = root;
    private DesktopFeedbackService? _feedback;
    private readonly CancellationTokenSource _downloadsStop = new();
    private CancellationTokenSource _planStop = new();
    private Task<XsrResult<ResourceInstallPlan>>? _planning;
    private ResourceModInstallCommand? _installDraft;
    private bool _optionalVisible;
    private XsrUiEntityId _optionalInstall;
    private bool _installReturnDetail;
    private readonly HashSet<ResourceReference> _optionalSelection = [];
    private bool _picking, _favoriteMode, _hideInstalled, _hideLibraries;
    private Task<XsrResult<ResourceFavoritesSnapshot>>? _favoritesReading;
    private ResourceFavoritesSnapshot _favorites = new([]);
    private Task<XsrResult<ResourceInstanceContext>>? _contextReading;
    private ResourceInstanceQuery? _contextRequest;
    private ResourceInstanceContext? _context;
    private readonly ConcurrentQueue<Action> _presentation = new();
    private readonly List<(XsrUiEntityId Entity, Task<XsrResult<ResourceTranslation>> Read)> _translations = [];
    private ResourceProject? _detailProject;
    private Task<XsrResult<ResourceSearchResult>>? _searching;
    private Task<XsrResult<ResourceDetail>>? _reading;
    private readonly List<(XsrUiEntityId Entity, Task<XsrResult<ResourceIconResult>> Read)> _icons = [];
    private readonly List<ResourcePageIcon> _iconDescriptors = [];
    private XsrUiEntityId _iconPage;
    private Task<XsrResult<MinecraftInstallEditSnapshot>>? _instanceReading;
    private MinecraftInstallEditQuery? _instanceRequest;
    private XsrQueryRouter? _instanceQueries;
    private Func<MinecraftInstallEditQuery?>? _selectedInstance;
    private ResourceSearchQuery _filter = new();
    private ResourceSearchResult? _result;
    private ResourceDetail? _detail;
    private string? _detailId;
    private int _detailPage;
    private bool _started, _disposed, _visible;
    private long _wake;
    private string? _resourcePresentationLanguage;
    internal XsrUiEntityId Page { get; }
    internal XsrUiEntityId DetailPage { get; }

    internal ResourcesPageController(XsrUiShell shell, DesktopUiIntentSink intents, XsrQueryRouter queries,
        XsrStateStore store, Action<Uri> open, DesktopFunctionPatches? functionPatches = null,
        DesktopSidecarSignals? sidecarSignals = null, DesktopSidecarUiPatches? sidecarUi = null,
        Nexa.Services.RegionalPolicy? regionalPolicy = null)
    {
        _shell = shell; _intents = intents; _queries = queries; _store = store; _open = open;
        _region = regionalPolicy ?? Nexa.Services.RegionalPolicy.Current;
        _functionPatches = functionPatches ?? new();
        _sidecarSignals = sidecarSignals;
        _sidecarUi = sidecarUi;
        _resourceDynamic = new(shell.Tree, store);
        Page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("ResourcesPage.nxml"), shell.Tree, store);
        IndexResources(Page);
        var workspaceData = DesktopResourcesText.Defaults();
        workspaceData["initialLoading"] = "false";
        workspaceData["appendLoading"] = "false";
        workspaceData["mirrorEnabled"] = Flag(_region.IsMainlandChina);
        _workspace = AppendResource(_entities["ResourceWorkspaceSlot"], "ResourceWorkspace.d.nxml", workspaceData);
        InitializeCatalogVirtualMetrics();
        _filters = AppendResource(_entities["ResourceFilterSlot"], "ResourceFilters.d.nxml", new()
        {
            ["game"] = L(DesktopResourcesText.Raw("copy.8be216be7a84")),
            ["loader"] = L(DesktopResourcesText.Raw("copy.7a2bd8b6cb0e")),
            ["provider"] = L(DesktopResourcesText.Raw("copy.9f357adb6100")),
            ["tag"] = L(DesktopResourcesText.Raw("copy.a1054434857f")),
            ["loaderVisible"] = "true",
            ["hideInstalled"] = L(DesktopResourcesText.Raw("copy.b55ae05faf55")),
            ["hideLibraries"] = L(DesktopResourcesText.Raw("copy.cedf91a64972"))
        });
        _search = _entities["ResourceSearch"]; _game = _entities["ResourceGame"]; _loader = _entities["ResourceLoader"];
        InitializeResourceTrack("ResourceCategory", index =>
        { _favoriteMode = index == 0; if (!_favoriteMode) _filter = _filter with { Kind = (ResourceKind)(index - 1), Tag = "" }; ResetCategoryFilters(); Search(); });
        InitializeResourceTrack("ResourceOrder", index => { _filter = _filter with { Order = (ResourceOrder)index }; Search(); });
        InitializeResourceTrack("ResourceNetwork", SelectSourceFromPage);
        foreach (string kind in new[] { "Game", "Loader", "Provider", "Tag" })
        { string captured = kind; BindResource("Resource" + kind, () => OpenFilter(captured)); }
        BindResource("ResourceSearchButton", () => { if (_sidecarSignals?.CatchResourceSearch() != true) Search(); });
        BindResource("ResourceCurrentInstance", UseCurrentInstance);
        BindResource("ResourceReset", () =>
        {
            _shell.Renderer.SetTextInputValue(_search, "");
            _filter = _filter with { GameVersion = "", Loader = "", Tag = "", Provider = null };
            ResetCategoryFilters(); UpdateFilterCaptions(); Search();
        });
        BindResource("ResourceHideInstalled", () => { _hideInstalled = !_hideInstalled; UpdateResourceChecks(); ReadContext(); if (_result is not null) ShowResults(); });
        BindResource("ResourceHideLibraries", () => { _hideLibraries = !_hideLibraries; UpdateResourceChecks(); if (_result is not null) ShowResults(); });
        _status = _entities["ResourceStatus"];
        _previous = BindResource("ResourcePrevious", ReturnToListTop);
        _next = BindResource("ResourceNext", RequestNextPage);
        DetailPage = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("ResourceDetailPage.nxml"), shell.Tree, store);
        IndexResources(DetailPage); _detailBody = _entities["ResourceDetailBody"];
        _intents.IntentEmitted += OnIntent; shell.Renderer.FramePreparing += OnFrame;
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (args.Intent.Command == Action && (_shell.Stage.Navigation.Current == Page || _shell.Stage.Navigation.Current == DetailPage)) _pending.Enqueue(args.Intent.Source);
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        UpdateSourcePolicyNotice();
        if (_disposed) return;
        bool visible = _shell.Stage.Navigation.Current == Page || _shell.Stage.Navigation.Current == DetailPage;
        if (visible != _visible)
        {
            if (visible) { _listMediaPaused = false; }
            else
            {
                PauseContinuousList();
                Cancel(); CancelTranslations(); _planStop.Cancel(); _planning = null; _installDraft = null; _contextStop.Cancel(); _contextRequest = null; _contextReading = null;
            }
            _visible = visible; _shell.Tree.MarkDirty(_shell.Content, XsrUiDirtyKinds.Layout);
        }
        SyncIcons(visible ? _shell.Stage.Navigation.Current : default);
        SyncSidecarSource(_shell.Stage.Navigation.Current == Page);
        if (!visible) { CloseFilter(false); return; }
        UpdateFilter();
        string language = DesktopResourceText.Language(_store);
        if (_resourcePresentationLanguage is { } previous && previous != language)
        {
            UpdateFilterCaptions(); UpdateResourceChecks(); _captionRevision = -1;
            if (_sidecarUi is null) WorkspaceResource(("searchCaption", L(DesktopResourcesText.Raw("copy.44ce7ae909bb"))));
            CancelTranslations();
            if (_shell.Stage.Navigation.Current == Page && _result is not null)
            { if (_favoriteMode) ShowFavorites(); else ShowResults(); }
            else if (_shell.Stage.Navigation.Current == DetailPage && _detail is not null && !_optionalVisible) ShowDetail();
        }
        _resourcePresentationLanguage = language;
        ProjectSidecarCaptions();
        ProjectSidecarModules();
        UpdateSegmentWidths();
        if (_optionalVisible && _shell.Stage.Navigation.Current != DetailPage)
        { _optionalVisible = false; _planStop.Cancel(); _planning = null; _installDraft = null; }
        while (_presentation.TryDequeue(out var publish)) publish();
        if (_hideInstalled) ReadContext();
        if (_contextReading is { IsCompleted: true } contextRead)
        {
            _contextReading = null;
            if (_contextRequest == CurrentInstance() && PendingQuery.Succeeded(contextRead))
            { _context = contextRead.Result.Value; if (_result is not null && _shell.Stage.Navigation.Current == Page) ShowResults(); }
        }
        if (_favoritesReading is { IsCompleted: true } favoritesRead)
        {
            _favoritesReading = null;
            if (PendingQuery.Succeeded(favoritesRead))
            {
                _favorites = favoritesRead.Result.Value!;
                if (_favoriteMode && _shell.Stage.Navigation.Current == Page) ShowFavorites();
                else if (_shell.Stage.Navigation.Current == DetailPage && _detail is not null) UpdateFavoriteButton();
            }
            else _feedback?.Error(DesktopResourcesText.Raw("copy.64fd5c67620e"));
        }
        if (_planning is { IsCompleted: true } planned)
        {
            _planning = null;
            if (PendingQuery.Succeeded(planned) && _installDraft is { } draft && draft.Instance == CurrentInstance())
            {
                var plan = planned.Result.Value!;
                if (plan.Optional.Count == 0) { _installDraft = null; _ = RunInstallAsync(draft); }
                else
                {
                    if (_shell.Stage.Navigation.Current != DetailPage) _shell.Stage.Navigation.Push(DetailPage);
                    ShowOptionalDependencies(plan);
                }
            }
            else _feedback?.Error(planned.IsCompletedSuccessfully ? planned.Result.Error?.Message ?? DesktopResourcesText.Raw("copy.41a78f1cca57") : DesktopResourcesText.Raw("copy.41a78f1cca57"));
        }
        for (int i = _translations.Count - 1; i >= 0; i--)
        {
            var (entity, read) = _translations[i];
            if (!read.IsCompleted) continue;
            _translations.RemoveAt(i);
            if (DesktopResourceText.UsesChinese(_store) && PendingQuery.Succeeded(read) && read.Result.Value?.Description is { } description)
            {
                if (!_shell.Tree.IsAlive(entity) || !_resourceTexts.ContainsKey(entity)) continue;
                if (read.Result.Value.Notice is { } notice)
                    description = ResourceFormat("Notice", ("text", description), ("notice", _shell.Renderer.LocalizeText(notice)));
                SetResourceText(entity, description);
            }
        }
        for (int i = _icons.Count - 1; i >= 0; i--)
        {
            var (entity, read) = _icons[i];
            if (!read.IsCompleted) continue;
            _icons.RemoveAt(i);
            if (PendingQuery.Succeeded(read) && read.Result.Value?.Image is { } image)
            {
                _shell.Tree.GetComponent<XsrUiImage>(entity)!.Raster = new(image,
                    [new(new(0, 0, image.Width, image.Height), new(0, 0, 1, 1))])
                { FitToBounds = true };
                _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
            }
        }
        WorkspaceResource(("currentEnabled", Flag(_instanceReading is null && _selectedInstance?.Invoke() is not null)));
        if (!_started) { _started = true; ReadFavorites(); Search(); }
        while (_pending.TryDequeue(out var source)) if (AdmitResource(source) && _actions.TryGetValue(source, out var action)) action();
        if (_instanceReading is { IsCompleted: true } instanceReading)
        {
            _instanceReading = null;
            if (_instanceRequest != _selectedInstance?.Invoke())
                WorkspaceResource(("status", L(DesktopResourcesText.Raw("copy.288a3a86d3d7"))));
            else if (PendingQuery.Succeeded(instanceReading))
            {
                var instance = instanceReading.Result.Value!;
                _filter = _filter with { GameVersion = instance.GameVersion };
                string loader = instance.Selection.Where(item => item.Loader is InstallLoader.Fabric or InstallLoader.Quilt or InstallLoader.Forge or InstallLoader.NeoForge).Select(item => item.Loader.ToString().ToLowerInvariant()).FirstOrDefault() ?? "";
                _filter = _filter with { Loader = loader is "fabric" or "quilt" or "forge" or "neoforge" ? loader : "" };
                UpdateFilterCaptions();
                Search();
            }
            else WorkspaceResource(("status", L(DesktopResourcesText.Raw("copy.80731f640cea"))));
        }
        if (_shell.Stage.Navigation.Current == Page && _reading is not null) { Cancel(); _detailId = null; }
        if (_shell.Stage.Navigation.Current == Page) UpdateContinuousList();
        else DeactivateListMedia();
        if (_reading is { IsCompleted: true } reading)
        {
            _reading = null;
            if (PendingQuery.Succeeded(reading)) { _detail = reading.Result.Value!; ShowDetail(); RenewCatalogDetailIfStale(); }
            else if (!_catalogDetailRenewed || _detail is null) ShowFailure(_detailBody, DesktopResourcesText.Raw("copy.2fd0a4f8db55"), () => ReadDetail(_detailId!), _detailActions);
        }
        SyncIcons(_shell.Stage.Navigation.Current == Page || _shell.Stage.Navigation.Current == DetailPage ? _shell.Stage.Navigation.Current : default);
    }

    private string Draft(XsrUiEntityId entity) => entity == _game ? _filter.GameVersion : entity == _loader ? _filter.Loader
        : _shell.Tree.GetComponent<XsrUiTextInput>(entity)!.ReadDraft().Trim();
    private void ProjectSidecarCaptions()
    {
        if (_sidecarUi is null) return;
        var snapshot = _store.Read<XsrUiPatchSnapshot>(_sidecarUi.State);
        if (snapshot.Revision == _captionRevision) return;
        _captionRevision = snapshot.Revision;
        string? caption = snapshot.Value?.CaptionAt(_sidecarUi.SearchIndex);
        var button = _entities["ResourceSearchButton"];
        WorkspaceResource(("searchCaption", caption ?? DesktopResourcesText.Raw("copy.44ce7ae909bb")), ("searchLocalize", Flag(caption is null)));
    }
    private void Search() => BeginContinuousSearch();

    private void ShowResults() => ReconcileVirtualRows();

    private void ReadDetail(string id)
    {
        Cancel(); _detailId = id; _detail = null;
        Clear(_detailBody, _detailActions);
        AppendResource(_detailBody, "ResourceLoading.d.nxml", new() { ["key"] = "ResourceDetailLoading", ["message"] = L(DesktopResourcesText.Raw("copy.7e74cabf57c0")) });
        string loader = _filter.Kind == ResourceKind.DataPack ? "datapack" : _filter.Kind == ResourceKind.ResourcePack ? "" : _filter.Loader;
        if (_queries.TryResolve(ResourceCatalogContract.Detail, out var route))
        {
            _reading = _queries.QueryAsync<ResourceDetailQuery, ResourceDetail>(route, new(id, _filter.GameVersion, loader) { Sources = _detailProject?.Sources ?? [], MirrorFirst = _filter.MirrorFirst }, cancellationToken: _stop.Token).AsTask();
            Wake(_reading);
        }
    }

    private void ShowDetail()
    {
        Clear(_detailBody, _detailActions);
        var detail = _detail!;
        _detailHeading = AppendResource(_detailBody, "ResourceDetailHeading.d.nxml", new()
        {
            ["title"] = ProjectTitle(DesktopResourceText.Name(detail.Project, _store)),
            ["author"] = ResourceFormat("Author", ("author", detail.Project.Author), ("source", detail.Project.SourceLabel)),
            ["favorite"] = L(IsSaved(detail.Project) ? DesktopResourcesText.Raw("copy.dca60869e7d2") : DesktopResourcesText.Raw("copy.60a53514eb92")),
            ["favoriteClass"] = FavoriteClass(IsSaved(detail.Project))
        });
        TrackIcon(_entities["ResourceDetailIcon"], detail.Project, DetailPage);
        BindResource("ResourceFavorite", () => ToggleFavorite(detail.Project), _detailActions);
        var description = AppendResource(_detailBody, "ResourceDetailDescription.d.nxml", new()
        { ["description"] = DesktopResourceText.Description(detail.Project, _store) });
        RegisterResourceText(_entities["ResourceDetailDescription"], description, "description");
        Translate(_entities["ResourceDetailDescription"], detail.Project);
        if (!string.IsNullOrEmpty(detail.Notice))
            AppendResource(_detailBody, "ResourceDetailNotice.d.nxml", new() { ["notice"] = _shell.Renderer.LocalizeText(detail.Notice) });
        AppendResource(_detailBody, "ResourceDetailTools.d.nxml", new()
        {
            ["info"] = ResourceFormat("DetailInfo", ("source", detail.Project.SourceLabel),
                ("downloads", FormatDownloads(Math.Max(0, ResourceCaptions.DownloadCount(_functionPatches.Runtime, _functionPatches.DownloadCount, detail.Project.Downloads)))),
                ("license", detail.License))
        });
        BindResource("ResourceWebsite", () => _open(new Uri(detail.Project.Website)), _detailActions);
        AppendResource(_detailBody, "ResourceDetailVersionsTitle.d.nxml", new()
        {
            ["title"] = ResourceFormat(_filter.GameVersion.Length > 0 ? "VersionsGame" : "Versions",
            ("count", detail.Versions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("game", _filter.GameVersion))
        });
        if (detail.Versions.Count == 0) AppendResource(_detailBody, "ResourceDetailEmpty.d.nxml", new());
        foreach (var version in detail.Versions.Skip(_detailPage * 20).Take(20))
        {
            bool release = version.Channel == DesktopResourcesText.Raw("copy.93a48223a516"), beta = version.Channel == "Beta";
            string id = ResourceIdentity(version.Id);
            AppendResource(_detailBody, "ResourceVersion.d.nxml", new()
            {
                ["row"] = "ResourceVersion." + id,
                ["channel"] = version.Channel,
                ["icon"] = release ? DesktopResourcesText.Raw("icon-release") : beta ? DesktopResourcesText.Raw("icon-beta") : DesktopResourcesText.Raw("icon-alpha"),
                ["typeClass"] = DesktopResourcesText.Raw(release ? "release-style" : beta ? "beta-style" : "alpha-style"),
                ["name"] = version.Name,
                ["metadata"] = ResourceFormat("Version", ("provider", version.Provider.ToString()), ("channel", version.Channel),
                    ("loaders", string.Join(" / ", version.Loaders)), ("games", string.Join(", ", version.Games))),
                ["download"] = "ResourceDownload." + id,
                ["downloadCaption"] = L(version.File is not null && detail.Project.Kind == ResourceKind.Modpack ? DesktopResourcesText.Raw("modpack.install") : version.File is null ? DesktopResourcesText.Raw("copy.55c561dc5546") : DesktopResourcesText.Raw("copy.4673a2306165")),
                ["downloadIcon"] = version.File is null ? DesktopResourcesText.Raw("icon-link") : DesktopResourcesText.Raw("icon-download")
            });
            BindResource("ResourceDownload." + id, () =>
            { if (version.File is null) _open(new Uri(version.Website)); else _ = DownloadAsync(version); }, _detailActions);
        }
        if (detail.Versions.Count > 20)
        {
            AppendResource(_detailBody, "ResourceVersionPages.d.nxml", new()
            {
                ["previous"] = Flag(_detailPage > 0),
                ["next"] = Flag((_detailPage + 1) * 20 < detail.Versions.Count),
                ["page"] = ResourceFormat("Page", ("page", (_detailPage + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("pages", ((detail.Versions.Count + 19) / 20).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            });
            BindResource("ResourceVersionPrevious", () => { _detailPage--; ShowDetail(); }, _detailActions);
            BindResource("ResourceVersionNext", () => { _detailPage++; ShowDetail(); }, _detailActions);
        }
        _shell.Tree.GetComponent<XsrUiScroll>(_detailBody)!.OffsetY = 0;
    }

    private void UpdatePagination() => UpdateContinuousStatus();
    private void ShowFailure(XsrUiEntityId parent, string message, Action retry, List<XsrUiEntityId> actions)
    {
        Clear(parent, actions);
        AppendResource(parent, "ResourceFailure.d.nxml", new() { ["message"] = _shell.Renderer.LocalizeText(message) });
        BindResource("ResourceRetry", retry, actions);
    }
    internal void ConfigureInstanceFilter(XsrQueryRouter queries, Func<MinecraftInstallEditQuery?> selected, XsrCommandRouter? catalogCommands = null)
    { _instanceQueries = queries; _selectedInstance = selected; _resourceCatalogCommands = catalogCommands; }
    private void UseCurrentInstance()
    {
        if (_selectedInstance?.Invoke() is not { } query || _instanceQueries is null || !_instanceQueries.TryResolve(MinecraftInstallEditContract.Query, out var route)) return;
        Cancel();
        _instanceRequest = query;
        _instanceReading = _instanceQueries.QueryAsync<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(route, query, cancellationToken: _stop.Token).AsTask();
        WorkspaceResource(("status", L(DesktopResourcesText.Raw("copy.34cbc1b5c274"))));
        Wake(_instanceReading);
    }
    internal void ConfigureDownloads(XsrCommandRouter commands, Func<Task<string?>> picker, DesktopFeedbackService feedback)
    { _commands = commands; _pickDownload = picker; _feedback = feedback; }
    private async Task DownloadAsync(ResourceVersion version, ResourceKind? kind = null)
    {
        if (_picking || _commands is null || _pickDownload is null || !_commands.TryResolve(ResourceCatalogContract.Download, out var route)) return;
        if ((kind ?? _detailProject?.Kind ?? _filter.Kind) == ResourceKind.Mod && _commands.TryResolve(ResourceCatalogContract.InstallMod, out var installRoute))
        { await InstallModAsync(new(version.Provider, version.ProjectId), version.Id, installRoute); return; }
        if ((kind ?? _detailProject?.Kind ?? _filter.Kind) == ResourceKind.Modpack)
        {
            if (!_commands.TryResolve(ResourceModpackContract.Install, out var packRoute)) return;
            string? root = _modpackRoot?.Invoke();
            if (string.IsNullOrWhiteSpace(root)) { _feedback?.Error(DesktopResourcesText.Raw("modpack.select-root")); return; }
            _picking = true;
            try
            {
                var result = await _commands.Dispatch(packRoute, new ResourceModpackInstallCommand(new(version.Provider, version.ProjectId), version.Id, root, _filter.MirrorFirst), cancellationToken: _downloadsStop.Token).Completion;
                if (!_disposed && !result.IsSuccess && result.Error?.Code != XsrRuntimeErrors.Cancelled().Code)
                    _feedback?.Error(result.Error?.Message ?? DesktopResourcesText.Raw("modpack.failed"));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { if (!_disposed) _feedback?.Error(error.Message); }
            finally { _picking = false; }
            return;
        }
        _picking = true;
        try
        {
            string? folder;
            try { folder = await _pickDownload(); } finally { _picking = false; }
            if (folder is null || _disposed) return;
            var result = await _commands.Dispatch(route, new ResourceDownloadCommand(version.Provider, version.ProjectId, version.Id, folder, _filter.MirrorFirst), cancellationToken: _downloadsStop.Token).Completion.ConfigureAwait(false);
            if (!_disposed && !result.IsSuccess && result.Error?.Code != XsrRuntimeErrors.Cancelled().Code) _feedback?.Error(result.Error?.Message ?? DesktopResourcesText.Raw("copy.cea8420e2779"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { if (!_disposed) _feedback?.Error(DesktopResourcesText.Raw("copy.5eab7ff6cfb1") + error.Message); }
    }
    private void Translate(XsrUiEntityId entity, ResourceProject project, CancellationToken lease = default)
    {
        if (!DesktopResourceText.UsesChinese(_store) || project.Sources.Count == 0 || !_queries.TryResolve(ResourceCatalogContract.Translate, out var route)) return;
        var source = project.Sources[0];
        var token = lease.CanBeCanceled ? lease : _translationStop.Token;
        var read = _queries.QueryAsync<ResourceTranslationQuery, ResourceTranslation>(route, new(source, project.Description), cancellationToken: token).AsTask();
        _translations.Add((entity, read)); Wake(read, token);
    }
    private ResourceInstanceQuery? CurrentInstance() => _selectedInstance?.Invoke() is { } selected ? new(selected.RootDirectory, selected.InstanceId, _filter.MirrorFirst) : null;
    private void ReadContext()
    {
        var query = CurrentInstance();
        if (query == _contextRequest) return;
        _contextStop.Cancel(); _contextStop.Dispose(); _contextStop = new();
        _context = null; _contextRequest = query; _contextReading = null;
        if (query is not null && _queries.TryResolve(ResourceCatalogContract.Instance, out var route))
        { _contextReading = _queries.QueryAsync<ResourceInstanceQuery, ResourceInstanceContext>(route, query, cancellationToken: _contextStop.Token).AsTask(); Wake(_contextReading); }
    }
    private void ReadFavorites()
    {
        if (_queries.TryResolve(ResourceCatalogContract.Favorites, out var route))
        { _favoritesReading = _queries.QueryAsync<ResourceFavoritesQuery, ResourceFavoritesSnapshot>(route, new(), cancellationToken: _downloadsStop.Token).AsTask(); Wake(_favoritesReading); }
    }
    private void ShowFavorites()
    {
        string text = _filter.Text;
        var matches = _favorites.Projects.Where(p => text.Length == 0 || p.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) || p.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || p.DisplayDescription.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        _listHasMore = false;
        _result = new(matches, matches.Length, 0) { HasMore = false };
        ShowResults(); UpdatePagination();
    }
    private bool IsSaved(ResourceProject project) => _favorites.Projects.Any(p => p.Sources.Intersect(project.Sources).Any());
    private void UpdateFavoriteButton()
    {
        if (_detail is null || !_detailHeading.IsAssigned || !_shell.Tree.IsAlive(_detailHeading)) return;
        ReloadResource(_detailHeading, ("favorite", L(IsSaved(_detail.Project) ? DesktopResourcesText.Raw("copy.dca60869e7d2") : DesktopResourcesText.Raw("copy.60a53514eb92"))),
            ("favoriteClass", FavoriteClass(IsSaved(_detail.Project))));
    }
    private void ToggleFavorite(ResourceProject project)
    {
        if (_favoritesReading is not null || _commands is null || !_commands.TryResolve(ResourceCatalogContract.Favorite, out var route)) return;
        project = project with { Kind = _detailProject?.Kind ?? project.Kind };
        _favoritesReading = SaveFavoriteAsync(route, new(project, !IsSaved(project))); Wake(_favoritesReading);
    }
    private async Task<XsrResult<ResourceFavoritesSnapshot>> SaveFavoriteAsync(XsrCommandId route, ResourceFavoriteCommand command)
    {
        var result = await _commands!.Dispatch(route, command, cancellationToken: _downloadsStop.Token).Completion.ConfigureAwait(false);
        if (!result.IsSuccess) return XsrResult.Failure<ResourceFavoritesSnapshot>(result.Error!);
        if (!_queries.TryResolve(ResourceCatalogContract.Favorites, out var queryRoute)) throw new InvalidOperationException("Favorites route missing.");
        return await _queries.QueryAsync<ResourceFavoritesQuery, ResourceFavoritesSnapshot>(queryRoute, new(), cancellationToken: _downloadsStop.Token).ConfigureAwait(false);
    }
    private async Task DownloadProjectAsync(ResourceProject project)
    {
        try
        {
            if (_commands is null) return;
            var reference = project.Sources.Count > 0 ? project.Sources[0] : new(ResourceProvider.Modrinth, project.Id);
            if (project.Kind == ResourceKind.Mod && _commands.TryResolve(ResourceCatalogContract.InstallMod, out var installRoute))
            { await InstallModAsync(reference, null, installRoute); return; }
            if (!_queries.TryResolve(ResourceCatalogContract.Detail, out var route)) return;
            var result = await _queries.QueryAsync<ResourceDetailQuery, ResourceDetail>(route, new(project.Id, _filter.GameVersion, project.Kind == ResourceKind.DataPack ? "datapack" : project.Kind == ResourceKind.ResourcePack ? "" : _filter.Loader) { Sources = project.Sources, MirrorFirst = _filter.MirrorFirst }, cancellationToken: _downloadsStop.Token).ConfigureAwait(false);
            var version = result.Value?.Versions.FirstOrDefault(v => v.File is not null);
            if (!result.IsSuccess || version is null) { _feedback?.Error(DesktopResourcesText.Raw("copy.e551e8501b20")); return; }
            await DownloadAsync(version, project.Kind);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException) { if (!_disposed) _feedback?.Error(e.Message); }
    }
    private Task InstallModAsync(ResourceReference source, string? version, XsrCommandId route)
    {
        var instance = CurrentInstance();
        if (instance is null) { _feedback?.Error(DesktopResourcesText.Raw("copy.4953eb4500a9")); return Task.CompletedTask; }
        var command = new ResourceModInstallCommand(source, version, instance);
        if (!_queries.TryResolve(ResourceCatalogContract.PlanMod, out _)) return RunInstallAsync(command);
        _optionalSelection.Clear(); _installReturnDetail = _shell.Stage.Navigation.Current == DetailPage;
        StartInstallPlan(command); return Task.CompletedTask;
    }
    private void StartInstallPlan(ResourceModInstallCommand command)
    {
        Cancel();
        _planStop.Cancel(); _planStop.Dispose(); _planStop = new(); _installDraft = command;
        if (_optionalInstall.IsAssigned && _shell.Tree.IsAlive(_optionalInstall))
            ReloadResource(_optionalActions, ("enabled", "false"));
        if (!_queries.TryResolve(ResourceCatalogContract.PlanMod, out var route)) return;
        _planning = _queries.QueryAsync<ResourceModPlanQuery, ResourceInstallPlan>(route, new(command), cancellationToken: _planStop.Token).AsTask(); Wake(_planning);
    }
    private void ShowOptionalDependencies(ResourceInstallPlan plan)
    {
        _optionalVisible = true;
        Clear(_detailBody, _detailActions);
        AppendResource(_detailBody, "ResourceOptionalHeading.d.nxml", new()
        {
            ["summary"] = ResourceFormat("OptionalSummary", ("count", plan.Versions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("cycles", plan.HasCycles ? L(DesktopResourcesText.Raw("copy.3efa6b72a5b3")) : ""))
        });
        foreach (var option in plan.Optional)
        {
            string id = ResourceIdentity(option.Source.ProjectId);
            AppendResource(_detailBody, "ResourceOptionalRow.d.nxml", new()
            {
                ["row"] = "ResourceOptional." + id,
                ["name"] = option.Name,
                ["toggle"] = "ResourceOptionalToggle." + id,
                ["selected"] = L(_optionalSelection.Contains(option.Source) ? DesktopResourcesText.Raw("copy.d2f5277505a4") : DesktopResourcesText.Raw("copy.40f9b4605c33"))
            });
            BindResource("ResourceOptionalToggle." + id, () =>
            {
                if (!_optionalSelection.Add(option.Source)) _optionalSelection.Remove(option.Source);
                StartInstallPlan(_installDraft! with { OptionalDependencies = _optionalSelection.ToArray() });
            }, _detailActions);
        }
        _optionalActions = AppendResource(_detailBody, "ResourceOptionalActions.d.nxml", new() { ["enabled"] = "true" });
        BindResource("ResourceOptionalCancel", () => { _planStop.Cancel(); _planning = null; _installDraft = null; CloseOptionalPage(); }, _detailActions);
        _optionalInstall = BindResource("ResourceOptionalInstall", () =>
        {
            if (_planning is not null || _installDraft is null) return;
            var command = _installDraft with { OptionalDependencies = _optionalSelection.ToArray() }; _installDraft = null;
            CloseOptionalPage(); _ = RunInstallAsync(command);
        }, _detailActions);
    }
    private void CloseOptionalPage() { _optionalVisible = false; if (_installReturnDetail && _detail is not null) ShowDetail(); else _shell.Stage.Navigation.Pop(); }
    private async Task RunInstallAsync(ResourceModInstallCommand command)
    {
        if (_commands is null || !_commands.TryResolve(ResourceCatalogContract.InstallMod, out var route)) return;
        try
        {
            var result = await _commands.Dispatch(route, command, cancellationToken: _downloadsStop.Token).Completion.ConfigureAwait(false);
            if (_disposed) return;
            if (!result.IsSuccess && result.Error?.Code != XsrRuntimeErrors.Cancelled().Code) _feedback?.Error(result.Error?.Message ?? DesktopResourcesText.Raw("copy.8a899b4fa4e8"));
            _presentation.Enqueue(() => { _contextRequest = null; _context = null; ReadContext(); }); Wake(Task.CompletedTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException) { if (!_disposed) _feedback?.Error(e.Message); }
    }
    internal static string FormatDownloads(long value)
    {
        double divisor = value >= 1_000_000_000 ? 1_000_000_000 : value >= 1_000_000 ? 1_000_000 : value >= 1_000 ? 1_000 : 1;
        string suffix = divisor == 1_000_000_000 ? DesktopResourcesText.Raw("downloads-b") : divisor == 1_000_000 ? DesktopResourcesText.Raw("downloads-m") : divisor == 1_000 ? DesktopResourcesText.Raw("downloads-k") : "";
        return (value / divisor).ToString(DesktopResourcesText.Raw(divisor == 1 ? "downloads-single-format" : "downloads-scaled-format"), System.Globalization.CultureInfo.InvariantCulture) + suffix;
    }
    private void Cancel() { ResetCatalogRenewal(); _stop.Cancel(); _stop.Dispose(); _stop = new(); _reading = null; _instanceReading = null; }
    private void CancelIcons() { _iconStop.Cancel(); _iconStop.Dispose(); _iconStop = new(); _icons.Clear(); _iconPage = default; }
    private void CancelTranslations() { _translations.Clear(); _translationStop.Cancel(); _translationStop.Dispose(); _translationStop = new(); }
    private readonly record struct ResourcePageIcon(XsrUiEntityId Entity, string Url, XsrUiEntityId Page, CancellationToken Lease = default);
    private void TrackIcon(XsrUiEntityId entity, ResourceProject project, XsrUiEntityId page, CancellationToken lease = default)
    {
        if (project.IconUrl is not { } url) return;
        ResourcePageIcon descriptor = new(entity, url, page, lease);
        _iconDescriptors.Add(descriptor);
        if (_iconPage == page) ReadIcon(descriptor);
    }
    private void ReadIcon(ResourcePageIcon descriptor)
    {
        if (descriptor.Lease.IsCancellationRequested || !_queries.TryResolve(ResourceCatalogContract.Icon, out var route)) return;
        var token = descriptor.Lease.CanBeCanceled ? descriptor.Lease : _iconStop.Token;
        var read = _queries.QueryAsync<ResourceIconQuery, ResourceIconResult>(route, new(descriptor.Url), cancellationToken: token).AsTask();
        _icons.Add((descriptor.Entity, read)); Wake(read, token);
    }
    private void SyncIcons(XsrUiEntityId page)
    {
        if (_iconPage == page) return;
        CancelIcons(); ReleaseIcons(); _iconPage = page;
        foreach (var descriptor in _iconDescriptors)
            if (descriptor.Page == page && _shell.Tree.IsAlive(descriptor.Entity)) ReadIcon(descriptor);
    }
    private void ReleaseIcons()
    {
        foreach (var descriptor in _iconDescriptors)
        {
            if (!_shell.Tree.IsAlive(descriptor.Entity) || _shell.Tree.GetComponent<XsrUiImage>(descriptor.Entity) is not { Raster: not null } image) continue;
            image.Raster = null;
            _shell.Tree.MarkDirty(descriptor.Entity, XsrUiDirtyKinds.Paint);
        }
    }
    private void Wake(Task task, CancellationToken token = default) => _ = task.ContinueWith(_ =>
    {
        if (_disposed || token.IsCancellationRequested) return;
        try { _store.Publish(_store.Resolve(ResourcesPresentationState.Wake), Interlocked.Increment(ref _wake)); }
        catch (ObjectDisposedException) { }
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private void Clear(XsrUiEntityId parent, List<XsrUiEntityId> actions)
    {
        foreach (var action in actions) _actions.Remove(action); actions.Clear();
        var removed = new HashSet<XsrUiEntityId>();
        foreach (var child in _shell.Tree.Children(parent)) _shell.Tree.Walk(child, entity => { removed.Add(entity); return true; });
        _resourceDynamic.Clear(parent); ForgetResourceViews(removed);
        _translations.RemoveAll(item => removed.Contains(item.Entity)); _icons.RemoveAll(item => removed.Contains(item.Entity)); _iconDescriptors.RemoveAll(item => removed.Contains(item.Entity));
    }
    private string ProjectTitle(string title) => ResourceCaptions.ProjectTitle(_functionPatches.Runtime, _functionPatches.ProjectTitle, title);
    internal XsrUiEntityId Find(string key) => _entities[key];
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; DisposeContinuousList(); RetireSidecarSource(); _moduleSourceStop.Dispose(); _planStop.Cancel(); _planStop.Dispose(); _contextStop.Cancel(); _contextStop.Dispose(); _downloadsStop.Cancel(); _downloadsStop.Dispose();
        _iconStop.Cancel(); _iconStop.Dispose(); _translationStop.Cancel(); _translationStop.Dispose(); ReleaseIcons(); _iconDescriptors.Clear(); _icons.Clear(); _translations.Clear();
        CloseFilter(false);
        _stop.Cancel(); _stop.Dispose(); _intents.IntentEmitted -= OnIntent; _shell.Renderer.FramePreparing -= OnFrame;
    }
}
