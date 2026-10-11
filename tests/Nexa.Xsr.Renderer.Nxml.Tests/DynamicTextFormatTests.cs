using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void DynamicTextFormatsLocalizeBeforeLiteralSubstitution()
    {
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><Text Label="Count {data count}: {data name}" Content="Count {data count}: {data name}" Localize="false" /></nxml-dynamic>""");
        var data = new Dictionary<string, string> { ["count"] = "2", ["name"] = "<Button/> {data count} {state secret}" };
        var formats = new List<string>();
        var ir = template.CompileLocalized(data, NxmlComponentLibrary.Default, text =>
        {
            formats.Add(text);
            return "Name {data name}; count {data count}";
        });
        AssertEqual("Name " + data["name"] + "; count 2", ir.Root.Content);
        AssertEqual(0, ir.Root.Bindings.Count);
        AssertTrue(formats.All(format => format == "Count {data count}: {data name}"));
        AssertFalse(ir.Root.Localize);
        AssertThrows<NxmlCompileException>(() => template.CompileLocalized(data, NxmlComponentLibrary.Default, _ => "Missing {data count}"));
        AssertThrows<NxmlCompileException>(() => template.CompileLocalized(data, NxmlComponentLibrary.Default, _ => "Extra {data name} {data count} {data other}"));
    }

    private static void DynamicTextFormatsRejectUnsupportedGrammarAndExpansion()
    {
        foreach (string attribute in new[] { "Key", "Command", "Class", "Width", "Recipe" })
            AssertThrows<NxmlParseException>(() => _ = new NxmlDynamicTemplate("<nxml-dynamic><Text " + attribute + "=\"prefix {data value}\" /></nxml-dynamic>"));
        foreach (string value in new[] { "text {data value", "text {data a b}", "text {state secret} {data value}", "text } {data value}", "text {data }" })
            AssertThrows<NxmlParseException>(() => _ = new NxmlDynamicTemplate("<nxml-dynamic><Text Content=\"" + value + "\" /></nxml-dynamic>"));
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("""<Text Content="Text {data value}" />""")));
        AssertThrows<NxmlParseException>(() => _ = new NxmlDynamicTemplate("<nxml-dynamic><Text Content=\"" + string.Concat(Enumerable.Repeat("{data value}", 257)) + "\" /></nxml-dynamic>"));
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><Text Content="{data value}/{data value}" /></nxml-dynamic>""");
        AssertThrows<NxmlCompileException>(() => template.Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default));
        AssertThrows<NxmlCompileException>(() => template.Compile(new Dictionary<string, string> { ["value"] = new('x', 600000) }, NxmlComponentLibrary.Default));
        AssertThrows<NxmlParseException>(() => template.CompileLocalized(new Dictionary<string, string> { ["value"] = "ok" }, NxmlComponentLibrary.Default, _ => new('x', 1048577)));
    }

    private static void DynamicTextFormatReloadPreservesIdentity()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.Load(NxmlCompiler.Compile(NxmlParser.Parse("<Dynamic Key=\"items\" />")), tree, store, tree.Create("host"));
        var dynamic = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><Text Key="caption" Content="Count: {data count}" Localize="false" /></nxml-dynamic>""");
        var data = new Dictionary<string, string> { ["count"] = "1" };
        var row = dynamic.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        data["count"] = "2";
        dynamic.Reload(row, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(row, tree.Children(slot).Single());
        AssertEqual("Count: 2", tree.GetComponent<XsrUiText>(row)!.Content);
        AssertThrows<NxmlCompileException>(() => dynamic.Reload(row, template.Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default)));
        AssertEqual("Count: 2", tree.GetComponent<XsrUiText>(row)!.Content);
    }
}
