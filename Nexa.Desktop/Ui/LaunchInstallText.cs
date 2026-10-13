using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

/// <summary>External launch/install terms, formats and virtual collection metrics.</summary>
internal static class LaunchInstallText
{
    private static readonly Lazy<Dictionary<string, string>> Terms = new(() =>
    {
        var nodes = DesktopMarkup.Load("LaunchInstallTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Launch/install terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private static readonly Lazy<Dictionary<string, NxmlIrNode>> Metrics = new(() =>
        DesktopMarkup.Load("LaunchInstallMetrics.nxml").Root.Children.ToDictionary(node => node.Key!, StringComparer.Ordinal));
    internal static string Raw(string key) => Terms.Value[key];
    internal static NxmlIrNode Metric(string key) => Metrics.Value[key];
    internal static string Format(string template, Dictionary<string, string> data, Func<string, string> localize)
    {
        var text = DesktopMarkup.Dynamic("LaunchInstallText" + template + ".d.nxml", data, localize).Root;
        if (text.Recipe != NxmlRuntimeRecipe.Text || text.Content is null || text.Bindings.Count != 0 || text.Children.Count != 0)
            throw new NxmlCompileException("Launch/install formats must produce literal text only.");
        return text.Content;
    }
}
