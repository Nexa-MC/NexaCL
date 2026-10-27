using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void ScrollObservationsAreAdmittedBoundedAndRetained()
    {
        var data = new Dictionary<string, string> { ["height"] = "600" };
        var source = new NxmlExtensionProgram("Page.nxml", new Dictionary<string, string>
        {
            ["Page.nxml"] = """<StackPanel Key="list" Scroll="true" ScrollCommand="test.append"><Text Height="{data height}" /></StackPanel>"""
        });
        var before = source.Compile(data);
        AssertTrue(before.Commands.Contains(XsrSemanticId.Parse("test.append")));
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        using var page = new NxmlDocumentInstance(tree, store, before);
        var observation = tree.GetComponent<NxmlScrollObservation>(page.Root)!;
        AssertTrue(!observation.TryBegin(double.NaN, 100, 600, out _));
        AssertTrue(!observation.TryBegin(0, 0, 600, out _));
        AssertTrue(observation.TryBegin(0, 100, 600, out var argument)); AssertEqual("0|100|600", argument);
        AssertTrue(!observation.TryBegin(400, 100, 600, out _));
        observation.Complete();
        AssertTrue(!observation.TryBegin(0, 100, 600, out _));
        var scroll = tree.GetComponent<XsrUiScroll>(page.Root)!; scroll.OffsetY = 400;
        data["height"] = "900"; page.Reload(source.Compile(data));
        AssertTrue(ReferenceEquals(observation, tree.GetComponent<NxmlScrollObservation>(page.Root)));
        AssertEqual(400d, scroll.OffsetY);
        AssertTrue(observation.TryBegin(400, 100, 900, out _)); observation.Complete();
        foreach (var invalid in new[] { "<Text ScrollCommand=\"test.append\" />", "<StackPanel Key=\"list\" ScrollCommand=\"test.append\" />", "<StackPanel Scroll=\"true\" ScrollCommand=\"test.append\" />" })
            AssertThrows<NxmlCompileException>(() => Compile(invalid));
    }
}
