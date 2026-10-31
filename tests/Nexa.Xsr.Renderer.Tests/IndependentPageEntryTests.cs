using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static void PageEntryTracksBelongToControls()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var stage = new XsrUiStage(tree, store);
        tree.SetComponent(stage.ContentHost, new XsrUiTransition { Key = "navigation", OffsetY = 16 });
        stage.Renderer.Viewport = new(400, 400);
        stage.Renderer.Render();
        var page = tree.Create("page"); tree.SetComponent(page, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        var controls = new List<XsrUiEntityId>();
        foreach (string name in new[] { "one", "two", "three" })
        {
            var child = tree.Create(name); tree.SetComponent(child, new XsrUiText(name));
            tree.SetComponent(child, new XsrUiElement { Height = 30 }); tree.Attach(child, page); controls.Add(child);
        }
        stage.Navigation.Push(page);
        var scene = stage.Renderer.Render();
        AssertEqual(0d, stage.Renderer.GetTransitionOffsetY(stage.ContentHost));
        for (int i = 0; i < controls.Count; i++)
        {
            AssertEqual(8d, stage.Renderer.GetTransitionOffsetY(controls[i]));
            AssertEqual(i, scene.Nodes.Single(node => node.Entity == controls[i]).TransitionEntryOrder);
            stage.Renderer.SetTransitionOffsetY(controls[i], 0);
        }
        stage.Renderer.Render(); stage.Renderer.Render();
        foreach (var control in controls) AssertEqual(0d, stage.Renderer.GetTransitionOffsetY(control));
    }
}
