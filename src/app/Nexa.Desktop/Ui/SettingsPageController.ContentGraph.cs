using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private XsrUiEntityId _graphSearch, _graphCanvas, _graphBody;
    private string _graphFilter = "";
    private InstanceContentGraph? _graphIdentity;
    private XsrUiGraph? _graph;
    private readonly HashSet<XsrUiEntityId> _graphActions = [];
    private void ResetContentGraph()
    {
        _graphFilter = ""; _graphIdentity = null; _graph = null;
        _graphSearch = _graphCanvas = _graphBody = default;
    }
    private void ReleaseContentGraph()
    {
        ResetContentGraph();
        if (_management is { } snapshot) _management = snapshot with { ContentGraph = null };
    }
    private XsrUiEntityId _graphSelectionWorkspace;
    private int? _graphSelectionKey;
    private readonly Dictionary<int, XsrUiEntityId> _graphDependencyRows = [];
    private readonly Dictionary<(int Edge, int Provider), XsrUiEntityId> _graphProviderRows = [];
    private readonly Dictionary<int, XsrUiEntityId> _graphConsumerRows = [];
    private void BuildContentGraph(InstanceManagementSnapshot snapshot)
    {
        if (snapshot.ContentGraph is not { } graph)
        {
            var loading = AppendContent(_sections, "SettingsContentLoading.d.nxml", new() { ["loading"] = "true", ["enabled"] = ContentFlag(_managementWrite is null), ["message"] = ContentTerm("graphLoading") });
            BindContentAction(ContentNodes(loading), ContentKey("refresh"), RefreshManagement); return;
        }
        if (!ReferenceEquals(_graphIdentity, graph))
        {
            _graphIdentity = graph;
            var nodes = graph.Nodes.Select(n => new XsrUiGraphNode(n.Key, n.Id, DesktopMarkup.Visual(!n.Enabled ? "settings-content-graph-disabled" : n.InCycle ? "settings-content-graph-cycle" : n.Dependencies.Any(d => d.State == InstanceDependencyState.Missing) ? "settings-content-graph-missing" : "settings-content-graph-enabled").Foreground));
            var edges = graph.Nodes.SelectMany(n => n.Dependencies.SelectMany(d => d.Providers.Select(p => new XsrUiGraphEdge(n.Key, graph.Nodes[p].Key))));
            _graph = new(nodes, edges); var metric = ContentMetric("graph"); _graph.Fit(metric.Width!.Value, metric.Height!.Value);
        }
        _contentWorkspace = AppendContent(_sections, "SettingsContentGraph.d.nxml", new() { ["enabled"] = "true", ["count"] = ContentFormat("GraphCount", ("count", graph.Nodes.Count.ToString(System.Globalization.CultureInfo.CurrentCulture))), ["notice"] = graph.Notice is { } notice ? _shell.Renderer.LocalizeText(notice) : "", ["hasNotice"] = ContentFlag(graph.Notice is not null), ["unknown"] = ContentFormat("UnknownFiles", ("count", graph.UnknownFiles.ToString(System.Globalization.CultureInfo.CurrentCulture))), ["hasUnknown"] = ContentFlag(graph.UnknownFiles > 0) });
        var entities = ContentNodes(_contentWorkspace); _graphSearch = entities["ContentGraphSearch"]; _graphCanvas = entities["ContentGraphCanvas"]; _graphBody = entities["ContentGraphSelection"];
        _shell.Renderer.SetTextInputValue(_graphSearch, _graphFilter); _shell.Tree.SetComponent(_graphCanvas, _graph!);
        BindContentAction(entities, "GraphZoomOut", () => ZoomGraph(.8)); BindContentAction(entities, "GraphZoomIn", () => ZoomGraph(1.25));
        BindContentAction(entities, ContentKey("refresh"), RefreshManagement);
        BindContentAction(entities, "GraphFit", () => { var node = _shell.Renderer.Render().Nodes.First(n => n.Entity == _graphCanvas); _graph!.Fit(node.Rect.Width, node.Rect.Height); _shell.Tree.MarkDirty(_graphCanvas, XsrUiDirtyKinds.Paint); });
        BindContentAction(entities, "ContentGraphCanvas", RenderGraphSelection); _graphActions.Add(_graphCanvas);
        _graphSelectionWorkspace = default; _graphSelectionKey = null; _graphDependencyRows.Clear(); _graphProviderRows.Clear(); _graphConsumerRows.Clear(); RenderGraphSelection();
    }

    private void ReloadContentGraph()
    {
        if (_graphIdentity is not { } graph) return;
        ReloadContent(_contentWorkspace, ("count", ContentFormat("GraphCount", ("count", graph.Nodes.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)))),
            ("notice", graph.Notice is { } notice ? _shell.Renderer.LocalizeText(notice) : ""), ("unknown", ContentFormat("UnknownFiles", ("count", graph.UnknownFiles.ToString(System.Globalization.CultureInfo.CurrentCulture)))));
        UpdateContentGraph(); RenderGraphSelection();
    }
    private void ZoomGraph(double factor)
    {
        if (_graph is null) return;
        _graph.Zoom = Math.Clamp(_graph.Zoom * factor, .05, 6);
        _shell.Tree.MarkDirty(_graphCanvas, XsrUiDirtyKinds.Paint);
    }
    private void UpdateContentGraph()
    {
        if (_selected != "contentgraph" || !_graphSearch.IsAssigned || !_shell.Tree.IsAlive(_graphSearch) || _graph is null) return;
        string draft = _shell.Tree.GetComponent<XsrUiTextInput>(_graphSearch)!.ReadDraft();
        if (draft == _graphFilter) return;
        _graph.Filter = _graphFilter = draft;
        _shell.Tree.MarkDirty(_graphCanvas, XsrUiDirtyKinds.Paint);
    }
    private void RenderGraphSelection()
    {
        if (!_graphBody.IsAssigned || !_shell.Tree.IsAlive(_graphBody)) return;
        var node = _graphIdentity?.Nodes.FirstOrDefault(n => n.Key == _graph?.Selected);
        Dictionary<string, string> data = new() { ["empty"] = ContentFlag(node is null), ["selected"] = ContentFlag(node is not null), ["title"] = node is null ? "" : ContentFormat("GraphTitle", ("id", StripContentFormatting(node.Id)), ("version", node.Version)), ["state"] = ContentTerm(node?.Enabled == true ? "enabledState" : "disabledState"), ["incomplete"] = ContentFlag(node?.DependenciesComplete == false), ["cycle"] = ContentFlag(node?.InCycle == true), ["consumers"] = ContentFormat("Consumers", ("count", (node?.Consumers.Count ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture))) };
        if (!_shell.Tree.IsAlive(_graphSelectionWorkspace)) _graphSelectionWorkspace = AppendContent(_graphBody, "SettingsContentGraphSelection.d.nxml", data);
        else ReloadContent(_graphSelectionWorkspace, data.Select(pair => (pair.Key, pair.Value)).ToArray());
        var nodes = ContentNodes(_graphSelectionWorkspace);
        if (_graphSelectionKey != node?.Key)
        {
            foreach (var slot in new[] { nodes["ContentGraph.Dependencies"], nodes["ContentGraph.Consumers"] }) foreach (var child in _shell.Tree.Children(slot).ToArray()) RemoveContentView(child);
            _graphSelectionKey = node?.Key; _graphDependencyRows.Clear(); _graphProviderRows.Clear(); _graphConsumerRows.Clear();
        }
        if (node is null) return;
        ApplyContentFormatting(nodes["ContentGraph.SelectedTitle"], ContentFormat("GraphTitle", ("id", node.Id), ("version", node.Version)));
        for (int edgeIndex = 0; edgeIndex < node.Dependencies.Count; edgeIndex++)
        {
            var edge = node.Dependencies[edgeIndex]; string title = ContentFormat("Dependency", ("id", edge.Id), ("requirement", edge.Requirement), ("state", DependencyLabel(edge.State)));
            if (!_graphDependencyRows.TryGetValue(edgeIndex, out var row))
            { row = AppendContent(nodes["ContentGraph.Dependencies"], "SettingsContentGraphDependency.d.nxml", new() { ["title"] = StripContentFormatting(title) }); _graphDependencyRows[edgeIndex] = row; }
            else ReloadContent(row, ("title", StripContentFormatting(title)));
            var relation = ContentNodes(row); ApplyContentFormatting(relation["ContentGraph.DependencyTitle"], title); var providers = relation["ContentGraph.Providers"];
            foreach (int provider in edge.Providers.Take(8))
            {
                int key = _graphIdentity!.Nodes[provider].Key;
                if (!_graphProviderRows.TryGetValue((edgeIndex, provider), out var action))
                { action = AppendContent(providers, "SettingsContentGraphProvider.d.nxml", new() { ["key"] = "GraphProvider." + key, ["enabled"] = "true" }); _graphProviderRows[(edgeIndex, provider)] = action; _graphActions.Add(action); }
                BindContentAction(ContentNodes(action), "GraphProvider." + key, () => SelectGraphNode(key));
            }
        }
        for (int consumerIndex = 0; consumerIndex < node.Consumers.Count; consumerIndex++)
        {
            var dependent = _graphIdentity!.Nodes[node.Consumers[consumerIndex].Node];
            if (!_graphConsumerRows.TryGetValue(consumerIndex, out var action))
            { action = AppendContent(nodes["ContentGraph.Consumers"], "SettingsContentGraphConsumer.d.nxml", new() { ["key"] = "GraphConsumer." + dependent.Key, ["name"] = dependent.Id, ["enabled"] = "true" }); _graphConsumerRows[consumerIndex] = action; _graphActions.Add(action); }
            else ReloadContent(action, ("name", dependent.Id));
            BindContentAction(ContentNodes(action), "GraphConsumer." + dependent.Key, () => SelectGraphNode(dependent.Key));
        }
        _shell.Tree.MarkDirty(_graphBody, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    private void SelectGraphNode(int key)
    {
        _graph!.Selected = key;
        var point = _graph.Snapshot().Points.First(n => n.Key == key);
        _graph.PanX = -point.X * _graph.Zoom; _graph.PanY = -point.Y * _graph.Zoom;
        _shell.Tree.MarkDirty(_graphCanvas, XsrUiDirtyKinds.Paint); RenderGraphSelection();
    }


    private string DependencyLabel(InstanceDependencyState state) => ContentTerm(state switch
    {
        InstanceDependencyState.Present => "dependencyPresent",
        InstanceDependencyState.Disabled => "dependencyDisabled",
        InstanceDependencyState.Missing => "dependencyMissing",
        InstanceDependencyState.Ambiguous => "dependencyAmbiguous",
        InstanceDependencyState.Runtime => "dependencyRuntime",
        InstanceDependencyState.NestedCandidate => "dependencyNested",
        _ => "dependencyUnknown"
    });
}
