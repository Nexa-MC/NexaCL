using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Host.Services;
using Nexa.Native.Ui;

namespace Nexa.Native.Host;

internal static class HostServiceExports
{
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_services_identity_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Identity(byte* output, int capacity)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(HostServiceCatalog.Identity);
        if (capacity < 0 || output == null && capacity != 0) return -1;
        if (output != null && capacity >= bytes.Length) bytes.CopyTo(new Span<byte>(output, bytes.Length));
        return bytes.Length;
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_services_configure_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Configure(byte* directory, int length)
    {
        try
        {
            if (directory == null || length is < 1 or > 32768) return -1;
            HostServices.Configure(new UTF8Encoding(false, true).GetString(new ReadOnlySpan<byte>(directory, length))); return 0;
        }
        catch { return -1; }
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_service_open_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Open(int module, byte* identity, int length, NativeUiCallTable* peer, nint retirement, long client, NativeUiCallTable* owner, long* lease)
    {
        if (identity == null || length is < 1 or > 512 || peer == null || owner == null || lease == null) return -1;
        *owner = default; *lease = 0;
        try { *owner = HostServices.Open(module, new ReadOnlySpan<byte>(identity, length), *peer, retirement, client, out long handle); *lease = handle; return 0; }
        catch (InvalidDataException) { return -2; }
        catch (Exception error) { Console.Error.WriteLine("Native Host admission failed: " + error.Message); return -1; }
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_service_close_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Close(long lease) { try { return HostServices.CloseOwner(lease); } catch { return -1; } }
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_service_complete_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Complete(long lease) { try { HostServices.Complete(lease); return 0; } catch { return -1; } }
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_services_count_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Count() => HostServices.Count;
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_services_shutdown_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Shutdown()
    {
        try
        {
            Task task = HostServices.ShutdownAsync();
            if (!task.IsCompleted) return 0;
            if (task.IsFaulted) { _ = task.Exception; return -1; }
            return 1;
        }
        catch { return -1; }
    }
}
