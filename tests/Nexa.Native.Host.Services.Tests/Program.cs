using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Host.Services;
using Nexa.Native.Network.Client;
using Nexa.Native.Updates.Client;

namespace Nexa.Native.Host.Services.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        AppContext.SetSwitch("Nexa.Native.Host.AdmissionRequired", true);
        if (args is ["--reject-image"])
        {
            try { _ = new NativeNetworkClient(); }
            catch (Exception error) when (error is IOException or InvalidOperationException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException)
            { Console.WriteLine("PASS Host: missing/incompatible image rejected without direct fallback."); return 0; }
            throw new InvalidOperationException("Invalid native image admitted.");
        }
        if (args.Length != 1) return 2;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = budget.Token;
        using var handler = new HeldHttp(); using var http = new HttpClient(handler);
        await using var network = new NativeNetworkClient(); await network.TraceAsync(new() { DnsOverHttps = false }, token);
        await using var updates = new NativeUpdatesClient(new(AppContext.BaseDirectory,
            File.ReadAllText(Path.Combine(args[0], "key.asc")), File.ReadAllText(Path.Combine(args[0], "fingerprint")).Trim()), http);
        await updates.Initialization.WaitAsync(token);
        if (Native.Count() != 2) throw new InvalidDataException("Host did not own both typed client admissions.");
        Native.RejectSchemas();
        var first = Native.OpenRaw(2); var second = Native.OpenRaw(4);
        if (first.Lease == second.Lease || first.Context != second.Context)
            throw new InvalidDataException("Host lease identity did not separate overlapping module contexts.");
        if (Native.Complete(first.Lease) == 0 || Native.Count() != 4) throw new InvalidDataException("Premature callback retirement accepted.");
        await Native.CloseRawAsync(first.Lease, token); await Native.CloseRawAsync(second.Lease, token);
        if (Native.Count() != 2) throw new InvalidDataException("Raw service leases were not released.");
        string seed = Path.Combine(Path.GetTempPath(), "nexa-host-seed-" + Guid.NewGuid().ToString("N"));
        await updates.ConfigureDiscoveryAsync(new(seed, "alpha", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, false), token);
        var pending = updates.CheckAsync(new("2.0.0.alpha.5", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "alpha"), token);
        await handler.Entered.Task.WaitAsync(token);
        if (Native.Shutdown() != 0) throw new InvalidOperationException("Host shutdown did not queue physical retirement.");
        await handler.Canceled.Task.WaitAsync(token);
        if (Native.Shutdown() != 0 || Native.Count() != 2) throw new InvalidOperationException("Host retired callbacks before held HTTP completion or closed Network too early.");
        handler.Release.TrySetResult();
        while (true)
        {
            int status = Native.Shutdown(); if (status == 1) break;
            if (status != 0) throw new IOException("Host physical retirement failed.");
            await Task.Delay(5, token);
        }
        try { await pending; } catch (OperationCanceledException) { }
        if (Native.Count() != 0) throw new InvalidDataException("Host retained completed service leases.");
        try { _ = new NativeNetworkClient(); throw new InvalidOperationException("Host accepted a service after shutdown."); }
        catch (InvalidOperationException error) when (error.Message.Contains("retiring", StringComparison.Ordinal)) { }
        Console.WriteLine("PASS Host: pinned schema admission, separate opaque leases, owner/client retirement acknowledgement, held HTTP physical drain, dependent Network lifetime and closed admission.");
        return 0;
    }

    private sealed class HeldHttp : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "api.github.com") throw new InvalidOperationException("Unexpected update request.");
            Entered.TrySetResult(); using var registration = token.Register(() => Canceled.TrySetResult());
            await Release.Task; token.ThrowIfCancellationRequested();
            return new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent("[]"u8.ToArray()) };
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Table
        {
            internal int Size, Version;
            internal long Context;
            internal nint Execute, Copy, Release;
        }
        private static readonly nint Image = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,
            "nexa-host" + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so")));
        internal static unsafe int Count() => ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(Image, "nexa_host_services_count_v1"))();
        internal static unsafe int Shutdown() => ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(Image, "nexa_host_services_shutdown_v1"))();
        internal static unsafe int Complete(long lease) => ((delegate* unmanaged[Cdecl]<long, int>)NativeLibrary.GetExport(Image, "nexa_host_service_complete_v1"))(lease);
        private static unsafe int Close(long lease) => ((delegate* unmanaged[Cdecl]<long, int>)NativeLibrary.GetExport(Image, "nexa_host_service_close_v1"))(lease);
        internal static Task CloseRawAsync(long lease, CancellationToken token) => CloseRawCoreAsync(lease, token);
        private static Task CloseRawCoreAsync(long lease, CancellationToken token) => Task.Run(async () =>
        {
            while (true)
            {
                int status = Close(lease); if (status == 1) break;
                if (status != 0) throw new IOException("Raw owner retirement failed.");
                await Task.Delay(5, token);
            }
            if (Complete(lease) != 0) throw new InvalidOperationException("Client acknowledgement rejected.");
        }, token);
        internal static (long Lease, long Context) OpenRaw(int module)
        {
            int status = Open(module, HostServiceCatalog.Get(module).Identity, out long lease, out long context);
            if (status != 0 || lease <= 0 || context <= 0) throw new InvalidOperationException("Raw Host admission failed.");
            return (lease, context);
        }
        internal static void RejectSchemas()
        {
            if (Open(2, "wrong-schema", out _, out _) != -2 || Open(999, "wrong-schema", out _, out _) == 0 || Count() != 2)
                throw new InvalidDataException("Host accepted an unknown schema/module.");
        }
        private static unsafe int Open(int module, string identity, out long lease, out long context)
        {
            Table peer = new()
            {
                Size = sizeof(Table),
                Version = 1,
                Context = 777,
                Execute = (nint)(delegate* unmanaged[Cdecl]<long, byte*, int, int*, long>)&Execute,
                Copy = (nint)(delegate* unmanaged[Cdecl]<long, long, byte*, int, int>)&Copy,
                Release = (nint)(delegate* unmanaged[Cdecl]<long, long, void>)&Release
            };
            Table owner = default; long handle = 0; byte[] bytes = Encoding.UTF8.GetBytes(identity); int status;
            fixed (byte* input = bytes)
                status = ((delegate* unmanaged[Cdecl]<int, byte*, int, Table*, nint, long, Table*, long*, int>)NativeLibrary.GetExport(Image, "nexa_host_service_open_v1"))
                    (module, input, bytes.Length, &peer, (nint)(delegate* unmanaged[Cdecl]<long, int>)&Retire, 777, &owner, &handle);
            lease = handle; context = owner.Context; return status;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe long Execute(long context, byte* bytes, int length, int* size) { *size = 0; return 0; }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe int Copy(long context, long result, byte* bytes, int length) => -1;
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void Release(long context, long result) { }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int Retire(long context) => -1;
    }
}
