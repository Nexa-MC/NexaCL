using Nexa.Xsr.Renderer.Nxml;
namespace Nexa.Desktop.Ui;

internal static class DesktopOverlayText
{
    private static readonly Lazy<Dictionary<string, string>> Terms = new(() =>
    {
        var nodes = DesktopMarkup.Load("OverlayTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Overlay terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    internal static string Raw(string key) => Terms.Value[key];
    internal static string Format(string name, Dictionary<string, string> data, Func<string, string>? localize = null)
    {
        var node = DesktopMarkup.Dynamic("Overlay" + name + ".d.nxml", data, localize).Root;
        if (node.Recipe != NxmlRuntimeRecipe.Text || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0)
            throw new NxmlCompileException("Overlay formats must produce literal text only.");
        return node.Content;
    }
}
