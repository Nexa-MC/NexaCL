using System.Globalization;
using Nexa.Services.Resources;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    private static readonly double CatalogRowHeight = DesktopResourcesText.Metric("virtual").Height!.Value;
    private static readonly double CatalogRowSpacing = DesktopResourcesText.Metric("virtual").Spacing;
    private static readonly double CatalogRowPitch = CatalogRowHeight + CatalogRowSpacing;
    private static readonly int CatalogAttachedRows = (int)DesktopResourcesText.Metric("virtual").Width!.Value;
    private static readonly int CatalogOverscan = (int)DesktopResourcesText.Metric("virtual").Padding.Left;
    private readonly Dictionary<string, CatalogRowView> _catalogRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _catalogRowNames = new(StringComparer.Ordinal);
    private readonly List<CatalogRowView> _attachedRows = [];
    private readonly List<ResourceProject> _projectProjection = [];
    private XsrUiEntityId _catalogTopSpacer, _catalogBottomSpacer, _catalogEmpty;
    private int _windowStart = -1, _windowEnd = -1;
    private bool _windowDirty;

    private sealed class CatalogRowView(string key, ResourceProject project, XsrUiEntityId row, XsrUiEntityId icon,
        XsrUiEntityId title, XsrUiEntityId description, XsrUiEntityId metadata)
    {
        internal string Key { get; } = key;
        internal ResourceProject Project { get; set; } = project;
        internal XsrUiEntityId Row { get; } = row;
        internal XsrUiEntityId Icon { get; } = icon;
        internal XsrUiEntityId Title { get; } = title;
        internal XsrUiEntityId Description { get; } = description;
        internal XsrUiEntityId Metadata { get; } = metadata;
        internal XsrUiEntityId Details { get; set; }
        internal XsrUiEntityId Download { get; set; }
        internal string? Language { get; set; }
        internal bool Attached { get; set; }
        internal CancellationTokenSource? Media { get; set; }
    }

    private static string CatalogProjectKey(ResourceProject project) => project.Sources.Count > 0
        ? ((int)project.Sources[0].Provider).ToString(CultureInfo.InvariantCulture) + ":" + project.Sources[0].ProjectId
        : "unqualified:" + project.Id;

    private void ReconcileVirtualRows()
    {
        if (_result is null) return;
        var next = _result.Projects.Where(project => !_hideLibraries || project.Kind != ResourceKind.Mod || !project.IsLibrary)
            .Where(project => !_hideInstalled || project.Kind != ResourceKind.Mod || _context is null
                || !project.Sources.Any(reference => _context.Installed.Any(file => file.Source == reference))).ToArray();
        bool changed = !_projectProjection.Select(CatalogProjectKey).SequenceEqual(next.Select(CatalogProjectKey), StringComparer.Ordinal);
        _projectProjection.Clear(); _projectProjection.AddRange(next);
        var retained = _result.Projects.Select(CatalogProjectKey).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in _catalogRows.Values.Where(row => !retained.Contains(row.Key)).ToArray()) RemoveCatalogRow(removed);
        foreach (var project in _result.Projects)
            if (_catalogRows.TryGetValue(CatalogProjectKey(project), out var row)) UpdateCatalogRow(row, project);
        if (!_catalogTopSpacer.IsAssigned)
        {
            Clear(_entities["ResourceList"], _listActions);
            var parking = _entities["ResourceRowParking"];
            _catalogTopSpacer = AppendResource(parking, "ResourceSpacer.d.nxml", new() { ["key"] = "ResourceListTopSpacer", ["height"] = "0" });
            _catalogBottomSpacer = AppendResource(parking, "ResourceSpacer.d.nxml", new() { ["key"] = "ResourceListBottomSpacer", ["height"] = "0" });
            _catalogEmpty = AppendResource(parking, "ResourceMessage.d.nxml", new() { ["key"] = "ResourceListEmpty", ["message"] = L(DesktopResourcesText.Raw("copy.661ae707311c")) });
            RegisterResourceText(_catalogEmpty, _catalogEmpty, "message");
            _windowDirty = true;
        }
        if (changed) _windowDirty = true;
        RefreshVirtualWindow();
    }

    private void RefreshVirtualWindow()
    {
        if (_result is null || !_catalogTopSpacer.IsAssigned) return;
        if (_shell.Stage.Navigation.Current != Page) { DeactivateListMedia(); return; }
        var list = _entities["ResourceList"];
        double offset = _shell.Tree.GetComponent<XsrUiScroll>(list)!.OffsetY;
        double viewport = DesktopResourcesText.Metric("virtual").MaxHeight!.Value;
        if (_shell.Renderer.TryGetScrollSnapshot(list, out var snapshot)) viewport = Math.Max(1, snapshot.ViewportHeight);
        int start = Math.Clamp((int)Math.Floor(Math.Max(0, offset) / CatalogRowPitch) - CatalogOverscan, 0, _projectProjection.Count);
        int count = Math.Min(CatalogAttachedRows, (int)Math.Ceiling(viewport / CatalogRowPitch) + CatalogOverscan * 2);
        int end = Math.Min(_projectProjection.Count, start + count);
        if (_windowDirty || start != _windowStart || end != _windowEnd)
        {
            var wanted = _projectProjection.Skip(start).Take(end - start).Select(CatalogProjectKey).ToHashSet(StringComparer.Ordinal);
            foreach (var old in _attachedRows)
            {
                if (!wanted.Contains(old.Key)) { DeactivateRowMedia(old); old.Attached = false; }
                _resourceDynamic.Move(old.Row, _entities["ResourceRowParking"]);
            }
            _attachedRows.Clear();
            foreach (var fragment in new[] { _catalogTopSpacer, _catalogBottomSpacer, _catalogEmpty }) _resourceDynamic.Move(fragment, _entities["ResourceRowParking"]);
            if (_projectProjection.Count == 0)
            {
                SetCatalogText(_catalogEmpty, _result.Projects.Count == 0 ? DesktopResourcesText.Raw("copy.f104ec7d2e97") : DesktopResourcesText.Raw("copy.661ae707311c"), localize: true);
                _resourceDynamic.Move(_catalogEmpty, list);
            }
            else
            {
                if (start > 0)
                { ReloadResource(_catalogTopSpacer, ("height", Number(start * CatalogRowPitch - CatalogRowSpacing))); _resourceDynamic.Move(_catalogTopSpacer, list); }
                for (int index = start; index < end; index++)
                {
                    var project = _projectProjection[index]; string key = CatalogProjectKey(project);
                    if (!_catalogRows.TryGetValue(key, out var row))
                    { row = CreateCatalogRow(key, project); _catalogRows.Add(key, row); }
                    UpdateCatalogRow(row, project);
                    row.Attached = true; _resourceDynamic.Move(row.Row, list); _attachedRows.Add(row);
                }
                int remaining = _projectProjection.Count - end;
                if (remaining > 0)
                { ReloadResource(_catalogBottomSpacer, ("height", Number(remaining * CatalogRowPitch - CatalogRowSpacing))); _resourceDynamic.Move(_catalogBottomSpacer, list); }
            }
            _windowStart = start; _windowEnd = end; _windowDirty = false;
            _shell.Tree.MarkDirty(list, XsrUiDirtyKinds.Layout);
        }
        foreach (var row in _attachedRows) ActivateRowMedia(row);
    }

    private CatalogRowView CreateCatalogRow(string key, ResourceProject project)
    {
        string name = ResourceIdentity(project.Id); int suffix = _catalogRows.Count;
        while (_catalogRowNames.TryGetValue(name, out var owner) && owner != key)
            name = ResourceIdentity(project.Id) + "." + (suffix++).ToString(CultureInfo.InvariantCulture);
        _catalogRowNames[name] = key;
        var row = AppendResource(_entities["ResourceRowParking"], "ResourceProject.d.nxml", new()
        {
            ["row"] = "ResourceProject." + name,
            ["body"] = "ResourceProject." + name + ".Body",
            ["title"] = "",
            ["description"] = "",
            ["metadata"] = "",
            ["icon"] = project.Kind switch { ResourceKind.Mod => DesktopResourcesText.Raw("icon-mod"), ResourceKind.Shader => DesktopResourcesText.Raw("icon-shader"), _ => DesktopResourcesText.Raw("icon-package") },
            ["details"] = "ResourceDetails." + name,
            ["download"] = "ResourceQuickDownload." + name
        });
        var view = new CatalogRowView(key, project, row, _entities["ResourceProjectIcon"], _entities["ResourceProjectTitle"],
            _entities["ResourceProjectDescription"], _entities["ResourceProjectMetadata"]);
        RegisterResourceText(view.Title, row, "title"); RegisterResourceText(view.Description, row, "description"); RegisterResourceText(view.Metadata, row, "metadata");
        view.Details = BindResource("ResourceDetails." + name, () =>
        {
            if (!view.Attached || _shell.Stage.Navigation.Current != Page) return;
            _detailProject = view.Project; _detailPage = 0;
            DeactivateListMedia(); _shell.Stage.Navigation.Push(DetailPage); ReadDetail(view.Project.Id);
        }, _listActions);
        view.Download = BindResource("ResourceQuickDownload." + name, () =>
        { if (view.Attached && _shell.Stage.Navigation.Current == Page) _ = DownloadProjectAsync(view.Project); }, _listActions);
        return view;
    }

    private void UpdateCatalogRow(CatalogRowView row, ResourceProject project)
    {
        string language = DesktopResourceText.Language(_store);
        if (row.Language == language && CatalogFactsEqual(row.Project, project)) return;
        DeactivateRowMedia(row); row.Project = project; row.Language = language;
        string count = FormatDownloads(Math.Max(0, ResourceCaptions.DownloadCount(_functionPatches.Runtime, _functionPatches.DownloadCount, project.Downloads)));
        ReloadResource(row.Row, ("title", ProjectTitle(DesktopResourceText.Name(project, _store))), ("description", DesktopResourceText.Description(project, _store)),
            ("metadata", ResourceFormat("ProjectMetadata", ("source", project.SourceLabel), ("author", project.Author), ("downloads", count))));
    }

    private static bool CatalogFactsEqual(ResourceProject left, ResourceProject right) =>
        left.Sources.SequenceEqual(right.Sources) && (left with { Sources = right.Sources }) == right;

    private void SetCatalogText(XsrUiEntityId entity, string text, bool localize = false) => SetResourceText(entity, localize ? _shell.Renderer.LocalizeText(text) : text);

    private void ActivateRowMedia(CatalogRowView row)
    {
        if (_listMediaPaused || row.Media is { IsCancellationRequested: false }) return;
        DeactivateRowMedia(row);
        row.Media = CancellationTokenSource.CreateLinkedTokenSource(_iconStop.Token, _translationStop.Token);
        TrackIcon(row.Icon, row.Project, Page, row.Media.Token);
        Translate(row.Description, row.Project, row.Media.Token);
    }

    private void DeactivateRowMedia(CatalogRowView row)
    {
        row.Media?.Cancel(); row.Media?.Dispose(); row.Media = null;
        _iconDescriptors.RemoveAll(item => item.Entity == row.Icon);
        _icons.RemoveAll(item => item.Entity == row.Icon);
        _translations.RemoveAll(item => item.Entity == row.Description);
        if (_shell.Tree.IsAlive(row.Icon) && _shell.Tree.GetComponent<XsrUiImage>(row.Icon) is { Raster: not null } image)
        { image.Raster = null; _shell.Tree.MarkDirty(row.Icon, XsrUiDirtyKinds.Paint); }
    }

    private void DeactivateListMedia()
    {
        foreach (var row in _attachedRows) DeactivateRowMedia(row);
    }

    private void RemoveCatalogRow(CatalogRowView row)
    {
        DeactivateRowMedia(row); row.Attached = false;
        _attachedRows.Remove(row); _catalogRows.Remove(row.Key);
        foreach (var name in _catalogRowNames.Where(pair => pair.Value == row.Key).Select(pair => pair.Key).ToArray()) _catalogRowNames.Remove(name);
        foreach (var action in new[] { row.Details, row.Download })
        {
            _actions.Remove(action); _listActions.Remove(action);
            foreach (var name in _entities.Where(item => item.Value == action).Select(item => item.Key).ToArray()) _entities.Remove(name);
        }
        if (_shell.Tree.IsAlive(row.Row)) RemoveResource(row.Row);
        _windowDirty = true;
    }

    private void DestroyVirtualRows()
    {
        foreach (var row in _catalogRows.Values.ToArray()) RemoveCatalogRow(row);
        foreach (var spacer in new[] { _catalogTopSpacer, _catalogBottomSpacer, _catalogEmpty })
            if (spacer.IsAssigned && _shell.Tree.IsAlive(spacer)) RemoveResource(spacer);
        _catalogTopSpacer = _catalogBottomSpacer = _catalogEmpty = default;
        _catalogRows.Clear(); _catalogRowNames.Clear(); _attachedRows.Clear(); _projectProjection.Clear();
        _windowStart = _windowEnd = -1; _windowDirty = true;
    }
}
