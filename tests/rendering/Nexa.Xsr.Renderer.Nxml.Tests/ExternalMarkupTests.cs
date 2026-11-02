using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void ExternalComponentsComposeWithoutGeneratedTypes()
    {
        var library = new NxmlComponentLibrary(new Dictionary<string, string>
        {
            ["Inset"] = """<nxml-component><Node Recipe="StackLayout" Padding="12"><Node Recipe="Text" Content="inside" /></Node></nxml-component>""",
            ["Card"] = """<nxml-component><Inset Class="surface" /></nxml-component>""",
        });
        var document = NxmlParser.Parse("""<nxml-page><Card Key="card" Padding="20"><Node Recipe="Text" Content="appended" /></Card></nxml-page>""");
        var ir = NxmlCompiler.Compile(document, library);
        AssertEqual(20d, ir.Root.Padding.Left);
        AssertEqual(2, ir.Root.Children.Count);
        AssertEqual("inside", ir.Root.Children[0].Content);
        AssertEqual("appended", ir.Root.Children[1].Content);
    }

    private static void ExternalComponentsRejectCyclesAndExpansionBombs()
    {
        var cycle = new NxmlComponentLibrary(new Dictionary<string, string>
        {
            ["A"] = """<nxml-component><B /></nxml-component>""",
            ["B"] = """<nxml-component><A /></nxml-component>""",
        });
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("""<A />"""), cycle));
        var definitions = new Dictionary<string, string> { ["C0"] = """<nxml-component><Node /></nxml-component>""" };
        for (int i = 1; i < 20; i++) definitions.Add("C" + i, $"<nxml-component><Node><C{i - 1} /><C{i - 1} /></Node></nxml-component>");
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("""<C19 />"""), new(definitions)));
        AssertThrows<NxmlCompileException>(() => NxmlCompiler.Compile(NxmlParser.Parse("""<Missing />"""), cycle));
        AssertThrows<NxmlCompileException>(() => _ = new NxmlComponentLibrary(new Dictionary<string, string> { ["Bad"] = """<nxml-component><Node /><Node /></nxml-component>""" }));
        AssertThrows<DirectoryNotFoundException>(() => NxmlComponentLibrary.LoadDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    private static void NssRulesPreserveCascadeAndExplicitValues()
    {
        NssStyleSheet styles = NssStyleSheet.Parse("Node { FontSize: 12; Padding: 2; } .card { FontSize: 14; } #chosen { FontSize: 18; Background: #10203040; } .card { FontSize: 16; }");
        var ir = NxmlCompiler.Compile(NxmlParser.Parse("""<Node Key="chosen" Class="card" Padding="7" />"""), new(new Dictionary<string, string>()), styles);
        AssertEqual(18d, ir.Root.Visual!.Value.FontSize);
        AssertEqual(new XsrUiColor(16, 32, 48, 64), ir.Root.Visual.Value.Background);
        AssertEqual(7d, ir.Root.Padding.Left);
        var explicitStyle = NxmlCompiler.Compile(NxmlParser.Parse("""<Node Key="chosen" FontSize="21" />"""), new(new Dictionary<string, string>()), styles);
        AssertEqual(21d, explicitStyle.Root.Visual!.Value.FontSize);
        var library = new NxmlComponentLibrary(new Dictionary<string, string>
        {
            ["Box"] = """<nxml-component><Node Kind="Box" Width="12" /></nxml-component>""",
        });
        var defaults = NxmlCompiler.Compile(NxmlParser.Parse("<Box />"), library, NssStyleSheet.Parse("Box { Width: 24; }"));
        AssertEqual(24d, defaults.Root.Width!.Value);
        var overridden = NxmlCompiler.Compile(NxmlParser.Parse("""<Box Width="36" />"""), library, NssStyleSheet.Parse("Box { Width: 24; }"));
        AssertEqual(36d, overridden.Root.Width!.Value);
        AssertThrows<NxmlCompileException>(() => NssStyleSheet.Parse(".invalid { FontSize: NaN; }"));
        AssertThrows<NxmlCompileException>(() => NssStyleSheet.Parse(".invalid { Command: app.delete; }"));
        AssertThrows<NxmlCompileException>(() => NssStyleSheet.Parse("@import { Width: 10; }"));
        AssertThrows<NxmlCompileException>(() => NssStyleSheet.Parse(".invalid { Opacity: 2; }"));
    }
}
