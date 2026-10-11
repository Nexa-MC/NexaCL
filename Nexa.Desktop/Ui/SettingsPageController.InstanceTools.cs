using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Core;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly Dictionary<XsrUiEntityId, (string Template, Dictionary<string, string> Data)> _instanceToolViews = [];
    private static readonly Lazy<Dictionary<string, string>> InstanceToolTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("InstanceToolTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Instance tool terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private string InstanceToolTerm(string key) => _shell.Renderer.LocalizeText(InstanceToolTerms.Value[key]);
    private string InstanceToolFormat(string template, Dictionary<string, string> data)
        => DesktopMarkup.Dynamic("SettingsInstance" + template + ".d.nxml", data, _shell.Renderer.LocalizeText).Root.Content!;
    private string InstanceToolSize(long bytes)
    {
        string unit = bytes >= 1024L * 1024 * 1024 ? "GiB" : bytes >= 1024 * 1024 ? "MiB" : "KiB";
        double divisor = unit == "GiB" ? 1024d * 1024 * 1024 : unit == "MiB" ? 1024d * 1024 : 1024d;
        return InstanceToolFormat("Tool" + unit, new() { ["value"] = (bytes / divisor).ToString("0.##", CultureInfo.CurrentCulture) });
    }
    private static string InstanceToolIdentity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    private XsrUiEntityId AppendInstanceTool(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        var root = _dynamic.Append(slot, DesktopMarkup.Dynamic(template, data, _shell.Renderer.LocalizeText));
        _instanceToolViews[root] = (template, data); return root;
    }
    private void ReloadInstanceTool(XsrUiEntityId root, Dictionary<string, string> data)
    {
        if (!_shell.Tree.IsAlive(root) || !_instanceToolViews.TryGetValue(root, out var view)) return;
        if (view.Data.Count == data.Count && data.All(item => view.Data.GetValueOrDefault(item.Key) == item.Value)) return;
        _dynamic.Reload(root, DesktopMarkup.Dynamic(view.Template, data, _shell.Renderer.LocalizeText));
        _instanceToolViews[root] = (view.Template, data);
    }
    private void ResetInstanceToolControls()
    {
        _instanceToolViews.Clear(); _workspaceRoot = _offlineRoot = default;
        _workspaceNodes.Clear(); _workspaceRowsProjection = null;
        _workspaceCaptionDocument = null; _workspacePreviewRoot = default;
    }
    private void BindInstanceToolAction(Dictionary<string, XsrUiEntityId> nodes, string key, Action action)
    {
        var source = nodes[key]; string? scope = _instance; string page = _selected;
        _managementActions[source] = () =>
        {
            if (_disposed || !_visible || _selected != page || !PathIdentity.Comparer.Equals(scope, _instance)
                || IsDevelopmentPage(page) && !InstanceDevelopmentMode
                || _store.Read<long>(_revisionId).Value != _revision || !_shell.Tree.IsAlive(source)
                || _shell.Tree.GetComponent<XsrUiInput>(source)?.Enabled != true) return;
            for (var entity = source; entity.IsAssigned; entity = _shell.Tree.Parent(entity))
                if (_shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible == false) return;
            action();
        };
    }
    private static void ObserveRetiredInstanceToolRead(Task? retired)
    {
        if (retired is not null)
            _ = retired.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
