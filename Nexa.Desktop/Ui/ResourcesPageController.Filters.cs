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
        DesktopElementVisibility.Set(_shell.Tree, _loader, _filter.Kind is ResourceKind.Mod or ResourceKind.Modpack or ResourceKind.Shader);
        UpdateFilterCaptions();
        CloseFilter(false);
    }

    private void UpdateFilterCaptions()
    {
        Caption(_game, _filter.GameVersion.Length == 0 ? "游戏版本" : _filter.GameVersion);
        Caption(_loader, _filter.Loader.Length == 0 ? "加载器" : _filter.Loader);
        Caption(_entities["ResourceProvider"], _filter.Provider?.ToString() ?? "全部来源");
        // The category label is declared alongside its dual-provider identity in NXML.
        string tag = "全部分类";
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
        var text = _shell.Tree.GetComponent<XsrUiText>(entity)!;
        if (text.Content == caption) return;
        text.Content = caption;
        _shell.Tree.GetComponent<XsrUiSemantic>(entity)!.Label = caption;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
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
        DesktopElementVisibility.Set(_shell.Tree, _entities["ResourceOverlaySlot"], true);
        _openFilterKind = kind;
        _filterAnchor = _entities["Resource" + kind];
        _filterOverlay = _resourceDynamic.Append(_entities["ResourceOverlaySlot"], DesktopMarkup.Dynamic("ResourceFilterDropdown.d.nxml",
            new Dictionary<string, string> { ["height"] = Math.Min(340, Math.Max(120, _shell.Renderer.Viewport.Height - 220)).ToString(System.Globalization.CultureInfo.InvariantCulture), ["searchable"] = kind == "Game" ? "true" : "false" }));
        var names = new Dictionary<string, XsrUiEntityId>(StringComparer.Ordinal);
        _shell.Tree.Walk(_filterOverlay, entity => { names[_shell.Tree.Name(entity)] = entity; return true; });
        _filterCard = names["ResourceFilterCard"];
        _filterOptions = names["ResourceFilterOptions"]; _filterSearch = names["ResourceFilterSearch"];
        _shell.Tree.SetComponent(_filterOverlay, new XsrUiOverlayLayer(isModal: true));
        _shell.Tree.SetComponent(_filterOverlay, new XsrUiDismissBinding(Action));
        _shell.Tree.SetComponent(names["ResourceFilterCard"], new XsrUiAnchoredOverlay(_filterAnchor));
        _shell.Tree.SetComponent(_filterOptions, new XsrUiScrollGesture());
        _shell.Tree.GetComponent<XsrUiScroll>(_filterOptions)!.ShowsVerticalIndicator = true;
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
            var root = _resourceDynamic.Append(_filterOptions, DesktopMarkup.Dynamic(template));
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
                _shell.Tree.SetComponent(_status, new XsrUiText("无法读取游戏版本，请稍后重试。"));
        }
        if (_openFilterKind == "Game") RefreshGameOptions();
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
        _resourceDynamic.Clear(_filterOptions);
        var versions = (games?.Versions.Select(item => item.Id) ?? Enumerable.Empty<string>())
            .Prepend(_filter.GameVersion).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal);
        Add("", "全部版本");
        foreach (string version in versions.Where(value => draft.Length == 0 || value.Contains(draft, StringComparison.OrdinalIgnoreCase))) Add(version, version);
        FitFilterHeight(searchable: true);
        void Add(string value, string title)
        {
            var row = _resourceDynamic.Append(_filterOptions, DesktopMarkup.Dynamic("ResourceFilterOption.d.nxml",
                new Dictionary<string, string> { ["key"] = "ResourceFilterOption." + value, ["title"] = title }));
            RegisterFilterOptions(row);
        }
    }

    private void FitFilterHeight(bool searchable)
    {
        int count = _filterActions.Count(entity => _shell.Tree.Name(entity).StartsWith("ResourceFilterOption.", StringComparison.Ordinal));
        E(_filterCard).Height = Math.Min(Math.Max(80, _shell.Renderer.Viewport.Height - 220),
            Math.Min(340, 16 + count * 34 + (searchable ? 40 : 0)));
        _shell.Tree.GetComponent<XsrUiScroll>(_filterOptions)!.ShowsVerticalIndicator = count * 34 > E(_filterCard).Height - 16 - (searchable ? 40 : 0);
        _shell.Tree.MarkDirty(_filterCard, XsrUiDirtyKinds.Layout);
    }

    private void CloseFilter(bool restoreFocus)
    {
        if (!_filterOverlay.IsAssigned) return;
        foreach (var entity in _filterActions) _actions.Remove(entity);
        _filterActions.Clear();
        DesktopElementVisibility.Set(_shell.Tree, _entities["ResourceOverlaySlot"], false);
        _resourceDynamic.Remove(_filterOverlay); _filterOverlay = default; _openFilterKind = null;
        if (restoreFocus && _shell.Tree.IsAlive(_filterAnchor)) _shell.Renderer.Focus(_filterAnchor, showIndicator: false);
    }
}
