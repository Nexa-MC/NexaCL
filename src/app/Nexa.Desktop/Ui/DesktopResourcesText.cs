using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal static class DesktopResourcesText
{
    private static readonly Lazy<Dictionary<string, string>> Terms = new(() => ReadValues("ResourceTerms.nxml"));
    private static readonly Lazy<Dictionary<string, string>> ProjectionDefaults = new(() => ReadValues("ResourceProjectionDefaults.nxml"));
    private static readonly Lazy<Dictionary<string, NxmlIrNode>> Metrics = new(() =>
    {
        var nodes = DesktopMarkup.Load("ResourceMetrics.nxml").Root.Children;
        if (nodes.Any(node => string.IsNullOrWhiteSpace(node.Key) || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Resource viewport metrics must be keyed literal leaves.");
        return nodes.ToDictionary(node => node.Key!, StringComparer.Ordinal);
    });
    private static Dictionary<string, string> ReadValues(string name)
    {
        var nodes = DesktopMarkup.Load(name).Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key) || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Resource values must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    }
    internal static string Raw(string key) => Terms.Value[key];
    internal static Dictionary<string, string> Defaults() => new(ProjectionDefaults.Value, StringComparer.Ordinal);
    internal static NxmlIrNode Metric(string key) => Metrics.Value[key];
    internal static string Format(string name, IReadOnlyDictionary<string, string> data, Func<string, string> localize)
    {
        var node = DesktopMarkup.Dynamic("ResourceText" + name + ".d.nxml", data, localize).Root;
        if (node.Recipe != NxmlRuntimeRecipe.Text || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0)
            throw new NxmlCompileException("Resource formats must produce literal text only.");
        return node.Content;
    }
}
