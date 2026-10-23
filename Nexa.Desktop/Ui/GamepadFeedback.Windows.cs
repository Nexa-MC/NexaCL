using System.Runtime.InteropServices;

namespace Nexa.Desktop.Ui;

internal sealed class WindowsGamepadActuator(uint index) : IGamepadActuator
{
    public GamepadFeedbackChannels Channels => GamepadFeedbackChannels.Vibration;
    internal static IGamepadActuator? Open(string device)
    {
        if (device.Length != 8 || !device.StartsWith("xinput:", StringComparison.Ordinal) || device[7] is < '0' or > '3') return null;
        uint index = (uint)(device[7] - '0');
        return GetCapabilities(index, 0, out var info) == 0 && ((info.Flags & 1) != 0
            || info.Motors.Left != 0 || info.Motors.Right != 0) ? new WindowsGamepadActuator(index) : null;
    }
    public bool Set(GamepadFeedback feedback)
    {
        ushort strength = (ushort)Math.Round(feedback.Bounded.Vibration * ushort.MaxValue);
        Motors motors = new() { Left = strength, Right = strength };
        return SetState(index, ref motors) == 0;
    }
    public void Dispose() => Set(default);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Motors { public ushort Left, Right; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Capabilities
    {
        public byte Type, SubType; public ushort Flags;
        public ushort Buttons; public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
        public Motors Motors;
    }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")] private static extern uint GetCapabilities(uint index, uint flags, out Capabilities capabilities);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] private static extern uint SetState(uint index, ref Motors vibration);
}
