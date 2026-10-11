using Nexa.Sidecar.Protocol;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    private Guid _moduleActivation;
    private readonly Dictionary<ushort, XsrUiEntityId> _moduleNodes = [];
    private readonly Dictionary<ushort, XsrUiEntityId> _moduleSubmits = [];
    private readonly Dictionary<ushort, XsrUiEntityId> _moduleLabels = [];
    private readonly Dictionary<ushort, XsrUiNodeView> _moduleViews = [];
    private readonly Dictionary<ushort, XsrUiEntityId> _moduleFragments = [];
    private readonly List<XsrUiEntityId> _moduleActions = [];
    private CancellationTokenSource _moduleSourceStop = new();
    private bool _moduleSourceVisible;

    private void SyncSidecarSource(bool visible)
    {
        if (!_visible)
            while (_pending.TryDequeue(out _)) { }
        if (visible == _moduleSourceVisible) return;
        _moduleSourceVisible = visible;
        if (!visible)
        {
            int pending = _pending.Count;
            for (int index = 0; index < pending && _pending.TryDequeue(out var source); index++)
                if (_visible && !_moduleActions.Contains(source)) _pending.Enqueue(source);
            _moduleActivation = Guid.Empty; _moduleRevision = -1;
        }
        _moduleSourceStop.Cancel(); _moduleSourceStop.Dispose(); _moduleSourceStop = new();
    }
    private void RetireSidecarSource()
    {
        _moduleSourceStop.Cancel();
        foreach (var entity in _moduleActions) _actions.Remove(entity);
        _moduleActions.Clear(); _moduleNodes.Clear(); _moduleSubmits.Clear(); _moduleLabels.Clear(); _moduleViews.Clear(); _moduleFragments.Clear();
    }
    private void ProjectSidecarModules()
    {
        if (_sidecarUi is null) return;
        var snapshot = _store.Read<XsrUiModuleSnapshot>(_sidecarUi.ModuleState);
        if (snapshot.Revision == _moduleRevision) return;
        _moduleRevision = snapshot.Revision;
        var module = snapshot.Value?.ModuleAt(_sidecarUi.CardIndex);
        var slot = _entities["ResourceExtensionCard"];
        WorkspaceResource(("extensionVisible", Flag(module is not null)));
        if (_moduleActivation != (module?.Activation ?? Guid.Empty))
        {
            RetireSidecarSource(); _moduleSourceStop.Dispose(); _moduleSourceStop = new();
            _moduleActivation = module?.Activation ?? Guid.Empty;
            Clear(slot, _moduleActions);
            if (module is not null)
            {
                AppendResource(slot, "ResourceExtensionHeading.d.nxml", new() { ["title"] = module.Card.Title, ["body"] = module.Card.Body });
                foreach (var node in module.Nodes) CreateSidecarNode(module.Activation, node, node.Parent == 0 ? slot : _moduleNodes[node.Parent]);
            }
            _shell.Tree.GetComponent<XsrUiScroll>(slot)!.OffsetY = 0;
        }
        if (module is not null)
            foreach (var node in module.Nodes) UpdateSidecarNode(node);
        _shell.Tree.MarkDirty(slot, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }
    private void CreateSidecarNode(Guid activation, XsrUiNodeView node, XsrUiEntityId parent)
    {
        string name = "ResourcePluginNode." + node.Id;
        string template = node.Kind switch
        {
            SidecarUiNodeKind.Stack => "ResourceExtensionStack.d.nxml",
            SidecarUiNodeKind.Row => "ResourceExtensionRow.d.nxml",
            SidecarUiNodeKind.Button or SidecarUiNodeKind.Toggle => "ResourceExtensionButton.d.nxml",
            SidecarUiNodeKind.TextInput => "ResourceExtensionInput.d.nxml",
            SidecarUiNodeKind.Image => "ResourceExtensionImage.d.nxml",
            _ => "ResourceExtensionText.d.nxml",
        };
        var fragment = AppendResource(parent, template, new()
        { ["key"] = name, ["labelKey"] = name + ".Label", ["label"] = node.Label, ["caption"] = node.Label,
          ["submit"] = name + ".Submit", ["visible"] = Flag(node.Visible), ["enabled"] = Flag(node.Enabled) });
        var entity = _entities[name]; _moduleFragments[node.Id] = fragment;
        if (node.Kind is SidecarUiNodeKind.Button or SidecarUiNodeKind.Toggle)
            BindResource(name, () => RunSidecarAction(activation, node.Id), _moduleActions);
        if (node.Kind == SidecarUiNodeKind.TextInput)
        {
            _moduleLabels[node.Id] = _entities[name + ".Label"];
            _shell.Renderer.SetTextInputValue(entity, node.Value);
            _moduleSubmits[node.Id] = BindResource(name + ".Submit", () => RunSidecarAction(activation, node.Id), _moduleActions);
        }
        if (node.Kind == SidecarUiNodeKind.Image && node.Image is { } image)
            _shell.Tree.GetComponent<XsrUiImage>(entity)!.Raster = new(image,
                [new(new(0, 0, image.Width, image.Height), new(0, 0, 1, 1))]) { FitToBounds = true };
        _moduleNodes[node.Id] = entity;
    }
    private void UpdateSidecarNode(XsrUiNodeView node)
    {
        var entity = _moduleNodes[node.Id];
        ReloadResource(_moduleFragments[node.Id], ("visible", Flag(node.Visible)), ("enabled", Flag(node.Enabled)), ("label", node.Label));
        if (_moduleSubmits.TryGetValue(node.Id, out var submit))
        {
            if (!_moduleViews.TryGetValue(node.Id, out var previous) || previous.Value != node.Value)
                _shell.Renderer.SetTextInputValue(entity, node.Value);
        }
        else if (node.Kind is SidecarUiNodeKind.Text or SidecarUiNodeKind.Button or SidecarUiNodeKind.Toggle)
        {
            string caption = node.Kind == SidecarUiNodeKind.Toggle ? ResourceFormat("Toggle", ("marker", DesktopResourcesText.Raw(node.ToggleValue ? "checked-marker" : "unchecked-marker")), ("label", node.Label))
                : node.Value.Length == 0 ? node.Label : node.Label.Length == 0 ? node.Value : ResourceFormat("LabelValue", ("label", node.Label), ("value", node.Value));
            ReloadResource(_moduleFragments[node.Id], ("caption", caption));
        }
        _moduleViews[node.Id] = node;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }
    private void RunSidecarAction(Guid activation, ushort node)
    {
        if (_disposed || !_moduleSourceVisible || _sidecarUi is null || activation != _moduleActivation
            || !_moduleNodes.TryGetValue(node, out var entity) || !_shell.Tree.IsAlive(entity)) return;
        string? draft = _shell.Tree.GetComponent<XsrUiTextInput>(entity)?.ReadDraft();
        var token = _moduleSourceStop.Token;
        var task = _sidecarUi.Modules.DispatchAsync(_sidecarUi.CardIndex, activation, node, draft, token).AsTask();
        Wake(task, token);
        _ = ReportSidecarActionAsync(task, token);
    }
    private async Task ReportSidecarActionAsync(Task<Nexa.Xsr.XsrResult> task, CancellationToken token)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            if (!_disposed && !token.IsCancellationRequested && !result.IsSuccess)
                _presentation.Enqueue(() => { if (!token.IsCancellationRequested) _feedback?.Error(result.Error!.Message); });
        }
        catch (OperationCanceledException) { }
    }
}
