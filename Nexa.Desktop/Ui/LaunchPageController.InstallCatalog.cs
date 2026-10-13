using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private readonly XsrCommandRouter? _installCatalogCommands;
    private readonly XsrQueryRouter? _installCatalogQueries;
    private readonly XsrCommandRouter? _installRunCommands;
    private Task _installPrefetchTask = Task.CompletedTask;
    private void PrefetchInstallCatalog(string game)
    {
        if (_installCatalogCommands is not null && _installCatalogCommands.TryResolve(InstallCatalogRoutes.Prefetch, out XsrCommandId id))
            _installPrefetchTask = _installCatalogCommands.Dispatch(id, new InstallCatalogPrefetchCommand(game), cancellationToken: _lifetimeCancellation.Token).Completion;
    }
    private static InstallLoader? ParseInstallLoader(string? value) => Enum.TryParse(value?.Replace(" ", "", StringComparison.Ordinal), ignoreCase: true, out InstallLoader loader) ? loader : null;
    private void ResetInstallSelection()
    {
        _editingInstall = false;
        _installEdit = null;
        _installEditRead = null;
        SetInstallEditorLabels(false);
        _installGameChosen = false;
        _selectedInstallVersion = "";
        _selectedInstallBuilds.Clear();
        _selectedInstallAddons.Clear();
        _selectedInstallLoader = LaunchInstallText.Raw("loader.vanilla");
        _activeJavaInstallPage = "JavaMinecraftPage";
        _catalogRequest = "";
        _catalogSearch = "";
        _catalogRevision = -1;
        _shell.Renderer.SetTextInputValue(_javaInstallEntities["JavaInstallVersionInput"], "");
        UpdateJavaInstallSubpageVisibility("JavaMinecraftPage");
    }
    private void ChooseInstallGame(string version)
    {
        if (_editingInstall) return;
        _installGameChosen = true;
        _selectedInstallVersion = version;
        _selectedInstallBuilds.Clear();
        SelectInstallLoader(LaunchInstallText.Raw("loader.vanilla"), string.Empty);
        UpdateJavaInstallSubpageVisibility();
        PrefetchInstallCatalog(version);
        _locateCatalogSelection = true;
        _catalogRevision = -1;
    }
    private Task _installCatalogTask = Task.CompletedTask;
    private string _catalogRequest = "";
    private long _catalogRevision = -1;
    private long _catalogStateRevision = -1;
    private int _catalogFirst = -1, _catalogCount;
    private bool _locateCatalogSelection;
    private string _catalogSearch = "";
    private long _filteredRevision = -1;
    private IReadOnlyList<InstallCatalogVersion> _filteredGames = [];
    private readonly Dictionary<string, InstallCatalogSnapshot> _catalogCache = [];
    private string CurrentCatalogKey => ActiveCatalogLoader is null ? "games" : _selectedInstallVersion + ":" + _activeJavaInstallPage;
    private readonly Dictionary<string, XsrUiEntityId> _catalogHosts = [];
    private readonly Dictionary<XsrUiEntityId, InstallCatalogVersion> _catalogRows = [];
    private readonly Dictionary<string, XsrUiEntityId> _catalogStatus = [];
    private static readonly XsrSemanticId CatalogSelect = XsrSemanticId.Parse("ui.install.catalog.select");
    private static readonly XsrSemanticId CatalogRetry = XsrSemanticId.Parse("ui.install.catalog.retry");
    private readonly Dictionary<string, (XsrUiEntityId Before, XsrUiEntityId After)> _catalogSpacers = [];
    private static readonly double InstallRowHeight = LaunchInstallText.Metric("catalog").Height!.Value;
    private static readonly double InstallRowPitch = InstallRowHeight + LaunchInstallText.Metric("catalog").Spacing;
    private static readonly int InstallOverscan = (int)LaunchInstallText.Metric("catalog").Padding.Left;
    private static readonly int InstallExtraRows = (int)LaunchInstallText.Metric("catalog").Width!.Value;
    private static readonly double InstallCenterInset = LaunchInstallText.Metric("catalog").MinHeight!.Value;
    private InstallLoader? ActiveCatalogLoader => (_activeJavaInstallPage == "JavaOptiFabricPage" ? "OptiFabric" : _activeJavaInstallPage == "JavaFabricApiPage" ? "Fabric API" : _activeJavaInstallPage == "JavaQslPage" ? "QSL" : JavaInstallSubpages.First(p => p.PageKey == _activeJavaInstallPage).Loader) switch
    {
        "Fabric API" => InstallLoader.FabricApi,
        "QSL" => InstallLoader.Qsl,
        "OptiFabric" => InstallLoader.OptiFabric,
        "Forge" => InstallLoader.Forge,
        "Cleanroom" => InstallLoader.Cleanroom,
        "NeoForge" => InstallLoader.NeoForge,
        "Fabric" => InstallLoader.Fabric,
        "Legacy Fabric" => InstallLoader.LegacyFabric,
        "Quilt" => InstallLoader.Quilt,
        "LabyMod" => InstallLoader.LabyMod,
        "OptiFine" => InstallLoader.OptiFine,
        "LiteLoader" => InstallLoader.LiteLoader,
        _ => null,
    };
    private void InitializeInstallCatalog()
    {
        foreach (JavaInstallSubpage page in JavaInstallSubpages)
        {
            _catalogHosts[page.PageKey] = _javaInstallEntities[page.PageKey == "JavaMinecraftPage" ? "JavaMinecraftVersions" : page.PageKey + "Rows"];
            var status = AppendInstall(_javaInstallEntities[page.PageKey + "StatusSlot"], "LaunchInstallCatalogStatus.d.nxml", new()
            { ["key"] = page.PageKey + "Status", ["message"] = InstallCopy("catalog.loading"), ["label"] = InstallCopy("catalog.retry"), ["visible"] = "true" });
            _catalogStatus[page.PageKey] = status;
            var parking = _javaInstallEntities["JavaInstallRowParking"];
            var before = AppendInstall(parking, "LaunchInstallSpacer.d.nxml", new() { ["key"] = "CatalogBefore." + page.PageKey, ["height"] = "0" });
            var after = AppendInstall(parking, "LaunchInstallSpacer.d.nxml", new() { ["key"] = "CatalogAfter." + page.PageKey, ["height"] = "0" });
            _catalogSpacers[page.PageKey] = (before, after);
        }
    }
    private void RequestInstallCatalog(bool force = false)
    {
        if (force) _startupCatalogOffline = false;
        if (_installCatalogCommands is null || !_catalogHosts.ContainsKey(_activeJavaInstallPage)) return;
        InstallLoader? loader = ActiveCatalogLoader;
        string key = CurrentCatalogKey;
        if (!force && key == _catalogRequest) return;
        _catalogRequest = key;
        _locateCatalogSelection = true;
        _catalogRevision = -1;
        _catalogFirst = -1;
        if (!force && _catalogCache.ContainsKey(key)) return;
        if (!force && (_store.ReadAppliedValue(_installCatalogId) as InstallCatalogState)?.Catalogs
            .Any(item => item.Revision > 0 && item.Loader == loader && (loader is null || item.GameVersion == _selectedInstallVersion)) == true) return;
        _catalogCache.Remove(key);
        if (_installCatalogCommands.TryResolve(InstallCatalogRoutes.Read, out XsrCommandId id))
            _installCatalogTask = _installCatalogCommands.Dispatch(id, new InstallCatalogReadCommand(loader is null ? "" : _selectedInstallVersion, loader, force), cancellationToken: _lifetimeCancellation.Token).Completion;
    }
    private bool HandleInstallCatalogIntent(DesktopUiIntentEventArgs e)
    {
        if (_shell.Stage.Navigation.Current != _javaInstallPage) return false;
        if (e.Intent.Command == CatalogRetry)
        {
            RequestInstallCatalog(true);
            return true;
        }
        if (e.Intent.Command != CatalogSelect || !_catalogRows.TryGetValue(e.Intent.Source, out InstallCatalogVersion? version)) return false;
        if (ActiveCatalogLoader is null)
        {
            if (_editingInstall) return true;
            if (_installGameChosen && _selectedInstallVersion == version.Id)
            {
                _installGameChosen = false;
                PrefetchInstallCatalog("");
                _selectedInstallBuilds.Clear();
                SelectInstallLoader(LaunchInstallText.Raw("loader.vanilla"), string.Empty);
                _shell.Renderer.SetTextInputValue(_javaInstallEntities["JavaInstallVersionInput"], "");
                _catalogRevision = -1;
                return true;
            }
            ChooseInstallGame(version.Id);
            _shell.Renderer.SetTextInputValue(_javaInstallEntities["JavaInstallVersionInput"], version.Id);
            SelectInstallLoader(LaunchInstallText.Raw("loader.vanilla"), string.Empty);
        }
        else
        {
            var result = QueryInstallEligibility(ActiveCatalogLoader, [version.Id], version.Id);
            if (result is null || result.Rejection is not null) return true;
            _selectedInstallLoader = result.PrimaryLoader is { } primary
                ? JavaInstallSubpages.First(page => ParseInstallLoader(page.Loader) == primary).Loader! : LaunchInstallText.Raw("loader.vanilla");
            _selectedInstallBuilds.Clear();
            _selectedInstallAddons.Clear();
            foreach (var item in result.Selection)
            {
                _selectedInstallBuilds[item.Loader] = item.Version;
                if (result.Loaders.Any(loader => loader.Loader == item.Loader && loader.IsAddon))
                    _selectedInstallAddons.Add(item.Loader == InstallLoader.FabricApi ? "Fabric API" : item.Loader == InstallLoader.Qsl ? "QSL" : item.Loader.ToString());
            }
            UpdateJavaInstallSubpageVisibility();
        }
        _catalogRevision = -1;
        return true;
    }
    private InstallEligibilityResult? QueryInstallEligibility(InstallLoader? catalog = null, IReadOnlyList<string>? candidates = null, string? toggle = null)
    {
        if (_installCatalogQueries is null || !_installCatalogQueries.TryResolve(InstallEligibilityContract.Query, out XsrQueryId id)) return null;
        var query = new InstallEligibilityQuery(_installGameChosen ? _selectedInstallVersion : "",
            Array.AsReadOnly(_selectedInstallBuilds.Select(pair => new InstallBuildSelection(pair.Key, pair.Value)).ToArray()),
            ParseInstallLoader(_selectedInstallLoader), catalog, candidates, toggle, _installEdit?.Selection);
        var result = _installCatalogQueries.QueryAsync<InstallEligibilityQuery, InstallEligibilityResult>(id, query);
        // This route is a bounded in-memory projection. Never synchronously wait for an incomplete query.
        if (!result.IsCompletedSuccessfully) return null;
        var completed = result.Result;
        return completed.IsSuccess ? completed.Value : null;
    }
    private void ProjectInstallCatalog()
    {
        if (_shell.Stage.Navigation.Current != _javaInstallPage || _installCatalogCommands is null) return;
        XsrUiEntityId inputEntity = _javaInstallEntities["JavaInstallVersionInput"];
        XsrUiTextInput input = _shell.Tree.GetComponent<XsrUiTextInput>(inputEntity)!;
        ProjectInstallInputLabel();
        RequestInstallCatalog();
        var catalogState = _store.ReadAppliedValue(_installCatalogId) as InstallCatalogState;
        if (catalogState is not null && catalogState.Revision != _catalogStateRevision)
        {
            _catalogStateRevision = catalogState.Revision;
            UpdateJavaInstallSubpageVisibility();
            _catalogRevision = -1;
            foreach (string key in _catalogCache.Keys.ToArray())
            {
                var cached = _catalogCache[key];
                if (catalogState.Catalogs.Any(item => item.Loader == cached.Loader && item.GameVersion == cached.GameVersion && item.Revision > cached.Revision))
                    _catalogCache.Remove(key);
            }
        }
        string cacheKey = CurrentCatalogKey;
        InstallCatalogSnapshot? snapshot = _installEdit is { } edit && ActiveCatalogLoader is null
            ? new InstallCatalogSnapshot(1, "", null, [new(edit.GameVersion, LaunchInstallText.Raw("catalog.immutable"))], false)
            : _catalogCache.GetValueOrDefault(cacheKey);
        if (snapshot is null)
        {
            snapshot = (_store.ReadAppliedValue(_installCatalogId) as InstallCatalogState)?.Catalogs
                .FirstOrDefault(item => item.Loader == ActiveCatalogLoader && (item.Loader is null || item.GameVersion == _selectedInstallVersion));
            if (snapshot is null || snapshot.Loader != ActiveCatalogLoader
                || snapshot.Loader is not null && snapshot.GameVersion != _selectedInstallVersion) return;
            if (!snapshot.Loading && snapshot.Error is null)
            {
                if (_catalogCache.Count >= 24) _catalogCache.Clear();
                _catalogCache[cacheKey] = snapshot;
            }
        }
        if (_installEdit is not null && snapshot.Loader is { } selectedLoader
            && _selectedInstallBuilds.TryGetValue(selectedLoader, out string? selectedBuild)
            && !snapshot.Versions.Any(version => version.Id == selectedBuild))
            snapshot = snapshot with { Versions = new[] { new InstallCatalogVersion(selectedBuild, LaunchInstallText.Raw("catalog.installed")) }.Concat(snapshot.Versions).ToArray() };
        if (!_catalogHosts.TryGetValue(_activeJavaInstallPage, out XsrUiEntityId host)) return;
        XsrUiScroll scroll = _shell.Tree.GetComponent<XsrUiScroll>(_javaInstallEntities[_activeJavaInstallPage])!;
        string query = !_installGameChosen && snapshot.Loader is null ? input.ReadDraft().Trim() : "";
        if (query != _catalogSearch)
        {
            _catalogSearch = query; _filteredRevision = -1; _catalogRevision = -1;
            if (snapshot.Loader is null) scroll.OffsetY = 0;
        }
        if (snapshot.Loader is null && query.Length > 0)
        {
            if (_filteredRevision != snapshot.Revision)
            {
                _filteredGames = snapshot.Versions.Where(version => version.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
                _filteredRevision = snapshot.Revision;
            }
            snapshot = snapshot with { Versions = _filteredGames };
        }
        if (_locateCatalogSelection && !snapshot.Loading)
        {
            _locateCatalogSelection = false;
            string? selected = snapshot.Loader is null ? _installGameChosen ? _selectedInstallVersion : null
                : _selectedInstallBuilds.GetValueOrDefault(snapshot.Loader.Value);
            int selectedIndex = -1;
            if (selected is not null)
                for (int index = 0; index < snapshot.Versions.Count; index++)
                    if (snapshot.Versions[index].Id == selected) { selectedIndex = index; break; }
            if (selectedIndex >= 0)
            {
                _shell.Renderer.FinishScrollInertia(_javaInstallEntities[_activeJavaInstallPage]);
                scroll.OffsetY = Math.Max(0, selectedIndex * InstallRowPitch - Math.Max(0, _shell.Renderer.Viewport.Height - InstallCenterInset) / 2);
                _catalogRevision = -1;
            }
        }
        int first = Math.Clamp((int)(Math.Max(0, scroll.OffsetY - InstallRowHeight) / InstallRowPitch) - InstallOverscan, 0, Math.Max(0, snapshot.Versions.Count - 1));
        int count = Math.Min(snapshot.Versions.Count - first, (int)Math.Ceiling(_shell.Renderer.Viewport.Height / InstallRowPitch) + InstallExtraRows);
        if (snapshot.Revision == _catalogRevision && first == _catalogFirst && count == _catalogCount) return;
        _catalogRevision = snapshot.Revision; _catalogFirst = first; _catalogCount = count;
        // A fixed-height window bounds ECS/native controls independently of catalog length.
        // Keep overlapping rows alive, preserving focus and avoiding repeated entry animations.
        var window = snapshot.Versions.Skip(first).Take(count).ToArray();
        var wanted = window.Select(version => version.Id).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, XsrUiEntityId> retained = [];
        foreach (var child in _shell.Tree.Children(host).ToArray())
        {
            if (!_catalogRows.TryGetValue(child, out var old)) continue;
            if (wanted.Contains(old.Id)) retained[old.Id] = child;
            else { _catalogRows.Remove(child); RemoveInstall(child); }
        }
        var spacers = _catalogSpacers[_activeJavaInstallPage];
        var parking = _javaInstallEntities["JavaInstallRowParking"];
        List<XsrUiEntityId> order = [];
        ReloadInstall(spacers.Before, ("height", InstallNumber(first * InstallRowPitch)));
        InstallDynamic.Move(spacers.Before, first > 0 ? host : parking);
        if (first > 0) order.Add(spacers.Before);
        var eligibility = QueryInstallEligibility(snapshot.Loader, window.Select(version => version.Id).ToArray());
        string message = snapshot.IsStale ? snapshot.Error ?? InstallCopy("catalog.stale")
            : snapshot.Loading ? InstallCopy(_startupCatalogOffline ? "catalog.offline" : "catalog.loading")
                : snapshot.Unsupported ?? (snapshot.Error is not null ? InstallCopy("catalog.failed") : snapshot.Versions.Count == 0
                    ? InstallCopy(query.Length > 0 ? "catalog.unmatched" : "catalog.empty")
                    : snapshot.Versions[0].Warning is not null ? InstallCopy("catalog.partial") : "");
        if (message.Length == 0 && snapshot.Loader is not null && snapshot.Versions.Count > 0)
            message = eligibility?.Builds.Values.Select(item => item.Notice).FirstOrDefault(notice => notice is not null) ?? "";
        ProjectInstallStatus(_activeJavaInstallPage, message);
        foreach (var version in window)
        {
            string key = (snapshot.Loader is null ? "game:" : "loader:") + InstallIdentity(version.Id);
            bool selected = _installGameChosen && (snapshot.Loader is null ? version.Id == _selectedInstallVersion : _selectedInstallBuilds.GetValueOrDefault(snapshot.Loader.Value) == version.Id);
            string? conflict = snapshot.Loader is not null ? eligibility?.Builds.GetValueOrDefault(version.Id)?.Conflict : null;
            bool immutable = _editingInstall && snapshot.Loader is null;
            string detail = immutable ? InstallCopy("catalog.immutable") : conflict ?? (snapshot.Loader is not null ? version.Detail : InstallCopy(version.Stable ? "catalog.release" : "catalog.preview"));
            string label = InstallFormat(immutable ? "CatalogImmutable" : selected ? "CatalogDeselect" : "CatalogSelect", ("version", version.Id));
            string rowStyle = LaunchInstallText.Raw(selected ? "style.row-selected" : "style.row");
            string checkStyle = LaunchInstallText.Raw(selected ? "style.check-selected" : "style.check");
            if (!retained.TryGetValue(version.Id, out var row))
                row = AppendInstall(host, "LaunchInstallCatalogRow.d.nxml", new()
                {
                    ["row"] = "CatalogRow:" + key,
                    ["check"] = "CatalogCheck:" + key,
                    ["name"] = "CatalogName:" + key,
                    ["detailKey"] = "CatalogDetail:" + key,
                    ["version"] = version.Id,
                    ["detail"] = detail,
                    ["label"] = label,
                    ["selected"] = InstallFlag(selected),
                    ["enabled"] = InstallFlag(!immutable && (selected || conflict is null)),
                    ["rowStyle"] = rowStyle,
                    ["checkStyle"] = checkStyle,
                });
            else ReloadInstall(row, ("version", version.Id), ("detail", detail), ("label", label),
                ("selected", InstallFlag(selected)), ("enabled", InstallFlag(!immutable && (selected || conflict is null))),
                ("rowStyle", rowStyle), ("checkStyle", checkStyle));
            _catalogRows[row] = version;
            order.Add(row);
        }
        int remaining = snapshot.Versions.Count - first - count;
        ReloadInstall(spacers.After, ("height", InstallNumber(remaining * InstallRowPitch)));
        InstallDynamic.Move(spacers.After, remaining > 0 ? host : parking);
        if (remaining > 0) order.Add(spacers.After);
        InstallDynamic.Reorder(host, order);
    }
}
