using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void CommandCapsulesDeclareOnlyHorizontalCaptionScrollAndRetainOffsets()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var host = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"host\"/>"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><Button Key="bubble" Height="48" MaxWidth="420" HoverExpand="true"
              Scroll="true" ShowsHorizontalIndicator="true" UseVerticalWheelForHorizontalScroll="true"
              Content="{data caption}" Label="{data caption}" Command="test.bubble.action"/></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["caption"] = new('W', 240) };
        var bubble = owner.Append(host, template.Compile(data, NxmlComponentLibrary.Default));
        var scroll = tree.GetComponent<XsrUiScroll>(bubble)!;
        AssertTrue(scroll.ShowsHorizontalIndicator); AssertTrue(scroll.UseVerticalWheelForHorizontalScroll);
        AssertTrue(tree.GetComponent<XsrUiStackPanel>(bubble) is null);
        var renderer = new XsrUiRenderer(tree, store) { ReducedMotion = true, Viewport = new(850, 500) };
        renderer.SetRoot(host); renderer.Render(); renderer.Focus(bubble); renderer.Render();
        renderer.HandleKey(XsrUiKey.Right); renderer.Render();
        AssertTrue(scroll.OffsetX > 0);
        double offset = scroll.OffsetX;
        data["caption"] += " updated";
        owner.Reload(bubble, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(scroll, tree.GetComponent<XsrUiScroll>(bubble)));
        AssertEqual(offset, scroll.OffsetX);
        AssertEqual(bubble, renderer.Focused);
        foreach (string xml in new[]
        {
            "<Button Scroll=\"true\"/>",
            "<Button HoverExpand=\"true\" Scroll=\"true\" ShowsVerticalIndicator=\"true\"/>",
            "<Button HoverExpand=\"true\" Scroll=\"true\" StickToEnd=\"true\"/>",
            "<Button HoverExpand=\"true\" Scroll=\"true\" ScrollGesture=\"true\"/>",
        }) AssertThrows<NxmlCompileException>(() => Compile(xml));
    }
}
