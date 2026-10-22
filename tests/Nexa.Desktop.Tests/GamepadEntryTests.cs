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
        AssertTrue(scene.Nodes.Any(node => node.Label == "NexaCL" && node.RasterImage is not null));
        AssertFalse(scene.Nodes.Any(node => node.Text == "左右摇杆内推以进入手柄模式"));
        entry.UpdatePresentation(5500); AssertTrue(entry.IsIntroComplete);
        scene = shell.Render(new(1100, 750));
        AssertFalse(scene.Nodes.Any(node => node.Label == "NexaCL" && node.RasterImage is not null));
        AssertEqual((byte)255, shell.Tree.GetComponent<XsrUiVisualStyle>(entry.Root)!.Background.Alpha);
        AssertTrue(shell.Renderer.HandleKey(XsrUiKey.Escape));
        entry.Step([centered], 5600); AssertFalse(entry.IsEntered); AssertFalse(entry.IsPromptOpen);
        AssertTrue(transitions.SequenceEqual([true, false])); AssertEqual(previous, shell.Renderer.Focused);
        entry.Step([centered], 6000); AssertFalse(entry.IsPromptOpen);
        entry.Step([], 6100); entry.Step([centered], 6200); AssertTrue(entry.IsPromptOpen);
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
    }

    private sealed class EmptyGamepadInput : IGamepadInput
    {
        internal int Disposals;
        public IReadOnlyList<GamepadSample> Read() => [];
        public void Dispose() => Disposals++;
    }
}
