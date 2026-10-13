using Nexa.Services.Minecraft.Management;
using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly HashSet<ResourceContentOnlineQuery> _selectedContentUpdates = [];
    private Task<Nexa.Xsr.XsrResult<ResourceChangelog>>? _contentChangelogRead;
    private string? _contentChangelog;
    private static readonly XsrSemanticId ContentSelectionToggle = XsrSemanticId.Parse("ui.settings.content.select-toggle");
    private static readonly XsrSemanticId ContentSelectionExtend = XsrSemanticId.Parse("ui.settings.content.select-range");
    private static readonly XsrSemanticId ContentSelectionAddRange = XsrSemanticId.Parse("ui.settings.content.select-add-range");
    private readonly Dictionary<XsrUiEntityId, (ResourceContentOnlineQuery Query, XsrUiEntityId Mark)> _contentSelectionRows = [];
    private ResourceContentOnlineQuery? _contentSelectionAnchor;
    private XsrUiEntityId _contentSelectionCount, _contentSelectionClear, _contentSelectionUpdate;

    private static bool IsContentSelectionIntent(XsrSemanticId command) =>
        command == ContentSelectionToggle || command == ContentSelectionExtend || command == ContentSelectionAddRange;

    private void HandleContentSelectionIntent(DesktopUiIntent intent)
    {
        if (!ContentActionCurrent(intent.Source) || !_contentSelectionRows.TryGetValue(intent.Source, out var row)) return;
        SelectContentRow(row.Query, intent.Command == ContentSelectionToggle ? XsrUiClickModifiers.Toggle
            : intent.Command == ContentSelectionAddRange ? XsrUiClickModifiers.Toggle | XsrUiClickModifiers.Extend
            : XsrUiClickModifiers.Extend);
    }


    private void ReadContentChangelog(ResourceVersion version)
    {
        if (_resourceQueries?.TryResolve(ResourceCatalogContract.Changelog, out var route) != true || _contentChangelogRead is not null) return;
        _contentChangelog = null;
        _contentChangelogRead = _resourceQueries.QueryAsync<ResourceChangelogQuery, ResourceChangelog>(route,
            new(new(version.Provider, version.ProjectId), version.Id, _onlineQuery?.MirrorFirst ?? true), cancellationToken: _onlineStop?.Token ?? default).AsTask();
        WakeOnPlatformCompletion(_contentChangelogRead);
    }

    private void UpdateContentChangelog()
    {
        if (_contentChangelogRead is not { IsCompleted: true } completed) return;
        _contentChangelogRead = null;
        _contentChangelog = PendingQuery.Succeeded(completed) ? completed.Result.Value!.Text : ContentTerm("copy.f93aad25e6b3");
        if (string.IsNullOrWhiteSpace(_contentChangelog)) _contentChangelog = ContentTerm("copy.b94ec5a65cc6");
        if (_onlineSection.IsAssigned && _shell.Tree.IsAlive(_onlineSection)) RenderOnlineContent();
    }

    private void BuildContentUpdateActions(XsrUiEntityId parent)
    {
        if (_resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContentBatch, out _) != true) return;
        _contentUpdateWorkspace = AppendContent(parent, "SettingsContentUpdates.d.nxml", new() { ["caption"] = "", ["clearEnabled"] = "false", ["updateEnabled"] = "false", ["enabled"] = ContentFlag(_managementWrite is null) });
        var nodes = ContentNodes(_contentUpdateWorkspace); _contentSelectionCount = nodes["ContentSelectionCount"]; _contentSelectionClear = nodes["ContentSelectionClear"]; _contentSelectionUpdate = nodes[ContentKey("updateSelected")];
        BindContentAction(nodes, "ContentSelectionClear", () => { _selectedContentUpdates.Clear(); _contentSelectionAnchor = null; UpdateContentSelectionPresentation(); });
        BindContentAction(nodes, ContentKey("updateSelected"), () => DispatchContentUpdates(selectedOnly: true));
        BindContentAction(nodes, ContentKey("updateAll"), () => DispatchContentUpdates(selectedOnly: false));
        UpdateContentSelectionPresentation();
    }

    private void DispatchContentUpdates(bool selectedOnly)
    {
        if (_instance is null || _managementWrite is not null || _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContentBatch, out var route) != true) return;
        var updates = _onlineList.Where(pair => pair.Key.InstanceDirectory == _instance && pair.Key.PageId == _selected
            && pair.Value.UpdateVersion is not null && (!selectedOnly || _selectedContentUpdates.Contains(pair.Key)))
            .Select(pair => new ResourceContentUpdateCommand(pair.Key, new(pair.Value.UpdateVersion!.Provider, pair.Value.UpdateVersion.ProjectId), pair.Value.UpdateVersion.Id)).ToArray();
        if (updates.Length == 0) { _feedback.Error(ContentTerm("copy.fe2c27764524")); return; }
        if (updates.Length > 100) { _feedback.Error(ContentTerm("copy.30e9a80e5440")); return; }
        _managementWriteInstance = _instance;
        _managementWrite = _resourceCommands.Dispatch(route, new ResourceContentUpdateBatchCommand(updates)).Completion;
        CancelOnlineList(); _selectedContentUpdates.Clear(); WakeOnPlatformCompletion(_managementWrite);
        BuildSections(true);
    }

    private void BuildContentUpdateSelection(XsrUiEntityId row, XsrUiEntityId mark, InstanceContentEntry item)
    {
        if (OnlineFile(item) is not { } query || _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContentBatch, out _) != true) return;
        _shell.Tree.SetComponent(row, new XsrUiModifiedClick(ContentSelectionToggle, ContentSelectionExtend, ContentSelectionAddRange));
        _contentSelectionRows[row] = (query, mark); RegisterContentAction(row, () => SelectContentRow(query, XsrUiClickModifiers.None));
    }

    private void SelectContentRow(ResourceContentOnlineQuery query, XsrUiClickModifiers modifiers)
    {
        if (query.InstanceDirectory != _instance || query.PageId != _selected || _contentSnapshot is null) return;
        var files = _contentSnapshot.Entries.Select(OnlineFile).OfType<ResourceContentOnlineQuery>().ToArray();
        int target = Array.IndexOf(files, query);
        if (target < 0) return;
        bool extend = modifiers.HasFlag(XsrUiClickModifiers.Extend), toggle = modifiers.HasFlag(XsrUiClickModifiers.Toggle);
        int anchor = _contentSelectionAnchor is { } previous ? Array.IndexOf(files, previous) : -1;
        if (!toggle) _selectedContentUpdates.Clear();
        if (extend && anchor >= 0)
        {
            for (int index = Math.Min(anchor, target); index <= Math.Max(anchor, target); index++)
                _selectedContentUpdates.Add(files[index]);
        }
        else
        {
            if (!toggle || !_selectedContentUpdates.Remove(query)) _selectedContentUpdates.Add(query);
            _contentSelectionAnchor = query;
        }
        UpdateContentSelectionPresentation();
    }

    private void UpdateContentSelectionPresentation()
    {
        foreach (var (entity, row) in _contentSelectionRows)
        {
            if (!_shell.Tree.IsAlive(entity)) continue;
            bool selected = _selectedContentUpdates.Contains(row.Query);
            ReloadContent(entity, ("selected", ContentFlag(selected)), ("selectedStyle", selected ? "settings-content-file-card settings-content-card-selected" : "settings-content-file-card"));
        }
        if (!_contentSelectionCount.IsAssigned || !_shell.Tree.IsAlive(_contentSelectionCount)) return;
        int count = _selectedContentUpdates.Count(query => query.InstanceDirectory == _instance && query.PageId == _selected);
        int updates = _onlineList.Count(pair => pair.Key.InstanceDirectory == _instance && pair.Key.PageId == _selected && _selectedContentUpdates.Contains(pair.Key) && pair.Value.UpdateVersion is not null);
        ReloadContent(_contentUpdateWorkspace, ("caption", count == 0 ? ContentTerm("selectionHint") : ContentFormat("SelectionCount", ("count", count.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("updates", updates.ToString(System.Globalization.CultureInfo.CurrentCulture)))),
            ("clearEnabled", ContentFlag(count > 0)), ("updateEnabled", ContentFlag(updates > 0 && _managementWrite is null)), ("enabled", ContentFlag(_managementWrite is null)));
    }

    private void BuildContentUpdateHistory(XsrUiEntityId parent, InstanceManagementSnapshot snapshot)
    {
        if (snapshot.ContentUpdates.Count == 0) return;
        var root = AppendContent(parent, "SettingsContentHistory.d.nxml", new()); var rows = ContentNodes(root)["ContentHistory.Rows"];
        foreach (var item in snapshot.ContentUpdates.Take(20))
        {
            var row = AppendContent(rows, "SettingsContentHistoryRow.d.nxml", new()
            {
                ["enabled"] = ContentFlag(_managementWrite is null),
                ["rollback"] = ContentFlag(item.Phase != "rolled-back"),
                ["caption"] = ContentFormat("UpdateRecord", ("created", item.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.CurrentCulture)), ("items", item.Items.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("phase", ContentTerm(item.Phase == "rolled-back" ? "rolledBack" : item.Phase == "committed" ? "committed" : "needsRecovery")))
            });
            BindContentAction(ContentNodes(row), "ContentHistory.Rollback", () =>
            {
                if (_managementWrite is not null || _instance != snapshot.InstanceDirectory || !_commands.TryResolve(InstanceManagementContract.RollbackContentUpdate, out var route)) return;
                _managementWriteInstance = snapshot.InstanceDirectory; _managementWrite = _commands.Dispatch(route, new InstanceContentUpdateRollbackCommand(snapshot.InstanceDirectory, item.Id)).Completion; WakeOnPlatformCompletion(_managementWrite);
            });
        }
    }


}
