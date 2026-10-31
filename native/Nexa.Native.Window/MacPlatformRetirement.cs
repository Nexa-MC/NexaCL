using System.Runtime.CompilerServices;
using Avalonia;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

/// <summary>Releases Cocoa callback ownership while this shared NativeAOT runtime can still admit calls.</summary>
internal static class MacPlatformRetirement
{
    private static bool _released;

    internal static void Complete(bool required)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (!AvaloniaUiStartupSession.IsOnProcessMainThread)
            throw new InvalidOperationException("Native macOS platform retirement requires the process main thread.");
        if (_released) return;
        // Avalonia.Native 12.1.0 binds this exact factory and normally disposes it from
        // its own ProcessExit handler. That event is not raised by a foreign runtime.
        object? factory = FindFactory();
        if (factory is IDisposable owner) owner.Dispose();
        else if (factory is not null || required)
            throw new InvalidOperationException("The native macOS factory lifetime contract is unavailable.");
        _released = true;
    }

    internal static object? FindFactory()
    {
        Type factoryType = Type.GetType("Avalonia.Native.Interop.IAvaloniaNativeFactory, Avalonia.Native", throwOnError: true)!;
        return GetService(GetLocator(null), factoryType);
    }

    // PrivateApi members are intentionally absent from Avalonia's reference assembly.
    // These exact AOT accessors target the implementation pinned by the package lock.
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_CurrentMutable")]
    private static extern AvaloniaLocator GetLocator(AvaloniaLocator? type);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetService")]
    private static extern object? GetService(AvaloniaLocator locator, Type serviceType);
}
