using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void GamepadEntryRequiresNeutralSameDeviceGestureAndRestores()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell; shell.Render(new(1100, 750));
        var previous = shell.NavigationEntities.Values.First(); shell.Renderer.Focus(previous);
        List<bool> transitions = [];
        using var entry = new GamepadEntrySession(shell, fixture.Intents, fixture.Store, new(), runTimer: false, immersive: transitions.Add);
        var centered = new GamepadSample("pad", GamepadLayout.SymmetricLow);
        var inward = centered with { LeftX = .9f, RightX = -.9f };
        entry.Step([inward], 0); entry.Step([inward], 1000);
        AssertTrue(entry.IsPromptOpen); AssertFalse(entry.IsEntered);
        entry.Step([centered with { Ready = false }], 1100);
        entry.Step([inward], 1200); entry.Step([inward], 1500); AssertFalse(entry.IsEntered);
        entry.Step([centered], 1600);
        entry.Step([centered with { LeftX = 1 }, new("other", GamepadLayout.OffsetLeftHigh, RightX: -1)], 1700);
        entry.Step([centered with { LeftX = 1 }, new("other", GamepadLayout.OffsetLeftHigh, RightX: -1)], 2000);
        AssertFalse(entry.IsEntered);
        entry.Step([inward], 2100); entry.Step([inward], 2400, foreground: false); AssertFalse(entry.IsEntered);
        entry.Step([inward], 2500); entry.Step([inward], 2800); AssertFalse(entry.IsEntered);
        entry.Step([centered], 2900); entry.Step([inward with { LeftY = .8f }], 3000);
        entry.Step([inward with { LeftY = .8f }], 3300); AssertFalse(entry.IsEntered);
        entry.Step([inward], 3400); entry.Step([inward], 3599); AssertFalse(entry.IsEntered);
        entry.Step([inward], 3600); AssertTrue(entry.IsEntered); AssertTrue(transitions.SequenceEqual([true]));
        entry.UpdatePresentation(4400);
        var scene = shell.Render(new(1100, 750));
        AssertFalse(scene.Nodes.Any(node => node.ImageSource == "brand/n" && node.PresentationOpacity * node.VisualStyle.Opacity > 0));
        AssertFalse(scene.Nodes.Any(node => node.Text == "左右摇杆内推以进入手柄模式"));
        entry.UpdatePresentation(9600); AssertTrue(entry.IsIntroComplete);
        scene = shell.Render(new(1100, 750));
        AssertTrue(scene.Nodes.Any(node => node.ImageSource == "brand/n" && node.RasterImage is not null));
        AssertTrue(scene.Nodes.Any(node => node.Text == "NexaCL"));
        AssertTrue(scene.Nodes.Any(node => node.Text == "手柄模式"));
        AssertEqual((byte)255, shell.Tree.GetComponent<XsrUiVisualStyle>(entry.Root)!.Background.Alpha);
        AssertTrue(shell.Renderer.HandleKey(XsrUiKey.Escape));
        entry.Step([centered], 9700); AssertFalse(entry.IsEntered); AssertFalse(entry.IsPromptOpen);
        AssertTrue(transitions.SequenceEqual([true, false])); AssertEqual(previous, shell.Renderer.Focused);
        entry.Step([centered], 9800); AssertFalse(entry.IsPromptOpen);
        entry.Step([], 9900); entry.Step([centered], 10000); AssertTrue(entry.IsPromptOpen);
    }

    private static void GamepadEntryCinematicTimelineAndStableLockup()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell;
        shell.Renderer.ReducedMotion = false;
        shell.Render(new(1440, 900));
        using var entry = new GamepadEntrySession(shell, fixture.Intents, fixture.Store, new(), runTimer: false, immersive: _ => { });
        var pad = new GamepadSample("pad", GamepadLayout.OffsetLeftHigh);
        entry.Step([pad], 0); entry.Step([pad with { LeftX = 1, RightX = -1 }], 10);
        entry.Step([pad with { LeftX = 1, RightX = -1 }], 210);

        var black = At(800);
        AssertEqual((byte)0, shell.Tree.GetComponent<XsrUiVisualStyle>(entry.Root)!.Background.Red);
        AssertEqual((byte)255, shell.Tree.GetComponent<XsrUiVisualStyle>(entry.Root)!.Background.Alpha);
        AssertFalse(black.Nodes.Any(node => node.ImageSource is "brand/n" or "gamepad/flash" or "gamepad/horizon" && node.PresentationOpacity * node.VisualStyle.Opacity > 0));
        AssertFalse(black.Nodes.Any(node => shell.Tree.Name(node.Entity) is "GamepadName" or "GamepadCaption"));

        var flash = At(1600);
        AssertTrue(flash.Nodes.Any(node => node.ImageSource == "gamepad/flash" && node.PresentationOpacity * node.VisualStyle.Opacity > .9));
        AssertFalse(At(1849).Nodes.Any(node => shell.Tree.Name(node.Entity) == "GamepadDawnVeil"));
        var giant = At(2000);
        var horizon = giant.Nodes.Single(node => node.ImageSource == "gamepad/horizon");
        var corner = horizon.Rect;
        AssertTrue(corner.Width > 1440 && corner.Height > 900 && corner.X < 0 && corner.Y < 0);
        AssertTrue(Math.Abs(corner.X + corner.Width * 118 / 1024 - 720) < .001);
        AssertTrue(Math.Abs(corner.Y + corner.Height * 118 / 1024 - 450) < .001);
        AssertEqual(1024, horizon.RasterImage!.Image.Width); AssertTrue(horizon.RasterImage.FitToBounds);
        AssertFalse(giant.Nodes.Any(node => node.ImageSource == "brand/n"));
        AssertFalse(giant.Nodes.Any(node => shell.Tree.Name(node.Entity) is "GamepadName" or "GamepadDawnVeil"));
        var sun = giant.Nodes.Single(node => node.ImageSource == "gamepad/flash");
        AssertTrue(sun.Depth == horizon.Depth && giant.Nodes.ToList().IndexOf(sun) < giant.Nodes.ToList().IndexOf(horizon));
        var pullingBack = Logo(At(3000));
        AssertFalse(At(3650).Nodes.Any(node => node.ImageSource == "gamepad/horizon"));
        var centered = Logo(At(4100));
        AssertTrue(pullingBack.Width < corner.Width && pullingBack.Width > centered.Width);
        AssertTrue(Math.Abs(centered.X + centered.Width / 2 - 720) < .001);
        AssertTrue(Math.Abs(centered.Y + centered.Height / 2 - 450) < .001);
        var named = At(5000);
        var moved = Logo(named);
        AssertTrue(moved.X < centered.X);
        AssertTrue(named.Nodes.Any(node => shell.Tree.Name(node.Entity) == "GamepadName" && node.PresentationOpacity * node.VisualStyle.Opacity == 1));
        AssertFalse(named.Nodes.Any(node => node.Text == "手柄模式"));
        var caption = At(5500);
        AssertTrue(Logo(caption).Y < moved.Y);
        AssertTrue(caption.Nodes.Any(node => node.Text == "手柄模式" && node.PresentationOpacity * node.VisualStyle.Opacity > 0));
        var final = At(5900);
        AssertTrue(entry.IsIntroComplete);
        var still = At(10000);
        AssertEqual(Logo(final), Logo(still));
        AssertFalse(still.Nodes.Any(node => node.ImageSource == "gamepad/flash"));
        AssertTrue(still.Nodes.Any(node => node.Text == "手柄模式" && node.PresentationOpacity * node.VisualStyle.Opacity == 1));
        shell.Render(new(1920, 1080)); entry.UpdatePresentation(10220);
        AssertTrue(Math.Abs(Logo(shell.Render(new(1920, 1080))).Height - 168) < .001);

        XsrUiScene At(int age) { entry.UpdatePresentation(210 + age); return shell.Render(new(1440, 900)); }
        static XsrUiRect Logo(XsrUiScene scene) => scene.Nodes.Single(node => node.ImageSource == "brand/n").Rect;
    }

    private static void GamepadEntryCameraStartsGentlyAnchorsTheRimAndRespectsFrameRate()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell; shell.Renderer.ReducedMotion = false;
        shell.Render(new(1440, 900));
        using var entry = new GamepadEntrySession(shell, fixture.Intents, fixture.Store, new(), runTimer: false, immersive: _ => { });
        var pad = new GamepadSample("camera", GamepadLayout.OffsetLeftHigh);
        entry.Step([pad], 0); entry.Step([pad with { LeftX = 1, RightX = -1 }], 10);
        entry.Step([pad with { LeftX = 1, RightX = -1 }], 210);
        var initial = Camera(2100);
        AssertTrue(Camera(2200).Width / initial.Width > .97);
        var previous = initial;
        for (int age = 2116; age < 3900; age += 16)
        {
            var current = Camera(age);
            AssertTrue(current.Width <= previous.Width && current.Width >= 168 - .001);
            AssertTrue(Math.Log(previous.Width / current.Width) < .09);
            double rim = current.X + current.Width * 118 / 1024;
            double oldRim = previous.X + previous.Width * 118 / 1024;
            AssertTrue(rim <= oldRim + .001 && oldRim - rim < 6);
            AssertTrue(rim >= 720 - (.5 - 118d / 1024) * 168 - .001);
            previous = current;
        }
        AssertTrue(Math.Abs(Camera(2600).X + Camera(2600).Width * 118 / 1024 - 720) < .001);
        AssertTrue(Math.Log(initial.Width / Camera(2116).Width) < .0001);
        AssertTrue(Math.Log(Camera(3884).Width / Camera(3900).Width) < .0001);
        AssertEqual(Camera(3900), Camera(4200));
        var fps = fixture.Store.Resolve(Nexa.Xsr.XsrSemanticId.Parse("UiAniFPS"));
        fixture.Store.Publish(fps, 29); shell.Render(new(1440, 900));
        AssertTrue(Math.Abs(entry.PresentationInterval.TotalMilliseconds - 1000d / 30) < .001);
        fixture.Store.Publish(fps, 119); shell.Render(new(1440, 900));
        AssertTrue(Math.Abs(entry.PresentationInterval.TotalMilliseconds - 1000d / 120) < .001);

        XsrUiRect Camera(int age)
        {
            entry.UpdatePresentation(210 + age);
            var scene = shell.Render(new(1440, 900));
            return scene.Nodes.First(node => node.ImageSource is "gamepad/horizon" or "brand/n").Rect;
        }
    }

    private static void GamepadEntryDisconnectDisposesAndDefersExistingModal()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var palette = new DesktopCommandPalette(fixture.Shell, fixture.Intents, fixture.Store, new(), _ => { });
        var source = new EmptyGamepadInput(); List<bool> transitions = [];
        using var entry = new GamepadEntrySession(fixture.Shell, fixture.Intents, fixture.Store, new(), source, runTimer: false, immersive: transitions.Add);
        var pad = new GamepadSample("pad", GamepadLayout.SymmetricHigh);
        AssertTrue(palette.Open()); entry.Step([pad], 0); AssertFalse(entry.IsPromptOpen);
        palette.Close(); entry.Step([pad], 10); AssertTrue(entry.IsPromptOpen);
        entry.Step([pad with { Cancel = true }], 20); entry.Step([pad], 30); AssertFalse(entry.IsPromptOpen);
        entry.Step([], 40); entry.Step([pad], 50);
        var inward = pad with { LeftX = 1, RightX = -1 };
        entry.Step([inward], 100); entry.Step([inward], 310); AssertTrue(entry.IsEntered);
        entry.Step([], 320); AssertFalse(entry.IsEntered); AssertTrue(transitions.SequenceEqual([true, false]));
        entry.Step([pad], 400); entry.Step([inward], 410); entry.Step([inward], 620);
        entry.Dispose(); entry.Dispose(); AssertEqual(1, source.Disposals);
        AssertTrue(transitions.SequenceEqual([true, false, true, false]));
        entry.Step([pad], 1000); AssertFalse(entry.IsPromptOpen);
    }

    private static void GamepadEntryLayoutsArtworkAndReducedMotion()
    {
        AssertEqual(GamepadLayout.SymmetricLow, NativeGamepadInput.Layout("", 0x054c, 0x0ce6));
        AssertEqual(GamepadLayout.SymmetricHigh, NativeGamepadInput.Layout("", 0x057e, 0x0330));
        AssertEqual(GamepadLayout.OffsetLeftHigh, NativeGamepadInput.Layout("Pro Controller", 0x057e, 0x2009));
        AssertEqual(GamepadLayout.OffsetLeftHigh, NativeGamepadInput.Layout("unknown"));
        AssertEqual(-1f, NativeGamepadInput.NormalizeHidAxis(0x8000, -32768, 32767, 16));
        AssertEqual(1f, NativeGamepadInput.NormalizeHidAxis(0x7fff, -32768, 32767, 16));
        AssertTrue(Math.Abs(NativeGamepadInput.NormalizeHidAxis(127, 0, 255, 8)) < .01);
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Renderer.ReducedMotion = true;
        using var entry = new GamepadEntrySession(fixture.Shell, fixture.Intents, fixture.Store, new(), runTimer: false, immersive: _ => { });
        foreach (var layout in Enum.GetValues<GamepadLayout>())
        {
            var first = entry.Artwork(layout, 0); var next = entry.Artwork(layout, 1); var tilted = entry.Artwork(layout, 24);
            AssertEqual(1500, first.Image.Width); AssertEqual(720, first.Image.Height);
            AssertTrue(ReferenceEquals(first.Image, next.Image)); AssertFalse(first.Layers[0].Source == next.Layers[0].Source);
            AssertFalse(first.Image.Key == tilted.Image.Key);
        }
        var pad = new GamepadSample("pad", GamepadLayout.OffsetLeftHigh);
        entry.Step([pad], 0); var root = entry.Root;
        entry.Step([pad with { LeftX = 1, RightX = -1 }], 10);
        entry.Step([pad with { LeftX = 1, RightX = -1 }], 220);
        AssertTrue(entry.IsEntered); AssertTrue(entry.IsIntroComplete);
        AssertEqual((byte)255, fixture.Shell.Tree.GetComponent<XsrUiVisualStyle>(root)!.Background.Alpha);
        var scene = fixture.Shell.Render(new(1100, 750));
        AssertFalse(scene.Nodes.Any(node => node.ImageSource == "gamepad/flash"));
        AssertTrue(scene.Nodes.Any(node => node.Text == "NexaCL"));
        AssertTrue(scene.Nodes.Any(node => node.Text == "手柄模式"));
    }

    private sealed class EmptyGamepadInput : IGamepadInput
    {
        internal int Disposals;
        public IReadOnlyList<GamepadSample> Read() => [];
        public void Dispose() => Disposals++;
    }
}
