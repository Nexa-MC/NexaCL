using Nexa.Core.Patching;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static void RenderingRegistersNoAutomaticEntries()
    {
        XsrUiTree tree = new();
        XsrUiEntityId root = tree.Create("root");
        tree.SetComponent(root, new XsrUiElement { Width = 10, Height = 10 });
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build());
        renderer.SetRoot(root);
        AssertEqual(1, renderer.Render().Count);

        // The assemblies below rendering register their functions when the renderer first runs.
        IReadOnlyList<PatchTarget> catalog = Patch.Catalog;
        AssertTrue(catalog.Any(target => target.Status == PatchTargetStatus.Instrumented
            && target.Signature.Contains("Nexa.Xsr.State.", StringComparison.Ordinal)));
        // Rendering itself offers nothing to intercept: extensions use the UI interface.
        AssertFalse(catalog.Any(target => target.Signature.Contains("Nexa.Xsr.Renderer.", StringComparison.Ordinal)));
    }
}
