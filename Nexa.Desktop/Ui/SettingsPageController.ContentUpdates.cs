using Nexa.Services.Minecraft.Management;
using Nexa.Services.Resources;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

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
        if (!_shell.Tree.IsAlive(intent.Source) || !_contentSelectionRows.TryGetValue(intent.Source, out var row)) return;
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
        _contentChangelog = PendingQuery.Succeeded(completed) ? completed.Result.Value!.Text : "暂时无法获取更新日志。";
        if (string.IsNullOrWhiteSpace(_contentChangelog)) _contentChangelog = "此版本未提供更新日志。";
        if (_onlineSection.IsAssigned && _shell.Tree.IsAlive(_onlineSection)) RenderOnlineContent();
    }

    private void BuildContentUpdateActions(XsrUiEntityId parent)
    {
        if (_resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContentBatch, out _) != true) return;
        var row = Stack(parent, "ContentUpdateActions", XsrUiOrientation.Horizontal, 10);
        _contentSelectionCount = Text(row, "", 12, Muted, 32);
        _shell.Tree.GetComponent<XsrUiElement>(_contentSelectionCount)!.Weight = 1;
        _shell.Tree.SetComponent(_contentSelectionCount, new XsrUiLiveRegion(XsrUiLiveSetting.Polite));
        _contentSelectionClear = ActionButton(row, "ContentSelectionClear", "清除选择", ManagementAction, 84);
        _managementActions[_contentSelectionClear] = () =>
        { _selectedContentUpdates.Clear(); _contentSelectionAnchor = null; UpdateContentSelectionPresentation(); };
        _contentSelectionUpdate = ActionButton(row, "Management.更新已选择", "更新已选择", ManagementAction, 106);
        _managementActions[_contentSelectionUpdate] = () => DispatchContentUpdates(selectedOnly: true);
        ManagementButton(row, "更新全部已识别", () => DispatchContentUpdates(selectedOnly: false), 136);
        UpdateContentSelectionPresentation();
    }

    private void DispatchContentUpdates(bool selectedOnly)
    {
        if (_instance is null || _managementWrite is not null || _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContentBatch, out var route) != true) return;
        var updates = _onlineList.Where(pair => pair.Key.InstanceDirectory == _instance && pair.Key.PageId == _selected
            && pair.Value.UpdateVersion is not null && (!selectedOnly || _selectedContentUpdates.Contains(pair.Key)))
            .Select(pair => new ResourceContentUpdateCommand(pair.Key, new(pair.Value.UpdateVersion!.Provider, pair.Value.UpdateVersion.ProjectId), pair.Value.UpdateVersion.Id)).ToArray();
        if (updates.Length == 0) { _feedback.Error("没有已识别且可更新的选中内容。"); return; }
        if (updates.Length > 100) { _feedback.Error("一次最多更新 100 项，请分批选择。"); return; }
        _managementWriteInstance = _instance;
        _managementWrite = _resourceCommands.Dispatch(route, new ResourceContentUpdateBatchCommand(updates)).Completion;
        CancelOnlineList(); _selectedContentUpdates.Clear(); WakeOnPlatformCompletion(_managementWrite);
        BuildSections(true);
    }

    private void BuildContentUpdateSelection(XsrUiEntityId row, InstanceContentEntry item)
    {
        if (OnlineFile(item) is not { } query || _resourceCommands?.TryResolve(ResourceCatalogContract.UpdateContentBatch, out _) != true) return;
        _shell.Tree.SetComponent(row, new XsrUiInput { Clickable = true, Focusable = true });
        _shell.Tree.SetComponent(row, new XsrUiSemantic(XsrUiSemanticRole.ListItem, item.DisplayName.Length > 0 ? item.DisplayName : item.Name));
        _shell.Tree.SetComponent(row, new XsrUiSelection());
        _shell.Tree.SetComponent(row, new XsrUiCommandBinding(ManagementAction));
        _shell.Tree.SetComponent(row, new XsrUiModifiedClick(ContentSelectionToggle, ContentSelectionExtend, ContentSelectionAddRange));
        var mark = Element(row, "ContentSelectionMark." + item.Name, XsrUiSemanticRole.None, null, 3, 28);
        _shell.Tree.GetComponent<XsrUiElement>(mark)!.Margin = new(5, 0, 0, 0);
        _shell.Tree.GetComponent<XsrUiElement>(mark)!.HorizontalAlignment = XsrUiAlignment.Start;
        _shell.Tree.SetComponent(mark, new XsrUiOverlayLayer());
        Style(mark, Blue, Blue, 1.5);
        _contentSelectionRows[row] = (query, mark);
        RegisterContentAction(row, () => SelectContentRow(query, XsrUiClickModifiers.None));
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
            _shell.Tree.GetComponent<XsrUiSelection>(entity)!.IsSelected = selected;
            Style(entity, selected ? new(231, 240, 255) : White, Ink, 12);
            _shell.Tree.GetComponent<XsrUiElement>(row.Mark)!.IsVisible = selected;
            _shell.Tree.MarkDirty(row.Mark, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        }
        if (!_contentSelectionCount.IsAssigned || !_shell.Tree.IsAlive(_contentSelectionCount)) return;
        int count = _selectedContentUpdates.Count(query => query.InstanceDirectory == _instance && query.PageId == _selected);
        int updates = _onlineList.Count(pair => pair.Key.InstanceDirectory == _instance && pair.Key.PageId == _selected
            && _selectedContentUpdates.Contains(pair.Key) && pair.Value.UpdateVersion is not null);
        string caption = count == 0 ? "点击选择，Ctrl 多选，Shift 连选" : string.Format(System.Globalization.CultureInfo.CurrentCulture,
            _shell.Renderer.LocalizeText("已选 {0} 项 · 可更新 {1} 项"), count, updates);
        _shell.Tree.SetComponent(_contentSelectionCount, new XsrUiText(caption) { TrimOverflow = true, MaxLines = 1 });
        _shell.Tree.SetComponent(_contentSelectionCount, new XsrUiSemantic(XsrUiSemanticRole.Status, caption));
        _shell.Tree.GetComponent<XsrUiInput>(_contentSelectionClear)!.Enabled = count > 0;
        _shell.Tree.GetComponent<XsrUiInput>(_contentSelectionUpdate)!.Enabled = updates > 0 && _managementWrite is null;
        _shell.Tree.MarkDirty(_contentSelectionCount, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        _shell.Tree.MarkDirty(_contentSelectionClear, XsrUiDirtyKinds.Paint);
        _shell.Tree.MarkDirty(_contentSelectionUpdate, XsrUiDirtyKinds.Paint);
    }

    private void BuildContentUpdateHistory(InstanceManagementSnapshot snapshot)
    {
        if (snapshot.ContentUpdates.Count == 0) return;
        Text(_sections, "内容更新事务", 16, Ink, 30, 600);
        foreach (var item in snapshot.ContentUpdates.Take(20))
        {
            var row = Stack(_sections, "ContentUpdateRecord", XsrUiOrientation.Horizontal, 12);
            var label = Text(row, item.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.CurrentCulture) + " · "
                + string.Format(System.Globalization.CultureInfo.CurrentCulture, _shell.Renderer.LocalizeText("{0} 项"), item.Items) + " · "
                + _shell.Renderer.LocalizeText(item.Phase == "rolled-back" ? "已撤回" : item.Phase == "committed" ? "已完成" : "需要恢复"), 13, Ink, 38);
            _shell.Tree.GetComponent<XsrUiElement>(label)!.Weight = 1;
            if (item.Phase == "rolled-back") continue;
            ManagementButton(row, "联合撤回", () =>
            {
                if (_managementWrite is not null || _instance != snapshot.InstanceDirectory || !_commands.TryResolve(InstanceManagementContract.RollbackContentUpdate, out var route)) return;
                _managementWriteInstance = snapshot.InstanceDirectory;
                _managementWrite = _commands.Dispatch(route, new InstanceContentUpdateRollbackCommand(snapshot.InstanceDirectory, item.Id)).Completion;
                WakeOnPlatformCompletion(_managementWrite);
            }, 90);
        }
    }
}
