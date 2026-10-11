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
using Nexa.UI.Next;
using Nexa.UI.Next.Backend.Avalonia;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.UI.Next.Backend.Avalonia.Tests;

internal static partial class Program
{

    private static void PostNavigationDoubleClickBoundaries()
    {
        var clicks = new PostNavigationDoubleClick();
        Action open = () => { };
        TimeSpan interval = TimeSpan.FromMilliseconds(500);
        clicks.Press(new(20, 30), 100, true, interval, 2, 2);
        clicks.Arm(open);
        AssertEqual(open, clicks.Press(new(21, 31), 200, true, interval, 2, 2));
        AssertTrue(clicks.Press(new(21, 31), 250, true, interval, 2, 2) is null);
        clicks.Arm(open);
        AssertTrue(clicks.Press(new(21, 31), 751, true, interval, 2, 2) is null);
        clicks.Arm(open);
        AssertTrue(clicks.Press(new(30, 31), 800, true, interval, 2, 2) is null);
        clicks.Arm(open);
        AssertTrue(clicks.Press(new(30, 31), 850, false, interval, 2, 2) is null);
        clicks.Press(new(30, 31), 900, true, interval, 2, 2);
        clicks.Arm(open);
        clicks.Cancel();
        AssertTrue(clicks.Press(new(30, 31), 950, true, interval, 2, 2) is null);
    }

    private static void AutomationInvokeAndFocusRouteThroughRenderer()
    {
        XsrUiTree tree = new();
        XsrStateStore store = new XsrStateStoreBuilder().Build();
        XsrUiIntentBuffer intents = new();
        XsrUiEntityId root = tree.Create("root");
        XsrUiEntityId button = tree.Create("button");
        tree.SetComponent(button, new XsrUiElement { Width = 100, Height = 40 });
        tree.SetComponent(button, new XsrUiInput { Focusable = true, Clickable = true });
        tree.SetComponent(button, new XsrUiCommandBinding(XsrSemanticId.Parse("app.save")));
        tree.SetComponent(button, new XsrUiSemantic(XsrUiSemanticRole.Button, "Save"));
        tree.Attach(button, root);

        XsrUiRenderer renderer = new(tree, store, intents);
        renderer.SetRoot(root);
        XsrUiScene scene = renderer.Render();
        AvaloniaUiSceneNodeControl control = new(
            entity => _ = renderer.Focus(entity),
            entity =>
            {
                _ = renderer.Focus(entity);
                _ = renderer.Activate(entity);
            },
            reducedMotion: () => true);
        control.Apply(Node(scene, button));

        AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(control);
        peer.SetFocus();
        AssertEqual(button, renderer.Focused);

        IInvokeProvider invoke = AssertNotNull(peer.GetProvider<IInvokeProvider>());
        invoke.Invoke();
        AssertEqual(1, intents.Count);
        AssertEqual(button, intents.Drain()[0].Source);

        AvaloniaUiSceneNodeControl text = new(_ => { }, _ => { }, reducedMotion: () => true);
        text.Apply(new XsrUiSceneNode(
            tree.Create("text"),
            new XsrUiRect(0, 0, 10, 10),
            0,
            XsrUiSemanticRole.Text,
            "Status",
            "ready",
            null,
            false,
            null,
            null));
        AssertTrue(ControlAutomationPeer.CreatePeerForElement(text).GetProvider<IInvokeProvider>() is null);
    }

    private static void NavigationPeersExposeSelectionAndRouteSelection()
    {
        XsrUiTree tree = new();
        XsrUiEntityId navigationEntity = tree.Create("navigation");
        XsrUiEntityId selectedEntity = tree.Create("selected");
        XsrUiEntityId otherEntity = tree.Create("other");
        int focusCount = 0;
        int invokeCount = 0;

        AvaloniaUiSceneNodeControl navigation = new(_ => focusCount++, _ => invokeCount++, reducedMotion: () => true);
        AvaloniaUiSceneNodeControl selected = new(_ => focusCount++, _ => invokeCount++, reducedMotion: () => true);
        AvaloniaUiSceneNodeControl other = new(_ => focusCount++, _ => invokeCount++, reducedMotion: () => true);
        navigation.Apply(Node(navigationEntity, XsrUiSemanticRole.Navigation));
        selected.Apply(Node(
            selectedEntity,
            XsrUiSemanticRole.NavigationItem,
            selected: true,
            focusable: true,
            clickable: true));
        other.Apply(Node(
            otherEntity,
            XsrUiSemanticRole.NavigationItem,
            focusable: true,
            clickable: true));
        selected.SetSelectionContainer(navigation);
        other.SetSelectionContainer(navigation);
        navigation.AddSelectionItem(selected);
        navigation.AddSelectionItem(other);

        ISelectionProvider selection = AssertNotNull(
            ControlAutomationPeer.CreatePeerForElement(navigation).GetProvider<ISelectionProvider>());
        ISelectionItemProvider selectedItem = AssertNotNull(
            ControlAutomationPeer.CreatePeerForElement(selected).GetProvider<ISelectionItemProvider>());

        AssertFalse(selection.CanSelectMultiple);
        AssertTrue(selection.IsSelectionRequired);
        AssertEqual(1, selection.GetSelection().Count);
        AssertTrue(selectedItem.IsSelected);
        AssertTrue(ReferenceEquals(selection, selectedItem.SelectionContainer));

        selectedItem.Select();
        AssertEqual(1, invokeCount);
        AssertEqual(0, focusCount);
    }

    private static void FeedbackOverlaysExposeLiveSemanticsAndReducedMotion()
    {
        XsrUiTree tree = new();
        AvaloniaUiSceneNodeControl control = new(_ => { }, _ => { }, reducedMotion: () => true);
        XsrUiSceneNode info = Node(tree.Create("info"), XsrUiSemanticRole.Status) with
        {
            Label = "Info: ready",
            LiveSetting = XsrUiLiveSetting.Polite,
            OverlayMotion = XsrUiOverlayMotionKind.Notification,
            OverlayAnchor = new XsrUiRect(0, 0, 360, 76),
        };

        control.Apply(info);
        AssertEqual(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(control));
        AssertEqual("Info: ready", AutomationProperties.GetItemStatus(control));
        AssertEqual(1d, control.PresentedOverlayProgress);
        AssertEqual(0d, control.PresentedOverlayTranslationY);

        control.Apply(info with { IsOverlayClosing = true });
        AssertEqual(0d, control.PresentedOverlayProgress);
        AssertTrue(control.PresentedOverlayTranslationY > 0);

        XsrUiSceneNode dialog = Node(tree.Create("dialog"), XsrUiSemanticRole.Dialog) with
        {
            LiveSetting = XsrUiLiveSetting.Assertive,
            OverlayMotion = XsrUiOverlayMotionKind.Dialog,
            OverlayAnchor = new XsrUiRect(0, 0, 440, 238),
        };
        control.Apply(dialog);
        AssertEqual(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(control));
        AssertEqual(1d, control.PresentedOverlayProgress);
    }

    private static void CapsuleSpringPreservesReversalVelocity()
    {
        (double position, double velocity) = AvaloniaUiMotion.StepCriticalSpring(0, 0, 1, .08, .3);
        AssertTrue(position > 0 && position < 1 && velocity > 0);
        (double reversedPosition, double reversedVelocity) = AvaloniaUiMotion.StepCriticalSpring(position, velocity, 0, 0, .3);
        AssertEqual(position, reversedPosition);
        AssertEqual(velocity, reversedVelocity);
        (double settled, double speed) = AvaloniaUiMotion.StepCriticalSpring(position, velocity, 0, 1, .3);
        AssertTrue(Math.Abs(settled) < .0001 && Math.Abs(speed) < .001);
        for (int i = 0; i < 100; i++)
        {
            (double value, _) = AvaloniaUiMotion.StepCriticalSpring(0, 0, 1, i * .01, .3);
            AssertTrue(value >= 0 && value <= 1);
        }
    }

    private static void CapsulesRespondToFocusPressAndDisabledState()
    {
        XsrUiTree tree = new();
        XsrUiEntityId entity = tree.Create("capsule");
        AvaloniaUiSceneNodeControl control = new(_ => { }, _ => { }, reducedMotion: () => true);
        XsrUiSceneNode node = Node(entity, XsrUiSemanticRole.Button, focusable: true, clickable: true) with
        {
            Text = "版本列表",
            Label = "打开版本列表",
            VisualStyle = new XsrUiVisualStyle { HoverExpand = true }.Snapshot(),
        };
        control.Apply(node);
        AssertEqual(0, control.PresentedPillExpand);
        control.Apply(node with { IsFocused = true });
        AssertEqual(0, control.PresentedPillExpand);
        AssertTrue(control.FocusAdorner is null);
        control.Apply(node with { IsFocused = true, IsFocusVisible = true, CapsuleExpansionProgress = 1 });
        AssertEqual(1, control.PresentedPillExpand);
        control.Apply(node with { IsHovered = true, CapsuleExpansionProgress = 1 });
        AssertEqual(1, control.PresentedPillExpand);
        control.Apply(node with { IsHovered = true, IsEnabled = false });
        AssertEqual(0, control.PresentedPillExpand);
        AssertFalse(control.IsEnabled);
        control.Apply(node);
        AssertEqual(0, control.PresentedPillExpand);
        AssertTrue(control.IsEnabled);
    }

    private static void SelectionAndHoverFactsPresentUnderReducedMotion()
    {
        // Reduced motion applies every scene fact immediately, so the assertions below are
        // synchronous even though the animated path eases the same values.
        XsrUiTree tree = new();
        XsrUiEntityId itemEntity = tree.Create("item");
        AvaloniaUiSceneNodeControl item = new(_ => { }, _ => { }, reducedMotion: () => true);

        item.Apply(Node(itemEntity, XsrUiSemanticRole.NavigationItem, selected: true, focusable: true, clickable: true));
        AssertEqual(1, item.PresentedPillScale);
        AssertEqual(0, item.PresentedHoverOpacity);

        item.Apply(Node(itemEntity, XsrUiSemanticRole.NavigationItem, focusable: true, clickable: true));
        AssertEqual(0, item.PresentedPillScale);

        item.Apply(Node(
            itemEntity,
            XsrUiSemanticRole.NavigationItem,
            selected: false,
            focusable: true,
            clickable: true,
            hovered: true));
        AssertEqual(1, item.PresentedHoverOpacity);

        item.Apply(Node(itemEntity, XsrUiSemanticRole.NavigationItem, focusable: true, clickable: true));
        AssertEqual(0, item.PresentedHoverOpacity);
        AssertEqual(0, item.PresentedPillScale);
    }

    // A 1x1 transparent PNG so the splash has a decodable icon without an asset dependency.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static MemoryStream TinyPng() => new MemoryStream(Convert.FromBase64String(TinyPngBase64));
}
