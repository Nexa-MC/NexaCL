using System.Globalization;
using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    private readonly SortedDictionary<int, RetainedCatalogPage> _retainedPages = [];
    private CancellationTokenSource _listStop = new();
    private ResourceSearchQuery? _listRequest;
    private long _listGeneration, _requestGeneration, _listLayoutVersion;
    private int _nextCatalogPage;
    private bool _listHasMore = true, _autoLoadStalled, _listMediaPaused;
    private string? _listFailure;

    private sealed class RetainedCatalogPage(ResourceSearchQuery query, ResourceSearchResult result)
    {
        internal ResourceSearchQuery Query { get; } = query;
        internal ResourceSearchResult Result { get; set; } = result;
        internal bool Renewed { get; set; }
        internal Task<XsrResult<ResourceSearchResult>>? Renewal { get; set; }
    }

    private void BeginContinuousSearch()
    {
        Cancel(); PauseContinuousList(); DestroyVirtualRows();
        CancelIcons(); ReleaseIcons(); _iconPage = Page; CancelTranslations();
        _listGeneration++;
        _retainedPages.Clear(); _result = null; _nextCatalogPage = 0;
        _listHasMore = true; _autoLoadStalled = false; _listMediaPaused = false; _listFailure = null;
        _filter = _filter with { Text = Draft(_search), GameVersion = Draft(_game), Loader = Draft(_loader), Page = 0, WaitForRefresh = false };
        Clear(_entities["ResourceList"], _listActions);
        _shell.Tree.GetComponent<XsrUiScroll>(_entities["ResourceList"])!.OffsetY = 0;
        _listLayoutVersion = _shell.Renderer.SceneVersion;
        if (_favoriteMode) { ShowFavorites(); return; }

        RequestNextPage();
    }

    private void RequestNextPage()
    {
        if (_disposed || _favoriteMode || _shell.Stage.Navigation.Current != Page || _searching is not null
            || !_listHasMore) return;
        _listFailure = null; _listMediaPaused = false;
        if (!_queries.TryResolve(ResourceCatalogContract.Search, out var route))
        { _listFailure = DesktopResourcesText.Raw("copy.0260b97dada7"); UpdateContinuousStatus(); return; }
        _listRequest = _filter with { Page = _nextCatalogPage, WaitForRefresh = false };
        _requestGeneration = _listGeneration;
        _searching = _queries.QueryAsync<ResourceSearchQuery, ResourceSearchResult>(route, _listRequest,
            cancellationToken: _listStop.Token).AsTask();
        Wake(_searching, _listStop.Token);
        UpdateContinuousStatus();
    }

    private void UpdateContinuousList()
    {
        if (_searching is { IsCompleted: true } completed)
        {
            var captured = _listRequest;
            _searching = null; _listRequest = null;
            bool matching = _requestGeneration == _listGeneration && captured is not null;
            bool success = matching && PendingQuery.Succeeded(completed) && completed.Result.Value?.Page == captured!.Page;
            _sidecarSignals?.ResourceSearchCompleted(success);
            if (success)
            {
                ResourceSearchResult page = completed.Result.Value!;
                bool empty = page.Projects.Count == 0;
                _retainedPages[captured!.Page] = new(captured, page);
                _nextCatalogPage = captured.Page + 1;
                _listHasMore = !empty && (page.HasMore ?? (_nextCatalogPage * ResourceCatalogContract.PageSize < page.Total));
                int previousCount = _result?.Projects.Count ?? 0;
                RebuildRetainedProjects();
                _autoLoadStalled = _result!.Projects.Count == previousCount;
                _listFailure = null;
                ShowResults();
                _listLayoutVersion = _shell.Renderer.SceneVersion;
            }
            else if (matching)
            {
                _listFailure = DesktopResourcesText.Raw("copy.0260b97dada7");
                if (_result is null) ShowFailure(_entities["ResourceList"], _listFailure, RequestNextPage, _listActions);
            }
            UpdateContinuousStatus();
        }
        foreach (var page in _retainedPages.Values)
        {
            if (page.Renewal is not { IsCompleted: true } renewal) continue;
            page.Renewal = null;
            if (!PendingQuery.Succeeded(renewal) || renewal.Result.Value?.Page != page.Query.Page)
            { UpdateContinuousStatus(); continue; }
            var refreshed = renewal.Result.Value!;
            if (page.Query.Page == _nextCatalogPage - 1)
                _listHasMore = refreshed.Projects.Count > 0 && (refreshed.HasMore ?? (_nextCatalogPage * ResourceCatalogContract.PageSize < refreshed.Total));
            page.Result = refreshed;
            RebuildRetainedProjects(); ShowResults();
            _listLayoutVersion = _shell.Renderer.SceneVersion;
            UpdateContinuousStatus();
        }
        RenewRetainedPages();
        RefreshVirtualWindow();
        if (_searching is not null || _favoriteMode || _listFailure is not null || !_listHasMore || _autoLoadStalled) return;
        if (_result is null) { RequestNextPage(); return; }
        // Never use the loading row or pre-append geometry to admit another page.
        if (_shell.Renderer.SceneVersion <= _listLayoutVersion
            || !_shell.Renderer.TryGetScrollSnapshot(_entities["ResourceList"], out var scroll)) return;
        double threshold = Math.Max(160, scroll.ViewportHeight / 2);
        if (scroll.MaximumOffsetY - scroll.OffsetY <= threshold) RequestNextPage();
    }

    private void RenewRetainedPages()
    {
        if (!_queries.TryResolve(ResourceCatalogContract.Search, out var route)) return;
        bool admitted = false;
        foreach (var page in _retainedPages.Values)
        {
            if (page.Renewed || !page.Result.IsStale) continue;
            page.Renewed = true; admitted = true;
            page.Renewal = _queries.QueryAsync<ResourceSearchQuery, ResourceSearchResult>(route,
                page.Query with { WaitForRefresh = true }, cancellationToken: _listStop.Token).AsTask();
            Wake(page.Renewal, _listStop.Token);
        }
        if (admitted) UpdateContinuousStatus();
    }

    private sealed class RetainedProjectFact(ResourceProject project, int order)
    {
        internal ResourceProject Project { get; set; } = project;
        internal int Order { get; } = order;
        internal bool Active { get; set; } = true;
    }

    private sealed class CatalogFactIndex
    {
        private readonly List<RetainedProjectFact> _facts = [];
        private readonly Dictionary<ResourceReference, RetainedProjectFact> _sources = [];
        private readonly Dictionary<string, RetainedProjectFact> _unqualified = new(StringComparer.Ordinal);
        internal ResourceProject[] Projects() => _facts.Where(fact => fact.Active).Select(fact => fact.Project).ToArray();

        internal bool Admit(ResourceProject project)
        {
            var duplicates = project.Sources.Select(source => _sources.GetValueOrDefault(source))
                .OfType<RetainedProjectFact>().Distinct().OrderBy(fact => fact.Order).ToArray();
            RetainedProjectFact? target = duplicates.FirstOrDefault();
            if (project.Sources.Count == 0) _unqualified.TryGetValue(project.Id, out target);
            if (target is null)
            {
                target = new(project, _facts.Count); _facts.Add(target);
                if (project.Sources.Count == 0) _unqualified[project.Id] = target;
            }
            else
            {
                var joined = target.Project.Sources.Concat(project.Sources)
                    .Concat(duplicates.Skip(1).SelectMany(fact => fact.Project.Sources)).Distinct().ToArray();
                target.Project = target.Project with { Sources = joined };
                foreach (var duplicate in duplicates.Skip(1)) { duplicate.Active = false; }
            }
            foreach (var source in target.Project.Sources) _sources[source] = target;
            return true;
        }
    }

    private void RebuildRetainedProjects()
    {
        var index = new CatalogFactIndex();
        // Projection never overwrites raw page membership. A bridge joins the exact-source
        // connected component, so subsequent renewal can still reconstruct unrelated pages.
        foreach (var page in _retainedPages.Values)
            foreach (var project in page.Result.Projects)
                index.Admit(project);
        var projects = index.Projects();
        var last = _retainedPages.Values.LastOrDefault()?.Result;
        string? notice = string.Join(" · ", _retainedPages.Values.Select(page => page.Result.Notice).Where(text => !string.IsNullOrWhiteSpace(text)).Distinct());
        _result = new(projects, last?.Total ?? projects.Length, Math.Max(0, _nextCatalogPage - 1))
        { HasMore = _listHasMore, IsStale = _retainedPages.Values.Any(page => page.Result.IsStale), Notice = notice };

    }

    private void UpdateContinuousStatus()
    {
        _windowDirty = true;
        bool refreshing = _retainedPages.Values.Any(page => page.Renewal is not null);
        string status = _searching is not null ? _result is null ? L(DesktopResourcesText.Raw("copy.da0d228992f9")) : L(DesktopResourcesText.Raw("copy.4d050d55d8aa"))
            : _listFailure is { } failure ? _shell.Renderer.LocalizeText(failure)
            : _result is null ? L(DesktopResourcesText.Raw("copy.d1d044826a45")) : ResourceFormat("Loaded", ("count", _result.Projects.Count.ToString(CultureInfo.InvariantCulture)));
        if (_result is not null && _searching is null && _listFailure is null)
        {
            if (refreshing) status = ResourceFormat("Notice", ("text", status), ("notice", L(DesktopResourcesText.Raw("copy.4ad4d42b0706"))));
            else if (_result.IsStale) status = ResourceFormat("Notice", ("text", status), ("notice", L(DesktopResourcesText.Raw("copy.e5b3a998b012"))));
            if (!string.IsNullOrWhiteSpace(_result.Notice)) status = ResourceFormat("Notice", ("text", status), ("notice", _shell.Renderer.LocalizeText(_result.Notice)));
        }
        // Localize finite status fragments before appending provider notices/counts.
        WorkspaceResource(("initialLoading", Flag(_searching is not null && _result is null)),
            ("appendLoading", Flag(_searching is not null && _result is not null)), ("status", status), ("previousEnabled", Flag(_result?.Projects.Count > 0)),
            ("nextEnabled", Flag(_searching is null && !_favoriteMode && _listHasMore)),
            ("nextCaption", L(_listFailure is null ? DesktopResourcesText.Raw("copy.af90a08fec8e") : DesktopResourcesText.Raw("copy.8924c7887461"))));
    }

    private void ReturnToListTop()
    {
        _shell.Tree.GetComponent<XsrUiScroll>(_entities["ResourceList"])!.OffsetY = 0;
        RefreshVirtualWindow();
        _shell.Tree.MarkDirty(_entities["ResourceList"], XsrUiDirtyKinds.Layout);
    }

    private void PauseContinuousList()
    {
        _listStop.Cancel(); _listStop.Dispose(); _listStop = new();
        _searching = null; _listRequest = null;
        foreach (var page in _retainedPages.Values) page.Renewal = null;
        DeactivateListMedia();
        if (!_disposed) UpdateContinuousStatus();
    }

    private void DisposeContinuousList()
    {
        PauseContinuousList(); _listStop.Dispose();
        // The page tree owns attached views; callers may still inspect the released image
        // placeholders until the shell retires that tree. Detached virtual views have no owner.
        foreach (var row in _catalogRows.Values.Where(row => !row.Attached).ToArray()) RemoveCatalogRow(row);
        _retainedPages.Clear();
    }
}
