using System.Runtime.InteropServices;

namespace Nexa.Desktop.Ui;

internal enum GamepadLayout { SymmetricLow, OffsetLeftHigh, SymmetricHigh }
internal sealed record GamepadSample(string Id, GamepadLayout Layout, float LeftX = 0, float LeftY = 0,
    float RightX = 0, float RightY = 0, bool Cancel = false, bool Ready = true, bool Activity = false)
{
    internal bool Valid => Ready && float.IsFinite(LeftX) && float.IsFinite(LeftY) && float.IsFinite(RightX) && float.IsFinite(RightY);
    internal bool Neutral => Valid && Math.Abs(LeftX) < .25 && Math.Abs(RightX) < .25 && Math.Abs(LeftY) < .25 && Math.Abs(RightY) < .25;
    internal bool HasActivity => Valid && (Activity || Math.Abs(LeftX) > .25 || Math.Abs(RightX) > .25 || Math.Abs(LeftY) > .25 || Math.Abs(RightY) > .25);
}
internal interface IGamepadInput : IDisposable { IReadOnlyList<GamepadSample> Read(); }

/// <summary>Nonexclusive, session-owned native readers; never installs keyboard or mouse hooks.</summary>
internal sealed partial class NativeGamepadInput : IGamepadInput, IGamepadFeedback
{
    private readonly Dictionary<string, LinuxPad> _linux = [];
    private readonly WindowsHidGamepads? _hid = OperatingSystem.IsWindows() ? new() : null;
    private long _nextScan;
    private bool _disposed;
    private NativeGamepadFeedback? _feedback;
    private readonly HashSet<string> _knownDevices = [];

    public IReadOnlyList<GamepadSample> Read()
    {
        var samples = ReadCore();
        _knownDevices.Clear(); foreach (var sample in samples) _knownDevices.Add(sample.Id);
        _feedback?.RetainDevices(samples);
        return samples;
    }
    public void ApplyFeedback(string device, GamepadFeedback feedback)
    {
        if (_disposed) return;
        if (feedback == default) { _feedback?.ApplyFeedback(device, default); return; }
        if (_knownDevices.Contains(device)) (_feedback ??= new()).ApplyFeedback(device, feedback);
    }
    private List<GamepadSample> ReadCore()
    {
        if (_disposed) return [];
        try
        {
            if (OperatingSystem.IsWindows())
            {
                List<GamepadSample> result = [.. _hid!.Read()];
                for (uint index = 0; index < 4; index++)
                    if (XInputGetState(index, out var state) == 0)
                        result.Add(new("xinput:" + index, GamepadLayout.OffsetLeftHigh,
                            state.LX / 32768f, -state.LY / 32768f, state.RX / 32768f, -state.RY / 32768f,
                            (state.Buttons & 0x2020) != 0, Activity: state.Buttons != 0 || state.LT > 30 || state.RT > 30));
                return result;
            }
            if (OperatingSystem.IsLinux()) return ReadLinux();
            if (OperatingSystem.IsMacOS()) return ReadMac();
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException) { }
        return [];
    }

    internal static GamepadLayout Layout(string name, int vendor = 0, int product = 0)
    {
        if (vendor == 0x054c || name.Contains("PlayStation", StringComparison.OrdinalIgnoreCase)
            || name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) || name.Contains("DualShock", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sony", StringComparison.OrdinalIgnoreCase)) return GamepadLayout.SymmetricLow;
        if (vendor == 0x057e && product == 0x0330 || name.Contains("Wii U", StringComparison.OrdinalIgnoreCase))
            return GamepadLayout.SymmetricHigh;
        return GamepadLayout.OffsetLeftHigh;
    }

    internal static float NormalizeHidAxis(uint raw, int minimum, int maximum, int bits)
    {
        if (maximum <= minimum || bits is < 1 or > 32) return 0;
        double value = minimum < 0 ? bits < 32 ? (int)(raw << (32 - bits)) >> (32 - bits) : unchecked((int)raw) : raw;
        return (float)Math.Clamp(2 * (value - minimum) / ((double)maximum - minimum) - 1, -1, 1);
    }

    private List<GamepadSample> ReadLinux()
    {
        if (Environment.TickCount64 >= _nextScan)
        {
            _nextScan = Environment.TickCount64 + 1000;
            string[] paths = Directory.Exists("/dev/input") ? Directory.GetFiles("/dev/input", "js*").Take(16).ToArray() : [];
            foreach (string removed in _linux.Keys.Except(paths).ToArray()) { _linux[removed].Dispose(); _linux.Remove(removed); }
            foreach (string path in paths.Where(path => !_linux.ContainsKey(path)))
                if (LinuxPad.Open(path) is { } pad) _linux.Add(path, pad);
        }
        List<GamepadSample> samples = [];
        foreach (var (path, pad) in _linux.ToArray())
            if (pad.Read() is { } sample) samples.Add(sample);
            else { pad.Dispose(); _linux.Remove(path); }
        return samples;
    }

    private sealed class LinuxPad(string id, int fd, GamepadLayout layout, byte[] mapping) : IDisposable
    {
        private readonly float[] _axes = new float[64];
        private bool _cancel;
        private readonly HashSet<byte> _buttons = [];
        internal static LinuxPad? Open(string path)
        {
            int fd = OpenDevice(path, 0x800 | 0x80000); // nonblocking + close-on-exec
            if (fd < 0) return null;
            byte[] name = new byte[128], axes = new byte[64];
            if (Ioctl(fd, 0x80806a13, name) < 0 || Ioctl(fd, 0x80406a32, axes) < 0) { _ = CloseDevice(fd); return null; }
            int Identity(string key)
            {
                try { return Convert.ToInt32(File.ReadAllText("/sys/class/input/" + Path.GetFileName(path) + "/device/id/" + key).Trim(), 16); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException) { return 0; }
            }
            return new(path, fd, Layout(System.Text.Encoding.UTF8.GetString(name).TrimEnd('\0'), Identity("vendor"), Identity("product")), axes);
        }
        internal GamepadSample? Read()
        {
            byte[] data = new byte[8];
            for (int i = 0; i < 256; i++)
            {
                nint count = ReadDevice(fd, data, 8);
                if (count < 0) { if (Marshal.GetLastPInvokeError() is 11 or 4) break; return null; }
                if (count != 8) return null;
                short value = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(4));
                int type = data[6] & 0x7f, number = data[7];
                if (type == 2 && number < mapping.Length) _axes[mapping[number]] = value / 32768f;
                if (type == 1 && number == (layout == GamepadLayout.SymmetricLow ? 2 : 1)) _cancel = value != 0;
                if (type == 1) { if (value != 0) _buttons.Add((byte)number); else _buttons.Remove((byte)number); }
            }
            bool rx = mapping.Contains((byte)3) && mapping.Contains((byte)4);
            return new(id, layout, _axes[0], _axes[1], _axes[rx ? 3 : 2], _axes[rx ? 4 : 5], _cancel, Activity: _buttons.Count != 0);
        }
        public void Dispose() => _ = CloseDevice(fd);
    }

    private static bool _macLoaded;
    private static readonly string[] MacActivityButtons = ["buttonA", "buttonB", "buttonX", "buttonY", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger", "buttonMenu", "buttonOptions", "leftThumbstickButton", "rightThumbstickButton"];
    private static nint Selector(string name) => RegisterSelector(name);
    private static nint Property(nint value, string name) => value == 0 ? 0 : Send(value, Selector(name));
    private static float Axis(nint stick, string axis) => stick == 0 ? 0 : SendFloat(Property(stick, axis), Selector("value"));
    private static List<GamepadSample> ReadMac()
    {
        if (!_macLoaded) { NativeLibrary.Load("/System/Library/Frameworks/GameController.framework/GameController"); _macLoaded = true; }
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        try
        {
            nint controllers = Send(GetClass("GCController"), Selector("controllers"));
            int count = Math.Clamp((int)Send(controllers, Selector("count")), 0, 16);
            List<GamepadSample> result = [];
            for (int i = 0; i < count; i++)
            {
                nint pad = SendIndex(controllers, Selector("objectAtIndex:"), i), profile = Property(pad, "extendedGamepad");
                if (profile == 0) continue;
                string name = Marshal.PtrToStringUTF8(Property(Property(pad, "vendorName"), "UTF8String")) ?? "";
                nint left = Property(profile, "leftThumbstick"), right = Property(profile, "rightThumbstick");
                bool cancel = SendFloat(Property(profile, "buttonB"), Selector("value")) > .5f;
                bool activity = MacActivityButtons.Any(button => SendResponds(profile, Selector("respondsToSelector:"), Selector(button)) != 0
                    && SendFloat(Property(profile, button), Selector("value")) > .25f);
                nint dpad = Property(profile, "dpad");
                activity |= Math.Abs(Axis(dpad, "xAxis")) > .25f || Math.Abs(Axis(dpad, "yAxis")) > .25f;
                result.Add(new("gc:" + pad, Layout(name), Axis(left, "xAxis"), -Axis(left, "yAxis"), Axis(right, "xAxis"), -Axis(right, "yAxis"), cancel, Activity: activity));
            }
            return result;
        }
        finally { Send(pool, Selector("drain")); }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _feedback?.Dispose(); _knownDevices.Clear();
        _hid?.Dispose(); foreach (var pad in _linux.Values) pad.Dispose(); _linux.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out XInputState state);
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)] private static partial int OpenDevice(string path, int flags);
    [DllImport("libc", EntryPoint = "close")] private static extern int CloseDevice(int fd);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)] private static extern nint ReadDevice(int fd, [Out] byte[] data, nuint size);
    [DllImport("libc", EntryPoint = "ioctl")] private static extern int Ioctl(int fd, nuint request, [Out] byte[] data);
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint RegisterSelector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint value, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendIndex(nint value, nint selector, nint index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern float SendFloat(nint value, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendResponds(nint value, nint selector, nint argument);
}
