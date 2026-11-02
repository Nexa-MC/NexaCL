using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Ui;

namespace Nexa.Native.Host.Services;

// A local copy is linked into each client runtime. Only the poll address and an
// opaque local key cross into Host; the callback closure stays in this dictionary.
internal sealed class NativeHostServiceAdmission
{
    private sealed class Retirement(Func<Task> close)
    {
        internal readonly Func<Task> Close = close;
        internal Task? Task;
    }
    private static readonly ConcurrentDictionary<long, Retirement> Retirements = new();
    private static readonly object Admission = new();
    private static long _next;
    private readonly long _lease, _client;
    internal NativeUiCallTable Owner { get; }
    internal static bool Required => AppContext.TryGetSwitch("Nexa.Native.Host.AdmissionRequired", out bool enabled) && enabled;

    private NativeHostServiceAdmission(long lease, long client, NativeUiCallTable owner)
    { _lease = lease; _client = client; Owner = owner; }

    internal static unsafe NativeHostServiceAdmission Open(int module, string identity, NativeUiCallTable peer, Func<Task> retire)
    {
        long client;
        lock (Admission)
        {
            if (Retirements.Count >= 128) throw new IOException("Local Host retirement budget exceeded.");
            client = checked(++_next);
            if (!Retirements.TryAdd(client, new(retire))) throw new IOException("Unable to register Host retirement.");
        }
        try
        {
            byte[] directory = Encoding.UTF8.GetBytes(AppContext.BaseDirectory);
            fixed (byte* path = directory)
                if (((delegate* unmanaged[Cdecl]<byte*, int, int>)Api.Configure)(path, directory.Length) != 0)
                    throw new InvalidOperationException("The native Host installation directory is incompatible or retiring.");
            byte[] expected = Encoding.UTF8.GetBytes(identity);
            NativeUiCallTable owner = default; long lease = 0; int status;
            fixed (byte* bytes = expected)
                status = ((delegate* unmanaged[Cdecl]<int, byte*, int, NativeUiCallTable*, nint, long, NativeUiCallTable*, long*, int>)Api.Open)
                    (module, bytes, expected.Length, &peer, (nint)(delegate* unmanaged[Cdecl]<long, int>)&Poll, client, &owner, &lease);
            if (status == -2) throw new InvalidDataException("Incompatible native Host service schema.");
            if (status != 0 || lease <= 0) throw new IOException("Native Host service admission failed.");
            _ = new NativeUiCallClient(owner);
            return new(lease, client, owner);
        }
        catch { Retirements.TryRemove(client, out _); throw; }
    }
    internal unsafe int CloseOwner() => ((delegate* unmanaged[Cdecl]<long, int>)Api.Close)(_lease);
    internal unsafe void Complete()
    {
        if (((delegate* unmanaged[Cdecl]<long, int>)Api.Complete)(_lease) != 0)
            throw new IOException("Native Host refused premature client retirement.");
        Retirements.TryRemove(_client, out _);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Poll(long client)
    {
        try
        {
            if (!Retirements.TryGetValue(client, out var source)) return 1;
            Task task;
            lock (source) task = source.Task ??= Task.Run(source.Close);
            if (!task.IsCompleted) return 0;
            if (task.IsFaulted) { _ = task.Exception; return -1; }
            return task.IsCanceled ? -1 : 1;
        }
        catch { return -1; }
    }

    private static class Api
    {
        internal static readonly nint Configure, Open, Close, Complete;
        static unsafe Api()
        {
            string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            nint image = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-host" + suffix));
            var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, "nexa_host_services_identity_v1");
            byte[] expected = Encoding.UTF8.GetBytes(HostServiceCatalog.Identity), actual = new byte[expected.Length];
            if (identity(null, 0) != actual.Length) throw new InvalidDataException("Incompatible native Host service catalog.");
            fixed (byte* bytes = actual)
                if (identity(bytes, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("Incompatible native Host service catalog.");
            Configure = NativeLibrary.GetExport(image, "nexa_host_services_configure_v1");
            Open = NativeLibrary.GetExport(image, "nexa_host_service_open_v1");
            Close = NativeLibrary.GetExport(image, "nexa_host_service_close_v1");
            Complete = NativeLibrary.GetExport(image, "nexa_host_service_complete_v1");
        }
    }
}
