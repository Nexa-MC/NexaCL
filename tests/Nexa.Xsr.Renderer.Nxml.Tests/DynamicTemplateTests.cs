using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void DynamicFragmentsAppendInOrder()
    {
        XsrUiTree tree = new();
        var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.Load(NxmlCompiler.Compile(NxmlParser.Parse("<Dynamic Key=\"items\" />")), tree, store, tree.Create("host"));
        var content = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><Text Key="row" Content="{data value}" Height="20" /></nxml-dynamic>""");
        var first = content.Append(slot, template.Compile(new Dictionary<string, string> { ["value"] = "first" }, NxmlComponentLibrary.Default));
        var second = content.Append(slot, template.Compile(new Dictionary<string, string> { ["value"] = "second" }, NxmlComponentLibrary.Default));
        AssertSequence(new[] { first, second }, tree.Children(slot).ToArray());
        AssertEqual("row", tree.Name(first)); AssertEqual("row", tree.Name(second));
        AssertEqual("second", tree.GetComponent<XsrUiText>(second)!.Content);
        AssertTrue(content.Remove(first)); AssertFalse(tree.IsAlive(first)); AssertTrue(tree.IsAlive(second));
        content.Clear(slot); AssertEqual(0, tree.Children(slot).Count);
    }

    private static void DynamicFailuresPreserveContents()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.Load(NxmlCompiler.Compile(NxmlParser.Parse("<Dynamic Key=\"items\" />")), tree, store, tree.Create("host"));
        var content = new NxmlDynamicContent(tree, store);
        var good = new NxmlDynamicTemplate("""<nxml-dynamic><Text Content="kept" /></nxml-dynamic>""").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
        var first = content.Append(slot, good);
        var bad = new NxmlDynamicTemplate("""<nxml-dynamic><Text Content="{state missing.value}" /></nxml-dynamic>""").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
        AssertThrows<NxmlLoadException>(() => content.Replace(slot, bad));
        AssertSequence(new[] { first }, tree.Children(slot).ToArray());
        var replacement = content.Replace(slot, good);
        AssertFalse(tree.IsAlive(first)); AssertSequence(new[] { replacement }, tree.Children(slot).ToArray());
    }

    private static void DynamicDataIsLiteralAndBounded()
    {
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><Text Content="{data value}" /></nxml-dynamic>""");
        var data = new Dictionary<string, string> { ["value"] = "<Button Command=\"delete\" /> {state secret}" };
        var ir = template.Compile(data, NxmlComponentLibrary.Default);
        AssertEqual(data["value"], ir.Root.Content); AssertEqual(0, ir.Root.Bindings.Count);
        AssertThrows<NxmlCompileException>(() => template.Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default));
        data["value"] = new string('a', 1024 * 1024 + 1);
        AssertThrows<NxmlCompileException>(() => template.Compile(data, NxmlComponentLibrary.Default));
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("""<Text Content="{data value}" />""")));
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("<Dynamic />")));
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("""<Dynamic Key="slot"><Text Content="bad" /></Dynamic>""")));
    }

    private static void DynamicSlotsRejectStaleTargets()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var root = tree.Create("host");
        var slot = NxmlUiLoader.Load(NxmlCompiler.Compile(NxmlParser.Parse("<Dynamic Key=\"items\" />")), tree, store, root);
        var content = new NxmlDynamicContent(tree, store);
        var ir = new NxmlDynamicTemplate("""<nxml-dynamic><Dynamic Key="nested" /></nxml-dynamic>""").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
        AssertThrows<NxmlLoadException>(() => content.Append(root, ir));
        AssertThrows<NxmlLoadException>(() => content.Append(slot, NxmlCompiler.Compile(NxmlParser.Parse("<Text />"))));
        AssertThrows<NxmlLoadException>(() => NxmlUiLoader.Load(NxmlCompiler.Compile(NxmlParser.Parse("<Text />")), tree, store, slot));
        AssertEqual(0, tree.Children(slot).Count);
        var nested = content.Append(slot, ir); content.Append(nested, ir);
        tree.Destroy(slot); tree.Create("replacement");
        AssertThrows<NxmlLoadException>(() => content.Append(slot, ir)); AssertFalse(content.Remove(nested));
    }
}
