using Nexa.Services.Minecraft.Install;
using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    private readonly NxmlDynamicContent _resourceDynamic;
    private XsrCommandRouter? _resourceCatalogCommands;
    private Task<XsrResult>? _resourceGamesReading;
    private XsrUiEntityId _filterOverlay, _filterAnchor, _filterOptions, _filterSearch, _filterCard;
    private readonly List<XsrUiEntityId> _filterActions = [];
    private string? _openFilterKind;
    private string _gameFilterDraft = "";
    private long _gameFilterRevision = -1;

    private void ResetCategoryFilters()
    {
        if (!_entities.ContainsKey("ResourceTag")) return;
        if (_filter.Kind is not (ResourceKind.Mod or ResourceKind.Modpack or ResourceKind.Shader))
            _filter = _filter with { Loader = "" };
        ReloadResource(_filters, ("loaderVisible", Flag(_filter.Kind is ResourceKind.Mod or ResourceKind.Modpack or ResourceKind.Shader)));
        UpdateFilterCaptions();
        CloseFilter(false);
    }

    private void UpdateFilterCaptions()
    {
        Caption(_game, _filter.GameVersion.Length == 0 ? DesktopResourcesText.Raw("copy.8be216be7a84") : _filter.GameVersion);
        Caption(_loader, _filter.Loader.Length == 0 ? DesktopResourcesText.Raw("copy.7a2bd8b6cb0e") : _filter.Loader);
        Caption(_entities["ResourceProvider"], _filter.Provider?.ToString() ?? DesktopResourcesText.Raw("copy.9f357adb6100"));
        // The category label is declared alongside its dual-provider identity in NXML.
        string tag = DesktopResourcesText.Raw("copy.a1054434857f");
        void FindTag(NxmlIrNode node)
        {
            if (node.Key == "ResourceFilterOption." + _filter.Tag) tag = node.Content ?? tag;
            foreach (var child in node.Children) FindTag(child);
        }
        FindTag(DesktopMarkup.Dynamic(TagTemplate()).Root);
        Caption(_entities["ResourceTag"], tag);
    }

    private void Caption(XsrUiEntityId entity, string caption)
    {
        string field = entity == _game ? "game" : entity == _loader ? "loader"
            : entity == _entities["ResourceProvider"] ? "provider" : "tag";
        ReloadResource(_filters, (field, _shell.Renderer.LocalizeText(caption)));
    }

    private string TagTemplate() => "ResourceFilterTag" + (_filter.Kind switch
    {
        ResourceKind.Mod => "Mod",
        ResourceKind.Modpack => "Modpack",
        ResourceKind.ResourcePack => "ResourcePack",
        ResourceKind.Shader => "Shader",
        _ => "Other",
    }) + ".d.nxml";

    private void OpenFilter(string kind)
    {
        bool toggle = _openFilterKind == kind;
        CloseFilter(false);
        if (toggle) return;
        WorkspaceResource(("overlayVisible", "true"));
        _openFilterKind = kind;
        _filterAnchor = _entities["Resource" + kind];
        var metric = DesktopResourcesText.Metric("overlay");
        _filterOverlay = AppendResource(_entities["ResourceOverlaySlot"], "ResourceFilterDropdown.d.nxml", new()
        { ["height"] = Number(Math.Min(metric.MaxHeight!.Value, Math.Max(metric.Height!.Value, _shell.Renderer.Viewport.Height - metric.Margin.Left))),
          ["searchable"] = Flag(kind == "Game"), ["anchor"] = "Resource" + kind, ["indicator"] = "true" });
        var names = new Dictionary<string, XsrUiEntityId>(StringComparer.Ordinal);
        _shell.Tree.Walk(_filterOverlay, entity => { names[_shell.Tree.Name(entity)] = entity; return true; });
        _filterCard = names["ResourceFilterCard"];
        _filterOptions = names["ResourceFilterOptions"]; _filterSearch = names["ResourceFilterSearch"];
        _actions[_filterOverlay] = () => CloseFilter(true);
        _actions[names["ResourceFilterDismiss"]] = () => CloseFilter(true);
        _filterActions.Add(_filterOverlay); _filterActions.Add(names["ResourceFilterDismiss"]);
        if (kind == "Game")
        {
            _gameFilterDraft = ""; _gameFilterRevision = -1;
            if (_resourceCatalogCommands?.TryResolve(InstallCatalogRoutes.Read, out var route) == true)
            {
                _resourceGamesReading = _resourceCatalogCommands.Dispatch(route, new InstallCatalogReadCommand(), cancellationToken: _downloadsStop.Token).Completion;
                Wake(_resourceGamesReading, _downloadsStop.Token);
            }
            RefreshGameOptions();
            _shell.Renderer.Focus(_filterSearch, showIndicator: false);
        }
        else
        {
            string template = kind switch { "Tag" => TagTemplate(), "Loader" when _filter.Kind == ResourceKind.Shader => "ResourceFilterShaderLoader.d.nxml", _ => "ResourceFilter" + kind + ".d.nxml" };
            var root = AppendResource(_filterOptions, template, new());
            RegisterFilterOptions(root);
            FitFilterHeight(searchable: false);
            var first = _filterActions.FirstOrDefault(entity => _shell.Tree.Name(entity).StartsWith("ResourceFilterOption.", StringComparison.Ordinal));
            if (first.IsAssigned) _shell.Renderer.Focus(first, showIndicator: false);
        }
    }

    private void RegisterFilterOptions(XsrUiEntityId root)
    {
        _shell.Tree.Walk(root, entity =>
        {
            string name = _shell.Tree.Name(entity);
            if (!name.StartsWith("ResourceFilterOption.", StringComparison.Ordinal)) return true;
            string value = name["ResourceFilterOption.".Length..];
            _actions[entity] = () => SelectFilter(value);
            _filterActions.Add(entity);
            return true;
        });
    }

    private void SelectFilter(string value)
    {
        _filter = _openFilterKind switch
        {
            "Game" => _filter with { GameVersion = value },
            "Loader" => _filter with { Loader = value },
            "Tag" => _filter with { Tag = value },
            "Provider" => _filter with { Provider = value switch { "modrinth" => ResourceProvider.Modrinth, "curseforge" => ResourceProvider.CurseForge, _ => null } },
            _ => _filter,
        };
        CloseFilter(true); UpdateFilterCaptions(); Search();
    }

    private void UpdateFilter()
    {
        if (_resourceGamesReading is { IsCompleted: true } done)
        {
            _resourceGamesReading = null;
            if (!done.IsCompletedSuccessfully || !done.Result.IsSuccess)
                WorkspaceResource(("status", L(DesktopResourcesText.Raw("copy.088ef6d8a832"))));
        }
        if (_openFilterKind == "Game") RefreshGameOptions();
        if (_filterOverlay.IsAssigned) FitFilterHeight(searchable: _openFilterKind == "Game");
    }

    private void RefreshGameOptions()
    {
        InstallCatalogSnapshot? games = _store.TryResolve(InstallCatalogStateContract.StateKey, out var key)
            ? _store.Read<InstallCatalogState>(key).Value?.Catalogs.FirstOrDefault(item => item.Loader is null) : null;
        string draft = _shell.Tree.GetComponent<XsrUiTextInput>(_filterSearch)!.ReadDraft().Trim();
        long revision = games?.Revision ?? 0;
        if (_gameFilterRevision == revision && draft == _gameFilterDraft) return;
        _gameFilterRevision = revision; _gameFilterDraft = draft;
        foreach (var entity in _filterActions.Where(entity => _shell.Tree.Name(entity).StartsWith("ResourceFilterOption.", StringComparison.Ordinal)).ToArray())
        { _actions.Remove(entity); _filterActions.Remove(entity); }
        Clear(_filterOptions, []);
        var versions = (games?.Versions.Select(item => item.Id) ?? Enumerable.Empty<string>())
            .Prepend(_filter.GameVersion).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal);
        Add("", DesktopResourcesText.Raw("copy.25efb3d5b616"));
        foreach (string version in versions.Where(value => draft.Length == 0 || value.Contains(draft, StringComparison.OrdinalIgnoreCase))) Add(version, version);
        FitFilterHeight(searchable: true);
        void Add(string value, string title)
        {
            var row = AppendResource(_filterOptions, "ResourceFilterOption.d.nxml", new()
            { ["key"] = "ResourceFilterOption." + (value.Length == 0 ? "" : ResourceIdentity(value)), ["title"] = title });
            _actions[row] = () => SelectFilter(value);
            _filterActions.Add(row);
        }
    }

    private void FitFilterHeight(bool searchable)
    {
        int count = _filterActions.Count(entity => _shell.Tree.Name(entity).StartsWith("ResourceFilterOption.", StringComparison.Ordinal));
        var metric = DesktopResourcesText.Metric("overlay");
        double height = Math.Min(Math.Max(metric.MinHeight!.Value, _shell.Renderer.Viewport.Height - metric.Margin.Left),
            Math.Min(metric.MaxHeight!.Value, metric.Padding.Left + count * metric.Width!.Value + (searchable ? metric.Spacing : 0)));
        ReloadResource(_filterOverlay, ("height", Number(height)),
            ("indicator", Flag(count * metric.Width!.Value > height - metric.Padding.Left - (searchable ? metric.Spacing : 0))));
    }

    private void CloseFilter(bool restoreFocus)
    {
        if (!_filterOverlay.IsAssigned) return;
        foreach (var entity in _filterActions) _actions.Remove(entity);
        _filterActions.Clear();
        WorkspaceResource(("overlayVisible", "false"));
        RemoveResource(_filterOverlay); _filterOverlay = default; _openFilterKind = null;
        if (restoreFocus && _shell.Tree.IsAlive(_filterAnchor)) _shell.Renderer.Focus(_filterAnchor, showIndicator: false);
    }
}
