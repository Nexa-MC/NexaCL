using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    public bool SetTaskbarProgress(bool active, double progress)
    {
        if (!OperatingSystem.IsWindows()) return true;
        if (!Dispatcher.UIThread.CheckAccess()) return false;
        nint window = (_owner as Window)?.TryGetPlatformHandle()?.Handle ?? 0;
        return window != 0 && WindowsTaskbar.Apply(window, active, progress);
    }

    private static unsafe class WindowsTaskbar
    {
        internal static bool Apply(nint window, bool active, double progress)
        {
            int initialized = CoInitializeEx(0, 2);
            if (initialized < 0 && initialized != unchecked((int)0x80010106)) return false;
            nint taskbar = 0;
            try
            {
                Guid clsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");
                Guid iid = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
                if (CoCreateInstance(ref clsid, 0, 1, ref iid, out taskbar) < 0 || taskbar == 0) return false;
                nint* slots = *(nint**)taskbar;
                if (((delegate* unmanaged[Stdcall]<nint, int>)slots[3])(taskbar) < 0) return false;
                uint state = !active ? 0u : double.IsFinite(progress) ? 2u : 1u;
                if (((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)slots[10])(taskbar, window, state) < 0) return false;
                return state != 2 || ((delegate* unmanaged[Stdcall]<nint, nint, ulong, ulong, int>)slots[9])
                    (taskbar, window, (ulong)(Math.Clamp(progress, 0, 1) * 10000), 10000) >= 0;
            }
            finally
            {
                if (taskbar != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)taskbar)[2])(taskbar);
                if (initialized >= 0) CoUninitialize();
            }
        }
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    }
}
