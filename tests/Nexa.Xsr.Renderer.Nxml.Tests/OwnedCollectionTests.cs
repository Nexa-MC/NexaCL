using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;
namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void OwnedReorderRetainsStateAndRejectsForeignPermutations()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" Scroll=\"true\" Height=\"120\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel Key="{data key}"><TextInput Key="edit" Height="30" Label="edit" /><Dynamic Key="nested" /></StackPanel></nxml-dynamic>
            """);
        var a = owner.Append(slot, template.Compile(new Dictionary<string, string> { ["key"] = "a" }, NxmlComponentLibrary.Default));
        var b = owner.Append(slot, template.Compile(new Dictionary<string, string> { ["key"] = "b" }, NxmlComponentLibrary.Default));
        var inputEntity = tree.Children(a)[0]; var input = tree.GetComponent<XsrUiTextInput>(inputEntity)!;
        var nested = owner.Append(tree.Children(a)[1], new NxmlDynamicTemplate("<nxml-dynamic><Text Content=\"nested\" /></nxml-dynamic>")
            .Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default));
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(slot); renderer.Render();
        AssertTrue(renderer.Focus(inputEntity)); AssertTrue(renderer.InsertText("retained draft"));
        var scroll = tree.GetComponent<XsrUiScroll>(slot)!; scroll.OffsetY = 7;
        owner.Reorder(slot, [b, a]); AssertSequence(new[] { b, a }, tree.Children(slot).ToArray());
        AssertEqual(inputEntity, renderer.Focused); AssertEqual("retained draft", input.ReadDraft()); AssertTrue(tree.IsAlive(nested));
        AssertTrue(ReferenceEquals(scroll, tree.GetComponent<XsrUiScroll>(slot))); AssertEqual(7d, scroll.OffsetY);
        foreach (var order in new XsrUiEntityId[][] { [a, a], [a], [a, nested] })
        { AssertThrows<NxmlLoadException>(() => owner.Reorder(slot, order)); AssertSequence(new[] { b, a }, tree.Children(slot).ToArray()); }
        AssertThrows<NxmlLoadException>(() => new NxmlDynamicContent(tree, store).Reorder(slot, [a, b]));
        AssertThrows<InvalidOperationException>(() => tree.ReorderChildren(slot, [a, a]));
        AssertSequence(new[] { b, a }, tree.Children(slot).ToArray());
        owner.Remove(b); AssertThrows<NxmlLoadException>(() => owner.Reorder(slot, [b, a]));
    }
    private static void LocalAnchorsAndViewportFactsPreserveLiveContracts()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var page = NxmlUiLoader.LoadDetached(Compile("<StackPanel Key=\"page\"><Button Key=\"anchor\" Label=\"anchor\" Height=\"40\"/><Dynamic Key=\"slot\" Layout=\"Overlay\" /></StackPanel>"), tree, store);
        var anchor = tree.Children(page)[0]; var slot = tree.Children(page)[1]; var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><Node Kind="Popup" Recipe="Element" Key="popup" ModalLayer="true">
              <StackPanel Key="card" Anchor="{data anchor}" AnchorGap="5" Scroll="true" ShowsVerticalIndicator="{data indicator}" StableContent="true" Height="80">
                <Text Content="{data text}" Height="240" />
              </StackPanel>
            </Node></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["anchor"] = "anchor", ["indicator"] = "false", ["text"] = "kept" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default)); var card = tree.Children(root)[0];
        AssertTrue(tree.GetComponent<XsrUiOverlayLayer>(root)!.IsModal);
        AssertTrue(tree.GetComponent<XsrUiStableContent>(card) is not null);
        var anchored = tree.GetComponent<XsrUiAnchoredOverlay>(card)!; AssertEqual(anchor, anchored.Anchor); AssertEqual(5d, anchored.Gap);
        var scroll = tree.GetComponent<XsrUiScroll>(card)!; scroll.OffsetY = 17;
        data["indicator"] = "true"; owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(scroll.ShowsVerticalIndicator); AssertEqual(17d, scroll.OffsetY);
        AssertTrue(ReferenceEquals(scroll, tree.GetComponent<XsrUiScroll>(card)));
        AssertTrue(ReferenceEquals(anchored, tree.GetComponent<XsrUiAnchoredOverlay>(card)));
        int before = tree.Count;
        foreach (var key in new[] { "missing", "card" })
        {
            var invalid = new Dictionary<string, string>(data) { ["anchor"] = key, ["text"] = "wrong" };
            AssertThrows<NxmlLoadException>(() => owner.Append(slot, template.Compile(invalid, NxmlComponentLibrary.Default)));
            AssertEqual(before, tree.Count); AssertThrows<NxmlLoadException>(() => owner.Reload(root, template.Compile(invalid, NxmlComponentLibrary.Default)));
            AssertEqual("kept", tree.GetComponent<XsrUiText>(tree.Children(card)[0])!.Content);
        }
        foreach (var xml in new[]{"<Text ModalLayer=\"true\" />","<Text Anchor=\"bad key\" />","<StackPanel AnchorGap=\"4\" />",
            "<StackPanel Anchor=\"anchor\" AnchorGap=\"-1\" />","<Text StableContent=\"maybe\" />"})
            AssertThrows<NxmlCompileException>(() => Compile(xml));
        // A repeated dynamic key in the closest scope is ambiguous, rather than selecting one arbitrarily.
        var buttons = new NxmlDynamicTemplate("<nxml-dynamic><Button Key=\"duplicate\" Label=\"target\" /></nxml-dynamic>").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
        owner.Append(slot, buttons); owner.Append(slot, buttons); before = tree.Count;
        data["anchor"] = "duplicate"; AssertThrows<NxmlLoadException>(() => owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default)));
        AssertEqual(before, tree.Count);
    }
}
