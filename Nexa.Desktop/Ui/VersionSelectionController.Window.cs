using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class VersionSelectionController
{
    private static double VersionRowHeight => Metrics.Row.Height!.Value;
    private static double VersionRowGap => Metrics.Row.Margin.Bottom;
    private static double VersionRowStride => VersionRowHeight + VersionRowGap;
    private readonly List<XsrUiEntityId> _windowSpacers = [];
    private IReadOnlyList<MinecraftInstanceDescriptor>? _instanceSource;
    private IReadOnlyList<MinecraftInstanceDescriptor> _shown = [];
    private readonly Dictionary<string, int> _shownIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XsrUiEntityId> _windowRows = new(StringComparer.Ordinal);
    private string _shownRoot = "", _shownFilter = "";
    private int _windowFirst = -1, _windowCount = -1;
    private string? _windowPinned;
    private XsrUiEntityId _observedFocus;
    private XsrUiFileDrag? _selectedTransfer;
    private HashSet<string> _runningDirectories = new(Nexa.Core.PathIdentity.Comparer);

    private IReadOnlyList<MinecraftInstanceDescriptor> PrepareShownVersions(MinecraftLibrarySnapshot snapshot, string filter)
    {
        if (ReferenceEquals(_instanceSource, snapshot.Instances) && _shownRoot == snapshot.RootDirectory && _shownFilter == filter)
            return _shown;
        if (_shownRoot != snapshot.RootDirectory)
        {
            foreach (var child in _shell.Tree.Children(_entities["LibraryVersionRows"]).ToArray()) _views.Remove(child);
            _dynamic.Clear(_entities["LibraryVersionRows"]); _windowSpacers.Clear();
            _versions.Clear(); _actions.Clear(); _windowRows.Clear();
            _observedFocus = default;
        }
        _instanceSource = snapshot.Instances;
        _shownRoot = snapshot.RootDirectory; _shownFilter = filter;
        _shown = filter.Length == 0 ? snapshot.Instances : [.. snapshot.Instances.Where(instance =>
            instance.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) || instance.VersionId.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || instance.Metadata.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true || instance.Metadata.Group?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
            || instance.Metadata.Tags?.Any(tag => tag?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true) == true)];
        _shownIndices.Clear();
        for (int index = 0; index < _shown.Count; index++) _shownIndices.Add(_shown[index].Id, index);
        _transferSelection.IntersectWith(_shownIndices.Keys);
        if (_transferAnchor is not null && !_shownIndices.ContainsKey(_transferAnchor)) _transferAnchor = null;
        _windowFirst = -1;
        return _shown;
    }

    private void SyncVersionWindow(MinecraftLibrarySnapshot snapshot, bool refresh)
    {
        XsrUiEntityId host = _entities["LibraryVersionRows"];
        XsrUiScroll scroll = _shell.Tree.GetComponent<XsrUiScroll>(host)!;
        double viewportHeight = _shell.Renderer.TryGetScrollSnapshot(host, out var geometry)
            ? Math.Max(1, geometry.ViewportHeight) : Math.Max(VersionRowHeight, _shell.Renderer.Viewport.Height);
        XsrUiEntityId focused = _shell.Renderer.Focused;
        int focusedIndex = -1;
        for (XsrUiEntityId ancestor = focused; ancestor.IsAssigned && _shell.Tree.IsAlive(ancestor); ancestor = _shell.Tree.Parent(ancestor))
        {
            if (_versions.TryGetValue(ancestor, out var version) && _shownIndices.TryGetValue(version.Id, out int index))
            { focusedIndex = index; break; }
        }
        if (_observedFocus != focused && focusedIndex >= 0 && IsKeyboard(focused))
        {
            double top = focusedIndex * VersionRowStride;
            double target = top < scroll.OffsetY ? top : top + VersionRowHeight > scroll.OffsetY + viewportHeight
                ? top + VersionRowHeight - viewportHeight : scroll.OffsetY;
            target = Math.Clamp(target, 0, Math.Max(0, _shown.Count * VersionRowStride - VersionRowGap - viewportHeight));
            if (target != scroll.OffsetY)
            {
                _shell.Renderer.FinishScrollInertia(host);
                scroll.OffsetY = target;
                _shell.Tree.MarkDirty(host, XsrUiDirtyKinds.Layout);
            }
        }
        _observedFocus = focused;
        double offset = double.IsFinite(scroll.OffsetY) ? Math.Max(0, scroll.OffsetY) : 0;
        // Whole-shell height is a conservative bound while the page's previous geometry may
        // be stale after resize. Extra leading coverage also covers the renderer's tail clamp.
        int viewportRows = (int)Math.Ceiling(Math.Max(VersionRowHeight, _shell.Renderer.Viewport.Height) / VersionRowStride);
        int first = Math.Max(0, (int)Math.Min(Math.Floor(offset / VersionRowStride), Math.Max(0, _shown.Count - 1)) - viewportRows - 4);
        int count = Math.Min(_shown.Count - first, viewportRows * 2 + 8);
        string? pinned = focusedIndex >= 0 && (focusedIndex < first || focusedIndex >= first + count) ? _shown[focusedIndex].Id : null;
        if (!refresh && first == _windowFirst && count == _windowCount && pinned == _windowPinned) return;
        _windowFirst = first; _windowCount = count; _windowPinned = pinned;

        if (refresh)
        {
            _runningDirectories = _store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var sessions)
                ? new(_store.ReadCollection<MinecraftProcessSnapshot>(sessions).Items
                    .Where(item => item.State is MinecraftProcessState.Created or MinecraftProcessState.Running)
                    .Select(item => item.InstanceDirectory), Nexa.Core.PathIdentity.Comparer) : new(Nexa.Core.PathIdentity.Comparer);
            _selectedTransfer = VersionTransfer(_transferSelection.Select(id => _shownIndices[id]).Order()
                .Select(index => _shown[index].DirectoryPath));
        }
        List<int> order = new(count + 1);
        for (int index = first; index < first + count; index++) order.Add(index);
        if (pinned is not null) { order.Add(focusedIndex); order.Sort(); }
        HashSet<string> wanted = new(order.Select(index => _shown[index].Id), StringComparer.Ordinal);
        foreach (string id in _windowRows.Keys.ToArray())
        {
            if (wanted.Contains(id)) continue;
            XsrUiEntityId row = _windowRows[id];
            _shell.Tree.Walk(row, entity => { _actions.Remove(entity); return true; });
            _versions.Remove(row); _windowRows.Remove(id); _views.Remove(row); _dynamic.Remove(row);
        }
        foreach (var spacer in _windowSpacers) _dynamic.Remove(spacer);
        _windowSpacers.Clear();
        List<XsrUiEntityId> arranged = [];
        int cursor = 0;
        foreach (int index in order)
        {
            VersionSpacer(host, (index - cursor) * VersionRowStride, arranged);
            MinecraftInstanceDescriptor instance = _shown[index];
            bool created = !_windowRows.TryGetValue(instance.Id, out XsrUiEntityId row);
            if (created)
            {
                row = Append(host, "VersionSelectionRow.d.nxml", VersionRowData(instance, snapshot));
                _windowRows.Add(instance.Id, row);
                _versions.Add(row, (snapshot.RootDirectory, instance.Id));
                _actions[row] = (snapshot.RootDirectory, instance.Id);
                _shell.Tree.SetComponent(row, new XsrUiModifiedClick(XsrSemanticId.Parse("ui.versions.toggle-transfer"),
                    XsrSemanticId.Parse("ui.versions.extend-transfer"), XsrSemanticId.Parse("ui.versions.add-range")));
                var nodes = Nodes(row);
                foreach (string key in new[] { "LibraryRowModify", "LibraryRowSettings", "LibraryRowDelete" })
                    _actions[nodes[key + ":" + RowSuffix(instance.Id)]] = (snapshot.RootDirectory, instance.Id);
            }
            arranged.Add(row);
            if (created || refresh) ProjectVersionRow(row, instance, snapshot);
            cursor = index + 1;
        }
        if (cursor < _shown.Count) VersionSpacer(host, (_shown.Count - cursor) * VersionRowStride - VersionRowGap, arranged);
        _dynamic.Reorder(host, arranged);
    }

    private void VersionSpacer(XsrUiEntityId host, double height, List<XsrUiEntityId> arranged)
    {
        if (height <= 0) return;
        var spacer = _dynamic.Append(host, DesktopMarkup.Dynamic("VersionSelectionSpacer.d.nxml", new Dictionary<string, string>
        { ["key"] = "LibraryVersionSpacer." + Number(_windowSpacers.Count), ["height"] = Number(height) }));
        _windowSpacers.Add(spacer); arranged.Add(spacer);
    }

    private XsrUiEntityId NavigateVersionFocus(XsrUiEntityId origin, bool forward)
    {
        if (_disposed || _shell.Stage.Navigation.Current != Page) return default;
        XsrUiEntityId row = origin;
        while (row.IsAssigned && !_versions.ContainsKey(row)) row = _shell.Tree.Parent(row);
        if (!row.IsAssigned || !_shownIndices.TryGetValue(_versions[row].Id, out int index)) return default;
        List<XsrUiEntityId> controls = VersionFocusControls(row);
        int next = controls.IndexOf(origin) + (forward ? 1 : -1);
        if (next >= 0 && next < controls.Count) return controls[next];
        index += forward ? 1 : -1;
        if (index < 0 || index >= _shown.Count) return default;
        XsrUiEntityId host = _entities["LibraryVersionRows"];
        if (!_windowRows.ContainsKey(_shown[index].Id))
        {
            _shell.Renderer.FinishScrollInertia(host);
            _shell.Tree.GetComponent<XsrUiScroll>(host)!.OffsetY = index * VersionRowStride;
            _shell.Tree.MarkDirty(host, XsrUiDirtyKinds.Layout);
            SyncVersionWindow(Snapshot, refresh: false);
        }
        controls = VersionFocusControls(_windowRows[_shown[index].Id]);
        return forward ? controls[0] : controls[^1];
    }

    private List<XsrUiEntityId> VersionFocusControls(XsrUiEntityId row)
    {
        List<XsrUiEntityId> controls = [];
        _shell.Tree.Walk(row, entity =>
        {
            if (_shell.Tree.GetComponent<XsrUiInput>(entity) is { Focusable: true, Enabled: true }) controls.Add(entity);
            return true;
        });
        return controls;
    }

    private XsrUiFileDrag VersionTransfer(IEnumerable<string> directories)
    {
        string[] paths = directories.ToArray();
        return new(paths, XsrUiFileDragEffects.Copy | XsrUiFileDragEffects.Link
            | (paths.Any(_runningDirectories.Contains) ? XsrUiFileDragEffects.None : XsrUiFileDragEffects.Move),
            XsrSemanticId.Parse("ui.versions.refresh"));
    }

    private void ProjectVersionRow(XsrUiEntityId row, MinecraftInstanceDescriptor instance, MinecraftLibrarySnapshot snapshot)
    {
        Reload(row, VersionRowData(instance, snapshot));
        _shell.Tree.SetComponent(row, _transferSelection.Contains(instance.Id) ? _selectedTransfer! : VersionTransfer([instance.DirectoryPath]));
        _shell.Tree.SetComponent(row, new XsrUiContextMenu([
            new(Copy("menu.modify"),XsrSemanticId.Parse("ui.versions.modify")),
            new(Copy("menu.settings"),XsrSemanticId.Parse("ui.versions.settings")),
            new(Copy("menu.delete"),XsrSemanticId.Parse("ui.versions.delete")),
        ]));
        var image = _shell.Tree.GetComponent<XsrUiImage>(Nodes(row)["LibraryRowIcon:" + RowSuffix(instance.Id)])!;
        image.Raster = instance.Icon is { } icon ? new(icon, []) { FitToBounds = true, AspectRatio = (double)icon.Width / icon.Height } : null;
    }
}
