using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

public enum NxmlContributionMode { Append, Prepend, Hide, Replace, Wrap }
public sealed record NxmlDocumentContribution(NxmlPreparedDocument Content, NxmlContributionMode Mode);

/// <summary>Structural composition of compiled external documents; no source markup or product commands are invented here.</summary>
public static class NxmlDocumentComposition
{
    public static NxmlPreparedDocument Compose(this NxmlPreparedDocument document, IEnumerable<NxmlDocumentContribution> contributions)
    {
        var root = document.Root.Root;
        foreach (var contribution in contributions)
        {
            if (contribution.Content.Fragments.Count != 0) throw new NxmlLoadException("A contribution must be one compiled fragment.");
            var node = contribution.Content.Root.Root;
            root = contribution.Mode switch
            {
                NxmlContributionMode.Append => root with { Children = [.. root.Children, node] },
                NxmlContributionMode.Prepend => root with { Children = [node, .. root.Children] },
                NxmlContributionMode.Hide => root with { Children = [] },
                NxmlContributionMode.Replace => root with { Children = [node] },
                NxmlContributionMode.Wrap when node.Recipe is NxmlRuntimeRecipe.StackLayout or NxmlRuntimeRecipe.Element =>
                    root with { Children = [node with { Children = [.. node.Children, .. root.Children] }] },
                _ => throw new NxmlLoadException("Unsupported contribution container.")
            };
        }
        int count = 0; Count(root, 0);
        return document with { Root = new(root) };
        void Count(NxmlIrNode node, int depth)
        {
            if (depth >= 128 || ++count > 16384) throw new NxmlLoadException("Composed document node budget exceeded.");
            foreach (var child in node.Children) Count(child, depth + 1);
        }
    }
    /// <summary>Embeds a page in a Dynamic slot without claiming standalone page layout.</summary>
    public static NxmlPreparedDocument AsFragment(this NxmlPreparedDocument document) => document with
    { Root = new(document.Root.Root with { Recipe = NxmlRuntimeRecipe.StackLayout, Role = XsrUiSemanticRole.None, Height = null, Weight = 0 }) };
}
