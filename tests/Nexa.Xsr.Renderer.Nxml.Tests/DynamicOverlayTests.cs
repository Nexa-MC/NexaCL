using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void DynamicOverlayPreservesPageInputAndChildOwnership()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build(); var intents = new XsrUiIntentBuffer();
        var stage = new XsrUiStage(tree, store, intents);
        stage.Renderer.Viewport = new(300, 200);
        stage.Navigation.Push(NxmlUiLoader.LoadDetached(Compile("""<Button Key="page" Width="120" Height="40" Label="page" Command="ui.page.test" />"""), tree, store));
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"overlay\" Layout=\"Overlay\" />"), tree, store);
        stage.Show(slot);
        stage.Renderer.Render();
        AssertTrue(tree.GetComponent<XsrUiStackPanel>(slot) is null);
        AssertTrue(stage.Renderer.PointerPressed(new(10, 10))); AssertTrue(stage.Renderer.PointerReleased(new(10, 10)));
        AssertEqual(1, intents.Count);
        var content = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><Button Key="dock" Label="dock" Command="ui.dock.test" Width="48" Height="48"
              HorizontalAlignment="End" VerticalAlignment="End" Margin="0,0,18,18" IsVisible="{data visible}" /></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["visible"] = "true" };
        var first = content.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var second = content.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var scene = stage.Renderer.Render();
        AssertEqual(new XsrUiRect(234, 134, 48, 48), scene.Nodes.Single(node => node.Entity == first).Rect);
        AssertEqual(scene.Nodes.Single(node => node.Entity == first).Rect, scene.Nodes.Single(node => node.Entity == second).Rect);
        AssertEqual(slot, tree.Parent(first));
        data["visible"] = "false";
        content.Reload(first, template.Compile(data, NxmlComponentLibrary.Default));
        content.Reload(second, template.Compile(data, NxmlComponentLibrary.Default));
        stage.Renderer.Render();
        AssertTrue(stage.Renderer.PointerPressed(new(10, 10))); AssertTrue(stage.Renderer.PointerReleased(new(10, 10)));
        AssertEqual(2, intents.Count);
        content.Clear(slot); stage.Dismiss(slot); tree.Destroy(slot);
        AssertFalse(tree.IsAlive(first)); AssertFalse(tree.IsAlive(second));
    }

    private static void DynamicSelectionAndLiteralTextReload()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var content = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><Button Key="row" Content="{data text}" Label="{data text}" Localize="false"
              Selected="{data selected}" Enabled="{data enabled}" Command="ui.log.select" /></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["text"] = "[stdout] user text", ["selected"] = "false", ["enabled"] = "false" };
        var row = content.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        AssertFalse(tree.GetComponent<XsrUiText>(row)!.Localize); AssertFalse(tree.GetComponent<XsrUiSemantic>(row)!.Localize);
        AssertFalse(tree.GetComponent<XsrUiSelection>(row)!.IsSelected); AssertFalse(tree.GetComponent<XsrUiInput>(row)!.Enabled);
        var selection = tree.GetComponent<XsrUiSelection>(row)!;
        data["selected"] = "true"; data["enabled"] = "true"; data["text"] = "[stderr] next";
        content.Reload(row, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(selection, tree.GetComponent<XsrUiSelection>(row)));
        AssertTrue(selection.IsSelected); AssertTrue(tree.GetComponent<XsrUiInput>(row)!.Enabled);
        AssertFalse(tree.GetComponent<XsrUiText>(row)!.Localize);
        AssertEqual("[stderr] next", tree.GetComponent<XsrUiSemantic>(row)!.Label!);
    }
}
