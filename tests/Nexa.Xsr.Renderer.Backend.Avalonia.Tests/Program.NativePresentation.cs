using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{

    private static void VerifyPostNavigationDoubleClickRouting(AvaloniaUiShellWindow window)
    {
        var actions = new AvaloniaUiPlatformActions();
        actions.Attach(window);
        int opened = 0, pressed = 0, released = 0;
        void OnPress(object? sender, PointerPressedEventArgs e) => pressed++;
        void OnRelease(object? sender, PointerReleasedEventArgs e) => released++;
        window.Surface.AddHandler(InputElement.PointerPressedEvent, OnPress);
        window.Surface.AddHandler(InputElement.PointerReleasedEvent, OnRelease);
        try
        {
            var point = window.Surface.TranslatePoint(new Point(400, 350), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            actions.CancelPostNavigationDoubleClick();
            actions.ArmPostNavigationDoubleClick(() => opened++);
            int beforePress = pressed, beforeRelease = released;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            AssertEqual(1, opened);
            AssertEqual(beforePress, pressed);
            AssertEqual(beforeRelease, released);
        }
        finally
        {
            window.Surface.RemoveHandler(InputElement.PointerPressedEvent, OnPress);
            window.Surface.RemoveHandler(InputElement.PointerReleasedEvent, OnRelease);
        }
    }

    private static async Task VerifySpringIgnoresStaleSceneReads()
    {
        object owner = new();
        List<double> positions = [];
        try
        {
            AvaloniaUiMotion.AnimateSpring(owner, "value", () => 0, positions.Add, 1, .3, () => false);
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while ((positions.Count == 0 || positions[^1] < .5) && DateTime.UtcNow < deadline)
                await Task.Delay(16).ConfigureAwait(true);
            AssertTrue(positions.Count > 1 && positions[^1] >= .5);
            for (int i = 1; i < positions.Count; i++) AssertTrue(positions[i] >= positions[i - 1]);
            double before = positions[^1];
            int split = positions.Count;
            AvaloniaUiMotion.AnimateSpring(owner, "value", () => 0, positions.Add, 0, .3, () => false);
            while (positions.Count == split && DateTime.UtcNow < deadline)
                await Task.Delay(16).ConfigureAwait(true);
            AssertTrue(positions[split] > before / 2); // No jump to the stale scene's zero.
            while (positions[^1] != 0 && DateTime.UtcNow < deadline)
                await Task.Delay(16).ConfigureAwait(true);
            AssertEqual(0d, positions[^1]);
        }
        finally { AvaloniaUiMotion.CancelAll(owner); }
        Console.WriteLine("PASS: spring integration and retargeting tolerate delayed scene commits");
    }

    private static void VerifyReentrantRemovalCommit(XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        var first = shell.Tree.Create("reentrant-first");
        var second = shell.Tree.Create("reentrant-second");
        foreach (var entity in new[] { first, second })
        {
            shell.Tree.SetComponent(entity, new XsrUiElement { Height = 30 });
            shell.Tree.SetComponent(entity, new XsrUiText("transient input"));
            shell.Tree.Attach(entity, shell.Root);
        }
        surface.CommitScene();
        bool reentered = false;
        void OnRemoved(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        {
            if (reentered || args.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Remove) return;
            reentered = true;
            shell.Tree.Destroy(second);
            surface.CommitScene();
        }
        surface.Children.CollectionChanged += OnRemoved;
        try
        {
            shell.Tree.Destroy(first);
            surface.CommitScene();
            surface.CommitScene();
            AssertTrue(reentered);
            AssertTrue(surface.Scene!.Nodes.All(node => node.Entity != first && node.Entity != second));
        }
        finally { surface.Children.CollectionChanged -= OnRemoved; }
        Console.WriteLine("PASS: native removal callbacks defer reentrant scene commits");
    }

    private static void VerifyCloseDoesNotRestoreShadows(
        AvaloniaUiShellWindow window, XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        // The shell draws no self-shadow anymore (an opaque margin would expose the true
        // window bounds): nothing may reintroduce one across close, scene updates, or state
        // changes.
        shell.Renderer.ReducedMotion = false;
        AvaloniaNativeWindowActions.WindowActionButton close = window.GetVisualDescendants()
            .OfType<AvaloniaNativeWindowActions.WindowActionButton>().Last();
        Point center = close.TranslatePoint(new Point(14, 14), window)!.Value;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        AssertTrue(window.GetVisualDescendants().OfType<Border>()
            .All(border => border.BoxShadow.Count == 0));
        shell.SetNavigationExpanded(!shell.IsNavigationExpanded);
        surface.CommitScene();
        window.WindowState = WindowState.Maximized;
        window.WindowState = WindowState.Normal;
        AssertTrue(window.GetVisualDescendants().OfType<Border>()
            .All(border => border.BoxShadow.Count == 0));
        AssertTrue(!window.TransparencyLevelHint.Contains(WindowTransparencyLevel.AcrylicBlur));
        Console.WriteLine("PASS: close collapse suppresses application shadow and backdrop across scene updates");
    }

    private static void VerifyAccessibleContentAndNativeFocus(
        AvaloniaUiShellWindow window, XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        surface.CommitScene();
        AutomationPeer root = ControlAutomationPeer.CreatePeerForElement(window);
        static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
        {
            yield return peer;
            foreach (AutomationPeer child in peer.GetChildren())
                foreach (AutomationPeer descendant in Descendants(child)) yield return descendant;
        }
        AssertTrue(Descendants(root).Any(peer => peer.IsContentElement() && peer.GetName() == "启动"));
        AssertTrue(window.FocusManager!.GetFocusedElement() is AvaloniaUiSceneNodeControl);
        AvaloniaUiSceneNodeControl navigation = surface.Children.OfType<AvaloniaUiSceneNodeControl>()
            .Single(control => control.Node.Role == XsrUiSemanticRole.NavigationItem && control.Node.Label == "设置");
        AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(navigation);
        peer.SetFocus();
        AssertEqual(navigation.Node.Entity, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(navigation, window.FocusManager.GetFocusedElement()));
        AssertTrue(peer.HasKeyboardFocus());
        AssertTrue(navigation.Node.IsFocusVisible);
        AssertNotNull(peer.GetProvider<IInvokeProvider>()).Invoke();
        AssertEqual(XsrSemanticId.Parse("navigation.settings"), shell.SelectedNavigationId);
        // Actual keyboard events bubble from the native focused control into the scene surface.
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        AssertTrue(window.FocusManager.GetFocusedElement() is AvaloniaUiSceneNodeControl focused
            && focused.Node.Entity == shell.Renderer.Focused && focused.Node.IsFocusVisible);
        navigation.Apply(navigation.Node with { Label = "应用设置" });
        AssertEqual("应用设置", peer.GetName());
        Console.WriteLine("PASS: native accessibility tree, keyboard focus and invoke are connected");
    }

    private static async Task VerifyOverlayReorderAndReentry(XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        AssertTrue(AvaloniaUiIcons.TryGetGeometry("lucide/power", out var power));
        AssertTrue(power.Count > 0);
        bool wasReduced = shell.Renderer.ReducedMotion;
        shell.Renderer.ReducedMotion = false;
        var preceding = shell.Tree.Create("reentry-preceding");
        shell.Tree.SetComponent(preceding, new XsrUiElement { Width = 48, Height = 48, IsVisible = false });
        shell.Tree.SetComponent(preceding, new XsrUiOverlayMotion(XsrUiOverlayMotionKind.Notification));
        shell.Stage.Show(preceding);
        var bubble = shell.Tree.Create("reentry-bubble");
        shell.Tree.SetComponent(bubble, new XsrUiElement { Width = 48, Height = 48 });
        shell.Tree.SetComponent(bubble, new XsrUiOverlayMotion(XsrUiOverlayMotionKind.Notification));
        shell.Stage.Show(bubble);
        surface.CommitScene();
        var control = surface.Children.OfType<AvaloniaUiSceneNodeControl>().Single(item => item.Node.Entity == bubble);
        int detached = 0;
        control.DetachedFromVisualTree += (_, _) => detached++;
        // Adding an earlier sibling must not detach live controls or interrupt their entry spring.
        shell.Tree.GetComponent<XsrUiElement>(preceding)!.IsVisible = true;
        shell.Tree.MarkDirty(preceding, XsrUiDirtyKinds.Layout);
        surface.CommitScene();
        AssertEqual(0, detached);
        await Task.Delay(450).ConfigureAwait(true);
        AssertTrue(control.PresentedOverlayProgress > .9);
        var motion = shell.Tree.GetComponent<XsrUiOverlayMotion>(bubble)!;
        motion.IsClosing = true; shell.Tree.MarkDirty(bubble, XsrUiDirtyKinds.Paint); surface.CommitScene();
        await Task.Delay(40).ConfigureAwait(true);
        motion.IsClosing = false; shell.Tree.MarkDirty(bubble, XsrUiDirtyKinds.Paint); surface.CommitScene();
        await Task.Delay(450).ConfigureAwait(true);
        AssertTrue(control.PresentedOverlayProgress > .9);
        shell.Tree.GetComponent<XsrUiElement>(bubble)!.IsVisible = false;
        shell.Tree.MarkDirty(bubble, XsrUiDirtyKinds.Layout); surface.CommitScene();
        shell.Tree.GetComponent<XsrUiElement>(bubble)!.IsVisible = true;
        shell.Tree.MarkDirty(bubble, XsrUiDirtyKinds.Layout); surface.CommitScene();
        control = surface.Children.OfType<AvaloniaUiSceneNodeControl>().Single(item => item.Node.Entity == bubble);
        await Task.Delay(450).ConfigureAwait(true);
        AssertTrue(control.PresentedOverlayProgress > .9);
        shell.Stage.Dismiss(bubble); shell.Tree.Destroy(bubble);
        shell.Stage.Dismiss(preceding); shell.Tree.Destroy(preceding);
        surface.CommitScene();
        shell.Renderer.ReducedMotion = wasReduced;
        Console.WriteLine("PASS: overlay reordering preserves attachment and hiding/reentry resumes animation");
    }

    private static void VerifyPointerCursorProjection(
        AvaloniaUiShellWindow window, XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        surface.CommitScene();
        window.UpdateLayout();
        XsrUiSceneNode navigation = surface.Scene!.Nodes.First(node =>
            node.Role == XsrUiSemanticRole.NavigationItem && node.IsClickable);
        Point clickable = surface.TranslatePoint(
            new Point(navigation.Rect.X + navigation.Rect.Width / 2, navigation.Rect.Y + navigation.Rect.Height / 2),
            window)!.Value;
        window.MouseMove(clickable, RawInputModifiers.None);
        AssertEqual(XsrUiPointerCursor.Hand, surface.PresentedPointerCursor);
        AssertTrue(surface.Cursor is not null);

        XsrUiSceneNode titleBar = Node(surface.Scene, shell.TitleBar);
        Point decorative = surface.TranslatePoint(
            new Point(titleBar.Rect.X + titleBar.Rect.Width / 2, titleBar.Rect.Y + titleBar.Rect.Height / 2),
            window)!.Value;
        window.MouseMove(decorative, RawInputModifiers.None);
        AssertEqual(XsrUiPointerCursor.Default, surface.PresentedPointerCursor);
        AssertTrue(surface.Cursor is null);
        Console.WriteLine("PASS: scene input facts drive one stable native pointer cursor");
    }

    private static async Task VerifyPlatformClipboard(AvaloniaUiShellWindow window)
    {
        AvaloniaUiPlatformActions actions = new();
        actions.SetWindowResizeEnabled(false);
        actions.SetAnimationFrameRate(24);
        actions.Attach(window);
        AssertEqual(TimeSpan.FromSeconds(1d / 24), AvaloniaUiMotion.FrameInterval);
        AssertFalse(window.CanResize);
        var buttons = window.GetVisualDescendants().OfType<AvaloniaNativeWindowActions.WindowActionButton>().ToArray();
        // macOS owns its traffic lights; only the other platforms create these custom buttons.
        AssertEqual(OperatingSystem.IsMacOS() ? 0 : 3, buttons.Length);
        var maximize = OperatingSystem.IsMacOS() ? null : buttons[1];
        if (maximize is not null) AssertFalse(maximize.IsEnabled);
        actions.SetWindowResizeEnabled(true);
        AssertTrue(window.CanResize);
        if (maximize is not null) AssertTrue(maximize.IsEnabled);
        actions.SetAnimationFrameRate(60);
        const string code = "ABCD-EFGH";
        await actions.CopyTextAsync(code).ConfigureAwait(true);
        AssertEqual(code, await window.Clipboard!.TryGetTextAsync().ConfigureAwait(true));
        Console.WriteLine("PASS: platform copy marshals to the UI thread and persists text");
    }

    private static async Task VerifyConfigurableMotionFrameClock()
    {
        object owner = new(); double presented = 0; bool completed = false;
        try
        {
            AvaloniaUiMotion.SetFrameRate(24);
            AvaloniaUiMotion.Animate(owner, "rate-test", () => presented, value => presented = value,
                1, 120, reducedMotion: () => false, completed: () => completed = true);
            AssertEqual(TimeSpan.FromSeconds(1d / 24), AvaloniaUiMotion.FrameInterval);
            AvaloniaUiMotion.SetFrameRate(120);
            AssertEqual(0d, presented); // Retiming the dispatcher must not jump or complete a live track.
            AssertFalse(completed);
            AssertEqual(TimeSpan.FromSeconds(1d / 120), AvaloniaUiMotion.FrameInterval);
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (!completed && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
            AssertTrue(completed); AssertEqual(1d, presented);
        }
        finally { AvaloniaUiMotion.CancelAll(owner); AvaloniaUiMotion.SetFrameRate(60); }
        Console.WriteLine("PASS: configured frame interval changes while active timelines retain their completion");
    }

    private static void VerifyWindowActionFeedback(AvaloniaUiShellWindow window, AvaloniaUiSceneSurface surface)
    {
        foreach (AvaloniaNativeWindowActions.WindowActionButton button in window.GetVisualDescendants()
            .OfType<AvaloniaNativeWindowActions.WindowActionButton>())
        {
            AssertTrue(button.HoverBrush is ISolidColorBrush brush && brush.Color.A > 0);
            Point center = button.TranslatePoint(new Point(14, 14), window)!.Value;
            window.MouseMove(center, RawInputModifiers.None);
            AssertEqual(1d, button.PresentedHoverOpacity);
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            AssertEqual(.9, button.PresentedPressScale);
            // Release outside to exercise cancellation without minimizing/closing this test window.
            window.MouseMove(new Point(400, 300), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(400, 300), MouseButton.Left, RawInputModifiers.None);
            AssertEqual(1d, button.PresentedPressScale);
            AssertEqual(0d, button.PresentedHoverOpacity);
        }
        Console.WriteLine("PASS: native caption hover, immediate press and cancellation feedback");
    }

    private static async Task VerifyCapsuleGeometryClock(XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        XsrUiEntityId page = shell.Tree.Create("capsule-clock-page");
        XsrUiEntityId capsule = shell.Tree.Create("capsule-clock-button");
        shell.Tree.SetComponent(capsule, new XsrUiElement { Width = 112, Height = 36 });
        shell.Tree.SetComponent(capsule, new XsrUiInput { Clickable = true, Focusable = true });
        shell.Tree.SetComponent(capsule, new XsrUiVisualStyle { HoverExpand = true });
        shell.Tree.SetComponent(capsule, new XsrUiSemantic(XsrUiSemanticRole.Button, "版本设置"));
        shell.Tree.SetComponent(capsule, new XsrUiText("版本设置"));
        shell.Tree.Attach(capsule, page);
        shell.Stage.Navigation.Replace(page);
        shell.Renderer.ReducedMotion = false;
        surface.CommitScene();
        XsrUiRect collapsed = Node(surface.Scene!, capsule).Rect;
        AssertEqual(36d, collapsed.Width);
        AssertTrue(shell.Renderer.PointerMoved(new XsrUiPoint(collapsed.X + 18, collapsed.Y + 18)));
        surface.CommitScene();
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        double previousWidth = collapsed.Width;
        while (Node(surface.Scene!, capsule).CapsuleExpansionProgress < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(16).ConfigureAwait(true);
            double width = Node(surface.Scene!, capsule).Rect.Width;
            AssertTrue(width >= previousWidth);
            previousWidth = width;
        }
        AssertEqual(112d, Node(surface.Scene!, capsule).Rect.Width);
        AssertTrue(shell.Renderer.PointerMoved(new XsrUiPoint(-1, -1)));
        surface.CommitScene();
        await Task.Delay(30).ConfigureAwait(true);
        shell.Renderer.ReducedMotion = true;
        deadline = DateTime.UtcNow.AddSeconds(2);
        while (Node(surface.Scene!, capsule).CapsuleExpansionProgress > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(16).ConfigureAwait(true);
        AssertEqual(36d, Node(surface.Scene!, capsule).Rect.Width);
        Console.WriteLine("PASS: capsule clock commits actual layout width and reduced-motion reversal");
        VerifyCapsuleCaptionScrollChangesActualInkAndKeepsIconFixed();
    }

    private static async Task VerifySegmentScrollClock(XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        var page = shell.Tree.Create("segment-clock-page"); var root = shell.Tree.Create("segment-clock-track");
        shell.Tree.SetComponent(page, new XsrUiStackPanel(XsrUiOrientation.Vertical)); shell.Tree.Attach(root, page);
        shell.Tree.SetComponent(root, new XsrUiElement { Width = 240, Height = 40, HorizontalAlignment = XsrUiAlignment.Start });
        shell.Tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        var scroll = new XsrUiScroll(); shell.Tree.SetComponent(root, scroll);
        var thumb = shell.Tree.Create("segment-clock-thumb"); shell.Tree.Attach(thumb, root);
        shell.Tree.SetComponent(thumb, new XsrUiElement { IsVisible = false }); shell.Tree.SetComponent(thumb, new XsrUiTransition());
        var track = new XsrUiSegmentedTrack(thumb); shell.Tree.SetComponent(root, track);
        var options = new List<XsrUiEntityId>();
        for (int i = 0; i < 10; i++)
        {
            var option = shell.Tree.Create("segment-clock-option-" + i); options.Add(option); shell.Tree.Attach(option, root);
            shell.Tree.SetComponent(option, new XsrUiElement { Width = 100, Height = 40 });
            shell.Tree.SetComponent(option, new XsrUiInput { Clickable = true, Focusable = true });
            shell.Tree.SetComponent(option, new XsrUiCommandBinding(XsrSemanticId.Parse("test.segment")));
        }
        track.Selected = options[0]; shell.Renderer.ReducedMotion = false; shell.Stage.Navigation.Replace(page); surface.CommitScene();
        AssertTrue(shell.Renderer.Activate(options[1])); track.Selected = options[1]; surface.CommitScene();
        AssertEqual(0d, scroll.OffsetX);
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (scroll.OffsetX == 0 && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
        AssertTrue(scroll.OffsetX > 0 && scroll.OffsetX < 100);
        surface.CommitScene();
        AssertTrue(Math.Abs(Node(surface.Scene!, options[1]).Rect.X - Node(surface.Scene!, root).Rect.X - 100 + scroll.OffsetX) < .01);
        double presented = scroll.OffsetX;
        AssertTrue(shell.Renderer.Activate(options[0])); track.Selected = options[0]; surface.CommitScene();
        AssertEqual(presented, scroll.OffsetX); // Retarget from the live offset, without a jump.
        deadline = DateTime.UtcNow.AddSeconds(2);
        while (scroll.OffsetX != 0 && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
        AssertEqual(0d, scroll.OffsetX);
        AssertTrue(shell.Renderer.Activate(options[2])); track.Selected = options[2]; surface.CommitScene();
        // Wait for a presented frame, rather than assuming the dispatcher has ticked
        // within 32 ms on a busy CI host. The earlier assertion still checks intermediate motion.
        deadline = DateTime.UtcNow.AddSeconds(2);
        while (scroll.OffsetX == 0 && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
        AssertTrue(scroll.OffsetX > 0);
        XsrUiRect viewport = Node(surface.Scene!, root).Rect;
        AssertTrue(shell.Renderer.PointerScroll(new(viewport.X + 150, viewport.Y + 20), 0, 10)); surface.CommitScene();
        double held = scroll.OffsetX; await Task.Delay(100).ConfigureAwait(true); AssertEqual(held, scroll.OffsetX);
        AssertTrue(shell.Renderer.Activate(options[1])); track.Selected = options[1]; surface.CommitScene();
        double target = Node(surface.Scene!, root).SegmentScroll!.Value.Target;
        shell.Renderer.ReducedMotion = true; deadline = DateTime.UtcNow.AddSeconds(2);
        while (scroll.OffsetX != target && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
        AssertEqual(target, scroll.OffsetX);
        Console.WriteLine("PASS: selector viewport clock moves through intermediate frames and supports reversal, wheel interruption and reduced motion");
    }

    private static async Task VerifyPagerNativeDragAndClock(
        AvaloniaUiShellWindow window, XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        XsrUiEntityId root = shell.Tree.Create("native-pager-page");
        XsrUiEntityId entity = shell.Tree.Create("native-pager");
        XsrUiPager pager = new();
        shell.Tree.SetComponent(entity, new XsrUiElement { Width = 220, Height = 140 });
        shell.Tree.SetComponent(entity, new XsrUiInput { Focusable = true });
        shell.Tree.SetComponent(entity, pager);
        shell.Tree.SetComponent(entity, new XsrUiSemantic(XsrUiSemanticRole.Content, "Information cards"));
        shell.Tree.Attach(entity, root);
        for (int i = 0; i < 2; i++)
        {
            XsrUiEntityId child = shell.Tree.Create($"native-pager-card-{i}");
            shell.Tree.SetComponent(child, new XsrUiSemantic(XsrUiSemanticRole.Text, $"Card {i}"));
            shell.Tree.SetComponent(child, new XsrUiText($"Card {i}"));
            shell.Tree.Attach(child, entity);
        }
        shell.Renderer.ReducedMotion = true;
        shell.Stage.Navigation.Replace(root);
        surface.CommitScene();
        window.UpdateLayout();
        XsrUiRect rect = Node(surface.Scene!, entity).Rect;
        Point start = surface.TranslatePoint(new Point(rect.X + 50, rect.Y + 110), window)!.Value;
        Point end = start.WithY(start.Y - 90);
        shell.Renderer.ReducedMotion = false;
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        AssertTrue(pager.IsDragging && pager.Position > .5 && pager.Position < 1);
        double held = pager.Position;
        await Task.Delay(40).ConfigureAwait(true);
        AssertEqual(held, pager.Position); // A captured drag, not an autonomous animation.
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
        AssertEqual(1, pager.PageIndex);
        AssertTrue(!pager.IsDragging);
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (pager.Position != 1 && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
        AssertEqual(1d, pager.Position);

        AssertTrue(shell.Renderer.MovePager(entity, -1));
        surface.CommitScene();
        await Task.Delay(32).ConfigureAwait(true);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        held = pager.Position;
        await Task.Delay(40).ConfigureAwait(true);
        AssertEqual(held, pager.Position); // Re-grab a moving card without a jump.
        window.MouseUp(start, MouseButton.Left, RawInputModifiers.None);
        shell.Renderer.ReducedMotion = true;
        deadline = DateTime.UtcNow.AddSeconds(2);
        while (pager.Position != 0 && DateTime.UtcNow < deadline) await Task.Delay(16).ConfigureAwait(true);
        AssertEqual(0d, pager.Position);
        AssertTrue(shell.Renderer.Focus(entity));
        surface.CommitScene();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        AssertEqual(1, pager.PageIndex);
        AssertEqual(1d, pager.Position);
        Console.WriteLine("PASS: native captured card drag, interruption, spring settling and keyboard paging");
    }
}
