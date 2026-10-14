using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Desktop WinRT SMTC ABI without reflection, RCWs or Store identity.</summary>
public sealed partial class AvaloniaUiWindowsMediaSession : IAsyncDisposable
{
    private readonly Func<AvaloniaUiMediaState> _capture;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _control, _report;
    private readonly string _title;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<int> _initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _run;
    private static readonly Guid InteropId = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
    private static readonly Guid ControlsId = new("99fa3ff4-1742-42a6-902e-087d41f965ec");
    // Windows SDK windows.media.h: TypedEventHandler<SMTC, SMTCButtonPressedEventArgs>.
    private static readonly Guid HandlerId = new("0557e996-7b23-5bae-aa81-ea0d671143a4");
    private static readonly Guid UnknownId = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid AgileId = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    private static readonly nint HandlerVTable = CreateHandlerVTable();
    private static readonly int[] SupportedControlSlots = [11, 13, 15, 17, 27];
    internal Task<int> InitializationResult => _initialization.Task;

    public AvaloniaUiWindowsMediaSession(nint window, Func<AvaloniaUiMediaState> capture, Func<bool> enabled,
        Action<string> control, Action<string> report, string title = "NexaCL 背景音乐")
    {
        _capture = capture; _enabled = enabled; _control = control; _report = report;
        _title = title;
        _run = OperatingSystem.IsWindows() && window != 0 ? Task.Run(() => Run(window)) : Task.CompletedTask;
        if (!OperatingSystem.IsWindows() || window == 0)
        { _initialization.TrySetResult(1); report("SMTC: PlatformUnsupported or native window unavailable."); }
    }
    private unsafe void Run(nint window)
    {
        nint factory = 0, controls = 0, handler = 0; long registration = 0;
        bool apartment = false, subscribed = false;
        int initializationResult = unchecked((int)0x80004005);
        try
        {
            int initialization = RoInitialize(1);
            if (initialization < 0)
            { initializationResult = initialization; _report($"SMTC: InitializationFailed (WinRT apartment, HRESULT 0x{initialization:X8})."); return; }
            apartment = true;
            nint name = CreateString("Windows.Media.SystemMediaTransportControls");
            try { Check(GetActivationFactory(name, in InteropId, out factory)); }
            finally { Check(DeleteString(name)); }
            Guid controlsId = ControlsId;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(factory, 6))(factory, window, &controlsId, &controls));
            handler = CreateHandler(_control);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(controls, 32))(controls, handler, &registration));
            subscribed = true;
            SetDisplay(controls, _title);
            _initialization.TrySetResult(0);
            while (!_stop.IsCancellationRequested)
            {
                byte enabled = _enabled() ? (byte)1 : (byte)0;
                foreach (int slot in SupportedControlSlots)
                    Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Slot(controls, slot))(controls, enabled));
                int status = _capture().Status switch { AvaloniaUiMediaStatus.Playing => 3, AvaloniaUiMediaStatus.Paused => 4, _ => 2 };
                Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(controls, 7))(controls, status));
                _ = _stop.Token.WaitHandle.WaitOne(200);
            }
        }
        catch (NativeCallException error)
        {
            initializationResult = error.HResult;
            _report($"SMTC: InitializationFailed (native HRESULT 0x{error.HResult:X8}).");
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { initializationResult = error.HResult; _report("SMTC: DependencyMissing (" + error.GetType().Name + ")."); }
        finally
        {
            _initialization.TrySetResult(initializationResult);
            if (controls != 0)
            {
                if (subscribed) _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(controls, 33))(controls, registration);
                _ = ((delegate* unmanaged[Stdcall]<nint, byte, int>)Slot(controls, 11))(controls, 0);
                Release(controls);
            }
            if (handler != 0) Release(handler);
            if (factory != 0) Release(factory);
            if (apartment) RoUninitialize();
        }
    }
    private static unsafe void SetDisplay(nint controls, string caption)
    {
        nint updater = 0, music = 0, title = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(controls, 8))(controls, &updater));
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(updater, 7))(updater, 1));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(updater, 12))(updater, &music));
            title = CreateString(caption);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(music, 7))(music, title));
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(updater, 17))(updater));
        }
        finally { if (title != 0) Check(DeleteString(title)); if (music != 0) Release(music); if (updater != 0) Release(updater); }
    }
    private static nint CreateString(string text) { Check(NewString(text, (uint)text.Length, out nint result)); return result; }
    // Marshal maps some native HRESULTs (including E_NOINTERFACE) to managed cast/
    // argument exceptions. Preserve the actual native failure category and code instead.
    internal static void Check(int result)
    { if (result < 0) throw new NativeCallException(result); }
    internal sealed class NativeCallException : Exception
    {
        public NativeCallException() { }
        public NativeCallException(string message) : base(message) { }
        public NativeCallException(string message, Exception innerException) : base(message, innerException) { }
        public NativeCallException(int result) : base($"SMTC native call failed (HRESULT 0x{result:X8}).") => HResult = result;
    }
    private static unsafe nint Slot(nint instance, int index) => (*(nint**)instance)[index];
    private static unsafe void Release(nint instance) => _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
    [StructLayout(LayoutKind.Sequential)] private struct Handler { internal nint Table; internal int References; internal nint Target; }
    internal static unsafe nint CreateHandler(Action<string> action)
    {
        var target = GCHandle.Alloc(action);
        nint memory = Marshal.AllocHGlobal(sizeof(Handler));
        *(Handler*)memory = new() { Table = HandlerVTable, References = 1, Target = GCHandle.ToIntPtr(target) };
        return memory;
    }
    private static unsafe nint CreateHandlerVTable()
    {
        nint* table = (nint*)Marshal.AllocHGlobal(4 * sizeof(nint));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryHandler;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddHandler;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&ReleaseHandler;
        table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&InvokeHandler;
        return (nint)table;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int QueryHandler(nint self, Guid* iid, nint* result)
    {
        if (iid == null || result == null) return unchecked((int)0x80004003);
        *result = 0;
        if (*iid != UnknownId && *iid != HandlerId && *iid != AgileId) return unchecked((int)0x80004002);
        Interlocked.Increment(ref ((Handler*)self)->References); *result = self; return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe uint AddHandler(nint self) => (uint)Interlocked.Increment(ref ((Handler*)self)->References);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe uint ReleaseHandler(nint self)
    {
        int remaining = Interlocked.Decrement(ref ((Handler*)self)->References);
        if (remaining == 0) { GCHandle.FromIntPtr(((Handler*)self)->Target).Free(); Marshal.FreeHGlobal(self); }
        return (uint)remaining;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int InvokeHandler(nint self, nint sender, nint args)
    {
        try
        {
            int button = 0; Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(args, 6))(args, &button));
            string? command = button switch { 0 => "play", 1 => "pause", 2 => "stop", 6 => "next", _ => null };
            if (command is not null && GCHandle.FromIntPtr(((Handler*)self)->Target).Target is Action<string> action) action(command);
            return 0;
        }
        catch (Exception) { return unchecked((int)0x80004005); }
    }
    public async ValueTask DisposeAsync()
    {
        try { await _stop.CancelAsync().ConfigureAwait(false); await _run.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
    [LibraryImport("combase.dll", EntryPoint = "RoInitialize")] private static partial int RoInitialize(uint apartment);
    [LibraryImport("combase.dll", EntryPoint = "RoUninitialize")] private static partial void RoUninitialize();
    [LibraryImport("combase.dll", EntryPoint = "WindowsCreateString", StringMarshalling = StringMarshalling.Utf16)] private static partial int NewString(string text, uint length, out nint result);
    [LibraryImport("combase.dll", EntryPoint = "WindowsDeleteString")] private static partial int DeleteString(nint text);
    [LibraryImport("combase.dll", EntryPoint = "RoGetActivationFactory")] private static partial int GetActivationFactory(nint name, in Guid iid, out nint factory);
}
