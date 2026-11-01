using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void LaunchQuietPreservesForegroundNavigationAndCapsuleMotion()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]), addProfile: true);
        using var productPages = new LaunchFixtureNavigationPages(fixture);
        XsrUiShell shell = fixture.Shell;
        shell.PublishWindowActivity(true, false);
        shell.Renderer.ReducedMotion = false;
        shell.Render(AccountTestSize);
        using (fixture.Foundation.Host.Work.EnterQuiet())
        {
            var scene = shell.Render(AccountTestSize);
            AssertFalse(shell.Renderer.OptionalMotionSuspended);
            AssertFalse(shell.Renderer.EffectiveReducedMotion);
            var capsule = FindByKey(shell, scene, "AccountSwitch");
            shell.Renderer.PointerMoved(new(capsule.Rect.X + 1, capsule.Rect.Y + 18));
            shell.Renderer.SetCapsulePresentationProgress(capsule.Entity, .35);
            shell.Render(AccountTestSize);
            AssertEqual(.35, shell.Tree.GetComponent<XsrUiInput>(capsule.Entity)!.CapsuleExpansionProgress);

            shell.Renderer.Activate(FindByKey(shell, scene, "AccountWardrobe").Entity);
            scene = shell.Render(AccountTestSize);
            AssertTrue(shell.Renderer.GetTransitionOffset(FindByKey(shell, scene, "TitleSubpage").Entity) > 0);
            shell.PublishWindowActivity(false, false); shell.Render(AccountTestSize);
            AssertTrue(shell.Renderer.OptionalMotionSuspended);
            shell.PublishWindowActivity(true, false); shell.Render(AccountTestSize);
            AssertFalse(shell.Renderer.EffectiveReducedMotion);
            shell.Renderer.ReducedMotion = true;
            shell.Render(AccountTestSize);
            AssertTrue(shell.Renderer.EffectiveReducedMotion);
            AssertFalse(shell.Renderer.OptionalMotionSuspended);
        }
        shell.Render(AccountTestSize);
        AssertFalse(shell.Renderer.OptionalMotionSuspended);
        AssertTrue(shell.Renderer.ReducedMotion);
    }

    private static void CapsulesIgnoreGeometryOnlyHoverMoves()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]), addProfile: true);
        XsrUiShell shell = fixture.Shell;
        shell.Renderer.ReducedMotion = false;
        XsrUiScene scene = shell.Render(AccountTestSize);
        XsrUiSceneNode capsule = FindByKey(shell, scene, "AccountSwitch");
        XsrUiPoint pointer = new(capsule.Rect.X + 1, capsule.Rect.Y + 18);
        shell.Renderer.PointerMoved(pointer);
        // A neighbour's geometry or a native arrange can move the same capsule under a stationary cursor.
        XsrUiElement actions = shell.Tree.GetComponent<XsrUiElement>(FindByKey(shell, scene, "AccountActions").Entity)!;
        actions.Margin = new XsrUiThickness(0, 20, 48, 0);
        shell.Tree.MarkDirty(FindByKey(shell, scene, "AccountActions").Entity, XsrUiDirtyKinds.Layout);
        for (int frame = 0; frame <= 30; frame++)
        {
            shell.Renderer.SetCapsulePresentationProgress(capsule.Entity, frame / 30d);
            scene = shell.Render(AccountTestSize);
            shell.Renderer.PointerMoved(pointer);
            AssertTrue(shell.Tree.GetComponent<XsrUiInput>(capsule.Entity)!.IsHovered);
            AssertEqual(XsrUiPointerCursor.Hand, shell.Renderer.PointerCursorAt(pointer));
        }
        // Real pointer departure is still immediate. Hysteresis never changes click hit rectangles.
        shell.Renderer.PointerMoved(new XsrUiPoint(800, 460));
        AssertFalse(shell.Tree.GetComponent<XsrUiInput>(capsule.Entity)!.IsHovered);
        shell.Renderer.PointerMoved(new XsrUiPoint(-1, -1));
    }

    private static void NavigationKeepsControlsStableAndImmediatelyReachable()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]), addProfile: true);
        using var productPages = new LaunchFixtureNavigationPages(fixture);
        XsrUiShell shell = fixture.Shell;
        shell.Renderer.ReducedMotion = true;
        XsrUiScene scene = shell.Render(AccountTestSize);
        XsrUiRect versionBounds = FindByKey(shell, scene, "VersionName").Rect;
        shell.Renderer.ReducedMotion = false;
        shell.Renderer.Activate(FindByKey(shell, scene, "AccountWardrobe").Entity);
        scene = shell.Render(AccountTestSize);
        XsrUiEntityId title = FindByKey(shell, scene, "TitleSubpage").Entity;
        XsrUiEntityId backEntity = FindByKey(shell, scene, "TitleBack").Entity;
        AssertTrue(shell.Tree.GetComponent<XsrUiTransition>(FindByKey(shell, scene, "TitleNavigation").Entity) is null);
        AssertTrue(shell.Renderer.GetTransitionOffset(title) > 0);
        AssertTrue(shell.Renderer.GetTransitionOffset(backEntity) > 0);
        AssertClose(0, shell.Renderer.GetTransitionOffsetY(shell.Content));
        // Entry tracks belong to leaves; visible controls remain immediately clickable.
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var back = FindByKey(shell, scene, "TitleBack");
            AssertEqual(back.Entity, shell.Renderer.HitTest(new(back.Rect.X + 15, back.Rect.Y + 15)));
            var entering = scene.Nodes.Where(node => node.TransitionEntryOrder >= 0).ToArray();
            AssertTrue(entering.Length > 0);
            AssertTrue(entering.All(node => shell.Tree.Children(node.Entity).Count == 0));
            shell.Renderer.Activate(back.Entity);
            scene = shell.Render(AccountTestSize);
            var version = FindByKey(shell, scene, "VersionName");
            AssertRectClose(versionBounds, version.Rect with
            {
                X = version.Rect.X - version.TransitionPresentedOffsetX,
                Y = version.Rect.Y - version.TransitionPresentedOffsetY,
            });
            AssertClose(8, shell.Renderer.GetTransitionOffsetY(version.Entity));
            AssertTrue(version.PresentationOpacity < 1d);
            AssertTrue(shell.Tree.GetComponent<XsrUiTransition>(version.Entity) is { StaggerEntry: true, MovesSelf: true });
            AssertTrue(shell.Tree.GetComponent<XsrUiTransition>(FindByKey(shell, scene, "AccountBody").Entity) is { StaggerEntry: false });
            shell.Renderer.Activate(FindByKey(shell, scene, "AccountWardrobe").Entity);
            scene = shell.Render(AccountTestSize);
        }
        shell.Renderer.ReducedMotion = true;
        scene = shell.Render(AccountTestSize);
        AssertEqual(0, scene.Outgoing.Count);
        AssertClose(0, shell.Renderer.GetTransitionOffsetY(shell.Content));
    }
}
