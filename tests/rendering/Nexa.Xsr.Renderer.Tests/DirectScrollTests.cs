using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static (XsrUiTree Tree, XsrUiRenderer Renderer, XsrUiEntityId Root, XsrUiEntityId Content,
        XsrUiScroll Scroll, XsrUiIntentBuffer Intents, GestureClock Clock) DirectScrollFixture(bool bothAxes = false)
    {
        XsrUiTree tree = new();
        XsrUiEntityId root = tree.Create("direct-scroll"), content = tree.Create("direct-scroll-content");
        tree.SetComponent(root, new XsrUiElement { Width = 300, Height = 120 });
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        XsrUiScroll scroll = new() { ShowsHorizontalIndicator = true, ShowsVerticalIndicator = bothAxes };
        tree.SetComponent(root, scroll);
        tree.SetComponent(root, new XsrUiScrollGesture());
        tree.SetComponent(content, new XsrUiElement
        {
            Width = 1200,
            Height = bothAxes ? 900 : 80,
            VerticalAlignment = XsrUiAlignment.Start,
        });
        tree.SetComponent(content, new XsrUiInput { Clickable = true, Focusable = true });
        tree.SetComponent(content, new XsrUiCommandBinding("test.direct-scroll".AsXsrId()));
        tree.Attach(content, root);
        XsrUiIntentBuffer intents = new();
        GestureClock clock = new();
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), intents, timeProvider: clock)
        {
            Viewport = new(300, 120),
        };
        renderer.SetRoot(root);
        renderer.Render();
        return (tree, renderer, root, content, scroll, intents, clock);
    }

    private static void HorizontalContentDragKeepsClicksAndPublishesAxisInertia()
    {
        var f = DirectScrollFixture();
        AssertEqual(XsrUiOrientation.Vertical, new XsrUiScrollMotionSnapshot(1, false, 10).Direction);
        AssertTrue(f.Renderer.PointerPressed(new(180, 40)));
        f.Clock.Advance(20);
        f.Renderer.PointerMoved(new(177, 42));
        AssertTrue(f.Renderer.PointerReleased(new(177, 42)));
        AssertEqual(1, f.Intents.Count);
        AssertEqual(0d, f.Scroll.OffsetX);

        AssertTrue(f.Renderer.PointerPressed(new(250, 40)));
        f.Clock.Advance(40);
        AssertTrue(f.Renderer.PointerMoved(new(190, 42)));
        f.Renderer.Render();
        f.Clock.Advance(40);
        AssertTrue(f.Renderer.PointerMoved(new(130, 44)));
        AssertTrue(f.Renderer.PointerReleased(new(130, 44)));
        XsrUiScrollMotionSnapshot motion = f.Renderer.Render().Nodes.Single(node => node.Entity == f.Root).ScrollMotion!.Value;
        AssertEqual(XsrUiOrientation.Horizontal, motion.Direction);
        AssertClose(1500, motion.Velocity);
        AssertClose(120, f.Scroll.OffsetX);
        AssertClose(120, f.Renderer.GetScrollPresentationOffset(f.Root));
        AssertEqual(0d, f.Scroll.OffsetY);
        AssertEqual(1, f.Intents.Count);
        f.Renderer.SetScrollPresentationOffset(f.Root, 200);
        f.Renderer.Render();
        AssertEqual(200d, f.Scroll.OffsetX);
        AssertEqual(0d, f.Scroll.OffsetY);
        f.Renderer.SetScrollPresentationOffset(f.Root, 10000);
        XsrUiScrollSnapshot scroll = f.Renderer.Render().Nodes.Single(node => node.Entity == f.Root).Scroll!.Value;
        AssertEqual(scroll.MaximumOffsetX, f.Scroll.OffsetX);
        f.Renderer.FinishScrollInertia(f.Root);
        f.Renderer.SetScrollPresentationOffset(f.Root, 50);
        AssertEqual(scroll.MaximumOffsetX, f.Scroll.OffsetX);

        AssertTrue(f.Renderer.PointerPressed(new(160, 40)));
        f.Clock.Advance(40);
        AssertTrue(f.Renderer.PointerMoved(new(220, 42)));
        double held = f.Scroll.OffsetX;
        f.Renderer.SetScrollPresentationOffset(f.Root, 0);
        AssertEqual(held, f.Scroll.OffsetX);
        AssertTrue(f.Renderer.CancelPointerGesture());
        f.Renderer.SetScrollPresentationOffset(f.Root, 0);
        AssertEqual(held, f.Scroll.OffsetX);
        AssertFalse(f.Renderer.PointerReleased(new(220, 42)));
    }

    private static void DirectScrollLocksAxisUntilReleaseAndRetiresLostOverflow()
    {
        var f = DirectScrollFixture(bothAxes: true);
        AssertTrue(f.Renderer.TryGetScrollSnapshot(f.Root, out XsrUiScrollSnapshot extent));
        AssertTrue(extent.MaximumOffsetX > 0 && extent.MaximumOffsetY > 0);
        AssertTrue(f.Renderer.PointerPressed(new(180, 80)));
        f.Clock.Advance(30);
        AssertTrue(f.Renderer.PointerMoved(new(100, 78)));
        f.Clock.Advance(30);
        AssertTrue(f.Renderer.PointerMoved(new(80, 10)));
        AssertEqual(100d, f.Scroll.OffsetX);
        AssertEqual(0d, f.Scroll.OffsetY);
        AssertTrue(f.Renderer.PointerReleased(new(80, 10)));
        f.Renderer.Render();

        AssertTrue(f.Renderer.PointerPressed(new(180, 80)));
        f.Clock.Advance(30);
        AssertTrue(f.Renderer.PointerMoved(new(178, 30)));
        f.Clock.Advance(30);
        AssertTrue(f.Renderer.PointerMoved(new(50, 10)));
        AssertEqual(100d, f.Scroll.OffsetX);
        AssertEqual(70d, f.Scroll.OffsetY);
        AssertEqual(XsrUiOrientation.Vertical,
            f.Renderer.Render().Nodes.Single(node => node.Entity == f.Root).ScrollMotion!.Value.Direction);
        AssertTrue(f.Renderer.CancelPointerGesture());

        f.Renderer.ReducedMotion = true;
        AssertTrue(f.Renderer.PointerPressed(new(180, 50)));
        f.Clock.Advance(30);
        AssertTrue(f.Renderer.PointerMoved(new(100, 50)));
        AssertTrue(f.Renderer.PointerReleased(new(100, 50)));
        AssertEqual(0d, f.Renderer.Render().Nodes.Single(node => node.Entity == f.Root).ScrollMotion!.Value.Velocity);
        f.Renderer.ReducedMotion = false;
        AssertTrue(f.Renderer.PointerPressed(new(180, 50)));
        f.Clock.Advance(30);
        AssertTrue(f.Renderer.PointerMoved(new(100, 50)));
        f.Tree.GetComponent<XsrUiElement>(f.Content)!.Width = 100;
        f.Tree.MarkDirty(f.Content, XsrUiDirtyKinds.Layout);
        XsrUiSceneNode node = f.Renderer.Render().Nodes.Single(node => node.Entity == f.Root);
        AssertEqual(0d, f.Scroll.OffsetX);
        AssertFalse(node.ScrollMotion!.Value.Dragging);
        AssertEqual(0d, node.ScrollMotion.Value.Velocity);
        AssertFalse(f.Renderer.PointerReleased(new(100, 50)));
        AssertEqual(0, f.Intents.Count);
    }

    private static void NestedDirectScrollingChoosesAncestorOnTheDominantAxis()
    {
        XsrUiTree tree = new();
        XsrUiEntityId outer = tree.Create("outer-vertical"), inner = tree.Create("inner-horizontal"),
            cards = tree.Create("inner-cards"), remainder = tree.Create("outer-remainder");
        tree.SetComponent(outer, new XsrUiElement { Width = 300, Height = 180 });
        tree.SetComponent(outer, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        XsrUiScroll outerScroll = new() { ShowsVerticalIndicator = true };
        tree.SetComponent(outer, outerScroll);
        tree.SetComponent(outer, new XsrUiScrollGesture());
        tree.SetComponent(inner, new XsrUiElement { Width = 280, Height = 100 });
        tree.SetComponent(inner, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        XsrUiScroll innerScroll = new() { ShowsHorizontalIndicator = true };
        tree.SetComponent(inner, innerScroll);
        tree.SetComponent(inner, new XsrUiScrollGesture());
        tree.SetComponent(cards, new XsrUiElement { Width = 1200, Height = 80 });
        tree.SetComponent(cards, new XsrUiInput { Clickable = true, Focusable = true });
        tree.SetComponent(cards, new XsrUiCommandBinding("test.nested-scroll".AsXsrId()));
        tree.SetComponent(remainder, new XsrUiElement { Height = 800 });
        tree.Attach(cards, inner);
        tree.Attach(inner, outer);
        tree.Attach(remainder, outer);
        XsrUiIntentBuffer intents = new();
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), intents) { Viewport = new(300, 180) };
        renderer.SetRoot(outer);
        renderer.Render();
        AssertTrue(renderer.PointerPressed(new(220, 50)));
        AssertTrue(renderer.PointerMoved(new(140, 52)));
        AssertEqual(80d, innerScroll.OffsetX);
        AssertEqual(0d, outerScroll.OffsetY);
        AssertTrue(renderer.PointerReleased(new(140, 52)));
        renderer.FinishScrollInertia(inner);
        renderer.Render();
        AssertTrue(renderer.PointerPressed(new(220, 70)));
        AssertTrue(renderer.PointerMoved(new(218, 20)));
        AssertEqual(80d, innerScroll.OffsetX);
        AssertEqual(50d, outerScroll.OffsetY);
        XsrUiScene scene = renderer.Render();
        AssertFalse(scene.Nodes.Single(node => node.Entity == inner).ScrollMotion!.Value.Dragging);
        AssertTrue(scene.Nodes.Single(node => node.Entity == outer).ScrollMotion!.Value.Dragging);
        AssertTrue(renderer.PointerReleased(new(218, 20)));
        AssertEqual(0, intents.Count);
    }

    private static void HorizontalContentScrollAndContainingPagerHaveExclusiveOwnership()
    {
        var (tree, renderer, root, pages) = CreatePagerFixture(direction: XsrUiOrientation.Vertical);
        tree.SetComponent(pages[0], new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        XsrUiScroll scroll = new() { ShowsHorizontalIndicator = true };
        tree.SetComponent(pages[0], scroll);
        tree.SetComponent(pages[0], new XsrUiScrollGesture());
        XsrUiEntityId cards = tree.Create("pager-horizontal-cards");
        tree.SetComponent(cards, new XsrUiElement { Width = 1200, Height = 80 });
        tree.Attach(cards, pages[0]);
        renderer.Render();
        XsrUiPager pager = tree.GetComponent<XsrUiPager>(root)!;
        AssertTrue(renderer.PointerPressed(new(140, 40)));
        AssertTrue(renderer.PointerMoved(new(80, 42)));
        AssertEqual(60d, scroll.OffsetX);
        AssertEqual(0d, pager.Position);
        AssertFalse(pager.IsDragging);
        AssertTrue(renderer.PointerReleased(new(80, 42)));
        renderer.FinishScrollInertia(pages[0]);
        renderer.Render();
        AssertTrue(renderer.PointerPressed(new(140, 80)));
        AssertTrue(renderer.PointerMoved(new(140, 20)));
        AssertClose(.6, pager.Position);
        AssertEqual(60d, scroll.OffsetX);
        AssertFalse(renderer.Render().Nodes.Single(node => node.Entity == pages[0]).ScrollMotion!.Value.Dragging);
        AssertTrue(renderer.PointerReleased(new(140, 20)));
        AssertEqual(1, pager.PageIndex);
    }

    private static void TransformedDirectScrollUsesLocalAxisThresholdAndInertia()
    {
        foreach (XsrUiOrientation direction in new[] { XsrUiOrientation.Vertical, XsrUiOrientation.Horizontal })
        {
            XsrUiTree tree = new();
            XsrUiEntityId root = tree.Create("rotated-scroll"), content = tree.Create("rotated-content");
            tree.SetComponent(root, new XsrUiElement { Width = 120, Height = 160 });
            tree.SetComponent(root, new XsrUiStackPanel(direction));
            XsrUiScroll scroll = new();
            tree.SetComponent(root, scroll);
            tree.SetComponent(root, new XsrUiScrollGesture());
            tree.SetComponent(content, new XsrUiElement
            {
                Width = direction == XsrUiOrientation.Horizontal ? 900 : 100,
                Height = direction == XsrUiOrientation.Vertical ? 900 : 100,
                HorizontalAlignment = XsrUiAlignment.Start,
                VerticalAlignment = XsrUiAlignment.Start,
            });
            tree.Attach(content, root);
            GestureClock clock = new();
            XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), timeProvider: clock)
            {
                Viewport = new(600, 600),
            };
            renderer.SetRoot(root);
            renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key == "rotated-scroll"
                ? new() { Transform = new(TranslateX: 160, TranslateY: 160, ScaleX: 2, ScaleY: 2, Rotation: 90) }
                : new()), 1);
            XsrUiSceneNode node = renderer.Render().Nodes.Single(item => item.Entity == root);
            XsrUiSceneTransform transform = node.Transform!.Value;
            XsrUiPoint localStart = direction == XsrUiOrientation.Vertical ? new(60, 120) : new(100, 60);
            XsrUiPoint localSmall = direction == XsrUiOrientation.Vertical
                ? localStart with { Y = localStart.Y - 7 } : localStart with { X = localStart.X - 7 };
            XsrUiPoint localEnd = direction == XsrUiOrientation.Vertical
                ? localStart with { Y = localStart.Y - 40 } : localStart with { X = localStart.X - 40 };
            XsrUiPoint start = transform.Map(localStart), small = transform.Map(localSmall), end = transform.Map(localEnd);
            AssertEqual(content, renderer.HitTest(start));
            AssertTrue(renderer.PointerPressed(start));
            clock.Advance(20);
            // At scale 2 this is 14 scene DIPs, but only 7 local DIPs: it remains a tap.
            AssertFalse(renderer.PointerMoved(small));
            AssertEqual(0d, scroll.OffsetX);
            AssertEqual(0d, scroll.OffsetY);
            clock.Advance(20);
            AssertTrue(renderer.PointerMoved(end));
            AssertTrue(renderer.PointerReleased(end));
            XsrUiScrollMotionSnapshot motion = renderer.Render().Nodes.Single(item => item.Entity == root).ScrollMotion!.Value;
            AssertEqual(direction, motion.Direction);
            AssertClose(1000, motion.Velocity);
            AssertClose(40, renderer.GetScrollPresentationOffset(root));
            renderer.SetScrollPresentationOffset(root, 80);
            AssertClose(direction == XsrUiOrientation.Horizontal ? 80 : 0, scroll.OffsetX);
            AssertClose(direction == XsrUiOrientation.Vertical ? 80 : 0, scroll.OffsetY);
        }
    }

    private static void NestedTransformedScrollCandidatesUseTheirOwnLocalAxes()
    {
        XsrUiTree tree = new();
        XsrUiEntityId outer = tree.Create("scaled-outer-scroll"), inner = tree.Create("rotated-inner-scroll"),
            content = tree.Create("nested-rotated-content"), remainder = tree.Create("nested-scaled-remainder");
        tree.SetComponent(outer, new XsrUiElement { Width = 400, Height = 400 });
        tree.SetComponent(outer, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        XsrUiScroll outerScroll = new();
        tree.SetComponent(outer, outerScroll);
        tree.SetComponent(outer, new XsrUiScrollGesture());
        tree.SetComponent(inner, new XsrUiElement { Width = 120, Height = 160 });
        tree.SetComponent(inner, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        XsrUiScroll innerScroll = new();
        tree.SetComponent(inner, innerScroll);
        tree.SetComponent(inner, new XsrUiScrollGesture());
        tree.SetComponent(content, new XsrUiElement { Width = 100, Height = 900 });
        tree.SetComponent(remainder, new XsrUiElement { Height = 800 });
        tree.Attach(content, inner);
        tree.Attach(inner, outer);
        tree.Attach(remainder, outer);
        GestureClock clock = new();
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), timeProvider: clock)
        {
            Viewport = new(700, 700),
        };
        renderer.SetRoot(outer);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key switch
        {
            "scaled-outer-scroll" => new() { Transform = new(TranslateX: 100, TranslateY: 100, ScaleX: 1.5, ScaleY: 1.5) },
            // Keep the rotated surface inside its reserved 160 DIP row. Translating it
            // down would overlap the following sibling, which correctly wins hit testing.
            "rotated-inner-scroll" => new() { Transform = new(TranslateX: 160, ScaleX: 1, ScaleY: 1, Rotation: 90) },
            _ => new(),
        }), 1);
        XsrUiScene scene = renderer.Render();
        XsrUiSceneTransform innerTransform = scene.Nodes.Single(node => node.Entity == inner).Transform!.Value;
        XsrUiSceneTransform outerTransform = scene.Nodes.Single(node => node.Entity == outer).Transform!.Value;
        XsrUiPoint start = innerTransform.Map(new(60, 100)), innerEnd = innerTransform.Map(new(60, 60));
        AssertEqual(content, renderer.HitTest(start));
        AssertTrue(renderer.PointerPressed(start));
        clock.Advance(40);
        AssertTrue(renderer.PointerMoved(innerEnd));
        AssertClose(40, innerScroll.OffsetY);
        AssertEqual(0d, outerScroll.OffsetY);
        AssertTrue(renderer.PointerReleased(innerEnd));
        scene = renderer.Render();
        XsrUiScrollMotionSnapshot motion = scene.Nodes.Single(node => node.Entity == inner).ScrollMotion!.Value;
        AssertEqual(XsrUiOrientation.Vertical, motion.Direction);
        AssertClose(1000, motion.Velocity);
        renderer.FinishScrollInertia(inner);
        renderer.Render();

        // This scene-vertical movement is local X for the rotated child, which has no X
        // overflow. The differently transformed parent must evaluate its own local Y.
        XsrUiPoint outerStart = outerTransform.Unmap(start);
        XsrUiPoint outerEnd = outerTransform.Map(outerStart with { Y = outerStart.Y - 40 });
        AssertTrue(renderer.PointerPressed(start));
        clock.Advance(40);
        AssertTrue(renderer.PointerMoved(outerEnd));
        AssertClose(40, innerScroll.OffsetY);
        AssertClose(40, outerScroll.OffsetY);
        AssertTrue(renderer.PointerReleased(outerEnd));
        motion = renderer.Render().Nodes.Single(node => node.Entity == outer).ScrollMotion!.Value;
        AssertEqual(XsrUiOrientation.Vertical, motion.Direction);
        AssertClose(1000, motion.Velocity);
        renderer.SetScrollPresentationOffset(outer, 75);
        AssertClose(75, outerScroll.OffsetY);
        AssertEqual(0d, outerScroll.OffsetX);
    }
}
