using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static void ThemePaintSwitchPreservesOwnedStateAndLiteralColors()
    {
        XsrUiTree tree = new();
        XsrUiEntityId root = tree.Create("root");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        XsrUiScroll scroll = new() { OffsetY = 10 };
        tree.SetComponent(root, scroll);
        XsrUiEntityId input = ThemeLeaf(tree, root, "editor", 120, 40);
        XsrUiEntityId tail = ThemeLeaf(tree, root, "tail", 120, 200);
        XsrUiTextInput draft = new(); tree.SetComponent(input, draft);
        XsrUiInput interaction = new() { Focusable = true, Clickable = true }; tree.SetComponent(input, interaction);
        var command = "theme.submit".AsXsrId(); tree.SetComponent(input, new XsrUiCommandBinding(command));
        XsrUiVisualStyle source = new() { Background = new(19, 112, 243), Surface = XsrUiSurfaceKind.Solid };
        tree.SetComponent(input, source);
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()) { Viewport = new(180, 100), ColorScheme = new(true) };
        renderer.SetRoot(root); renderer.Render();
        AssertTrue(renderer.Focus(input)); AssertTrue(renderer.SetTextInputValue(input, "中A😀draft"));
        AssertTrue(renderer.SetTextSelection(input, 1, 2)); renderer.Render();
        double offset = scroll.OffsetY;
        var baseChildren = tree.Children(root).ToArray();
        XsrUiColor literalAccent = new(19, 112, 243);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key == "editor"
            ? new() { Background = literalAccent, Foreground = new(52, 61, 74) } : XsrUiComputedStyle.Empty), 1);
        XsrUiScene scene = renderer.Render();
        AssertEqual(0, renderer.LastLayoutVisits);
        AssertEqual(literalAccent, scene.Nodes.Single(node => node.Entity == input).VisualStyle.Background);
        AssertEqual(new XsrUiColor(52, 61, 74), scene.Nodes.Single(node => node.Entity == input).VisualStyle.Foreground);
        AssertTrue(ReferenceEquals(draft, tree.GetComponent<XsrUiTextInput>(input)));
        AssertTrue(ReferenceEquals(interaction, tree.GetComponent<XsrUiInput>(input)));
        AssertTrue(ReferenceEquals(source, tree.GetComponent<XsrUiVisualStyle>(input)));
        AssertEqual("中A😀draft", draft.ReadDraft()); AssertEqual(1, draft.SelectionStart); AssertEqual(2, draft.SelectionEnd);
        AssertEqual(input, renderer.Focused); AssertEqual(command, tree.GetComponent<XsrUiCommandBinding>(input)!.Command);
        AssertClose(offset, scroll.OffsetY); AssertSequence(baseChildren, tree.Children(root).ToArray());
        long computes = renderer.ThemeStyleComputeCount;
        AssertTrue(ReferenceEquals(scene, renderer.Render())); AssertEqual(computes, renderer.ThemeStyleComputeCount);
        renderer.ClearTheme(2); renderer.Render(); AssertEqual(0, renderer.LastLayoutVisits);
        AssertEqual(input, renderer.Focused); AssertEqual(2L, renderer.ThemeActivationGeneration); AssertTrue(tree.IsAlive(tail));
        AssertThrows<InvalidOperationException>(() => renderer.ApplyTheme(new TestThemeProgram(1, _ => new()), 2));
    }

    private static void ThemeAdmissionIncludesRetainedNodesAndRejectsWithoutMutation()
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        var live = ThemeLeaf(tree, root, "live", 100, 40);
        var retained = ThemeLeaf(tree, default, "retained", 100, 40);
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()); renderer.SetRoot(root); renderer.Render();
        renderer.RegisterVirtualLayout(retained, new(100, 40)); renderer.Render();
        renderer.ApplyTheme(new TestThemeProgram(1, _ => new() { Background = new(90, 60, 30) }), 3);
        XsrUiScene valid = renderer.Render();
        var invalid = new TestThemeProgram(2, context => context.Identity.Key == "retained"
            ? new() { VirtualItemHeight = 90 } : new() { Background = new(1, 2, 3) });
        AssertThrows<InvalidOperationException>(() => renderer.ValidateTheme(invalid));
        AssertThrows<InvalidOperationException>(() => renderer.ApplyTheme(invalid, 4));
        AssertEqual(3L, renderer.ThemeActivationGeneration); AssertTrue(ReferenceEquals(valid, renderer.Render()));
        AssertEqual(new XsrUiColor(90, 60, 30), valid.Nodes.Single(node => node.Entity == live).VisualStyle.Background);
        renderer.RegisterVirtualLayout(retained, new(100, 40, SupportsThemeMetrics: true));
        renderer.ApplyTheme(invalid, 5);
        AssertTrue(renderer.TryGetVirtualLayoutMetrics(retained, out var metrics));
        AssertEqual(5L, metrics.ActivationGeneration); AssertClose(90, metrics.ItemHeight);
        AssertClose(40, tree.GetComponent<XsrUiElement>(retained)!.Height!.Value);
        tree.Destroy(retained); renderer.Render(); AssertFalse(renderer.TryGetVirtualLayoutMetrics(retained, out _));
        AssertTrue(renderer.ThemeStyleCacheCount <= tree.Count);
    }

    private static void ThemeInteractionResponsiveAndSourceChangesInvalidateOnlyNeededStyle()
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        var first = ThemeLeaf(tree, root, "first", 80, 40);
        var second = ThemeLeaf(tree, root, "second", 30, 40);
        tree.SetComponent(first, new XsrUiInput { Clickable = true });
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()) { Viewport = new(300, 100) };
        renderer.SetRoot(root);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key == "first"
            ? new() { Width = context.Viewport.Width < 250 ? 60 : context.InteractionState.HasFlag(XsrUiThemeInteractionState.Hover) ? 140 : 80 }
            : XsrUiComputedStyle.Empty), 1);
        renderer.Render(); AssertTrue(renderer.PointerMoved(new(20, 20)));
        XsrUiScene hovered = renderer.Render();
        AssertClose(140, hovered.Nodes.Single(node => node.Entity == first).Rect.Width);
        AssertClose(140, hovered.Nodes.Single(node => node.Entity == second).Rect.X);
        renderer.Viewport = new(200, 100);
        AssertClose(60, renderer.Render().Nodes.Single(node => node.Entity == first).Rect.Width);
        tree.GetComponent<XsrUiElement>(second)!.Width = 55; tree.MarkDirty(second, XsrUiDirtyKinds.Layout);
        AssertClose(55, renderer.Render().Nodes.Single(node => node.Entity == second).Rect.Width);
        var parked = ThemeLeaf(tree, default, "first", 15, 40);
        tree.Attach(parked, root); AssertClose(60, renderer.Render().Nodes.Single(node => node.Entity == parked).Rect.Width);
        tree.GetComponent<XsrUiElement>(parked)!.IsVisible = false; tree.MarkDirty(parked, XsrUiDirtyKinds.Layout); renderer.Render();
        renderer.Viewport = new(400, 100); renderer.Render();
        tree.GetComponent<XsrUiElement>(parked)!.IsVisible = true; tree.MarkDirty(parked, XsrUiDirtyKinds.Layout);
        AssertClose(80, renderer.Render().Nodes.Single(node => node.Entity == parked).Rect.Width);
    }

    private static void ThemeGridFractionsSpansOrderAndAbsoluteGeometryAgreeWithInput()
    {
        XsrUiTree tree = new(); var root = tree.Create("grid");
        var first = ThemeLeaf(tree, root, "first", null, 20);
        var second = ThemeLeaf(tree, root, "second", null, 30);
        var wide = ThemeLeaf(tree, root, "wide", null, 25);
        var absolute = ThemeLeaf(tree, root, "absolute", 20, 20);
        foreach (var child in new[] { first, second, wide, absolute }) tree.SetComponent(child, new XsrUiInput { Focusable = true, Clickable = true });
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()) { Viewport = new(300, 200) }; renderer.SetRoot(root);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key switch
        {
            "grid" => new() { Grid = new([new(XsrUiGridUnit.Fraction, 1), new(XsrUiGridUnit.Fraction, 2)], ColumnGap: 12, RowGap: 5) },
            "first" => new() { Order = 2 },
            "second" => new() { Order = 1 },
            "wide" => new() { GridColumnSpan = 2, Order = 3 },
            "absolute" => new() { Position = XsrUiThemePosition.Absolute, Left = 270, Top = 150, Order = 4 },
            _ => new(),
        }), 1);
        XsrUiScene scene = renderer.Render();
        AssertClose(0, scene.Nodes.Single(node => node.Entity == second).Rect.X);
        AssertClose(96, scene.Nodes.Single(node => node.Entity == second).Rect.Width);
        AssertClose(108, scene.Nodes.Single(node => node.Entity == first).Rect.X);
        AssertClose(192, scene.Nodes.Single(node => node.Entity == first).Rect.Width);
        AssertClose(300, scene.Nodes.Single(node => node.Entity == wide).Rect.Width);
        AssertClose(35, scene.Nodes.Single(node => node.Entity == wide).Rect.Y);
        AssertEqual(new XsrUiRect(270, 150, 20, 20), scene.Nodes.Single(node => node.Entity == absolute).Rect);
        AssertEqual(absolute, renderer.HitTest(new(275, 155)));
        AssertTrue(renderer.FocusNext()); AssertEqual(second, renderer.Focused);
        AssertTrue(renderer.FocusNext()); AssertEqual(first, renderer.Focused);
        AssertSequence(new[] { first, second, wide, absolute }, tree.Children(root).ToArray());
    }

    private static void ThemeTransformSharesDrawingInverseHitAndInheritedGeometry()
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        var container = ThemeLeaf(tree, root, "container", 100, 100);
        var child = ThemeLeaf(tree, container, "child", 40, 40);
        tree.SetComponent(child, new XsrUiInput { Clickable = true });
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()); renderer.SetRoot(root);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key switch
        {
            "container" => new() { Transform = new(TranslateX: 100, TranslateY: 80, ScaleX: 1, ScaleY: 1) },
            "child" => new() { Transform = new(ScaleX: 1, ScaleY: 1, Rotation: 45) },
            _ => new(),
        }), 1);
        var node = renderer.Render().Nodes.Single(node => node.Entity == child);
        AssertTrue(node.Transform is not null); AssertEqual(new XsrUiRect(0, 0, 40, 40), node.LayoutRect!.Value);
        XsrUiPoint center = node.Transform!.Value.Map(new(20, 20)); AssertEqual(child, renderer.HitTest(center));
        XsrUiPoint aabbCorner = new(node.Rect.X + .1, node.Rect.Y + .1);
        AssertTrue(node.Rect.Contains(aabbCorner)); AssertFalse(renderer.HitTest(aabbCorner) == child);
        AssertClose(20, node.Transform.Value.Unmap(center).X); AssertClose(20, node.Transform.Value.Unmap(center).Y);
    }

    private static void ThemeFontBridgeRevisionRemeasuresAndAnimationHonorsMotionAndIdles()
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        var text = ThemeLeaf(tree, root, "text", null, null); tree.SetComponent(text, new XsrUiText("中A😀"));
        tree.GetComponent<XsrUiElement>(text)!.HorizontalAlignment = XsrUiAlignment.Start;
        ThemeClock clock = new(); ThemeFontProvider provider = new();
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), timeProvider: clock) { TextLayoutProvider = provider };
        renderer.SetRoot(root);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key == "text" ? new()
        {
            FontFamily = "pack/font",
            FontSize = 20,
            Animations = [new(XsrUiStyleAnimationProperty.Opacity, TimeSpan.FromSeconds(1), [new(0, 0), new(1, 1)]),
                new(XsrUiStyleAnimationProperty.TranslateX, TimeSpan.FromSeconds(1), [new(0, 0), new(1, 40)])],
        } : new()), 1);
        AssertClose(40, renderer.Render().Nodes.Single(node => node.Entity == text).Rect.Width);
        AssertEqual("pack/font", provider.Last!.FontFamily);
        clock.Advance(500); AssertTrue(renderer.AdvanceThemeAnimations()); var half = renderer.Render().Nodes.Single(node => node.Entity == text);
        AssertClose(.5, half.VisualStyle.Opacity); AssertClose(20, half.Rect.X);
        provider.Width = 75; provider.Revision++; AssertClose(75, renderer.Render().Nodes.Single(node => node.Entity == text).Rect.Width);
        renderer.ApplyTheme(new TestThemeProgram(2, context => context.Identity.Key == "text" ? new()
        {
            FontFamily = "pack/font",
            FontSize = 20,
            Foreground = new(21, 41, 61),
            Animations = [new(XsrUiStyleAnimationProperty.Opacity, TimeSpan.FromSeconds(1), [new(0, 0), new(1, 1)]),
                new(XsrUiStyleAnimationProperty.TranslateX, TimeSpan.FromSeconds(1), [new(0, 0), new(1, 40)])],
        } : new()), 2);
        half = renderer.Render().Nodes.Single(node => node.Entity == text);
        AssertClose(.5, half.VisualStyle.Opacity); AssertClose(20, half.Rect.X); AssertEqual(0, renderer.LastLayoutVisits);
        AssertEqual(new XsrUiColor(21, 41, 61), half.VisualStyle.Foreground);
        renderer.ReducedMotion = true; var reduced = renderer.Render().Nodes.Single(node => node.Entity == text);
        AssertClose(1, reduced.VisualStyle.Opacity); AssertClose(40, reduced.Rect.X); AssertFalse(renderer.HasThemeAnimations);
        renderer.ReducedMotion = false; clock.Advance(1000); AssertTrue(renderer.AdvanceThemeAnimations());
        renderer.Render(); AssertFalse(renderer.HasThemeAnimations); AssertFalse(renderer.AdvanceThemeAnimations());
    }

    private static void ThemePreparingProjectsVirtualMetricsBeforeLayoutAndRejectsRecursiveRender()
    {
        XsrUiTree tree = new(); var root = tree.Create("list");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        var row = ThemeLeaf(tree, root, "row", 100, 10);
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()); renderer.SetRoot(root);
        renderer.RegisterVirtualLayout(root, new(100, 10, SupportsThemeMetrics: true));
        renderer.ProtectVirtualGeometry(row); renderer.Render();
        bool candidatePending = true;
        renderer.ThemePreparing += (_, _) =>
        {
            AssertThrows<InvalidOperationException>(() => renderer.Render());
            if (!candidatePending) return;
            candidatePending = false;
            renderer.ApplyTheme(new TestThemeProgram(3, context => context.Identity.Key == "list"
                ? new() { VirtualItemHeight = 30 } : new()), 8);
        };
        renderer.FramePreparing += (_, _) =>
        {
            AssertTrue(renderer.TryGetVirtualLayoutMetrics(root, out var metrics));
            AssertEqual(renderer.ThemeActivationGeneration, metrics.ActivationGeneration);
            tree.GetComponent<XsrUiElement>(row)!.Height = metrics.ItemHeight;
            tree.MarkDirty(row, XsrUiDirtyKinds.Layout);
        };
        AssertClose(30, renderer.Render().Nodes.Single(node => node.Entity == row).Rect.Height);
        AssertTrue(tree.GetComponent<XsrUiElement>(root) is null);
        XsrUiColor paint = new(42, 57, 81);
        renderer.ApplyTheme(new TestThemeProgram(4, context => context.Identity.Key switch
        {
            "row" => new()
            {
                Height = 100,
                Width = 500,
                Position = XsrUiThemePosition.Absolute,
                Left = 400,
                Transform = new(TranslateX: 50, ScaleX: 1, ScaleY: 1),
                Background = paint
            },
            "list" => new() { Height = 500, VirtualItemHeight = 30 },
            _ => new(),
        }), 9);
        var projected = renderer.Render().Nodes.Single(node => node.Entity == row);
        AssertClose(30, projected.Rect.Height); AssertClose(100, projected.Rect.Width); AssertClose(0, projected.Rect.X);
        AssertEqual(paint, projected.VisualStyle.Background); AssertClose(30, tree.GetComponent<XsrUiElement>(row)!.Height!.Value);
        AssertTrue(tree.GetComponent<XsrUiElement>(root) is null);
        var future = ThemeLeaf(tree, root, "row", 90, 20); renderer.ProtectVirtualGeometry(future);
        projected = renderer.Render().Nodes.Single(node => node.Entity == future);
        AssertClose(20, projected.Rect.Height); AssertClose(90, projected.Rect.Width); AssertEqual(paint, projected.VisualStyle.Background);
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ValidateTheme(new TestThemeProgram(5,
            context => context.Identity.Key == "list" ? new() { VirtualItemColumns = 2 } : new())));
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ValidateTheme(new TestThemeProgram(6,
            context => context.Identity.Key == "row" ? new() { Height = double.NaN } : new())));
        AssertEqual(9L, renderer.ThemeActivationGeneration);
    }

    private static void ThemeAnimationClockRetiresHiddenAndParkedMatchesAndWakesOnReveal()
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        var hidden = ThemeLeaf(tree, root, "hidden", 100, 40); tree.GetComponent<XsrUiElement>(hidden)!.IsVisible = false;
        var parked = ThemeLeaf(tree, default, "parked", 100, 40);
        ThemeClock clock = new(); XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build(), timeProvider: clock); renderer.SetRoot(root);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key is "hidden" or "parked"
            ? new() { Animations = [new(XsrUiStyleAnimationProperty.Opacity, TimeSpan.FromSeconds(1), [new(0, 0), new(1, 1)], Repeat: context.Identity.Key == "parked", Alternate: true)] }
            : new()), 1);
        XsrUiScene idle = renderer.Render(); AssertFalse(renderer.HasThemeAnimations); AssertFalse(renderer.AdvanceThemeAnimations());
        clock.Advance(5000); AssertTrue(ReferenceEquals(idle, renderer.Render()));
        tree.GetComponent<XsrUiElement>(hidden)!.IsVisible = true; tree.MarkDirty(hidden, XsrUiDirtyKinds.Layout);
        AssertClose(0, renderer.Render().Nodes.Single(node => node.Entity == hidden).VisualStyle.Opacity); AssertTrue(renderer.HasThemeAnimations);
        clock.Advance(1500); AssertTrue(renderer.AdvanceThemeAnimations());
        AssertClose(1, renderer.Render().Nodes.Single(node => node.Entity == hidden).VisualStyle.Opacity);
        AssertFalse(renderer.HasThemeAnimations); AssertFalse(renderer.AdvanceThemeAnimations());
        tree.Attach(parked, root); renderer.Render(); AssertTrue(renderer.HasThemeAnimations);
        tree.Detach(parked); renderer.Render(); AssertFalse(renderer.HasThemeAnimations);
        tree.GetComponent<XsrUiElement>(hidden)!.IsVisible = false; tree.MarkDirty(hidden, XsrUiDirtyKinds.Layout); renderer.Render();
        clock.Advance(2000); tree.GetComponent<XsrUiElement>(hidden)!.IsVisible = true; tree.MarkDirty(hidden, XsrUiDirtyKinds.Layout);
        AssertClose(0, renderer.Render().Nodes.Single(node => node.Entity == hidden).VisualStyle.Opacity); AssertTrue(renderer.HasThemeAnimations);
        renderer.ReducedMotion = true; renderer.Render(); AssertFalse(renderer.HasThemeAnimations);
    }

    private static void ThemeFutureGridFailureRecoversOnceWithoutLosingOwnedState()
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        var editor = ThemeLeaf(tree, root, "editor", 100, 30);
        XsrUiTextInput draft = new(); tree.SetComponent(editor, draft);
        tree.SetComponent(editor, new XsrUiInput { Focusable = true, Clickable = true });
        XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()) { Viewport = new(300, 200) };
        renderer.SetRoot(root); renderer.Render(); renderer.Focus(editor); renderer.SetTextInputValue(editor, "retained draft");
        renderer.SetTextSelection(editor, 1, 4); renderer.Render();
        XsrUiGridLayout grid = new([new(XsrUiGridUnit.Fraction)]);
        renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key == "root" ? new() { Grid = grid } : new()), 1);
        renderer.Render(); long generation = renderer.ThemeActivationGeneration; int cacheCount = renderer.ThemeStyleCacheCount;
        var invalid = new TestThemeProgram(2, context => context.Identity.Key == "root" ? new() { Grid = grid }
            : new() { GridColumn = 1 });
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ValidateTheme(invalid));
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ApplyTheme(invalid, 2));
        AssertEqual(generation, renderer.ThemeActivationGeneration); AssertEqual(cacheCount, renderer.ThemeStyleCacheCount);
        var tooWide = new TestThemeProgram(2, context => context.Identity.Key == "root" ? new() { Grid = grid }
            : new() { GridColumnSpan = 2 });
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ValidateTheme(tooWide));
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ApplyTheme(tooWide, 2));
        XsrUiGridLayout pair = new([new(XsrUiGridUnit.Fraction), new(XsrUiGridUnit.Fraction)]);
        var overflowingPlacement = new TestThemeProgram(2, context => context.Identity.Key == "root" ? new() { Grid = pair }
            : new() { GridColumn = 1, GridColumnSpan = 2 });
        AssertThrows<XsrUiThemeStyleException>(() => renderer.ValidateTheme(overflowingPlacement));
        AssertEqual(generation, renderer.ThemeActivationGeneration); AssertEqual(cacheCount, renderer.ThemeStyleCacheCount);
        renderer.ApplyTheme(new TestThemeProgram(3, context => context.Identity.Key switch
        {
            "root" => new() { Grid = grid },
            "future" => new() { GridColumn = 1 },
            _ => new(),
        }), 2);
        renderer.Render(); var future = ThemeLeaf(tree, root, "future", 100, 20);
        int failures = 0, frames = 0;
        renderer.FramePreparing += (_, _) => frames++;
        renderer.ThemeStyleFailed += (_, failure) =>
        {
            failures++; AssertEqual(future, failure.Exception.Entity!.Value);
            AssertEqual(2L, failure.ActivationGeneration); AssertEqual(3L, failure.ProgramRevision);
            AssertThrows<InvalidOperationException>(() => renderer.Render());
            renderer.ClearTheme(3);
        };
        var recovered = renderer.Render();
        AssertEqual(1, failures); AssertEqual(2, frames); AssertEqual(3L, renderer.ThemeActivationGeneration);
        AssertTrue(recovered.Nodes.Any(node => node.Entity == future)); AssertEqual(editor, renderer.Focused);
        AssertTrue(ReferenceEquals(draft, tree.GetComponent<XsrUiTextInput>(editor)));
        AssertEqual("retained draft", draft.ReadDraft()); AssertEqual(1, draft.SelectionStart); AssertEqual(4, draft.SelectionEnd);
        EventHandler ordinaryHostFailure = (_, _) => throw new ArgumentException("An unrelated application failure.");
        renderer.FramePreparing += ordinaryHostFailure;
        AssertThrows<ArgumentException>(() => renderer.Render()); AssertEqual(1, failures);
        renderer.FramePreparing -= ordinaryHostFailure;
    }

    private static void ThemeStyleFailureWithoutRecoveryAndRepeatedFailureStillPropagate()
    {
        foreach (bool recover in new[] { false, true })
        {
            XsrUiTree tree = new(); var root = tree.Create("root");
            XsrUiRenderer renderer = new(tree, new XsrStateStoreBuilder().Build()) { Viewport = new(300, 200) };
            renderer.SetRoot(root);
            XsrUiGridLayout grid = new([new(XsrUiGridUnit.Fraction)]);
            renderer.ApplyTheme(new TestThemeProgram(1, context => context.Identity.Key == "root" ? new() { Grid = grid }
                : new() { GridColumn = 1 }), 1);
            renderer.Render(); var future = ThemeLeaf(tree, root, "future", 50, 20);
            int failures = 0, frames = 0;
            renderer.FramePreparing += (_, _) => frames++;
            renderer.ThemeStyleFailed += (_, _) =>
            {
                failures++;
                if (!recover) return;
                renderer.ClearTheme(2);
                renderer.ApplyTheme(new TestThemeProgram(2, context => context.Identity.Key == "root" ? new() { Grid = grid }
                    : new() { GridColumn = context.Viewport.Width > 300 ? 1 : 0 }), 3);
                renderer.Viewport = new(400, 200);
            };
            AssertThrows<XsrUiThemeStyleException>(() => renderer.Render());
            AssertEqual(1, failures); AssertEqual(recover ? 2 : 1, frames); AssertTrue(tree.IsAlive(future));
            renderer.ClearTheme(recover ? 4 : 2); AssertTrue(renderer.Render().Nodes.Any(node => node.Entity == future));
        }
    }

    private static XsrUiEntityId ThemeLeaf(XsrUiTree tree, XsrUiEntityId parent, string key, double? width, double? height)
    {
        var entity = tree.Create(key); tree.SetComponent(entity, new XsrUiElement { Width = width, Height = height });
        tree.SetComponent(entity, new XsrUiStyleIdentity("Node", key, []));
        if (parent.IsAssigned) tree.Attach(entity, parent);
        return entity;
    }
    private sealed class TestThemeProgram(long revision, Func<XsrUiThemeStyleContext, XsrUiComputedStyle> compute) : IXsrUiThemeStyleProgram
    {
        public long Revision => revision;
        public XsrUiComputedStyle Compute(XsrUiThemeStyleContext context) => compute(context);
    }
    private sealed class ThemeClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int milliseconds) => _now += TimeSpan.FromMilliseconds(milliseconds);
    }
    private sealed class ThemeFontProvider : IXsrUiTextLayoutProvider
    {
        public long Revision { get; set; }
        public double Width { get; set; } = 40;
        public XsrUiTextLayoutRequest? Last { get; private set; }
        public XsrUiTextLayoutMetrics Measure(XsrUiTextLayoutRequest request)
        {
            Last = request; return new(new(Width, 30));
        }
    }
}
