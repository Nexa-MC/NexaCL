using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private sealed record ContentView(string Template, Dictionary<string, string> Data);
    private readonly Dictionary<XsrUiEntityId, ContentView> _contentViews = [];
    private readonly Dictionary<string, ContentFileView> _contentFileViews = new(StringComparer.Ordinal);
    private sealed record ContentFileView(XsrUiEntityId Root, XsrUiEntityId Icon, XsrUiEntityId Mark, InstanceContentEntry Item);
    private XsrUiEntityId _contentWorkspace, _contentParking, _contentBefore, _contentAfter, _contentMiddle, _contentEmpty, _contentUpdateWorkspace;
    private string? _contentWorkspaceInstance, _contentWorkspacePage, _contentPinnedName;
    private bool ContentContainsFocus(XsrUiEntityId root, XsrUiEntityId focus)
    {
        if (!_shell.Tree.IsAlive(root)) return false;
        for (var node = focus; _shell.Tree.IsAlive(node); node = _shell.Tree.Parent(node)) if (node == root) return true;
        return false;
    }
    private static readonly Lazy<Dictionary<string, string>> ContentTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("SettingsContentTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key) || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0)) throw new NxmlCompileException("Content terms require keyed literal text leaves.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private static string ContentKey(string key) => ContentTerms.Value["action." + key];
    private static readonly Lazy<Dictionary<string, NxmlIrNode>> ContentMetrics = new(() => DesktopMarkup.Load("SettingsContentMetrics.nxml").Root.Children.ToDictionary(node => node.Key!, StringComparer.Ordinal));
    private static NxmlIrNode ContentMetric(string key) => ContentMetrics.Value[key];
    private string ContentTerm(string key) => _shell.Renderer.LocalizeText(ContentTerms.Value[key]);
    private string ContentFormat(string name, params (string Key, string Value)[] data)
    {
        var node = DesktopMarkup.Dynamic("SettingsContentText" + name + ".d.nxml", data.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), _shell.Renderer.LocalizeText).Root;
        if (node.Recipe != NxmlRuntimeRecipe.Text || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0) throw new NxmlCompileException("Content formats require literal text leaves.");
        return node.Content;
    }
    private static string ContentNumber(double value) => value.ToString(CultureInfo.InvariantCulture);
    private static string ContentFlag(bool value) => value ? "true" : "false";
    private static bool SameContentFileIdentity(InstanceContentEntry left, InstanceContentEntry right) => left.Name == right.Name && left.Size == right.Size && left.ModifiedUtcTicks == right.ModifiedUtcTicks && left.IsDirectory == right.IsDirectory;
    private static string ContentFileKey(string name) => name.Length is > 0 and <= 80 && !name.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '{' or '}')
        ? name : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
    private XsrUiEntityId AppendContent(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        foreach (var stale in _contentViews.Keys.Where(entity => !_shell.Tree.IsAlive(entity)).ToArray()) _contentViews.Remove(stale);
        var root = _dynamic.Append(slot, DesktopMarkup.Dynamic(template, data));
        _contentViews[root] = new(template, data); return root;
    }
    private void ReloadContent(XsrUiEntityId root, params (string Key, string Value)[] values)
    {
        if (!_shell.Tree.IsAlive(root) || !_contentViews.TryGetValue(root, out var view)) return;
        var data = new Dictionary<string, string>(view.Data, StringComparer.Ordinal);
        foreach (var (key, value) in values) data[key] = value;
        if (view.Data.Count == data.Count && data.All(pair => view.Data.GetValueOrDefault(pair.Key) == pair.Value)) return;
        var projection = DesktopMarkup.Dynamic(view.Template, data);
        _dynamic.Reload(root, projection); _contentViews[root] = view with { Data = data };
    }
    private Dictionary<string, XsrUiEntityId> ContentNodes(XsrUiEntityId root)
    {
        Dictionary<string, XsrUiEntityId> nodes = new(StringComparer.Ordinal);
        _shell.Tree.Walk(root, entity => { nodes[_shell.Tree.Name(entity)] = entity; return true; }); return nodes;
    }
    private void RemoveContentView(XsrUiEntityId root)
    {
        if (_shell.Tree.IsAlive(root)) _shell.Tree.Walk(root, entity => { _managementActions.Remove(entity); _contentActions.Remove(entity); _contentSelectionRows.Remove(entity); _graphActions.Remove(entity); _contentViews.Remove(entity); return true; });
        _dynamic.Remove(root); _contentViews.Remove(root);
    }
    private bool ContentActionCurrent(XsrUiEntityId entity)
    {
        if (_disposed || !_visible || _shell.Stage.Navigation.Current != Page || !_shell.Tree.IsAlive(entity)) return false;
        if (_shell.Tree.GetComponent<XsrUiInput>(entity) is { } input && (!input.Enabled || input.BoundEnabled.IsAssigned && _store.ReadAppliedValue(input.BoundEnabled) is not true)) return false;
        bool Visible(XsrUiEntityId node)
        {
            var element = _shell.Tree.GetComponent<XsrUiElement>(node);
            return element?.BoundVisibility.IsAssigned == true ? _store.ReadAppliedValue(element.BoundVisibility) is true : element?.IsVisible != false;
        }
        bool ownedScope = false;
        for (var ancestor = entity; ancestor.IsAssigned; ancestor = _shell.Tree.Parent(ancestor))
        {
            if (!Visible(ancestor)) return false;
            ownedScope |= ancestor == Page || ancestor == _screenshotPreview;
            var parent = _shell.Tree.Parent(ancestor); if (!parent.IsAssigned) return ownedScope && ancestor == _shell.Stage.Root;
            var siblings = _shell.Tree.Children(parent).Where(Visible).ToArray(); int position = Array.IndexOf(siblings, ancestor);
            int modal = Array.FindLastIndex(siblings, sibling => _shell.Tree.GetComponent<XsrUiOverlayLayer>(sibling)?.IsModal == true);
            if (modal >= 0 && position < modal || _shell.Tree.GetComponent<XsrUiPager>(parent) is { } pager && position != pager.PageIndex) return false;
        }
        return false;
    }
    private void BindContentAction(Dictionary<string, XsrUiEntityId> nodes, string key, Action action)
    {
        var entity = nodes[key]; _managementActions[entity] = () => { if (ContentActionCurrent(entity)) action(); };
    }
    private string ContentIcon() => ContentTerms.Value["icon." + (_selected is "mods" or "resourcepacks" or "shaderpacks" or "saves" ? _selected : "screenshots")];
    private void BindContentImage(XsrUiEntityId entity, InstanceContentEntry item)
    {
        if (_shell.Tree.GetComponent<XsrUiImage>(entity) is not { } image) return;
        image.Raster = item.Icon is { } png ? new(png, [new(new(0, 0, png.Width, png.Height), new(0, 0, 1, 1))]) { FitToBounds = true } : null;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }
    private XsrUiEntityId AppendContentFact(XsrUiEntityId slot, string key, string value)
    {
        var root = AppendContent(slot, "SettingsContentFact.d.nxml", new() { ["key"] = "ContentFact." + key, ["valueKey"] = "ContentFact." + key + ".Value", ["label"] = ContentTerm("fact." + key), ["value"] = value });
        return ContentNodes(root)["ContentFact." + key + ".Value"];
    }
    private bool TryReloadContentManagement()
    {
        if (TryReloadWorldWorkspaces()) return true;
        if (_instanceDirectory is null || _instance is null || _contentWorkspaceInstance != _instance || _contentWorkspacePage != _selected || !_shell.Tree.IsAlive(_contentWorkspace)) return false;
        if (_contentDetail is not null) { ReloadContentDetail(); return true; }
        if (_selected == "contentgraph") { if (!ReferenceEquals(_management?.ContentGraph, _graphIdentity)) return false; ReloadContentGraph(); return true; }
        if (!_contentList.IsAssigned || !_shell.Tree.IsAlive(_contentList)) return false;
        ApplyContentFilter(); UpdateContentWindow(); ReloadContentManagement(); UpdateContentSelectionPresentation(); return true;
    }
    private void ReloadContentManagement()
    {
        if (_contentSnapshot is null || _management is null) return;
        var source = _management.Contents.FirstOrDefault(item => item.PageId == _selected);
        ReloadContent(_contentWorkspace, ("enabled", ContentFlag(_managementWrite is null)), ("timelineLabel", ContentTerm(_screenshotTimeline ? "gallery" : "timeline")), ("timelineVisible", ContentFlag(!_screenshotTimeline)), ("galleryVisible", ContentFlag(_screenshotTimeline)),
            ("count", ContentFormat("Count", ("count", _contentSnapshot.Entries.Count.ToString(CultureInfo.InvariantCulture)), ("total", (source?.Entries.Count ?? 0).ToString(CultureInfo.InvariantCulture)), ("incomplete", source?.Complete == false ? ContentTerm("incomplete") : ""))),
            ("error", _managementError ?? _contentSnapshot.Error ?? ""), ("hasError", ContentFlag(_managementError is not null || _contentSnapshot.Error is not null)));
    }
}
