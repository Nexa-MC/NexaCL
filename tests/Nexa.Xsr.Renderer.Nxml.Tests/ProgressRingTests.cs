using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void ProgressRingsRetainSquareGeometryAndReloadFacts()
    {
        var store = new XsrStateStoreBuilder().Build();
        var tree = new XsrUiTree();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" Layout=\"Overlay\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><ProgressRing Key="ring" Value="{data value}" Indeterminate="{data spinning}" /></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["value"] = ".25", ["spinning"] = "false" };
        var ring = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var component = tree.GetComponent<XsrUiProgress>(ring)!;
        var renderer = new XsrUiRenderer(tree, store);
        renderer.SetRoot(slot); renderer.Viewport = new(400, 300);
        renderer.SetProgressPresentation(ring, .25);
        var node = renderer.Render().Nodes.Single(node => node.Entity == ring);
        AssertEqual(40d, node.Rect.Width); AssertEqual(40d, node.Rect.Height);
        AssertEqual(new XsrUiProgressRingSnapshot(false, .25), node.ProgressRing!.Value);
        data["spinning"] = "true"; data["value"] = ".8";
        owner.Reload(ring, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(component, tree.GetComponent<XsrUiProgress>(ring)));
        node = renderer.Render().Nodes.Single(node => node.Entity == ring);
        AssertTrue(node.ProgressRing!.Value.Indeterminate);
        AssertEqual(.8, renderer.GetProgressTarget(ring));
        AssertEqual(40d, node.Rect.Width);
        AssertThrows<NxmlCompileException>(() => Compile("<Text Ring=\"true\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<Progress Indeterminate=\"true\" />"));
    }

    private static void InverseVisibilityIsBooleanOnlyAndRetainsContent()
    {
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><ContentHost><Text Content="retained" IsVisible="{data !loading}" /><ProgressRing IsVisible="{data loading}" /></ContentHost></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["loading"] = "true" };
        var compiled = template.Compile(data, NxmlComponentLibrary.Default);
        AssertFalse(compiled.Root.Children[0].IsVisible); AssertTrue(compiled.Root.Children[1].IsVisible);
        data["loading"] = "false";
        compiled = template.Compile(data, NxmlComponentLibrary.Default);
        AssertTrue(compiled.Root.Children[0].IsVisible); AssertFalse(compiled.Root.Children[1].IsVisible);
        data["loading"] = "maybe";
        AssertThrows<NxmlCompileException>(() => template.Compile(data, NxmlComponentLibrary.Default));
        AssertThrows<NxmlParseException>(() => _ = new NxmlDynamicTemplate("<nxml-dynamic><Text Content=\"{data !loading}\" /></nxml-dynamic>"));
    }
}
