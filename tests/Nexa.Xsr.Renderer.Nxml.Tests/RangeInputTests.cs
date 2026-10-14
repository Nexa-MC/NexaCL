using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void RangeInputReloadRetainsDraftAndAppliesCommittedValues()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"range-slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><Node Recipe="RangeInput" Kind="Slider" Role="Slider" Key="range" Label="volume"
              Minimum="{data min}" Maximum="{data max}" Step="{data step}" RangeValue="{data value}"
              Focusable="true" Clickable="true" Height="36" Command="ui.theme.range" /></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["min"] = "0", ["max"] = "100", ["step"] = "5", ["value"] = "20" };
        var original = template.Compile(data, NxmlComponentLibrary.Default);
        var entity = owner.Append(slot, original);
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(slot); renderer.Render();
        AssertTrue(renderer.Focus(entity)); AssertTrue(renderer.SetRangeValue(entity, 73));
        var range = tree.GetComponent<XsrUiRangeInput>(entity)!;
        AssertEqual(75d, range.Value);
        owner.Reload(entity, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(range, tree.GetComponent<XsrUiRangeInput>(entity)));
        AssertEqual(75d, range.Value); AssertEqual(entity, renderer.Focused);
        data["max"] = "50"; owner.Reload(entity, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(50d, range.Value);
        data["value"] = "30"; owner.Reload(entity, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(30d, range.Value);
        AssertEqual("ui.theme.range", tree.GetComponent<XsrUiCommandBinding>(entity)!.Command.Value);
        renderer.Render(); int invalidations = 0; tree.RenderInvalidated += (_, _) => invalidations++;
        owner.Reload(entity, template.Compile(data, NxmlComponentLibrary.Default)); AssertEqual(0, invalidations);
    }

    private static void RangeInputFactsRejectInvalidLoadsAtomically()
    {
        foreach (string attributes in new[] { "Minimum=\"100\" Maximum=\"0\"", "Step=\"0\"", "Step=\"0.0000000000001\"", "Maximum=\"Infinity\"", "RangeValue=\"101\"" })
            AssertThrows<NxmlCompileException>(() => Compile("<Node Recipe=\"RangeInput\" Role=\"Slider\" " + attributes + " />"));
        AssertThrows<NxmlCompileException>(() => Compile("<Node Minimum=\"0\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<Node Recipe=\"RangeInput\" Role=\"Button\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<Node Recipe=\"RangeInput\" Role=\"Slider\"><Text Content=\"child\" /></Node>"));
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"range-slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var original = new NxmlDynamicTemplate("""<nxml-dynamic><Node Recipe="RangeInput" Role="Slider" Key="range" Height="36" RangeValue="20" /></nxml-dynamic>""").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
        var entity = owner.Append(slot, original); var range = tree.GetComponent<XsrUiRangeInput>(entity)!;
        var invalid = new NxmlHostIr(original.Root with { Maximum = double.NaN, Height = 80 });
        var parent = tree.Create("invalid-range-parent");
        int count = tree.LiveEntities.Count;
        AssertThrows<NxmlLoadException>(() => NxmlUiLoader.Load(invalid, tree, store, parent));
        AssertEqual(count, tree.LiveEntities.Count);
        AssertEqual(0, tree.Children(parent).Count);
        AssertEqual(36d, tree.GetComponent<XsrUiElement>(entity)!.Height!.Value);
        AssertEqual(100d, range.Maximum); AssertEqual(20d, range.Value);
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("Slider{RangeValue:30;}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("Slider{Minimum:30;}"));
    }
}
