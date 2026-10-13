using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

/// <summary>External product text only; selection never constructs presentation nodes.</summary>
internal static class ProductPresentationText
{
    private static readonly Lazy<Dictionary<string, string>> Terms = new(() =>
    {
        Dictionary<string, string> terms = new(StringComparer.Ordinal);
        foreach (string asset in new[] { "ProductCopyTerms.nxml", "ProductCopyNativeTerms.nxml", "ProductCopySettingsTerms.nxml", "ProductCopyProgramTerms.nxml", "ProductCopyCatalogTerms.nxml" })
        {
            var nodes = DesktopMarkup.Load(asset).Root.Children;
            if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
                || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
                throw new NxmlCompileException("Product copy terms must be keyed literal text.");
            foreach (var node in nodes)
                if (!terms.TryAdd(node.Key!, node.Content!)) throw new NxmlCompileException("Product copy terms require unique keys.");
        }
        return terms;
    });
    internal static string Raw(string key) => Terms.Value[key];
    internal static string Term(string key, Func<string, string>? localize = null) => localize?.Invoke(Raw(key)) ?? Raw(key);
    internal static string Format(string name, Func<string, string>? localize, params (string Key, string Value)[] data)
    {
        var fragment = DesktopMarkup.Dynamic("ProductCopy" + name + ".d.nxml",
            data.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), localize);
        if (fragment.Root.Recipe != NxmlRuntimeRecipe.Text || fragment.Root.Content is null || fragment.Root.Children.Count != 0)
            throw new NxmlCompileException("Product copy formats must contain one text node.");
        return fragment.Root.Content;
    }
}
