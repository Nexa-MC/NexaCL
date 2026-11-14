using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static XsrUiRenderer BuildRows(int rows, int cells, out XsrUiTree tree, out XsrUiEntityId root)
    {
        tree = new XsrUiTree();
        root = tree.Create("root");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        for (int row = 0; row < rows; row++)
        {
            XsrUiEntityId line = tree.Create($"row-{row}");
            tree.SetComponent(line, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
            tree.Attach(line, root);
            for (int cell = 0; cell < cells; cell++)
            {
                XsrUiEntityId item = tree.Create($"cell-{row}-{cell}");
                tree.SetComponent(item, new XsrUiElement { Width = 10, Height = 10 });
                tree.Attach(item, line);
            }
        }

        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build());
        renderer.SetRoot(root);
        return renderer;
    }

    private static void FrameClearsEveryDirtyFlagBelowItsRootOnly()
    {
        XsrUiRenderer renderer = BuildRows(6, 5, out XsrUiTree tree, out XsrUiEntityId root);
        XsrUiEntityId retained = tree.Create("retained-page");
        XsrUiEntityId retainedChild = tree.Create("retained-child");
        tree.Attach(retainedChild, retained);

        // The first frame is dirty everywhere: every row and every cell carries its own flags.
        XsrUiScene first = renderer.Render();
        AssertEqual(1 + 6 + 30, first.Count);
        AssertFalse(tree.HasDirtySubtree(root));
        AssertFalse(tree.HasDirtyLayoutSubtree(root));
        // A page that is not mounted below the root keeps its dirt until it is presented.
        AssertSequence(new[] { retained, retainedChild }, tree.DirtyEntities().ToArray());
        AssertTrue(ReferenceEquals(first, renderer.Render()));

        // Scattered dirt in different rows and at different depths settles in the same frame.
        XsrUiEntityId rowTwo = tree.Children(root)[2];
        XsrUiEntityId rowFive = tree.Children(root)[5];
        tree.MarkDirty(tree.Children(rowTwo)[4], XsrUiDirtyKinds.Paint);
        tree.MarkDirty(rowFive, XsrUiDirtyKinds.Layout);
        tree.MarkDirty(tree.Children(rowFive)[0], XsrUiDirtyKinds.Structure);
        XsrUiScene second = renderer.Render();
        AssertTrue(second.Version > first.Version);
        AssertFalse(tree.HasDirtySubtree(root));
        foreach (XsrUiEntityId row in tree.Children(root))
        {
            AssertFalse(tree.HasDirtySubtree(row));
            AssertFalse(tree.HasDirtyLayoutSubtree(row));
        }
        AssertSequence(new[] { retained, retainedChild }, tree.DirtyEntities().ToArray());
    }

    private static void SceneArraysFollowNodeCountChangesWithoutAliasing()
    {
        XsrUiRenderer renderer = BuildRows(3, 4, out XsrUiTree tree, out XsrUiEntityId root);
        XsrUiScene initial = renderer.Render();
        AssertEqual(16, initial.Count);
        XsrUiEntityId firstRow = tree.Children(root)[0];
        XsrUiEntityId lastCell = tree.Children(tree.Children(root)[2])[3];

        // Same count: the next scene is a different array with the same nodes in the same order.
        tree.MarkDirty(lastCell, XsrUiDirtyKinds.Paint);
        XsrUiScene repainted = renderer.Render();
        AssertEqual(16, repainted.Count);
        AssertFalse(ReferenceEquals(initial.Nodes, repainted.Nodes));
        AssertSequence(initial.Nodes.Select(node => node.Entity).ToArray(), repainted.Nodes.Select(node => node.Entity).ToArray());

        // Growth: new nodes appear in tree order and earlier scenes keep what they presented.
        XsrUiEntityId[] added = new XsrUiEntityId[21];
        for (int index = 0; index < added.Length; index++)
        {
            added[index] = tree.Create($"added-{index}");
            tree.SetComponent(added[index], new XsrUiElement { Width = 10, Height = 10 });
            tree.Attach(added[index], firstRow);
        }
        XsrUiScene grown = renderer.Render();
        AssertEqual(16 + added.Length, grown.Count);
        AssertEqual(16, repainted.Count);
        AssertEqual(lastCell, repainted.Nodes[15].Entity);
        AssertEqual(added[^1], grown.Nodes[1 + 1 + 4 + added.Length - 1].Entity);
        AssertEqual(lastCell, grown.Nodes[grown.Count - 1].Entity);

        // Shrink: the scene holds exactly the visible nodes, never a stale tail.
        for (int index = 0; index < added.Length; index++) tree.Destroy(added[index]);
        tree.GetComponent<XsrUiElement>(lastCell)!.IsVisible = false;
        tree.MarkDirty(lastCell, XsrUiDirtyKinds.Layout);
        XsrUiScene shrunk = renderer.Render();
        AssertEqual(15, shrunk.Count);
        AssertFalse(shrunk.Nodes.Any(node => node.Entity == lastCell || !tree.IsAlive(node.Entity)));
        AssertEqual(16 + added.Length, grown.Count);
        AssertEqual(lastCell, grown.Nodes[grown.Count - 1].Entity);
    }

    private static void StableContentReachesEveryDescendantButNoSibling()
    {
        XsrUiRenderer renderer = BuildRows(3, 2, out XsrUiTree tree, out XsrUiEntityId root);
        XsrUiEntityId stableRow = tree.Children(root)[1];
        tree.SetComponent(stableRow, new XsrUiStableContent());
        XsrUiEntityId nested = tree.Create("nested");
        tree.SetComponent(nested, new XsrUiElement { Width = 4, Height = 4 });
        tree.Attach(nested, tree.Children(stableRow)[0]);

        XsrUiScene scene = renderer.Render();
        HashSet<XsrUiEntityId> stable = [stableRow, .. tree.Children(stableRow), nested];
        AssertEqual(1 + 3 + 6 + 1, scene.Count);
        foreach (XsrUiSceneNode node in scene.Nodes)
            AssertEqual(stable.Contains(node.Entity), node.SuppressEntryAnimation);
    }
}
