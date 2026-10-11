using Nexa.UI.Next;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
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
        shell.Renderer.ReducedMotion = false;
        shell.Renderer.Activate(FindByKey(shell, scene, "AccountWardrobe").Entity);
        scene = shell.Render(AccountTestSize);
        XsrUiEntityId title = FindByKey(shell, scene, "TitleSubpage").Entity;
        XsrUiEntityId backEntity = FindByKey(shell, scene, "TitleBack").Entity;
        AssertTrue(shell.Tree.GetComponent<XsrUiTransition>(FindByKey(shell, scene, "TitleNavigation").Entity) is null);
        AssertClose(0, shell.Renderer.GetTransitionOffset(title));
        AssertClose(0, shell.Renderer.GetTransitionOffset(backEntity));
        AssertClose(0, shell.Renderer.GetTransitionOffsetY(shell.Content));
        // Navigation must leave every control at its final hit rectangle immediately.
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var back = FindByKey(shell, scene, "TitleBack");
            AssertEqual(back.Entity, shell.Renderer.HitTest(new(back.Rect.X + 15, back.Rect.Y + 15)));
            AssertTrue(scene.Nodes.All(node => node.TransitionEntryOrder < 0));
            shell.Renderer.Activate(back.Entity);
            scene = shell.Render(AccountTestSize);
            var version = FindByKey(shell, scene, "VersionName");
            AssertClose(0, shell.Renderer.GetTransitionOffsetY(version.Entity));
            AssertEqual(1d, version.PresentationOpacity);
            AssertTrue(shell.Tree.GetComponent<XsrUiTransition>(version.Entity) is null);
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
