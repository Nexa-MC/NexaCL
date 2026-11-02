using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal static partial class AvaloniaWindowsFrame
{
    private sealed class RestoreState { public Placement? Pending; public long Generation; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, RestoreState> RestoreStates = new();

    internal static void OnStateTransition(Window window, WindowState previous, WindowState next)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;
        RestoreState state = RestoreStates.GetOrCreateValue(window);
        long generation = ++state.Generation;
        if (previous == WindowState.Maximized && next == WindowState.Minimized)
        {
            var placement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
            state.Pending = ReadPlacement(handle.Handle, ref placement) ? placement : null;
        }
        else if (previous == WindowState.Minimized && next == WindowState.Maximized && state.Pending is { } saved)
        {
            double scaling = window.RenderScaling;
            var workArea = window.Screens.ScreenFromWindow(window)?.WorkingArea;
            // Native frame recalculation during restore may rewrite the normal rectangle.
            // Repair only the saved normal placement after the platform transition, keeping
            // the current maximized state, flags and monitor/work-area coordinate system.
            Dispatcher.UIThread.Post(() =>
            {
                if (state.Pending is null || state.Generation != generation || window.WindowState != WindowState.Maximized
                    || window.TryGetPlatformHandle()?.Handle != handle.Handle) return;
                state.Pending = null;
                if (window.RenderScaling != scaling || window.Screens.ScreenFromWindow(window)?.WorkingArea != workArea) return;
                var current = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
                if (!ReadPlacement(handle.Handle, ref current) || current.Show != 3) return;
                if ((current.Normal.Left, current.Normal.Top, current.Normal.Right, current.Normal.Bottom)
                    == (saved.Normal.Left, saved.Normal.Top, saved.Normal.Right, saved.Normal.Bottom)) return;
                current.Normal = saved.Normal;
                _ = WritePlacement(handle.Handle, in current);
            }, DispatcherPriority.Render);
        }
        else if (next != WindowState.Minimized) state.Pending = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public uint Length, Flags, Show;
        public int MinX, MinY, MaxX, MaxY;
        public NativeRect Normal;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadPlacement(nint window, ref Placement placement);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WritePlacement(nint window, in Placement placement);
}
