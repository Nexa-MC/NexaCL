using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

internal sealed unsafe class WindowsScreenshotShare : IDisposable
{
    private readonly Window _window;
    private nint _manager;
    private long _token;
    private bool _subscribed;
    private bool _initialized;
    private static readonly Guid HandlerId = new("ec6f9cc8-46d0-5e0e-b4d2-7d7773ae37a0");
    private static readonly Guid UnknownId = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid AgileId = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    private static readonly nint HandlerVtable = CreateVtable();
    private sealed record Image(string Path, long Bytes, long Modified);
    [StructLayout(LayoutKind.Sequential)]
    private struct Handler { public nint Vtable; public nint Context; public int References; }

    internal WindowsScreenshotShare(Window window, string path, long bytes, long modified)
    {
        _window = window;
        nint interop = 0, handler = 0;
        try
        {
            int hr = RoInitialize(0);
            if (hr < 0 && hr != unchecked((int)0x80010106)) Check(hr);
            _initialized = hr >= 0;
            interop = Factory("Windows.ApplicationModel.DataTransfer.DataTransferManager", new("3a3dcd6c-3eab-43dc-bcde-45671ce800c8"));
            nint hwnd = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("The native share window is unavailable.");
            Guid managerId = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");
            nint manager;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slots(interop)[3])(interop, hwnd, &managerId, &manager));
            _manager = manager;
            handler = CreateHandler(new(path, bytes, modified));
            long eventToken;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slots(_manager)[6])(_manager, handler, &eventToken));
            _token = eventToken; _subscribed = true;
            window.Closed += OnClosed;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slots(interop)[4])(interop, hwnd));
        }
        catch { Release(handler); handler = 0; Release(interop); interop = 0; Dispose(); throw; }
        finally { Release(handler); Release(interop); }
    }

    private void OnClosed(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        _window.Closed -= OnClosed;
        if (_manager != 0)
        {
            if (_subscribed) _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Slots(_manager)[7])(_manager, _token);
            Release(_manager); _manager = 0; _subscribed = false;
        }
        if (_initialized) { _initialized = false; RoUninitialize(); }
    }

    private static void Populate(nint package, Image image)
    {
        AvaloniaUiPlatformActions.VerifyScreenshotIdentity(image.Path, image.Bytes, image.Modified);
        nint properties = 0, stream = 0, factory = 0, reference = 0;
        try
        {
            Guid streamId = new("905a0fe1-bc53-11df-8c49-001e4fc686da");
            Check(CreateRandomAccessStreamOnFile(image.Path, 0x20, ref streamId, out stream)); // STGM_READ | STGM_SHARE_DENY_WRITE
            AvaloniaUiPlatformActions.VerifyScreenshotIdentity(image.Path, image.Bytes, image.Modified);
            factory = Factory("Windows.Storage.Streams.RandomAccessStreamReference", new("857309dc-3fbf-4e7d-986f-ef3b1a07a964"));
            nint value;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slots(factory)[8])(factory, stream, &value));
            reference = value;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slots(package)[21])(package, reference));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slots(package)[7])(package, &value));
            properties = value;
            using var title = new HString("NexaCL · " + Path.GetFileName(image.Path));
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slots(properties)[7])(properties, title.Value));
        }
        finally { Release(properties); Release(reference); Release(factory); Release(stream); }
    }

    internal static void VerifyNativePackage(string path)
    {
        int initialized = RoInitialize(0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Check(initialized);
        nint instance = 0, package = 0;
        try
        {
            using var name = new HString("Windows.ApplicationModel.DataTransfer.DataPackage");
            Check(RoActivateInstance(name.Value, out instance));
            Guid id = new("61ebf5c7-efea-4346-9554-981d7e198ffe");
            nint value;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slots(instance)[0])(instance, &id, &value));
            package = value;
            var info = new FileInfo(path);
            Populate(package, new(path, info.Length, info.LastWriteTimeUtc.Ticks));
        }
        finally { Release(package); Release(instance); if (initialized >= 0) RoUninitialize(); }
    }

    internal static bool VerifyDelegateOwnership()
    {
        nint handler = CreateHandler(new("fixture", 0, 0));
        try
        {
            Guid id = HandlerId; nint queried;
            int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slots(handler)[0])(handler, &id, &queried);
            if (hr != 0 || queried != handler) return false;
            uint remaining = ((delegate* unmanaged[Stdcall]<nint, uint>)Slots(queried)[2])(queried);
            if (remaining != 1) return false;
            id = new("af86e2e0-b12d-4c6a-9c5a-d7aa65101e90"); // IInspectable is not a delegate.
            hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slots(handler)[0])(handler, &id, &queried);
            return hr == unchecked((int)0x80004002) && queried == 0;
        }
        finally { Release(handler); }
    }

    private static nint CreateVtable()
    {
        nint* table = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(WindowsScreenshotShare), 4 * IntPtr.Size);
        table[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&Query;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&ReleaseHandler;
        table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&Invoke;
        return (nint)table;
    }
    private static nint CreateHandler(Image image)
    {
        Handler* handler = (Handler*)NativeMemory.AllocZeroed((nuint)sizeof(Handler));
        try { handler->Context = GCHandle.ToIntPtr(GCHandle.Alloc(image)); handler->Vtable = HandlerVtable; handler->References = 1; return (nint)handler; }
        catch { NativeMemory.Free(handler); throw; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Query(nint self, Guid* id, nint* result)
    {
        if (id == null || result == null) return unchecked((int)0x80004003);
        *result = 0;
        if (*id != HandlerId && *id != UnknownId && *id != AgileId) return unchecked((int)0x80004002);
        Interlocked.Increment(ref ((Handler*)self)->References); *result = self; return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint self) => (uint)Interlocked.Increment(ref ((Handler*)self)->References);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ReleaseHandler(nint self)
    {
        Handler* handler = (Handler*)self;
        int remaining = Interlocked.Decrement(ref handler->References);
        if (remaining == 0) { GCHandle.FromIntPtr(handler->Context).Free(); NativeMemory.Free(handler); }
        return (uint)remaining;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Invoke(nint self, nint sender, nint args)
    {
        nint request = 0, package = 0;
        try
        {
            nint value;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slots(args)[6])(args, &value)); request = value;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slots(request)[6])(request, &value)); package = value;
            Populate(package, (Image)GCHandle.FromIntPtr(((Handler*)self)->Context).Target!);
            return 0;
        }
        catch
        {
            if (request != 0)
            {
                try
                {
                    using var error = new HString("截图已变化或无法读取，请刷新后重试。");
                    _ = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slots(request)[9])(request, error.Value);
                }
                catch { }
            }
            return unchecked((int)0x80004005);
        }
        finally { Release(package); Release(request); }
    }
    private static nint* Slots(nint value) => *(nint**)value;
    private static void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slots(value)[2])(value); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static nint Factory(string name, Guid id)
    {
        using var text = new HString(name);
        Check(RoGetActivationFactory(text.Value, ref id, out nint factory));
        return factory;
    }
    private sealed class HString : IDisposable
    {
        internal nint Value { get; }
        internal HString(string value) { Check(WindowsCreateString(value, (uint)value.Length, out nint text)); Value = text; }
        public void Dispose() => _ = WindowsDeleteString(Value);
    }
    [DllImport("combase.dll")] private static extern int RoInitialize(uint mode);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid id, out nint factory);
    [DllImport("combase.dll")] private static extern int RoActivateInstance(nint name, out nint instance);
    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int WindowsCreateString(string value, uint length, out nint text);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint text);
    [DllImport("shcore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int CreateRandomAccessStreamOnFile(string path, uint mode, ref Guid id, out nint stream);
}
