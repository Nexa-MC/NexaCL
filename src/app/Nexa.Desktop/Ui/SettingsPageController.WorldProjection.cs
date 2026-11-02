using System.Globalization;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly Lazy<Dictionary<string, string>> WorldTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("SettingsWorldTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("World workspace terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private readonly Dictionary<XsrUiEntityId, Dictionary<string, string>> _worldViews = [];
    private static readonly Lazy<double> WorldResponsiveWidth = new(() => DesktopMarkup.Load("SettingsWorldMetrics.nxml").Root.Children.Single(node => node.Key == "responsive").MinWidth!.Value);
    private string? _worldPresentationDirection;
    private string WorldActionDirection => _shell.Renderer.Viewport.Width >= WorldResponsiveWidth.Value ? "Horizontal" : "Vertical";
    private void UpdateWorldPresentation()
    {
        string direction = WorldActionDirection;
        if (!_visible || _worldPresentationDirection == direction) return;
        _worldPresentationDirection = direction;
        RefreshWorldDetails();
        if (_selected == "servers" && _shell.Tree.IsAlive(_serverWorkspaceRoot)) BuildServers();
        if (_selected == "modpack" && _shell.Tree.IsAlive(_exportRoot) && _management is { } snapshot) { CaptureExportDrafts(); BuildModpackExport(snapshot); }
        if (_onlineQuery is not null && _shell.Tree.IsAlive(_onlineSection)) RenderOnlineContent();
    }
    private bool TryReloadWorldWorkspaces()
    {
        if (_instanceDirectory is null || _instance is null || !_shell.Tree.IsAlive(_sections)) return false;
        if (_selected == "servers" && _serverInstance == _instance && _shell.Tree.IsAlive(_serverWorkspaceRoot) && _shell.Tree.Parent(_serverWorkspaceRoot) == _sections)
        { BuildServers(); return true; }
        if (_selected == "modpack" && _exportInstance == _instance && _management is { } snapshot && _shell.Tree.IsAlive(_exportRoot) && _shell.Tree.Parent(_exportRoot) == _sections)
        { CaptureExportDrafts(); BuildModpackExport(snapshot); return true; }
        return false;
    }
    private static string WorldKey(string name) => WorldTerms.Value["action." + name];
    private string WorldTerm(string key) => _shell.Renderer.LocalizeText(WorldTerms.Value[key]);
    private string WorldFormat(string name, Dictionary<string, string> data)
        => DesktopMarkup.Dynamic("SettingsWorld" + name + ".d.nxml", data, _shell.Renderer.LocalizeText).Root.Content!;
    private static string WorldNumber(long value) => value.ToString(CultureInfo.InvariantCulture);
    private Dictionary<string, XsrUiEntityId> WorldView(XsrUiEntityId slot, ref XsrUiEntityId root, string name, Dictionary<string, string> data)
    {
        data = new(data, StringComparer.Ordinal) { ["actionDirection"] = WorldActionDirection, ["language"] = DesktopResourceText.Language(_store) };
        string? loadingFlag = name switch
        {
            "Health" => "checking",
            "Details" or "Online" => "reading",
            "Servers" or "Export" or "ModpackUpdate" => "loading",
            _ => null,
        };
        if (loadingFlag is not null) data["loadingWeight"] = LoadingWeight(data[loadingFlag] == "true");
        var fragment = DesktopMarkup.Dynamic("SettingsWorld" + name + ".d.nxml", data, _shell.Renderer.LocalizeText);
        if (!_shell.Tree.IsAlive(root) || _shell.Tree.Parent(root) != slot)
        {
            foreach (var retired in _worldViews.Keys.Where(entity => !_shell.Tree.IsAlive(entity)).ToArray()) _worldViews.Remove(retired);
            root = _dynamic.Append(slot, fragment);
        }
        else if (!_worldViews.TryGetValue(root, out var before) || before.Count != data.Count || data.Any(pair => before.GetValueOrDefault(pair.Key) != pair.Value))
            _dynamic.Reload(root, fragment);
        _worldViews[root] = data;
        return CatalogNodes(root);
    }
    private void RemoveWorldView(ref XsrUiEntityId root)
    {
        if (_shell.Tree.IsAlive(root)) _shell.Tree.Walk(root, entity => { _managementActions.Remove(entity); _contentActions.Remove(entity); return true; });
        _worldViews.Remove(root); _dynamic.Remove(root); root = default;
    }
    private void RetireWorldRows(Dictionary<string, XsrUiEntityId> rows, IEnumerable<string> retained)
    {
        var current = retained.ToHashSet(StringComparer.Ordinal);
        foreach (var key in rows.Keys.Where(key => !current.Contains(key) || !_shell.Tree.IsAlive(rows[key])).ToArray())
        {
            var root = rows[key];
            if (_shell.Tree.IsAlive(root)) _shell.Tree.Walk(root, entity => { _managementActions.Remove(entity); _contentActions.Remove(entity); return true; });
            _worldViews.Remove(root); _dynamic.Remove(root); rows.Remove(key);
        }
    }
    private Dictionary<string, XsrUiEntityId> WorldRow(XsrUiEntityId slot, Dictionary<string, XsrUiEntityId> rows, string key, string template, Dictionary<string, string> data)
    {
        var root = rows.GetValueOrDefault(key);
        var nodes = WorldView(slot, ref root, template, data); rows[key] = root; return nodes;
    }
    private void BindWorldAction(Dictionary<string, XsrUiEntityId> nodes, string key, Action action)
    {
        string? detail = _contentDetail?.Name;
        var online = _onlineQuery; var servers = _serverList;
        BindInstanceToolAction(nodes, key, () =>
        {
            if (_contentDetail?.Name != detail || _selected == "servers" && !ReferenceEquals(servers, _serverList)
                || online is not null && online != _onlineQuery) return;
            action();
        });
    }
}
