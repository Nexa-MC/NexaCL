using Nexa.Core.Patching;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static void RenderingKeepsAutomaticEntriesOnMajorBodiesOnly()
    {
        XsrUiTree tree = new();
        XsrUiEntityId root = tree.Create("root");
        tree.SetComponent(root, new XsrUiElement { Width = 10, Height = 10 });
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build());
        renderer.SetRoot(root);
        _ = renderer.Render();
        IReadOnlyList<PatchTarget> catalog = Patch.Catalog;
        PatchTarget Target(string member) => catalog.Single(target =>
            target.Signature.Contains("Nexa.Xsr.Renderer." + member + "``", StringComparison.Ordinal));

        // Per-node helpers and value plumbing are inventoried, not intercepted.
        foreach (string member in new[] { "XsrUiTree::IsAlive", "XsrUiTree::MarkDirty", "XsrUiEntityId::get_IsAssigned", "XsrUiRenderer::IsVisible" })
        {
            PatchTarget helper = Target(member);
            AssertEqual(PatchTargetStatus.Internal, helper.Status);
            AssertEqual(-1, helper.Slot);
            AssertThrows<ArgumentException>(() => Patch.Install(helper, PatchPhase.Head, _ => { }));
        }
        AssertFalse(catalog.Any(target => target.Status == PatchTargetStatus.Instrumented
            && target.Signature.Contains("Nexa.Xsr.Renderer.XsrUiSceneNode::", StringComparison.Ordinal)));

        // The frame entry remains a real extension point.
        PatchTarget frame = Target("XsrUiRenderer::RenderCore");
        AssertEqual(PatchTargetStatus.Instrumented, frame.Status);
        int frames = 0;
        using (Patch.Install(frame, PatchPhase.Head, _ => frames++))
        {
            tree.MarkDirty(root, XsrUiDirtyKinds.Paint);
            _ = renderer.Render();
        }
        tree.MarkDirty(root, XsrUiDirtyKinds.Paint);
        _ = renderer.Render();
        AssertEqual(1, frames);
    }
}
