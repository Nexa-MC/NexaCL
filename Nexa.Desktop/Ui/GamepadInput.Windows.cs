using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Desktop.Ui;

/// <summary>Read-only Raw Input HID reports for non-XInput pads (including Sony/Nintendo).</summary>
internal sealed unsafe class WindowsHidGamepads : IDisposable
{
    private static readonly Dictionary<nint, WindowsHidGamepads> Owners = [];
    private readonly Dictionary<nint, Pad> _pads = [];
    private readonly string _className = "NexaCL.Gamepads." + Guid.NewGuid().ToString("N");
    private readonly nint _window, _instance;
    private long _nextScan;
    private bool _disposed;

    internal WindowsHidGamepads()
    {
        _instance = GetModuleHandle(null);
        nint name = Marshal.StringToHGlobalUni(_className);
        try
        {
            WindowClass wc = new()
            {
                Size = (uint)sizeof(WindowClass),
                Instance = _instance,
                Name = name,
                Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WndProc
            };
            if (RegisterClass(ref wc) == 0) return;
            _window = CreateWindow(0, _className, "", 0, 0, 0, 0, 0, new nint(-3), 0, _instance, 0);
            if (_window == 0) return;
            Owners.Add(_window, this);
            DeviceRegistration[] registrations = [new(1, 4, 0x2100, _window), new(1, 5, 0x2100, _window)];
            RegisterRawInputDevices(registrations, 2, (uint)sizeof(DeviceRegistration));
        }
        finally { Marshal.FreeHGlobal(name); }
    }

    internal IReadOnlyList<GamepadSample> Read()
    {
        if (_disposed || _window == 0) return [];
        if (Environment.TickCount64 >= _nextScan)
        {
            _nextScan = Environment.TickCount64 + 1000;
            uint count = 0;
            if (GetRawInputDeviceList(null, ref count, (uint)sizeof(Device)) != uint.MaxValue && count <= 1024)
            {
                Device[] devices = new Device[count];
                fixed (Device* list = devices)
                    if (GetRawInputDeviceList(list, ref count, (uint)sizeof(Device)) != uint.MaxValue)
                    {
                        var handles = devices.Take((int)Math.Min(count, devices.Length)).Where(device => device.Type == 2).Select(device => device.Handle).ToHashSet();
                        foreach (nint removed in _pads.Keys.Where(key => !handles.Contains(key)).ToArray()) _pads.Remove(removed);
                        foreach (nint handle in handles.Where(handle => !_pads.ContainsKey(handle)))
                            if (ReadPad(handle) is { } pad) _pads.Add(handle, pad);
                    }
            }
        }
        return _pads.Values.Select(pad => pad.Sample).ToArray();
    }

    private static Pad? ReadPad(nint handle)
    {
        uint size = 32;
        byte[] info = new byte[size]; BinaryPrimitives.WriteUInt32LittleEndian(info, size);
        fixed (byte* buffer = info)
            if (GetRawInputDeviceInfo(handle, 0x2000000b, buffer, ref size) == uint.MaxValue) return null;
        if (BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(20)) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(22)) is not (4 or 5)) return null;
        int vendor = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(8)), product = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(12));
        size = 0; if (GetRawInputDeviceInfo(handle, 0x20000007, null, ref size) == uint.MaxValue) return null;
        if (size > 4096) return null;
        char[] name = new char[size + 1];
        fixed (char* buffer = name)
            if (GetRawInputDeviceInfo(handle, 0x20000007, buffer, ref size) == uint.MaxValue) return null;
        // XInput exposes its own richer state; do not create a second prompt for the same pad.
        if (new string(name).Contains("IG_", StringComparison.OrdinalIgnoreCase)) return null;
        size = 0; if (GetRawInputDeviceInfo(handle, 0x20000005, null, ref size) == uint.MaxValue) return null;
        if (size is 0 or > 65536) return null;
        byte[] preparsed = new byte[size];
        fixed (byte* buffer = preparsed)
            if (GetRawInputDeviceInfo(handle, 0x20000005, buffer, ref size) == uint.MaxValue) return null;
        return new(handle, vendor, product, preparsed);
    }

    private sealed class Pad
    {
        private readonly byte[] _preparsed;
        private readonly Dictionary<ushort, (ushort Link, int Minimum, int Maximum, int Bits)> _values = [];
        private readonly int _cancel;
        private readonly bool _sony;
        internal GamepadSample Sample;
        internal Pad(nint handle, int vendor, int product, byte[] preparsed)
        {
            _preparsed = preparsed; _sony = vendor == 0x054c; _cancel = vendor == 0x054c ? 3 : vendor == 0x057e ? 1 : 2;
            Sample = new("hid:" + handle, NativeGamepadInput.Layout("", vendor, product), Ready: false);
            byte[] caps = new byte[72 * 64]; ushort count = 64;
            fixed (byte* data = preparsed)
            fixed (byte* buffer = caps)
                if (HidPGetValueCaps(0, buffer, ref count, data) == 0x110000)
                    for (int i = 0; i < Math.Min(count, (ushort)64); i++)
                    {
                        var cap = caps.AsSpan(i * 72, 72);
                        if (BinaryPrimitives.ReadUInt16LittleEndian(cap) != 1) continue;
                        ushort usage = BinaryPrimitives.ReadUInt16LittleEndian(cap[56..]);
                        ushort last = cap[12] == 0 ? usage : BinaryPrimitives.ReadUInt16LittleEndian(cap[58..]);
                        for (int key = usage; key <= Math.Min((int)last, 0x39); key++)
                            _values[(ushort)key] = (BinaryPrimitives.ReadUInt16LittleEndian(cap[6..]),
                                BinaryPrimitives.ReadInt32LittleEndian(cap[40..]), BinaryPrimitives.ReadInt32LittleEndian(cap[44..]), BinaryPrimitives.ReadUInt16LittleEndian(cap[18..]));
                    }
        }
        internal void Update(byte* report, uint length)
        {
            fixed (byte* data = _preparsed)
            {
                bool valid = true;
                float Axis(ushort usage)
                {
                    if (!_values.TryGetValue(usage, out var cap) || cap.Maximum <= cap.Minimum) { valid = false; return 0; }
                    fixed (byte* pp = _preparsed)
                    {
                        if (HidPGetUsageValue(0, 1, cap.Link, usage, out uint value, pp, report, length) != 0x110000) { valid = false; return 0; }
                        return NativeGamepadInput.NormalizeHidAxis(value, cap.Minimum, cap.Maximum, cap.Bits);
                    }
                }
                ushort[] buttons = new ushort[32]; uint count = 32;
                fixed (ushort* values = buttons)
                    if (HidPGetUsages(0, 9, 0, values, ref count, data, report, length) != 0x110000) count = 0;
                // Sony uses Z/Rz for the right stick and Rx/Ry for the analog triggers.
                bool rx = !_sony && _values.ContainsKey(0x33) && _values.ContainsKey(0x34);
                float lx = Axis(0x30), ly = Axis(0x31), rightX = Axis(rx ? (ushort)0x33 : (ushort)0x32), rightY = Axis(rx ? (ushort)0x34 : (ushort)0x35);
                if (!valid) return;
                Sample = Sample with
                {
                    LeftX = lx,
                    LeftY = ly,
                    RightX = rightX,
                    RightY = rightY,
                    Cancel = buttons.Take((int)Math.Min(count, 32)).Contains((ushort)_cancel),
                    Ready = true
                };
            }
        }
    }

    private void Receive(nint raw)
    {
        uint size = 0, header = (uint)(8 + 2 * IntPtr.Size);
        if (GetRawInputData(raw, 0x10000003, null, ref size, header) == uint.MaxValue) return;
        if (size < header + 8 || size > 65536) return;
        byte[] bytes = new byte[size];
        fixed (byte* buffer = bytes)
        {
            if (GetRawInputData(raw, 0x10000003, buffer, ref size, header) == uint.MaxValue) return;
            nint handle = *(nint*)(buffer + 8);
            if (!_pads.TryGetValue(handle, out var pad)) return;
            uint length = *(uint*)(buffer + header), count = *(uint*)(buffer + header + 4);
            if (length == 0 || (ulong)length * count > size - header - 8) return;
            for (uint index = 0; index < Math.Min(count, 64); index++) pad.Update(buffer + header + 8 + index * length, length);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (Owners.TryGetValue(window, out var owner))
        {
            if (message == 0xff) owner.Receive(lParam);
            if (message == 0xfe) owner._nextScan = 0;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_window != 0)
        {
            DeviceRegistration[] remove = [new(1, 4, 1, 0), new(1, 5, 1, 0)];
            RegisterRawInputDevices(remove, 2, (uint)sizeof(DeviceRegistration));
            Owners.Remove(_window); DestroyWindow(_window);
        }
        UnregisterClass(_className, _instance); _pads.Clear();
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowClass { public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background, Menu, Name, SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct Device { public nint Handle; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct DeviceRegistration(ushort Page, ushort Usage, uint Flags, nint Target);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")] private static extern ushort RegisterClass(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindow(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint w, nint l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices([In] DeviceRegistration[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputDeviceList(Device* devices, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")] private static extern uint GetRawInputDeviceInfo(nint device, uint command, void* data, ref uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint raw, uint command, void* data, ref uint size, uint header);
    [DllImport("hid.dll", EntryPoint = "HidP_GetValueCaps")] private static extern int HidPGetValueCaps(int type, byte* caps, ref ushort length, byte* preparsed);
    [DllImport("hid.dll", EntryPoint = "HidP_GetUsageValue")] private static extern int HidPGetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, byte* preparsed, byte* report, uint length);
    [DllImport("hid.dll", EntryPoint = "HidP_GetUsages")] private static extern int HidPGetUsages(int type, ushort page, ushort link, ushort* usages, ref uint count, byte* preparsed, byte* report, uint length);
}
