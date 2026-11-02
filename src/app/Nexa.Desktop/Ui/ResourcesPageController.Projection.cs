using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    private sealed record ResourceView(string Template, Dictionary<string, string> Data);
    private readonly Dictionary<XsrUiEntityId, ResourceView> _resourceViews = [];
    private readonly Dictionary<XsrUiEntityId, (XsrUiEntityId Root, string Field)> _resourceTexts = [];
    private XsrUiEntityId _workspace, _filters, _detailHeading, _optionalActions;

    private static string Flag(bool value) => value ? "true" : "false";
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    private string L(string key) => _shell.Renderer.LocalizeText(key);
    private string ResourceFormat(string name, params (string Key, string Value)[] data) =>
        DesktopResourcesText.Format(name, data.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), _shell.Renderer.LocalizeText);
    private static string ResourceIdentity(string value) => value.Length is > 0 and <= 80 && !value.Any(character => char.IsWhiteSpace(character) || character is '{' or '}')
        ? value : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string FavoriteClass(bool active) => DesktopResourcesText.Raw(active ? "favorite-active-style" : "favorite-idle-style");

    private void IndexResources(XsrUiEntityId root) => _shell.Tree.Walk(root, entity =>
    { _entities[_shell.Tree.Name(entity)] = entity; return true; });

    private XsrUiEntityId AppendResource(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        var root = _resourceDynamic.Append(slot, DesktopMarkup.Dynamic(template, data));
        _resourceViews[root] = new(template, data); IndexResources(root); return root;
    }
    private void ReloadResource(XsrUiEntityId root, params (string Key, string Value)[] values)
    {
        if (!_resourceViews.TryGetValue(root, out var view)) return;
        bool changed = false;
        foreach (var (key, value) in values)
            if (view.Data.GetValueOrDefault(key) != value) { view.Data[key] = value; changed = true; }
        if (changed) _resourceDynamic.Reload(root, DesktopMarkup.Dynamic(view.Template, view.Data));
    }
    private void WorkspaceResource(params (string Key, string Value)[] values) => ReloadResource(_workspace, values);
    private XsrUiEntityId BindResource(string key, Action action, List<XsrUiEntityId>? actions = null)
    {
        var entity = _entities[key]; _actions[entity] = action; actions?.Add(entity); return entity;
    }
    private void RegisterResourceText(XsrUiEntityId entity, XsrUiEntityId root, string field) => _resourceTexts[entity] = (root, field);
    private void SetResourceText(XsrUiEntityId entity, string text)
    {
        if (_resourceTexts.TryGetValue(entity, out var projection)) ReloadResource(projection.Root, (projection.Field, text));
    }
    private void ForgetResourceViews(HashSet<XsrUiEntityId> removed)
    {
        foreach (var entity in removed) { _resourceViews.Remove(entity); _resourceTexts.Remove(entity); _actions.Remove(entity); }
        foreach (var key in _entities.Where(pair => removed.Contains(pair.Value)).Select(pair => pair.Key).ToArray()) _entities.Remove(key);
    }
    private void RemoveResource(XsrUiEntityId root)
    {
        var removed = new HashSet<XsrUiEntityId>(); _shell.Tree.Walk(root, entity => { removed.Add(entity); return true; });
        _resourceDynamic.Remove(root); ForgetResourceViews(removed);
    }
    private bool AdmitResource(XsrUiEntityId entity)
    {
        if (_disposed || !_shell.Tree.IsAlive(entity) || !_actions.ContainsKey(entity)) return false;
        if (_shell.Tree.GetComponent<XsrUiInput>(entity) is { Enabled: false }) return false;
        for (var ancestor = entity; ancestor.IsAssigned; ancestor = _shell.Tree.Parent(ancestor))
        {
            if (_shell.Tree.GetComponent<XsrUiElement>(ancestor) is { IsVisible: false }) return false;
            if (ancestor == _shell.Stage.Navigation.Current) return true;
        }
        return false;
    }
    private void InitializeResourceTrack(string name, Action<int> selected)
    {
        var track = _entities[name]; var options = _shell.Tree.Children(track).Where(entity => _shell.Tree.GetComponent<XsrUiSelection>(entity) is not null).ToArray();
        _segments.Add((track, double.Parse(_resourceViews[_workspace].Data[name + ".0_width"], CultureInfo.InvariantCulture)));
        foreach (var (option, index) in options.Select((option, index) => (option, index)))
        {
            if (_shell.Tree.GetComponent<XsrUiSelection>(option)!.IsSelected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(track)!.Selected = option;
            _actions[option] = () =>
            {
                if (name == "ResourceNetwork" && (_sourcePolicyForced || !_region.IsMainlandChina)) return;
                SelectResourceTrack(name, index); selected(index);
            };
        }
    }
    private void SelectResourceTrack(string name, int index)
    {
        var track = _entities[name]; var options = _shell.Tree.Children(track).Where(entity => _shell.Tree.GetComponent<XsrUiSelection>(entity) is not null).ToArray();
        WorkspaceResource(options.Select((option, position) => (_shell.Tree.Name(option) + "_selected", Flag(position == index))).ToArray());
        _shell.Tree.GetComponent<XsrUiSegmentedTrack>(track)!.Selected = options[index];
    }
    private void UpdateSegmentWidths()
    {
        var metric = DesktopResourcesText.Metric("selector"); List<(string Key, string Value)> values = [];
        foreach (var (track, minimum) in _segments)
        {
            double total = 0; string name = _shell.Tree.Name(track);
            foreach (var option in _shell.Tree.Children(track))
            {
                if (_shell.Tree.GetComponent<XsrUiText>(option) is not { } caption) continue;
                string text = _shell.Renderer.LocalizeText(caption.Content);
                double desired = Math.Max(minimum, text.Sum(character => character > 127 ? metric.Width!.Value : metric.Height!.Value) + metric.Padding.Left);
                values.Add((_shell.Tree.Name(option) + "_width", Number(desired))); total += desired;
            }
            if (name != "ResourceCategory") values.Add((name + "_width", Number(total)));
        }
        WorkspaceResource(values.ToArray());
    }
    private void UpdateResourceChecks() => ReloadResource(_filters,
        ("hideInstalled", L(_hideInstalled ? DesktopResourcesText.Raw("copy.4210925779f6") : DesktopResourcesText.Raw("copy.b55ae05faf55"))),
        ("hideLibraries", L(_hideLibraries ? DesktopResourcesText.Raw("copy.057f07d84234") : DesktopResourcesText.Raw("copy.cedf91a64972"))));
}
