using Nexa.Services.Minecraft.Management;
using Nexa.Services.Resources;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private XsrQueryRouter? _resourceQueries;
    private Action<Uri>? _openResourceLink;
    private ResourceContentOnlineQuery? _onlineQuery;
    private ResourceContentOnline? _onlineContent;
    private Task<XsrResult<ResourceContentOnline>>? _onlineRead;
    private CancellationTokenSource? _onlineStop;
    private XsrUiEntityId _onlineSection, _onlineSectionSlot;
    private readonly Dictionary<string, XsrUiEntityId> _onlineVersionViews = [];
    private string? _onlineError;
    private Task<XsrResult<ResourceIconResult>>? _onlineIconRead;
    private Nexa.Core.Media.PngImage? _onlineIcon;
    private XsrCommandRouter? _resourceCommands;
    private string? _onlineListInstance, _onlineListPage;
    private CancellationTokenSource? _onlineListStop;
    private readonly List<(ResourceContentOnlineQuery[] Files, Task<XsrResult<ResourceContentOnlineBatch>> Read)> _onlineListReads = [];
    private bool _onlineListRefresh;
    private XsrUiEntityId _onlineListStatus, _onlineListStatusSlot;
    private int _onlineListTotal = -1, _onlineListMatched;
    private bool _onlineListIncomplete;
    private bool _onlineListFailed;
    private readonly Dictionary<ResourceContentOnlineQuery, ResourceContentOnline> _onlineList = [];
    private readonly HashSet<ResourceContentOnlineQuery> _onlineListRenewed = [];
    private bool _onlineContentRenewed;
    private readonly Dictionary<string, Nexa.Core.Media.PngImage?> _onlineListIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<XsrResult<ResourceIconResult>>> _onlineListIconReads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XsrUiEntityId> _contentIconEntities = new(StringComparer.Ordinal);
    private string? _onlinePresentationLanguage;
    private long _onlineSourceRevision = -1;
    private string? _onlineSourcePriority;
    private Task<XsrResult<ResourceNetworkPolicySnapshot>>? _onlineSourcePolicyRead;
    private bool _onlineForceRefresh;

    internal void ConfigureOnlineContent(XsrQueryRouter queries, Action<Uri> open, XsrCommandRouter? commands = null)
    { _resourceQueries = queries; _openResourceLink = open; _resourceCommands = commands; }

    private void CancelOnlineList()
    {
        _selectedContentUpdates.Clear();
        _contentSelectionAnchor = null;
        UpdateContentSelectionPresentation();
        _onlineListStop?.Cancel(); _onlineListStop?.Dispose(); _onlineListStop = null;
        foreach (var pending in _onlineListReads)
            _ = pending.Read.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _onlineListReads.Clear(); _onlineListRefresh = false;
        _onlineListTotal = -1; _onlineListMatched = 0; _onlineListIncomplete = false;
        _onlineListInstance = _onlineListPage = null; _onlineList.Clear(); _onlineListRenewed.Clear(); _onlineListFailed = false;
        _onlineListIcons.Clear(); _onlineListIconReads.Clear();
        _contentIconEntities.Clear();
    }
    private ResourceContentOnlineQuery? OnlineFile(InstanceContentEntry item) => _instance is not null && item.Size is { } size && !item.IsDirectory
        ? new(_instance, _selected, item.Name, size, item.ModifiedUtcTicks) : null;
    private InstanceContentEntry LinkedContent(InstanceContentEntry item)
    {
        if (OnlineFile(item) is not { } key || !_onlineList.TryGetValue(key, out var online) || online.Project is null) return item;
        var icon = online.Project.IconUrl is { } url ? _onlineListIcons.GetValueOrDefault(url) : null;
        return item with
        {
            DisplayName = DesktopResourceText.Name(online.Project, _store),
            Version = _selected == "mods" ? online.InstalledVersion ?? item.Version : item.Version,
            Icon = item.Icon ?? icon,
            UpdateAvailable = online.UpdateAvailable ?? item.UpdateAvailable,
            UpdateVersion = online.UpdateVersion?.Number ?? item.UpdateVersion
        };
    }
    private void UpdateOnlineList()
    {
        if (_onlineListInstance != _instance || _onlineListPage != _selected) { CancelOnlineList(); _onlineListInstance = _instance; _onlineListPage = _selected; }
        if (_instance is null || _selected is not ("mods" or "resourcepacks" or "shaderpacks") || !_managementLoaded || _management is null || _managementWrite is not null
            || _resourceQueries?.TryResolve(ResourceCatalogContract.ContentOnlineBatch, out var route) != true) return;
        bool changed = false;
        foreach (var pending in _onlineListReads.ToArray())
        {
            var read = pending.Read;
            if (!read.IsCompleted) continue;
            _onlineListReads.Remove(pending); changed = true;
            if (PendingQuery.Succeeded(read))
            {
                foreach (var match in read.Result.Value!.Matches)
                    if (pending.Files.Contains(match.File)) _onlineList[match.File] = RetainOnlineContent(_onlineList.GetValueOrDefault(match.File), match.Content);
            }
            else { _ = read.Exception; _onlineListFailed = true; }
            foreach (var file in pending.Files)
                if (!_onlineList.ContainsKey(file)) _onlineList[file] = new(null, null, [], WorldTerm("online.match-error"));
        }
        if (changed)
        {
            _onlineListMatched = _onlineList.Count(pair => pair.Value.Project is not null || pair.Value.InstalledFiles.Count > 0);
            _onlineListIncomplete = _onlineList.Any(pair => pair.Value.Notice is { Length: > 0 });
            ApplyContentFilter(); UpdateContentWindow();
        }
        UpdateOnlineListIcons();
        if (_onlineListTotal < 0) _onlineListTotal = _management.Contents.FirstOrDefault(p => p.PageId == _selected)?.Entries.Count(item => OnlineFile(item) is not null) ?? 0;
        if (_onlineListReads.Count >= 2 || _onlineList.Count >= _onlineListTotal && !_onlineList.Any(item => item.Value.IsStale && !_onlineListRenewed.Contains(item.Key))) { UpdateOnlineListStatus(); return; }
        var visible = _contentSnapshot?.Entries.Skip(Math.Max(0, _contentWindowStart * Math.Max(1, _contentColumns))).Take(_contentWindowCount * Math.Max(1, _contentColumns)) ?? [];
        var candidates = visible.Concat(_management.Contents.FirstOrDefault(p => p.PageId == _selected)?.Entries ?? []).Select(OnlineFile)
            .Where(q => q is not null).Select(q => q!).Distinct().ToArray();
        while (_onlineListReads.Count < 2)
        {
            var files = candidates.Where(q => (!_onlineList.TryGetValue(q, out var cached) || cached.IsStale && !_onlineListRenewed.Contains(q))
                && !_onlineListReads.Any(p => p.Files.Contains(q))).Take(2).ToArray();
            if (files.Length == 0) break;
            _onlineListStop ??= new();
            bool renewal = files.Any(file => _onlineList.TryGetValue(file, out var cached) && cached.IsStale);
            if (_onlineListRefresh || renewal) foreach (var file in files) _onlineListRenewed.Add(file);
            var read = _resourceQueries.QueryAsync<ResourceContentOnlineBatchQuery, ResourceContentOnlineBatch>(route, new(files) { Refresh = _onlineListRefresh, WaitForRefresh = renewal }, cancellationToken: _onlineListStop.Token).AsTask();
            _onlineListReads.Add((files, read)); WakeOnPlatformCompletion(read);
        }
        UpdateOnlineListStatus();
    }

    private ResourceContentOnline RetainOnlineContent(ResourceContentOnline? previous, ResourceContentOnline current)
        => previous?.Project is not null && current.Project is null
            ? previous with
            {
                IsStale = true,
                UpdateAvailable = null,
                UpdateVersion = null,
                Notice = WorldFormat("CacheNoticeFormat", new() { ["notice"] = current.Notice ?? WorldTerm("online.refresh-error") })
            } : current;

    private void BuildOnlineListStatus(XsrUiEntityId slot)
    {
        _onlineListStatusSlot = slot; UpdateOnlineListStatus();
    }
    private void UpdateOnlineListStatus()
    {
        if (!_shell.Tree.IsAlive(_onlineListStatusSlot)) return;
        int total = Math.Max(0, _onlineListTotal);
        string label = _resourceQueries?.TryResolve(ResourceCatalogContract.ContentOnlineBatch, out _) != true ? WorldTerm("online.unavailable")
            : _onlineList.Values.Any(value => value.IsStale) ? WorldTerm("online.cached")
            : _onlineListReads.Count > 0 || _onlineList.Count < total ? WorldTerm("online.linking")
            : _onlineListFailed ? WorldTerm("online.failed")
            : _onlineListIncomplete ? _onlineList.Values.Select(value => value.Notice).FirstOrDefault(notice => !string.IsNullOrWhiteSpace(notice)) ?? WorldTerm("online.incomplete") : WorldTerm("online.refreshed");
        WorldView(_onlineListStatusSlot, ref _onlineListStatus, "OnlineListStatus", new()
        { ["matched"] = WorldNumber(_onlineListMatched), ["completed"] = WorldNumber(_onlineList.Count), ["total"] = WorldNumber(total), ["status"] = label });
    }

    private void UpdateOnlineListIcons()
    {
        bool changed = false;
        foreach (var (url, read) in _onlineListIconReads.ToArray())
        {
            if (!read.IsCompleted) continue;
            _onlineListIconReads.Remove(url);
            if (_onlineListIcons.Count >= 256) _onlineListIcons.Remove(_onlineListIcons.Keys.First());
            _onlineListIcons[url] = PendingQuery.Succeeded(read) ? read.Result.Value?.Image : null;
            changed = true;
        }
        if (changed && _contentSnapshot is { } snapshot)
        {
            _contentSnapshot = snapshot with { Entries = snapshot.Entries.Select(LinkedContent).ToArray() };
            foreach (var item in _contentSnapshot.Entries)
                if (item.Icon is { } image && _contentIconEntities.TryGetValue(item.Name, out var entity) && _shell.Tree.IsAlive(entity)
                    && _shell.Tree.GetComponent<XsrUiImage>(entity) is { Raster: null } target)
                    BindContentImage(entity, item);
        }
        if (_contentDetail is not null || _contentSnapshot is null || _contentWindowStart < 0
            || _resourceQueries?.TryResolve(ResourceCatalogContract.Icon, out var route) != true) return;
        // Only visible virtualized rows request icons. Missing/failed previews are cached too.
        foreach (var item in _contentSnapshot.Entries.Skip(_contentWindowStart).Take(_contentWindowCount))
        {
            if (_onlineListIconReads.Count >= 4) break;
            if (item.Icon is not null || OnlineFile(item) is not { } key || !_onlineList.TryGetValue(key, out var online)
                || online.Project?.IconUrl is not { } url || _onlineListIcons.ContainsKey(url) || _onlineListIconReads.ContainsKey(url)) continue;
            _onlineListStop ??= new();
            var read = _resourceQueries.QueryAsync<ResourceIconQuery, ResourceIconResult>(route, new(url), cancellationToken: _onlineListStop.Token).AsTask();
            _onlineListIconReads[url] = read;
            WakeOnPlatformCompletion(read);
        }
    }

    private void UpdateOnlinePack(ResourceContentOnlineQuery query, ResourceVersion version)
    {
        if (_instance != query.InstanceDirectory || _selected != query.PageId || _managementWrite is not null
            || _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContent, out var route) != true) return;
        _managementWriteInstance = _instance;
        _managementWrite = _resourceCommands.Dispatch(route, new ResourceContentUpdateCommand(query, new(version.Provider, version.ProjectId), version.Id)).Completion;
        CancelOnlineList(); WakeOnPlatformCompletion(_managementWrite);
        RenderOnlineContent();
    }

    private void CancelOnlineContent()
    {
        CancelContentIntegrity();
        ObserveRetiredInstanceToolRead(_contentChangelogRead); _contentChangelogRead = null; _contentChangelog = null;
        _onlineStop?.Cancel(); _onlineStop?.Dispose(); _onlineStop = null;
        ObserveRetiredInstanceToolRead(_onlineRead); ObserveRetiredInstanceToolRead(_onlineIconRead);
        _onlineRead = null; _onlineQuery = null; _onlineContent = null; _onlineError = null;
        _onlineIconRead = null; _onlineIcon = null; _onlineContentRenewed = false;
    }

    private void BuildOnlineContent(XsrUiEntityId parent, InstanceContentEntry item)
    {
        if (_instance is null || item.IsDirectory || item.Size is not { } size || _selected is not ("mods" or "resourcepacks" or "shaderpacks")
            || _resourceQueries?.TryResolve(ResourceCatalogContract.ContentOnline, out var route) != true) return;
        var identity = new ResourceContentOnlineQuery(_instance, _selected, item.Name, size, item.ModifiedUtcTicks);
        var query = !_onlineForceRefresh && _onlineQuery is { } retained && retained with { Refresh = false } == identity
            ? retained : identity with { Refresh = _onlineForceRefresh };
        _onlineForceRefresh = false;
        if (query != _onlineQuery)
        {
            CancelOnlineContent(); _onlineQuery = query; _onlineStop = new(); _onlineContentRenewed = query.Refresh;
            _onlineRead = _resourceQueries.QueryAsync<ResourceContentOnlineQuery, ResourceContentOnline>(route, query, cancellationToken: _onlineStop.Token).AsTask();
            WakeOnPlatformCompletion(_onlineRead);
        }
        _onlineSectionSlot = parent;
        RenderOnlineContent();
    }

    private void UpdateOnlineContent()
    {
        long sourceRevision = _store.TryResolve(SettingsPolicyContract.RevisionKey, out var sourceKey) ? _store.Read<long>(sourceKey).Value : 0;
        if (_onlineSourcePolicyRead is null && _onlineSourceRevision != sourceRevision
            && _resourceQueries?.TryResolve(ResourceCatalogContract.NetworkPolicy, out var policyRoute) == true)
        {
            _onlineSourceRevision = sourceRevision;
            _onlineSourcePolicyRead = _resourceQueries.QueryAsync<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(policyRoute, new()).AsTask();
            WakeOnPlatformCompletion(_onlineSourcePolicyRead);
        }
        if (_onlineSourcePolicyRead is { IsCompleted: true } policyRead)
        {
            _onlineSourcePolicyRead = null;
            if (PendingQuery.Succeeded(policyRead))
            {
                string priority = policyRead.Result.Value!.Priority;
                bool changed = _onlineSourcePriority is not null && _onlineSourcePriority != priority;
                _onlineSourcePriority = priority;
                if (changed) { CancelOnlineList(); CancelOnlineContent(); BuildSections(); }
            }
        }
        UpdateContentIntegrity();
        UpdateContentChangelog();
        string language = DesktopResourceText.Language(_store);
        if (_onlinePresentationLanguage is { } previous && previous != language)
        {
            ApplyContentFilter(); _contentWindowStart = -1;
            if (_contentDetail is { } selected && _management?.Contents.FirstOrDefault(page => page.PageId == _selected)?.Entries.FirstOrDefault(item => item.Name == selected.Name) is { } original)
            { _contentDetail = LinkedContent(original); BuildSections(); }
            else
            {
                UpdateContentWindow();
                if (_onlineSection.IsAssigned && _shell.Tree.IsAlive(_onlineSection)) RenderOnlineContent();
            }
        }
        _onlinePresentationLanguage = language;
        UpdateOnlineList();
        if (_onlineQuery is not { } query) return;
        if (_contentDetail is not { } item || query.InstanceDirectory != _instance || query.PageId != _selected
            || query.Name != item.Name || query.ExpectedSize != item.Size || query.ExpectedModifiedUtcTicks != item.ModifiedUtcTicks)
        { CancelOnlineContent(); return; }
        bool updated = false;
        if (_onlineRead is { IsCompleted: true } read)
        {
            _onlineRead = null; updated = true;
            if (PendingQuery.Succeeded(read)) _onlineContent = RetainOnlineContent(_onlineContent, read.Result.Value!);
            else _onlineError = WorldTerm("online.read-error");
            // Join the service's one background renewal and consume its result once; no polling loop.
            if (_onlineContent is { IsStale: true } && !_onlineContentRenewed
                && _resourceQueries?.TryResolve(ResourceCatalogContract.ContentOnline, out var contentRoute) == true)
            {
                _onlineContentRenewed = true;
                _onlineRead = _resourceQueries.QueryAsync<ResourceContentOnlineQuery, ResourceContentOnline>(contentRoute, query with { Refresh = false, WaitForRefresh = true }, cancellationToken: _onlineStop!.Token).AsTask();
                WakeOnPlatformCompletion(_onlineRead);
            }
            if (_onlineContent?.Project?.IconUrl is { } url && _resourceQueries?.TryResolve(ResourceCatalogContract.Icon, out var route) == true)
            {
                _onlineIconRead = _resourceQueries.QueryAsync<ResourceIconQuery, ResourceIconResult>(route, new(url), cancellationToken: _onlineStop!.Token).AsTask();
                WakeOnPlatformCompletion(_onlineIconRead);
            }
        }
        if (_onlineIconRead is { IsCompleted: true } iconRead)
        {
            _onlineIconRead = null;
            if (PendingQuery.Succeeded(iconRead)) { _onlineIcon = iconRead.Result.Value!.Image; updated = true; }
        }
        if (updated && _onlineSection.IsAssigned && _shell.Tree.IsAlive(_onlineSection)) RenderOnlineContent();
        UpdateContentIntegritySource();
    }

    private void RenderOnlineContent()
    {
        if (!_shell.Tree.IsAlive(_onlineSectionSlot)) return;
        var project = _onlineContent?.Project;
        string description = project is null ? "" : DesktopResourceText.Description(project, _store);
        var update = _onlineContent?.UpdateVersion;
        var versions = _onlineContent?.Versions.Take(6).ToArray() ?? [];
        string? notice = _onlineContent?.Notice ?? _onlineError;
        var nodes = WorldView(_onlineSectionSlot, ref _onlineSection, "Online", new()
        {
            ["project"] = Flag(project is not null),
            ["icon"] = Flag(_onlineIcon is not null),
            ["name"] = project is null ? "" : StripContentFormatting(DesktopResourceText.Name(project, _store)),
            ["hasDescription"] = Flag(description.Length > 0),
            ["description"] = StripContentFormatting(description),
            ["source"] = project?.SourceLabel ?? "",
            ["author"] = project is { Author.Length: > 0 } ? project.Author : WorldTerm("not-provided"),
            ["downloads"] = ResourcesPageController.FormatDownloads(project?.Downloads ?? 0),
            ["installed"] = _onlineContent?.InstalledVersion ?? WorldTerm("unavailable"),
            ["hasUpdate"] = Flag(update is not null),
            ["update"] = update?.Number ?? "",
            ["canUpdate"] = Flag(update is not null && _managementWrite is null && _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContent, out _) == true),
            ["updating"] = Flag(_managementWrite is not null),
            ["hasVersions"] = Flag(versions.Length > 0),
            ["noVersions"] = Flag(versions.Length == 0),
            ["changelogRead"] = Flag(_contentChangelogRead is not null),
            ["hasChangelog"] = Flag(_contentChangelog is not null),
            ["changelog"] = StripContentFormatting(_contentChangelog ?? ""),
            ["hasNotice"] = Flag(notice is not null),
            ["notice"] = notice ?? "",
            ["reading"] = Flag(notice is null && _onlineRead is not null),
            ["refresh"] = Flag(_onlineRead is null)
        });
        if (_contentDetail is { } item) BindContentImage(nodes["ManagementOnlineIcon"], item with { Icon = _onlineIcon });
        if (project is not null)
        {
            ApplyContentFormatting(nodes["Online.Title"], DesktopResourceText.Name(project, _store));
            ApplyContentFormatting(nodes["Online.Description"], description);
        }
        ApplyContentFormatting(nodes["Online.Changelog"], _contentChangelog ?? "");
        var query = _onlineQuery;
        BindWorldAction(nodes, "ManagementOnlineUpdate", () => { if (query is not null && update is not null) UpdateOnlinePack(query, update); });
        BindWorldAction(nodes, WorldKey("Project"), () =>
        {
            if (project is null) return;
            try { _openResourceLink?.Invoke(new Uri(project.Website)); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { _feedback.Error(WorldTerm("online.open-error")); }
        });
        BindWorldAction(nodes, WorldKey("Recognize"), () =>
        {
            CancelOnlineContent(); _onlineForceRefresh = true;
            if (_contentDetail is { } selected) BuildOnlineContent(_onlineSectionSlot, selected);
        });
        var identities = versions.Select(version => "Online.Version." + InstanceToolIdentity(version.Provider + "\0" + version.ProjectId + "\0" + version.Id)).ToArray();
        RetireWorldRows(_onlineVersionViews, identities);
        for (int index = 0; index < versions.Length; index++)
        {
            var version = versions[index]; string key = identities[index];
            bool installed = _onlineContent!.InstalledFiles.Any(file => file.Source.Provider == version.Provider && file.Source.ProjectId == version.ProjectId && file.VersionId == version.Id);
            bool canInstall = version.File is not null && _managementWrite is null && _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContent, out _) == true;
            var row = WorldRow(nodes["Online.Versions"], _onlineVersionViews, key, "OnlineVersion", new()
            {
                ["key"] = key,
                ["number"] = version.Number,
                ["channel"] = version.Channel,
                ["release"] = Flag(version.Channel == WorldTerms.Value["channel.release"]),
                ["beta"] = Flag(version.Channel == "Beta"),
                ["other"] = Flag(version.Channel != WorldTerms.Value["channel.release"] && version.Channel != "Beta"),
                ["changelog"] = key + ".Changelog",
                ["reinstall"] = key + ".Reinstall",
                ["install"] = key + ".Install",
                ["canChangelog"] = Flag(_resourceQueries?.TryResolve(ResourceCatalogContract.Changelog, out _) == true),
                ["canReinstall"] = Flag(canInstall && installed),
                ["canInstall"] = Flag(canInstall && !installed)
            });
            BindWorldAction(row, key + ".Changelog", () => ReadContentChangelog(version));
            BindWorldAction(row, key + ".Reinstall", () => { if (query is not null) UpdateOnlinePack(query, version); });
            BindWorldAction(row, key + ".Install", () => { if (query is not null) UpdateOnlinePack(query, version); });
        }
        _dynamic.Reorder(nodes["Online.Versions"], identities.Select(key => _onlineVersionViews[key]).ToArray());
    }
}
