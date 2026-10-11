using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

/// <summary>Validated external copy and literal named formats; never builds a visual tree.</summary>
internal static class DesktopPresentationText
{
    private static readonly Lazy<Dictionary<string, string>> Terms = new(() =>
    {
        var nodes = DesktopMarkup.Load("OperationTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Operation terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    internal static string Raw(string key) => Terms.Value[key];
    internal static string Term(string key, Func<string, string> localize) => localize(Raw(key));
    internal static string Format(string template, Dictionary<string, string> data, Func<string, string>? localize = null)
    {
        var text = DesktopMarkup.Dynamic("Operation" + template + ".d.nxml", data, localize).Root;
        if (text.Recipe != NxmlRuntimeRecipe.Text || text.Content is null || text.Bindings.Count != 0 || text.Children.Count != 0)
            throw new NxmlCompileException("Operation formats must produce literal text only.");
        return text.Content;
    }
}
