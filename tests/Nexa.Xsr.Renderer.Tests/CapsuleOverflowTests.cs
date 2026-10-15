using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static void IntrinsicCapsulesBoundWidthAndScrollCaptionsWithoutInvokingCommands()
    {
        XsrUiTree tree = new();
        var root = tree.Create("root");
        var bubble = tree.Create("bubble");
        tree.Attach(bubble, root);
        var box = new XsrUiElement
        {
            Height = 48,
            MaxWidth = 420,
            HorizontalAlignment = XsrUiAlignment.End,
            VerticalAlignment = XsrUiAlignment.End,
            Margin = new(0, 0, 18, 18),
        };
        tree.SetComponent(bubble, box);
        tree.SetComponent(bubble, new XsrUiVisualStyle { HoverExpand = true, FontSize = 13 });
        tree.SetComponent(bubble, new XsrUiInput { Focusable = true, Clickable = true });
        tree.SetComponent(bubble, new XsrUiSemantic(XsrUiSemanticRole.Button, "complete instance caption"));
        var text = new XsrUiText("短标题"); tree.SetComponent(bubble, text);
        var scroll = new XsrUiScroll { ShowsHorizontalIndicator = true, UseVerticalWheelForHorizontalScroll = true };
        tree.SetComponent(bubble, scroll);
        var command = XsrSemanticId.Parse("test.bubble.action");
        tree.SetComponent(bubble, new XsrUiCommandBinding(command));
        XsrUiIntentBuffer intents = new();
        var renderer = new XsrUiRenderer(tree, new XsrStateStoreBuilder().Build(), intents)
        { Viewport = new(850, 500), ReducedMotion = true };
        renderer.SetRoot(root);
        XsrUiSceneNode Node() => renderer.Render().Nodes.Single(node => node.Entity == bubble);
        AssertEqual(48d, Node().Rect.Width);
        AssertTrue(renderer.Focus(bubble));
        var shortCaption = Node();
        AssertTrue(shortCaption.Rect.Width > 48 && shortCaption.Rect.Width < 208);
        AssertEqual(832d, shortCaption.Rect.X + shortCaption.Rect.Width);
        AssertFalse(shortCaption.Scroll!.Value.CanScrollHorizontally);
        AssertTrue(shortCaption.HorizontalScrollbar is null);

        text.Content = new string('W', 240);
        tree.MarkDirty(bubble, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        var longCaption = Node();
        AssertEqual(420d, longCaption.Rect.Width);
        AssertEqual(832d, longCaption.Rect.X + longCaption.Rect.Width);
        AssertEqual("complete instance caption", longCaption.Label);
        AssertTrue(longCaption.Scroll!.Value.CanScrollHorizontally);
        var point = new XsrUiPoint(longCaption.Rect.X + 30, longCaption.Rect.Y + 16);
        AssertTrue(renderer.PointerScroll(point, 90));
        AssertEqual(90d, Node().Scroll!.Value.OffsetX);
        AssertTrue(renderer.PointerScroll(point, 500, 20));
        AssertEqual(110d, Node().Scroll!.Value.OffsetX);
        AssertTrue(renderer.HandleKey(XsrUiKey.Right));
        AssertEqual(158d, Node().Scroll!.Value.OffsetX);
        AssertTrue(renderer.HandleKey(XsrUiKey.Left));
        AssertEqual(110d, Node().Scroll!.Value.OffsetX);
        AssertEqual(0, intents.Count);

        var bar = Node().HorizontalScrollbar!.Value;
        var thumb = ScrollbarThumbPoint(bar);
        AssertTrue(renderer.PointerPressed(thumb));
        AssertTrue(renderer.PointerMoved(thumb with { X = thumb.X + bar.Travel }));
        AssertTrue(renderer.PointerReleased(thumb));
        longCaption = Node();
        AssertClose(longCaption.Scroll!.Value.MaximumOffsetX, longCaption.Scroll!.Value.OffsetX);
        AssertEqual(0, intents.Count);
        AssertTrue(renderer.PointerScroll(point, -100000));
        AssertEqual(0d, Node().Scroll!.Value.OffsetX);

        renderer.Viewport = new(210, 500);
        var narrow = Node();
        AssertEqual(192d, narrow.Rect.Width);
        AssertEqual(0d, narrow.Rect.X);
        AssertTrue(narrow.Scroll!.Value.MaximumOffsetX > longCaption.Scroll!.Value.MaximumOffsetX);
        AssertTrue(renderer.HandleKey(XsrUiKey.Enter));
        AssertEqual(command, intents.Drain().Single().Command);
        text.Content = "短标题";
        tree.MarkDirty(bubble, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        AssertEqual(0d, Node().Scroll!.Value.OffsetX);
        AssertFalse(renderer.HandleKey(XsrUiKey.Right));
    }
}
