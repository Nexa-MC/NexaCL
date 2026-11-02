using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ManagementAction = XsrSemanticId.Parse("ui.settings.management.action");
    private InstanceManagementSnapshot? _management;
    private Task<XsrResult<InstanceManagementSnapshot>>? _managementRead;
    private CancellationTokenSource? _managementStop;
    private bool _managementLoaded;
    private long _contentScopeGeneration;
    private Guid _contentRemovalDialog;
    private void RetireContentScope()
    {
        Interlocked.Increment(ref _contentScopeGeneration); CancelModRemovalPreview();
        if (_contentRemovalDialog != Guid.Empty) _feedback.DismissDialog(_contentRemovalDialog);
        _contentRemovalDialog = default;
    }
    private string? _managementError;
    private readonly Dictionary<XsrUiEntityId, Action> _managementActions = [];
    private readonly List<XsrUiEntityId> _contentActions = [];
    private Task<XsrResult>? _managementWrite;
    private string? _managementWriteInstance;
    private XsrUiEntityId _contentList;
    private InstanceContentSnapshot? _contentSnapshot;
    private int _contentWindowStart = -1, _contentWindowCount;
    private XsrUiVirtualLayoutMetrics _contentVirtualMetrics;

    private void RegisterContentVirtualMetrics()
    {
        bool waterfall = _selected == "screenshots";
        var metric = ContentMetric(waterfall ? "gallery" : "files");
        double height = waterfall ? metric.Height!.Value : (_selected == "resourcepacks" ? metric.Width!.Value : metric.Height!.Value) - metric.Spacing;
        _shell.Renderer.RegisterVirtualLayout(_contentList, new(waterfall ? metric.Width!.Value : metric.MaxWidth!.Value, height,
            metric.Spacing, RealizationLimit: (int)metric.MaxHeight!.Value, SupportsThemeMetrics: true,
            Kind: waterfall ? XsrUiVirtualLayoutKind.Waterfall : XsrUiVirtualLayoutKind.List));
        _shell.Renderer.TryGetVirtualLayoutMetrics(_contentList, out _contentVirtualMetrics);
    }

    private bool ReadContentVirtualMetrics()
    {
        if (!_shell.Renderer.TryGetVirtualLayoutMetrics(_contentList, out var next)) return false;
        var previous = _contentVirtualMetrics;
        bool changed = previous.ItemHeight != next.ItemHeight || previous.ItemWidth != next.ItemWidth
            || previous.Spacing != next.Spacing || previous.Columns != next.Columns
            || previous.HasItemHeightOverride != next.HasItemHeightOverride || previous.HasColumnsOverride != next.HasColumnsOverride;
        _contentVirtualMetrics = next;
        if (!changed) return false;
        if (next.Kind == XsrUiVirtualLayoutKind.List && previous.ItemHeight > 0)
        {
            var metric = ContentMetric("files"); var scroll = _shell.Tree.GetComponent<XsrUiScroll>(_sections)!;
            double header = metric.Padding.Left;
            if (scroll.OffsetY > header)
                scroll.OffsetY = header + (scroll.OffsetY - header) / (previous.ItemHeight + previous.Spacing) * (next.ItemHeight + next.Spacing);
        }
        _contentWindowStart = -1;
        return true;
    }

    private static string ContentRowMargin(double spacing)
    {
        var margin = ContentMetric("files").Margin;
        return string.Join(',', new[] { margin.Left, margin.Top, margin.Right, spacing }.Select(ContentNumber));
    }
    private int _trashPage;
    private XsrUiEntityId _contentSearch;
    private XsrUiEntityId _contentCount;
    private string _contentFilter = "";
    private InstanceContentEntry? _contentDetail;
    private double _contentReturnOffset;
    private int _contentColumns;
    internal Action<string>? OpenManagementDirectory { get; set; }
    internal Action? ManagementChanged { get; set; }
    private IReadOnlyList<SettingsCatalogPage> ManagementPages => _management is { } snapshot
        ? snapshot.Pages.Where(page => InstanceDevelopmentMode || !IsDevelopmentPage(page.Id)).Select(page => new SettingsCatalogPage(page.Id, page.Label)).ToArray()
        : [new("overview", ContentTerm("copy.a33db5730556")), new("game", ContentTerm("copy.57444f5c18ae"))];

    private void CancelManagementRead()
    {
        RetireContentScope();
        CloseScreenshotPreview(); CancelScreenshotThumbnails();
        CancelOnlineList(); CancelServers(); CancelExport(); CancelModpackUpdate(); CancelFileWorkspace(); CancelWorldHealth(); CancelWorldDetails(); CancelInstanceDiagnostics();
        CancelAdvancedLaunchDiagnostics();
        ObserveRetiredInstanceToolRead(_managementRead);
        _managementStop?.Cancel(); _managementStop?.Dispose(); _managementStop = null;
        _managementRead = null; _managementLoaded = false;
    }

    private void ResetManagement()
    {
        if (_instanceDirectory is null) return;
        _contentDetail = null; _contentFilter = ""; _modCategory = "all";
        CancelManagementRead(); _management = null; _managementError = null;
        _selected = "overview"; _scrollPositions.Clear(); _recoverySnapshotPage = 0; _recoveryChangesPage = 0;
        ResetContentGraph();
        if (_catalog is not null) RebuildManagementNavigation();
    }

    private void UpdateManagement()
    {
        if (_instanceDirectory is null || _instance is null) return;
        UpdateScreenshotPreview();
        UpdateScreenshotThumbnails();
        UpdateWorldPlatformAction();
        UpdateFileWorkspace();
        UpdateInstanceDiagnostics();
        UpdateAdvancedLaunchDiagnostics();
        if (_contentSearch.IsAssigned && _shell.Tree.IsAlive(_contentSearch)
            && _shell.Tree.GetComponent<XsrUiTextInput>(_contentSearch) is { } search
            && search.ReadDraft() != _contentFilter)
        {
            _contentFilter = search.ReadDraft();
            ApplyContentFilter();
            _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY = 0;
            UpdateContentWindow();
        }
        if (_managementWrite is { IsCompleted: true } writing)
        {
            _managementWrite = null;
            if (_managementWriteInstance == _instance)
            {
                if (!PendingQuery.Succeeded(writing))
                    _feedback.Error(writing.IsCompletedSuccessfully ? writing.Result.Error?.Message ?? ContentTerm("copy.488e471d233b") : ContentTerm("copy.488e471d233b"));
                else { _contentDetail = null; ManagementChanged?.Invoke(); }
                CancelManagementRead();
            }
        }
        if (!_managementLoaded && _managementRead is null && _queries.TryResolve(InstanceManagementContract.Query, out var route))
        {
            _managementStop = new();
            _managementRead = _queries.QueryAsync<InstanceManagementQuery, InstanceManagementSnapshot>(route,
                new(_instance)
                {
                    IncludeRecoveryStorage = _selected == "recovery",
                    IncludeTrash = _selected == "trash",
                    IncludeContentGraph = _selected == "contentgraph"
                }, cancellationToken: _managementStop.Token).AsTask();
            WakeOnPlatformCompletion(_managementRead);
        }
        if (_managementRead is not { IsCompleted: true } reading) return;
        _managementRead = null; _managementLoaded = true;
        _managementStop?.Dispose(); _managementStop = null;
        if (PendingQuery.Succeeded(reading)
            && reading.Result.Value!.InstanceDirectory == _instance)
        {
            _management = reading.Result.Value; _managementError = null;
            if (_contentDetail is { } detail)
                _contentDetail = _management!.Contents.FirstOrDefault(page => page.PageId == _selected)?.Entries.FirstOrDefault(item => item.Name == detail.Name);
            if (!ManagementPages.Any(page => page.Id == _selected)) _selected = "overview";
            RebuildManagementNavigation();
        }
        else _managementError = ContentTerm("copy.ef9445a3331c");
        BuildSections(); UpdateEditors();
    }

    private void RebuildManagementNavigation()
    {
        if (_navigation.Count == Pages.Count && _navigation.Values.SequenceEqual(Pages.Select(page => page.Id))
            && _navigation.All(pair => _shell.Tree.GetComponent<XsrUiText>(pair.Key)?.Content == Pages.First(page => page.Id == pair.Value).Label))
        {
            _sections = _pages[_selected];
            int selectedIndex = Pages.ToList().FindIndex(page => page.Id == _selected);
            if (_shell.Tree.GetComponent<XsrUiPager>(_pager)!.PageIndex != selectedIndex)
                _shell.Renderer.RebasePagerPage(_pager, selectedIndex);
            UpdateNavigation(); return;
        }
        foreach (var page in _pages.Keys.Where(id => !Pages.Any(item => item.Id == id)).ToArray())
        { _dynamic.Remove(_pages[page]); _pages.Remove(page); }
        foreach (var entity in _navigation.Keys) _dynamic.Remove(entity);
        _navigation.Clear();
        BuildNavigation();
        _shell.Renderer.RebasePagerPage(_pager, Pages.ToList().FindIndex(page => page.Id == _selected));
    }

    private void RefreshManagement()
    {
        if (_managementWrite is not null) return;
        CancelOnlineContent(); CancelManagementRead(); _managementError = null;
        _onlineListInstance = _instance; _onlineListPage = _selected; _onlineListRefresh = true;
    }

    private void OpenContentDirectory(string directory)
    {
        try { OpenManagementDirectory?.Invoke(directory); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { _feedback.Error(ContentTerm("copy.e4614fb4bc41")); }
    }

    private void ToggleMod(InstanceContentEntry entry)
    {
        if (_managementWrite is not null || _management is null || entry.Enabled is not { } enabled || entry.Size is not { } size
            || !_commands.TryResolve(InstanceManagementContract.SetModEnabled, out var route)) return;
        _managementWriteInstance = _management.InstanceDirectory;
        _managementWrite = _commands.Dispatch(route, new InstanceModEnabledCommand(_managementWriteInstance, entry.Name, !enabled, size, entry.ModifiedUtcTicks)).Completion;
        WakeOnPlatformCompletion(_managementWrite);
    }

    private void BuildManagementSection()
    {
        _contentFileViews.Clear(); _contentBefore = _contentAfter = _contentMiddle = _contentEmpty = default; _contentPinnedName = null;
        _contentWorkspaceInstance = _instance; _contentWorkspacePage = _selected;
        if (_contentDetail is { } detail) { BuildContentDetail(detail); return; }
        _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY = _scrollPositions.GetValueOrDefault(_selected);
        if (_management is not { } snapshot)
        {
            var loading = AppendContent(_sections, "SettingsContentLoading.d.nxml", new() { ["loading"] = ContentFlag(_managementError is null), ["enabled"] = ContentFlag(_managementWrite is null), ["message"] = _managementError ?? ContentTerm("loading") });
            BindContentAction(ContentNodes(loading), ContentKey("refresh"), RefreshManagement); return;
        }
        var page = snapshot.Pages.First(item => item.Id == _selected);
        if (page.Directory is { } directory)
        {
            _contentSnapshot = snapshot.Contents.FirstOrDefault(item => item.PageId == _selected);
            _contentWorkspace = AppendContent(_sections, "SettingsContentManagement.d.nxml", new()
            {
                ["enabled"] = ContentFlag(_managementWrite is null),
                ["directory"] = directory,
                ["hasCategories"] = ContentFlag(_selected == "mods"),
                ["hasOnlineStatus"] = ContentFlag(_selected is "mods" or "resourcepacks" or "shaderpacks"),
                ["hasUpdates"] = ContentFlag(_selected is "mods" or "resourcepacks" or "shaderpacks" && _resourceCommands?.TryResolve(Nexa.Services.Resources.ResourceCatalogContract.UpdateContentBatch, out _) == true),
                ["screenshots"] = ContentFlag(_selected == "screenshots"),
                ["timelineLabel"] = ContentTerm(_screenshotTimeline ? "gallery" : "timeline"),
                ["timelineVisible"] = ContentFlag(!_screenshotTimeline),
                ["galleryVisible"] = ContentFlag(_screenshotTimeline),
                ["count"] = "",
                ["error"] = "",
                ["hasError"] = "false"
            });
            var nodes = ContentNodes(_contentWorkspace);
            _contentSearch = nodes["ManagementContentSearch"]; _contentCount = nodes["ManagementContentCount"];
            _contentList = nodes["ManagementContentList"]; _contentParking = nodes["ContentManagement.Parking"];
            RegisterContentVirtualMetrics();
            _shell.Renderer.SetTextInputValue(_contentSearch, _contentFilter);
            BindContentAction(nodes, ContentKey("folder"), () => OpenContentDirectory(directory));
            BindContentAction(nodes, ContentKey("refresh"), RefreshManagement);
            void SwitchScreenshotLayout()
            {
                bool focused = _shell.Renderer.Focused == nodes[ContentKey("timelineAction")] || _shell.Renderer.Focused == nodes[ContentKey("galleryAction")];
                bool visibleFocus = _shell.Tree.IsAlive(_shell.Renderer.Focused) && _shell.Tree.GetComponent<XsrUiInput>(_shell.Renderer.Focused)?.IsFocusVisible == true;
                _screenshotTimeline = !_screenshotTimeline; _contentWindowStart = -1; UpdateContentWindow(); ReloadContentManagement();
                if (focused) _shell.Renderer.Focus(nodes[ContentKey(_screenshotTimeline ? "galleryAction" : "timelineAction")], visibleFocus);
            }
            BindContentAction(nodes, ContentKey("timelineAction"), SwitchScreenshotLayout); BindContentAction(nodes, ContentKey("galleryAction"), SwitchScreenshotLayout);
            if (_selected == "mods") BuildModCategories(nodes["ContentManagement.Categories"]);
            if (_selected is "mods" or "resourcepacks" or "shaderpacks")
            {
                BuildContentUpdateActions(nodes["ContentManagement.Updates"]);
                BuildOnlineListStatus(nodes["ContentManagement.OnlineStatus"]);
            }
            ApplyContentFilter(); UpdateContentWindow(); ReloadContentManagement();
        }
        else if (_selected == "overview")
        {
            var information = AppendContent(_sections, "SettingsContentOverview.d.nxml", new() { ["enabled"] = ContentFlag(_managementWrite is null), ["open"] = ContentFlag(OpenManagementDirectory is not null) });
            var nodes = ContentNodes(information); var facts = nodes["ContentOverview.Facts"];
            AppendContentFact(facts, "instanceName", System.IO.Path.GetFileName(snapshot.InstanceDirectory));
            AppendContentFact(facts, "minecraft", snapshot.GameVersion); AppendContentFact(facts, "instanceDirectory", snapshot.InstanceDirectory); AppendContentFact(facts, "gameDirectory", snapshot.GameDirectory);
            if (snapshot.Description.Length > 0) AppendContentFact(facts, "description", snapshot.Description);
            BindContentAction(nodes, ContentKey("instanceFolder"), () => OpenContentDirectory(snapshot.InstanceDirectory));
            _modpackOverviewHost = nodes["ContentOverview.Modpack"];
            BuildModpackUpdate(snapshot);
        }
        else if (_selected == "recovery") { BuildRecoveryStorage(snapshot); BuildRecoveryTimeline(snapshot); }
        else if (_selected == "trash") BuildContentTrash(snapshot);
        else if (_selected == "contentgraph") BuildContentGraph(snapshot);
        else if (_selected == "files") BuildInstanceFiles();
        else if (_selected == "diagnostics") BuildInstanceDiagnostics();
        else if (_selected == "modpack") BuildModpackExport(snapshot);
        else if (_selected == "servers") BuildServers();
        BuildOfflineReadiness(snapshot);
        if (_selected is "overview" or "servers") BuildInstanceIdentity();
        if (_selected == "diagnostics") BuildAdvancedLaunchDiagnostics();
        _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY = _scrollPositions.GetValueOrDefault(_selected);
        _shell.Tree.MarkDirty(_sections, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    private void UpdateContentWindow()
    {
        if (!_contentList.IsAssigned || !_shell.Tree.IsAlive(_contentList) || _contentSnapshot is not { } snapshot) return;
        ReadContentVirtualMetrics();
        if (_selected == "screenshots") { UpdateScreenshotWaterfall(snapshot); return; }
        var metrics = ContentMetric("files");
        double rowHeight = _contentVirtualMetrics.ItemHeight + _contentVirtualMetrics.Spacing;
        int rows = snapshot.Entries.Count;
        int start = Math.Clamp((int)((_shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY - metrics.Padding.Left) / rowHeight) - (int)metrics.Padding.Top, 0, Math.Max(0, rows - 1));
        int count = Math.Min(rows - start, Math.Min(_contentVirtualMetrics.RealizationLimit - 1, (int)Math.Ceiling(_shell.Renderer.Viewport.Height / rowHeight) + (int)metrics.Padding.Right));
        string? focusedName = _contentFileViews.FirstOrDefault(pair => ContentContainsFocus(pair.Value.Root, _shell.Renderer.Focused)).Key;
        int pinned = focusedName is null ? -1 : snapshot.Entries.ToList().FindIndex(item => item.Name == focusedName);
        var indices = Enumerable.Range(start, count).Concat(pinned >= 0 ? [pinned] : Array.Empty<int>()).Distinct().Order().ToArray();
        if (_contentWindowStart == start && _contentWindowCount == count && _contentColumns == 1 && _contentPinnedName == focusedName) return;
        var projections = indices.ToDictionary(index => index, index => ContentFileProjection(snapshot.Entries[index], _contentVirtualMetrics.ItemHeight));
        foreach (var data in projections.Values) _ = DesktopMarkup.Dynamic("SettingsContentFileCard.d.nxml", data);
        _contentPinnedName = focusedName;
        _contentWindowStart = start; _contentWindowCount = count; _contentColumns = 1;
        _contentIconEntities.Clear(); _contentSelectionRows.Clear();
        foreach (var view in _contentFileViews.Values) _dynamic.Move(view.Root, _contentParking);
        foreach (var fragment in new[] { _contentBefore, _contentAfter, _contentMiddle, _contentEmpty }.Where(_shell.Tree.IsAlive)) _dynamic.Move(fragment, _contentParking);
        if (!_contentBefore.IsAssigned)
        {
            _contentBefore = AppendContent(_contentParking, "SettingsContentSpacer.d.nxml", new() { ["key"] = "ManagementContentBefore", ["height"] = "0" });
            _contentAfter = AppendContent(_contentParking, "SettingsContentSpacer.d.nxml", new() { ["key"] = "ManagementContentAfter", ["height"] = "0" });
            _contentMiddle = AppendContent(_contentParking, "SettingsContentSpacer.d.nxml", new() { ["key"] = "ManagementContentMiddle", ["height"] = "0" });
            _contentEmpty = AppendContent(_contentParking, "SettingsContentEmpty.d.nxml", new() { ["message"] = "" });
            foreach (var spacer in new[] { _contentBefore, _contentAfter, _contentMiddle }) _shell.Renderer.ProtectVirtualGeometry(spacer);
        }
        List<XsrUiEntityId> order = [];
        if (rows == 0)
        {
            ReloadContent(_contentEmpty, ("message", ContentTerm(_contentFilter.Length == 0 && (_selected != "mods" || _modCategory == "all") ? "empty" : "noMatches")));
            _dynamic.Move(_contentEmpty, _contentList); order.Add(_contentEmpty);
        }
        int cursor = 0, spacerIndex = 0; var spacers = new[] { _contentBefore, _contentMiddle, _contentAfter };
        void Gap(int size)
        {
            if (size <= 0) return; var spacer = spacers[spacerIndex++]; ReloadContent(spacer, ("height", ContentNumber(size * rowHeight))); _dynamic.Move(spacer, _contentList); order.Add(spacer);
        }
        var live = indices.Select(index => snapshot.Entries[index].Name).ToHashSet(StringComparer.Ordinal);
        foreach (int index in indices)
        {
            Gap(index - cursor); cursor = index + 1;
            var item = snapshot.Entries[index];
            if (_contentFileViews.TryGetValue(item.Name, out var retired) && !SameContentFileIdentity(retired.Item, item)) { RemoveContentView(retired.Root); _contentFileViews.Remove(item.Name); }
            if (!_contentFileViews.TryGetValue(item.Name, out var view))
            {
                var root = AppendContent(_contentParking, "SettingsContentFileCard.d.nxml", projections[index]);
                var nodes = ContentNodes(root); view = new(root, nodes["ManagementContentIcon"], nodes["ContentSelectionMark." + ContentFileKey(item.Name)], item);
                _contentFileViews[item.Name] = view; BuildContentRowActions(nodes, item);
            }
            else { ReloadContent(view.Root, projections[index].Select(pair => (pair.Key, pair.Value)).ToArray()); view = view with { Item = item }; _contentFileViews[item.Name] = view; BuildContentRowActions(ContentNodes(view.Root), item); }
            _shell.Renderer.ProtectVirtualGeometry(view.Root);
            BindContentImage(view.Icon, item); ApplyContentFormatting(ContentNodes(view.Root)["ContentFile.Title"], _selected == "resourcepacks" ? ResourcePackTitle(item) : item.DisplayName.Length == 0 ? item.Name : item.DisplayName);
            if (_selected == "resourcepacks") ApplyContentFormatting(ContentNodes(view.Root)["ContentFile.Description"], item.Description);
            _dynamic.Move(view.Root, _contentList); order.Add(view.Root); _contentIconEntities[item.Name] = view.Icon;
            BuildContentUpdateSelection(view.Root, view.Mark, item);
        }
        Gap(rows - cursor); _dynamic.Reorder(_contentList, order);
        foreach (var obsolete in _contentFileViews.Where(pair => !live.Contains(pair.Key)).ToArray()) { RemoveContentView(obsolete.Value.Root); _contentFileViews.Remove(obsolete.Key); }
        UpdateContentSelectionPresentation(); _shell.Tree.MarkDirty(_contentList, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    private Dictionary<string, string> ContentFileProjection(InstanceContentEntry item, double rowHeight)
    {
        string key = ContentFileKey(item.Name);
        bool pack = _selected == "resourcepacks", available = item.UpdateAvailable == true; var copy = ContentMetric("fileCopy");
        var query = OnlineFile(item); var online = query is not null && _onlineList.TryGetValue(query, out var linked) ? linked : null;
        string release = _selected == "mods" ? item.Version : online?.InstalledVersion ?? "";
        return new()
        {
            ["height"] = ContentNumber(rowHeight),
            ["width"] = ContentNumber(_contentVirtualMetrics.ItemWidth),
            ["margin"] = ContentRowMargin(_contentVirtualMetrics.Spacing),
            ["icon"] = ContentIcon(),
            ["title"] = StripContentFormatting(pack ? ResourcePackTitle(item) : item.DisplayName.Length == 0 ? item.Name : item.DisplayName),
            ["description"] = pack ? StripContentFormatting(item.Description) : ContentFormat("FileDescription", ("name", item.Name), ("disabled", item.Enabled == false ? ContentTerm("disabledSuffix") : ""), ("state", item.PackageProblem.Length > 0 ? ContentTerm("problemSuffix") : available ? ContentTerm("updateSuffix") : "")),
            ["descriptionHeight"] = ContentNumber(pack && !available ? copy.Width!.Value : copy.Height!.Value),
            ["descriptionLines"] = ContentNumber(pack && !available ? copy.MaxHeight!.Value : copy.MinHeight!.Value),
            ["update"] = available ? ContentFormat("UpdateVersion", ("version", item.UpdateVersion)) : "",
            ["hasUpdate"] = ContentFlag(pack && available),
            ["version"] = release.Length > 0 ? release : _selected == "mods" ? ContentTerm("unmarkedVersion") : "",
            ["versionVisible"] = ContentFlag(_selected is "mods" or "resourcepacks" or "shaderpacks"),
            ["actions"] = "ManagementContentActions." + key,
            ["details"] = "ManagementContentDetails." + key,
            ["toggle"] = "ManagementContentToggle." + key,
            ["updateKey"] = "ManagementContentUpdate." + key,
            ["remove"] = "ManagementContentRemove." + key,
            ["toggleLabel"] = ContentTerm(item.Enabled == false ? "enable" : "disable"),
            ["toggleIcon"] = ContentTerms.Value[item.Enabled == false ? "icon.enable" : "icon.disable"],
            ["canToggle"] = ContentFlag(item.Enabled is not null && _commands.TryResolve(InstanceManagementContract.SetModEnabled, out _)),
            ["canUpdate"] = ContentFlag(online is { IsStale: false, UpdateVersion: not null } && _resourceCommands?.TryResolve(Nexa.Services.Resources.ResourceCatalogContract.UpdateContent, out _) == true),
            ["canRemove"] = ContentFlag(_commands.TryResolve(InstanceManagementContract.RemoveContent, out _)),
            ["enabled"] = ContentFlag(_managementWrite is null),
            ["selectable"] = ContentFlag(query is not null && _resourceCommands?.TryResolve(Nexa.Services.Resources.ResourceCatalogContract.UpdateContentBatch, out _) == true),
            ["selected"] = ContentFlag(query is not null && _selectedContentUpdates.Contains(query)),
            ["selectedStyle"] = query is not null && _selectedContentUpdates.Contains(query) ? "settings-content-file-card settings-content-card-selected" : "settings-content-file-card",
            ["mark"] = "ContentSelectionMark." + key
        };
    }

    private void ApplyContentFilter()
    {
        if (_management?.Contents.FirstOrDefault(item => item.PageId == _selected) is not { } source) return;
        var liveFiles = source.Entries.Select(OnlineFile).OfType<Nexa.Services.Resources.ResourceContentOnlineQuery>().ToHashSet();
        _selectedContentUpdates.RemoveWhere(query => query.InstanceDirectory == _instance && query.PageId == _selected && !liveFiles.Contains(query));
        if (_contentSelectionAnchor is { } selectionAnchor && !liveFiles.Contains(selectionAnchor)) _contentSelectionAnchor = null;
        _contentSnapshot = source with { Entries = source.Entries.Select(LinkedContent).Where(item => (_selected != "mods" || MatchesModCategory(item, _modCategory)) && ((item.Name.Contains(_contentFilter, StringComparison.OrdinalIgnoreCase) || StripContentFormatting(item.Name).Contains(_contentFilter, StringComparison.OrdinalIgnoreCase)) || (StripContentFormatting(item.DisplayName).Contains(_contentFilter, StringComparison.OrdinalIgnoreCase) || StripContentFormatting(item.Description).Contains(_contentFilter, StringComparison.OrdinalIgnoreCase)) || item.Version.Contains(_contentFilter, StringComparison.OrdinalIgnoreCase))).ToArray() };
        ReloadContentManagement();
        _contentWindowStart = -1;
    }

    private void RemoveContent(InstanceContentEntry item)
    {
        if (_managementWrite is not null || _management is not { } snapshot
            || !_commands.TryResolve(InstanceManagementContract.RemoveContent, out var route)) return;
        string page = _selected;
        if (page == "mods" && _queries.TryResolve(InstanceManagementContract.ModRemovalPreview, out var previewRoute))
        {
            StartModRemovalPreview(new(snapshot.InstanceDirectory, page, item.Name, item.IsDirectory, item.Size, item.ModifiedUtcTicks), previewRoute);
            return;
        }
        long? previewGeneration = page == "screenshots" && _screenshotPreviewQuery is { } file && SameScreenshot(item, file) ? _screenshotPreviewGeneration : null;
        long contentGeneration = _contentScopeGeneration;
        var removalDialog = _feedback.ShowDialog("content.remove", ContentTerm("removeTitle"), ContentFormat("RemoveContent", ("name", item.Name)), ContentTerm("remove"), ContentTerm("cancel"), accepted =>
        {
            if (_contentScopeGeneration == contentGeneration) _contentRemovalDialog = default;
            if (!accepted || _managementWrite is not null || _instance != snapshot.InstanceDirectory || _contentScopeGeneration != contentGeneration || _selected != page || !_visible
                || previewGeneration is { } generation && (_screenshotPreviewGeneration != generation || _selected != "screenshots" || !_visible)) return;
            _managementWriteInstance = snapshot.InstanceDirectory;
            _managementWrite = _commands.Dispatch(route, new InstanceContentRemoveCommand(snapshot.InstanceDirectory, page, item.Name, item.IsDirectory, item.Size, item.ModifiedUtcTicks)).Completion;
            WakeOnPlatformCompletion(_managementWrite);
        }, localizeMessage: false);
        _contentRemovalDialog = removalDialog;
        if (previewGeneration is not null) _screenshotRemovalDialog = removalDialog;
    }

    private void BuildContentTrash(InstanceManagementSnapshot snapshot)
    {
        int pages = Math.Max(1, (snapshot.Trash.Count + 9) / 10); _trashPage = Math.Clamp(_trashPage, 0, pages - 1);
        var root = AppendContent(_sections, "SettingsContentTrash.d.nxml", new() { ["enabled"] = ContentFlag(_managementWrite is null), ["empty"] = ContentFlag(snapshot.Trash.Count == 0), ["hasPages"] = ContentFlag(pages > 1), ["next"] = ContentFormat("NextPage", ("page", (_trashPage + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)), ("pages", pages.ToString(System.Globalization.CultureInfo.CurrentCulture))) });
        var nodes = ContentNodes(root); BuildContentUpdateHistory(nodes["ContentTrash.History"], snapshot);
        foreach (var item in snapshot.Trash.Skip(_trashPage * 10).Take(10))
        {
            var row = AppendContent(nodes["ContentTrash.Rows"], "SettingsContentTrashRow.d.nxml", new() { ["name"] = item.Name, ["enabled"] = ContentFlag(_managementWrite is null) });
            BindContentAction(ContentNodes(row), "ContentTrash.Restore", () =>
            {
                if (_managementWrite is not null || _instance != snapshot.InstanceDirectory || !_commands.TryResolve(InstanceManagementContract.RestoreContent, out var route)) return;
                _managementWriteInstance = snapshot.InstanceDirectory; _managementWrite = _commands.Dispatch(route, new InstanceContentRestoreCommand(snapshot.InstanceDirectory, item.Id)).Completion; WakeOnPlatformCompletion(_managementWrite);
            });
        }
        BindContentAction(nodes, "ContentTrash.Next", () => { _trashPage = (_trashPage + 1) % pages; BuildSections(true); });
    }


}
