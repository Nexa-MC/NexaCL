using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nexa.Platform;

public static partial class PlatformProcessWindow
{
    public static bool Interact(Process process, bool reveal)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native game window interaction is unavailable on this platform.");
        process.Refresh(); if (process.HasExited) return false;
        nint window = process.MainWindowHandle;
        if (window == 0 || GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)process.Id) return false;
        if (!reveal) return process.CloseMainWindow();
        _ = ShowWindowAsync(window, 9); return SetForegroundWindow(window);
    }
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindowAsync(nint window, int command);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint window);
}
