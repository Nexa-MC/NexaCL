using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static void RangeInputCapturesPointerAndSharesFiniteKeyboardAndNativeRequests()
    {
        XsrUiTree tree = new(); var root = tree.Create("pager");
        XsrUiPager pager = new(XsrUiOrientation.Horizontal); tree.SetComponent(root, pager);
        var page = tree.Create("page"); tree.Attach(page, root); tree.SetComponent(page, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        var slider = ThemeLeaf(tree, page, "range", 200, 40);
        XsrUiRangeInput range = new(0, 100, 10, 0); tree.SetComponent(slider, range);
        tree.SetComponent(slider, new XsrUiInput { Focusable = true, Clickable = true });
        tree.SetComponent(slider, new XsrUiSemantic(XsrUiSemanticRole.Slider, "range"));
        var command = "theme.range".AsXsrId(); tree.SetComponent(slider, new XsrUiCommandBinding(command));
        XsrUiIntentBuffer intents = new(); XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), intents)
        { Viewport = new(200, 120) }; renderer.SetRoot(root); renderer.Render();
        AssertTrue(renderer.PointerPressed(new(100, 20))); AssertClose(50, renderer.GetRangeValue(slider)!.Value);
        AssertTrue(renderer.PointerMoved(new(350, 80))); AssertClose(100, renderer.GetRangeValue(slider)!.Value);
        AssertTrue(renderer.PointerReleased(new(350, 80))); AssertEqual(2, intents.Count);
        AssertClose(0, pager.Position); AssertFalse(pager.IsDragging);
        var routed = intents.Drain(); AssertTrue(routed.All(intent => intent.Source == slider && intent.Command == command));
        renderer.ApplyTheme(new TestThemeProgram(1, _ => new() { Foreground = new(20, 100, 140) }), 1);
        AssertClose(100, renderer.Render().Nodes.Single(node => node.Entity == slider).Range!.Value.Value);
        AssertTrue(ReferenceEquals(range, tree.GetComponent<XsrUiRangeInput>(slider)));
        AssertTrue(renderer.SetRangeValue(slider, 50)); AssertEqual(0, intents.Count);
        AssertTrue(renderer.HandleKey(XsrUiKey.Home)); AssertClose(0, range.Value);
        AssertTrue(renderer.HandleKey(XsrUiKey.End)); AssertClose(100, range.Value);
        AssertTrue(renderer.HandleKey(XsrUiKey.Left)); AssertClose(90, range.Value);
        AssertTrue(renderer.RequestRangeValue(slider, 999)); AssertClose(100, range.Value); AssertEqual(4, intents.Count);
        tree.GetComponent<XsrUiInput>(slider)!.Enabled = false; tree.MarkDirty(slider, XsrUiDirtyKinds.Paint);
        renderer.Render(); AssertFalse(renderer.RequestRangeValue(slider, 10)); AssertFalse(renderer.HandleKey(XsrUiKey.Right));
        AssertClose(100, range.Value); AssertEqual(4, intents.Count);
    }

    private static void RangeInputRetirementAndSourceBoundsDoNotLeakCapturedWrites()
    {
        AssertThrows<ArgumentException>(() => _ = new XsrUiRangeInput(10, 0, 1, 0));
        AssertThrows<ArgumentException>(() => _ = new XsrUiRangeInput(0, 1, 0, 0));
        AssertThrows<ArgumentException>(() => _ = new XsrUiRangeInput(0, 1, .1, double.NaN));
        XsrUiTree tree = new(); var root = tree.Create("root");
        var slider = ThemeLeaf(tree, root, "range", 100, 40);
        XsrUiRangeInput range = new(0, 10, 3, 3); tree.SetComponent(slider, range);
        tree.SetComponent(slider, new XsrUiInput { Focusable = true, Clickable = true });
        tree.SetComponent(slider, new XsrUiCommandBinding("theme.range".AsXsrId()));
        XsrUiIntentBuffer intents = new(); XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), intents);
        renderer.SetRoot(root); renderer.Render(); AssertTrue(renderer.Focus(slider));
        AssertTrue(renderer.HandleKey(XsrUiKey.End)); AssertClose(10, range.Value);
        AssertTrue(renderer.HandleKey(XsrUiKey.Left)); AssertClose(9, range.Value);
        range.Reset(0, 8, 2, 0, preserveValue: true); AssertClose(8, range.Value);
        AssertTrue(renderer.PointerPressed(new(80, 20))); intents.Drain();
        tree.Destroy(slider);
        var replacement = ThemeLeaf(tree, root, "replacement", 100, 40); XsrUiRangeInput fresh = new(0, 100, 1, 5);
        tree.SetComponent(replacement, fresh); tree.SetComponent(replacement, new XsrUiInput { Focusable = true, Clickable = true });
        AssertEqual(slider.Index, replacement.Index); AssertFalse(slider == replacement);
        AssertTrue(renderer.PointerMoved(new(5, 20))); AssertClose(5, fresh.Value); AssertEqual(0, intents.Count);
        AssertFalse(renderer.RequestRangeValue(slider, 50)); AssertFalse(renderer.GetRangeValue(slider).HasValue);
        renderer.Render(); AssertFalse(renderer.PointerReleased(new(5, 20))); AssertClose(5, fresh.Value);
        AssertTrue(renderer.SetRangeValue(replacement, 17.8)); AssertClose(18, fresh.Value);
        AssertFalse(renderer.SetRangeValue(replacement, double.PositiveInfinity)); AssertClose(18, fresh.Value);
    }
}
