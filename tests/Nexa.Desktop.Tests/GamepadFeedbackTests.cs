using System.Runtime.InteropServices;
using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class FeedbackGamepadInput : IGamepadInput, IGamepadFeedback
    {
        internal readonly List<(string Device, GamepadFeedback Value)> Calls = [];
        public IReadOnlyList<GamepadSample> Read() => [];
        public void ApplyFeedback(string device, GamepadFeedback feedback) => Calls.Add((device, feedback));
        public void Dispose() { }
    }
    private static void GamepadActivityHoldAndIndependentFeedbackTimelines()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Render(new(1100, 750));
        var input = new FeedbackGamepadInput();
        bool transitioned = false;
        using var entry = new GamepadEntrySession(fixture.Shell, fixture.Intents, fixture.Store, new(), input,
            runTimer: false, immersive: entering =>
            {
                if (!entering) return;
                AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
                transitioned = true;
            });
        var pad = new GamepadSample("active", GamepadLayout.OffsetLeftHigh);
        entry.Step([pad], 0); entry.Step([pad], 10000);
        AssertFalse(entry.IsPromptOpen); AssertEqual(0, input.Calls.Count);
        entry.Step([pad with { Activity = true }], 10010);
        AssertTrue(entry.IsPromptOpen);
        AssertTrue(fixture.Shell.Render(new(1100, 750)).Nodes.Any(node => node.Text == "左右摇杆内推以进入手柄模式"));
        var partial = pad with { LeftX = .3f, RightX = -.3f };
        entry.Step([pad with { LeftX = .3f }], 10020);
        AssertTrue(Math.Abs(input.Calls[^1].Value.Vibration - .12f) < .0001);
        AssertEqual(0f, input.Calls[^1].Value.AxisDamping);
        entry.Step([partial], 10220); var a = input.Calls[^1].Value;
        entry.Step([partial], 10420); var b = input.Calls[^1].Value;
        AssertTrue(b.Vibration > a.Vibration); AssertEqual(a.AxisDamping, b.AxisDamping);
        entry.Step([pad with { LeftX = .64f, RightX = -.64f }], 10440); var near = input.Calls[^1].Value;
        AssertTrue(near.AxisDamping > b.AxisDamping);
        AssertTrue(Math.Abs(near.Vibration - (.12 + .88 * 420 / 800)) < .0001);
        entry.Step([partial], 10460); var back = input.Calls[^1].Value;
        AssertTrue(back.Vibration > near.Vibration && back.AxisDamping < near.AxisDamping);
        var full = pad with { LeftX = 1, RightX = -1 };
        entry.Step([full], 10500);
        AssertTrue(fixture.Shell.Render(new(1100, 750)).Nodes.Any(node => node.Text == "保持 1 秒以进入手柄模式"));
        HoldGamepad(entry, full, 10500, 10820); AssertEqual(1f, input.Calls[^1].Value.Vibration);
        AssertFalse(entry.IsEntered);
        HoldGamepad(entry, full, 10820, 11000);
        var halfway = fixture.Shell.Render(new(1100, 750)).Nodes.Single(node => fixture.Shell.Tree.Name(node.Entity) == "GamepadHoldProgress");
        AssertEqual(.5, fixture.Shell.Renderer.GetProgressTarget(halfway.Entity));
        HoldGamepad(entry, full, 11000, 11499); AssertFalse(entry.IsEntered);
        entry.Step([full], 11500); AssertTrue(entry.IsEntered); AssertTrue(transitioned);
        AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
        AssertTrue(input.Calls.All(call => call.Device == "active"));
        int calls = input.Calls.Count;
        entry.Step([full], 11600); AssertEqual(calls, input.Calls.Count);
    }

    private static void GamepadFeedbackRetiresOnAbortAndHoldMustBeContinuous()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Render(new(1100, 750));
        var input = new FeedbackGamepadInput();
        using var entry = new GamepadEntrySession(fixture.Shell, fixture.Intents, fixture.Store, new(), input,
            runTimer: false, immersive: _ => { });
        var pad = new GamepadSample("pad", GamepadLayout.SymmetricLow);
        var full = pad with { LeftX = .8f, RightX = -.8f };
        entry.Step([pad], 0); entry.Step([full], 10); HoldGamepad(entry, full, 10, 510);
        entry.Step([full with { LeftX = .4f }], 610);
        AssertFalse(fixture.Shell.Render(new(1100, 750)).Nodes.Any(node => node.Text == "保持 1 秒以进入手柄模式"));
        entry.Step([full], 710); HoldGamepad(entry, full, 710, 1600); AssertFalse(entry.IsEntered);
        entry.Step([full], 1650, foreground: false);
        AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
        int writes = input.Calls.Count;
        entry.Step([full], 3000); AssertFalse(entry.IsEntered); AssertEqual(writes, input.Calls.Count);
        entry.Step([pad], 3100); entry.Step([full], 3110);
        AssertTrue(input.Calls[^1].Value.Vibration > 0);
        entry.Step([full with { Ready = false }], 3200);
        AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
        entry.Step([pad], 3210); entry.Step([full], 3220);
        entry.Step([], 3300); AssertFalse(entry.IsPromptOpen);
        AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
        entry.Step([pad], 3400); entry.Step([full], 3410);
        entry.Step([full with { Cancel = true }], 3500);
        AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
        entry.Step([], 3600); entry.Step([pad], 3610); entry.Step([full], 3620);
        entry.Step([full], 5000); AssertFalse(entry.IsEntered);
        AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
        entry.Step([pad], 5010); entry.Step([full], 5020);
        entry.Dispose(); AssertEqual(default(GamepadFeedback), input.Calls[^1].Value);
    }

    private sealed class RecordingGamepadActuator(GamepadFeedbackChannels channels) : IGamepadActuator
    {
        internal readonly List<GamepadFeedback> Values = [];
        internal int Disposals;
        public GamepadFeedbackChannels Channels => channels;
        public bool Set(GamepadFeedback feedback) { Values.Add(feedback); return true; }
        public void Dispose() => Disposals++;
    }
    private static void GamepadActuatorLeasesExpireAndNeverFakeAxisDamping()
    {
        long now = 0;
        var rumble = new RecordingGamepadActuator(GamepadFeedbackChannels.Vibration);
        var damping = new RecordingGamepadActuator(GamepadFeedbackChannels.AxisDamping);
        using var output = new NativeGamepadFeedback(id => id == "rumble" ? rumble : damping, () => now, runTimer: false);
        output.ApplyFeedback("rumble", new(.4f, 1));
        AssertEqual(new GamepadFeedback(.4f, 0), rumble.Values[^1]);
        output.ApplyFeedback("damping", new(.8f, .6f));
        AssertEqual(new GamepadFeedback(0, .6f), damping.Values[^1]);
        now = 200; output.ApplyFeedback("rumble", new(.5f, .7f));
        now = 300; output.Expire();
        AssertEqual(default(GamepadFeedback), damping.Values[^1]); AssertEqual(1, damping.Disposals);
        AssertEqual(.5f, rumble.Values[^1].Vibration); AssertEqual(0, rumble.Disposals);
        output.RetainDevices([]);
        AssertEqual(default(GamepadFeedback), rumble.Values[^1]); AssertEqual(1, rumble.Disposals);
        AssertEqual(default(GamepadFeedback), new GamepadFeedback(float.NaN, float.PositiveInfinity).Bounded);
        AssertEqual(new GamepadFeedback(1, 0), new GamepadFeedback(2, -1).Bounded);
        output.Dispose(); AssertEqual(1, rumble.Disposals); AssertEqual(1, damping.Disposals);
    }

    private static void GamepadNativeFeedbackLayoutsAndCoreHapticsPattern()
    {
        AssertEqual(48, Marshal.SizeOf<LinuxGamepadActuator.Effect>());
        AssertEqual(4, Marshal.SizeOf<WindowsGamepadActuator.Motors>());
        AssertEqual(20, Marshal.SizeOf<WindowsGamepadActuator.Capabilities>());
        AssertEqual(16, Marshal.OffsetOf<WindowsGamepadActuator.Capabilities>("Motors").ToInt32());
        AssertEqual(24, Marshal.SizeOf<LinuxGamepadActuator.InputEvent>());
        AssertEqual(10, Marshal.OffsetOf<LinuxGamepadActuator.Effect>("Duration").ToInt32());
        AssertEqual(16, Marshal.OffsetOf<LinuxGamepadActuator.Effect>("Strong").ToInt32());
        AssertEqual(32, Marshal.OffsetOf<LinuxGamepadActuator.Effect>("YRightCoefficient").ToInt32());
        var rumble = LinuxGamepadActuator.CreateEffect(0x50, -1, .5f);
        AssertEqual((short)-1, rumble.Id); AssertEqual((ushort)32768, rumble.Strong);
        AssertEqual((ushort)NativeGamepadFeedback.LeaseMilliseconds, rumble.Duration);
        var damper = LinuxGamepadActuator.CreateEffect(0x55, 2, 1);
        AssertEqual(short.MaxValue, damper.XRightCoefficient); AssertEqual(short.MaxValue, damper.YLeftCoefficient);
        AssertEqual(ushort.MaxValue, damper.YRightSaturation);
        if (OperatingSystem.IsMacOS())
        {
            AssertTrue(MacGamepadActuator.ValidatePattern());
            AssertTrue(MacGamepadActuator.Open("gc:0") is null);
        }
    }
}
