using System.Runtime.InteropServices;
using Avalonia.Controls;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Blittable Objective-C calls, compatible with NativeAOT on x64 and arm64.</summary>
internal static partial class AvaloniaMacWindow
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public double X, Y, Width, Height; }

    internal static void SetControlsScrimOpacity(Window window, double opacity)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { } handle) return;
        nint native = handle.HandleDescriptor == "NSWindow" ? handle.Handle
            : handle.HandleDescriptor == "NSView" ? Send(handle.Handle, Selector("window")) : 0;
        if (native == 0) return;
        for (nint kind = 0; kind < 3; kind++)
        {
            nint button = SendIntegerResult(native, Selector("standardWindowButton:"), kind);
            if (button == 0) continue;
            SendDouble(button, Selector("setAlphaValue:"), 1 - opacity);
            SendBool(button, Selector("setEnabled:"), opacity <= .01 ? (byte)1 : (byte)0);
        }
    }

    internal static double ConfigureAndMeasure(Window window)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { } handle) return 0;
        nint native = handle.HandleDescriptor == "NSWindow" ? handle.Handle
            : handle.HandleDescriptor == "NSView" ? Send(handle.Handle, Selector("window")) : 0;
        if (native == 0) return 82; // Conservative safe area until a native handle becomes available.
        SendBool(native, Selector("setTitlebarAppearsTransparent:"), 1);
        SendInteger(native, Selector("setTitleVisibility:"), 1);
        SendBool(native, Selector("setMovableByWindowBackground:"), 0);
        // The extension-height hint sizes Avalonia's material, not Cocoa's button
        // container (which stays 28 DIPs). Keep the real controls in the same
        // 52-DIP caption as the renderer, using NSView geometry only.
        if (window.WindowState != WindowState.FullScreen)
            AlignControls(native, XsrUiShell.TitleBarHeight);
        double right = 0;
        for (nint kind = 0; kind < 3; kind++)
        {
            nint button = SendIntegerResult(native, Selector("standardWindowButton:"), kind);
            if (button == 0) continue;
            NativeRect frame = Frame(button);
            right = Math.Max(right, frame.X + frame.Width);
        }
        return right > 0 ? right + 12 : 82;
    }

    private static NativeRect Frame(nint view)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return Rect(view, Selector("frame"));
        RectStret(out var result, view, Selector("frame"));
        return result;
    }

    private static void AlignControls(nint window, double height)
    {
        nint close = SendIntegerResult(window, Selector("standardWindowButton:"), 0);
        if (close == 0) return;
        nint title = Send(close, Selector("superview"));
        nint container = title == 0 ? 0 : Send(title, Selector("superview"));
        if (container == 0) return;
        NativeRect outer = Frame(container), inner = Frame(title);
        // Do not reshape an unexpected native hierarchy into a caption.
        if (outer.Height <= 0 || outer.Height > 128 || inner.Height <= 0 || inner.Height > 128) return;
        outer.Y += outer.Height - height;
        outer.Height = height;
        SetFrame(container, Selector("setFrame:"), outer);
        inner.Y = 0;
        inner.Height = height;
        SetFrame(title, Selector("setFrame:"), inner);
        for (nint kind = 0; kind < 3; kind++)
        {
            nint button = SendIntegerResult(window, Selector("standardWindowButton:"), kind);
            if (button == 0) continue;
            NativeRect frame = Frame(button);
            frame.Y = (height - frame.Height) / 2;
            SetFrame(button, Selector("setFrame:"), frame);
        }
    }
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint target, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(nint target, nint selector, byte value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendDouble(nint target, nint selector, double value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendInteger(nint target, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendIntegerResult(nint target, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial NativeRect Rect(nint target, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SetFrame(nint target, nint selector, NativeRect value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void RectStret(out NativeRect result, nint target, nint selector);
}
