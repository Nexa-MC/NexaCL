using System.Runtime.InteropServices;

namespace Nexa.Desktop.Ui;

internal sealed partial class LinuxGamepadActuator(int descriptor, GamepadFeedbackChannels channels) : IGamepadActuator
{
    private short _rumble = -1, _damper = -1;
    private bool _disposed;
    public GamepadFeedbackChannels Channels => channels;
    internal static IGamepadActuator? Open(string device)
    {
        if (!device.StartsWith("/dev/input/js", StringComparison.Ordinal)
            || device.Length <= 13 || !device[13..].All(char.IsAsciiDigit)) return null;
        string sysfs = "/sys/class/input/" + Path.GetFileName(device) + "/device";
        if (!Directory.Exists(sysfs)) return null;
        foreach (string child in Directory.EnumerateDirectories(sysfs, "event*").Take(8))
        {
            string name = Path.GetFileName(child);
            if (name.Length <= 5 || !name[5..].All(char.IsAsciiDigit)) continue;
            int fd = OpenDevice("/dev/input/" + name, 2 | 0x800 | 0x80000);
            if (fd < 0) continue;
            byte[] bits = new byte[16];
            if (GetBits(fd, 0x80104535, bits) >= 0)
            {
                GamepadFeedbackChannels support = (Has(0x50) ? GamepadFeedbackChannels.Vibration : 0)
                    | (Has(0x55) ? GamepadFeedbackChannels.AxisDamping : 0);
                if (support != 0) return new LinuxGamepadActuator(fd, support);
            }
            _ = CloseDevice(fd);
            bool Has(int bit) => (bits[bit / 8] & (1 << (bit % 8))) != 0;
        }
        return null;
    }
    public bool Set(GamepadFeedback feedback)
    {
        if (_disposed) return false;
        feedback = feedback.Bounded;
        return SetEffect(ref _rumble, 0x50, channels.HasFlag(GamepadFeedbackChannels.Vibration) ? feedback.Vibration : 0)
            & SetEffect(ref _damper, 0x55, channels.HasFlag(GamepadFeedbackChannels.AxisDamping) ? feedback.AxisDamping : 0);
    }
    private bool SetEffect(ref short id, ushort type, float strength)
    {
        if (strength <= 0) return id < 0 || Play(id, false);
        Effect effect = CreateEffect(type, id, strength);
        if (Upload(descriptor, 0x40304580, ref effect) < 0 || effect.Id < 0) return false;
        id = effect.Id;
        return Play(id, true);
    }
    internal static Effect CreateEffect(ushort type, short id, float strength)
    {
        Effect effect = new() { Type = type, Id = id, Duration = NativeGamepadFeedback.LeaseMilliseconds };
        strength = new GamepadFeedback(strength, 0).Bounded.Vibration;
        if (type == 0x50) effect.Strong = effect.Weak = (ushort)Math.Round(strength * ushort.MaxValue);
        else if (type == 0x55)
        {
            effect.XRightSaturation = effect.XLeftSaturation = effect.YRightSaturation = effect.YLeftSaturation = ushort.MaxValue;
            effect.XRightCoefficient = effect.XLeftCoefficient = effect.YRightCoefficient = effect.YLeftCoefficient = (short)Math.Round(strength * short.MaxValue);
        }
        return effect;
    }
    private bool Play(short id, bool enabled)
    {
        InputEvent message = new() { Type = 0x15, Code = (ushort)id, Value = enabled ? 1 : 0 };
        return WriteEvent(descriptor, in message, 24) == 24;
    }
    public void Dispose()
    {
        if (_disposed) return;
        foreach (short id in new[] { _rumble, _damper })
            if (id >= 0) { Play(id, false); _ = Remove(descriptor, 0x40044581, id); }
        _disposed = true; _ = CloseDevice(descriptor);
    }
    // linux/input.h: 64-bit timeval, ff_effect's pointer-sized periodic union.
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent { public long Seconds, Microseconds; public ushort Type, Code; public int Value; }
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct Effect
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(2)] public short Id;
        [FieldOffset(10)] public ushort Duration;
        [FieldOffset(16)] public ushort Strong;
        [FieldOffset(18)] public ushort Weak;
        [FieldOffset(16)] public ushort XRightSaturation;
        [FieldOffset(18)] public ushort XLeftSaturation;
        [FieldOffset(20)] public short XRightCoefficient;
        [FieldOffset(22)] public short XLeftCoefficient;
        [FieldOffset(28)] public ushort YRightSaturation;
        [FieldOffset(30)] public ushort YLeftSaturation;
        [FieldOffset(32)] public short YRightCoefficient;
        [FieldOffset(34)] public short YLeftCoefficient;
    }
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)] private static partial int OpenDevice(string path, int flags);
    [DllImport("libc", EntryPoint = "close")] private static extern int CloseDevice(int descriptor);
    [DllImport("libc", EntryPoint = "ioctl")] private static extern int GetBits(int descriptor, nuint request, [Out] byte[] bits);
    [DllImport("libc", EntryPoint = "ioctl")] private static extern int Upload(int descriptor, nuint request, ref Effect effect);
    [DllImport("libc", EntryPoint = "ioctl")] private static extern int Remove(int descriptor, nuint request, int id);
    [DllImport("libc", EntryPoint = "write")] private static extern nint WriteEvent(int descriptor, in InputEvent value, nuint count);
}
