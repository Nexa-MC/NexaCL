using System.Runtime.InteropServices;

namespace Nexa.Desktop.Ui;

/// <summary>GameController-owned CoreHaptics player; never writes proprietary HID reports.</summary>
internal sealed partial class MacGamepadActuator : IGamepadActuator
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private nint _framework, _gcFramework, _controller, _engine, _player;
    private long _started;
    private bool _disposed;
    public GamepadFeedbackChannels Channels => GamepadFeedbackChannels.Vibration;
    internal static IGamepadActuator? Open(string device)
    {
        if (!device.StartsWith("gc:", StringComparison.Ordinal)) return null;
        var result = new MacGamepadActuator();
        bool opened = false;
        try
        {
            result._gcFramework = NativeLibrary.Load("/System/Library/Frameworks/GameController.framework/GameController");
            result._framework = NativeLibrary.Load("/System/Library/Frameworks/CoreHaptics.framework/CoreHaptics");
            nint pool = Pool();
            try
            {
                nint controllers = Send(Class("GCController"), Sel("controllers"));
                int count = Math.Clamp((int)Send(controllers, Sel("count")), 0, 16);
                for (int index = 0; index < count; index++)
                {
                    nint pad = SendOne(controllers, Sel("objectAtIndex:"), index);
                    if ("gc:" + pad != device || !Responds(pad, "haptics")) continue;
                    nint haptics = Send(pad, Sel("haptics"));
                    nint locality = Constant(result._gcFramework, "GCHapticsLocalityDefault");
                    if (haptics == 0 || locality == 0 || !Responds(haptics, "createEngineWithLocality:")) return null;
                    nint engine = SendOne(haptics, Sel("createEngineWithLocality:"), locality);
                    if (engine == 0) return null;
                    result._engine = Send(engine, Sel("retain"));
                    result._controller = Send(pad, Sel("retain"));
                    if (StartEngine(engine, Sel("startAndReturnError:"), 0) == 0) return null;
                    opened = true;
                    return result;
                }
                return null;
            }
            finally { Send(pool, Sel("drain")); }
        }
        finally { if (!opened) result.Dispose(); }
    }

    public bool Set(GamepadFeedback feedback)
    {
        if (_disposed) return false;
        float strength = feedback.Bounded.Vibration;
        if (strength == 0) { StopPlayer(); return true; }
        nint pool = Pool(), parameter = 0;
        try
        {
            if (_player == 0 || Environment.TickCount64 - _started >= 29000)
            {
                StopPlayer();
                nint pattern = Pattern(_framework, strength);
                if (pattern == 0) return false;
                try
                {
                    nint player = SendTwo(_engine, Sel("createPlayerWithPattern:error:"), pattern, 0);
                    if (player == 0) return false;
                    _player = Send(player, Sel("retain"));
                    if (PlayerTime(_player, Sel("startAtTime:error:"), 0, 0) == 0) { StopPlayer(); return false; }
                    _started = Environment.TickCount64;
                }
                finally { Release(pattern); }
            }
            parameter = Dynamic(_framework, strength);
            return parameter != 0 && SendParameters(_player, Sel("sendParameters:atTime:error:"), Array(parameter), 0, 0) != 0;
        }
        finally { Release(parameter); Send(pool, Sel("drain")); }
    }

    private static nint Dynamic(nint framework, float value)
    {
        nint id = Constant(framework, "CHHapticDynamicParameterIDHapticIntensityControl");
        return id == 0 ? 0 : ParameterTime(Send(Class("CHHapticDynamicParameter"), Sel("alloc")),
            Sel("initWithParameterID:value:relativeTime:"), id, value, 0);
    }
    private static nint Pattern(nint framework, float strength)
    {
        nint type = Constant(framework, "CHHapticEventTypeHapticContinuous");
        nint intensity = Constant(framework, "CHHapticEventParameterIDHapticIntensity");
        if (type == 0 || intensity == 0 || Class("CHHapticPattern") == 0) return 0;
        nint parameter = 0, control = 0, haptic = 0;
        try
        {
            parameter = Parameter(Send(Class("CHHapticEventParameter"), Sel("alloc")), Sel("initWithParameterID:value:"), intensity, 1);
            control = Dynamic(framework, strength);
            if (parameter == 0 || control == 0) return 0;
            haptic = Event(Send(Class("CHHapticEvent"), Sel("alloc")), Sel("initWithEventType:parameters:relativeTime:duration:"), type, Array(parameter), 0, 30);
            return haptic == 0 ? 0 : SendThree(Send(Class("CHHapticPattern"), Sel("alloc")), Sel("initWithEvents:parameters:error:"), Array(haptic), Array(control), 0);
        }
        finally { Release(haptic); Release(control); Release(parameter); }
    }
    internal static bool ValidatePattern()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        nint foundation = NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        nint framework = 0, pool = 0, pattern = 0;
        try
        {
            framework = NativeLibrary.Load("/System/Library/Frameworks/CoreHaptics.framework/CoreHaptics");
            pool = Pool(); pattern = Pattern(framework, .12f);
            return pattern != 0;
        }
        finally
        {
            Release(pattern); if (pool != 0) Send(pool, Sel("drain"));
            if (framework != 0) NativeLibrary.Free(framework); NativeLibrary.Free(foundation);
        }
    }
    private void StopPlayer()
    {
        if (_player == 0) return;
        PlayerTime(_player, Sel("stopAtTime:error:"), 0, 0); Release(_player); _player = 0;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        StopPlayer();
        if (_engine != 0) { SendOne(_engine, Sel("stopWithCompletionHandler:"), 0); Release(_engine); _engine = 0; }
        Release(_controller); _controller = 0;
        if (_framework != 0) NativeLibrary.Free(_framework);
        if (_gcFramework != 0) NativeLibrary.Free(_gcFramework);
    }
    private static nint Constant(nint framework, string name) => NativeLibrary.TryGetExport(framework, name, out nint address) ? Marshal.ReadIntPtr(address) : 0;
    private static nint Pool() => Send(Class("NSAutoreleasePool"), Sel("new"));
    private static nint Array(nint value) => SendOne(Class("NSArray"), Sel("arrayWithObject:"), value);
    private static void Release(nint value) { if (value != 0) Send(value, Sel("release")); }
    private static bool Responds(nint value, string selector) => value != 0 && RespondsTo(value, Sel("respondsToSelector:"), Sel(selector)) != 0;
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Class(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Sel(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint target, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendOne(nint target, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendTwo(nint target, nint selector, nint first, nint second);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendThree(nint target, nint selector, nint first, nint second, nint third);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte RespondsTo(nint target, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte StartEngine(nint target, nint selector, nint error);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Parameter(nint target, nint selector, nint id, float value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint ParameterTime(nint target, nint selector, nint id, float value, double time);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Event(nint target, nint selector, nint type, nint parameters, double time, double duration);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte PlayerTime(nint target, nint selector, double time, nint error);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendParameters(nint target, nint selector, nint parameters, double time, nint error);
}
