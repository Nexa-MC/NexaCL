using Nexa.Native.Minecraft.Http;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Scheduling;

internal static class WorkLeaseCases
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var scheduler = new WorkScheduler();
        Exception? failure = null;
        await using var host = new HostWorkLeaseServer(scheduler, null, error => Interlocked.CompareExchange(ref failure, error, null));
        async Task<byte[]> Call(int operation, Action<ServiceWireWriter>? write = null)
        {
            using var request = MinecraftServiceProtocol.Request(operation); write?.Invoke(request);
            return await host.ExecuteAsync(request.Finish(), token);
        }
        for (int index = 0; index < 40; index++)
        {
            byte[] bytes = await Call(DownloadProtocol.AcquireQuiet); long handle;
            using (var reader = new ServiceWireReader(bytes)) { handle = reader.Int64(); reader.End(); }
            bytes = await Call(DownloadProtocol.ClaimQuiet, writer => writer.Int64(handle));
            using (var reader = new ServiceWireReader(bytes)) { if (!reader.Boolean()) throw new InvalidDataException("Quiet claim rejected."); reader.End(); }
            if (scheduler.Snapshot.QuietScopes != 1) throw new InvalidDataException("Canonical quiet admission was bypassed.");
            await Call(DownloadProtocol.GraceQuiet, writer => { writer.Int64(handle); writer.TimeSpan(TimeSpan.FromMilliseconds(1)); });
            while (scheduler.Snapshot.QuietScopes != 0) await Task.Delay(1, token);
            await Call(DownloadProtocol.ReleaseQuiet, writer => writer.Int64(handle));
        }
        byte[] pending = await Call(DownloadProtocol.AcquireQuiet); long retained;
        using (var reader = new ServiceWireReader(pending)) { retained = reader.Int64(); reader.End(); }
        await Call(DownloadProtocol.ClaimQuiet, writer => writer.Int64(retained));
        await Call(DownloadProtocol.GraceQuiet, writer => { writer.Int64(retained); writer.TimeSpan(TimeSpan.FromMinutes(2)); });
        await host.DisposeAsync();
        if (scheduler.Snapshot.QuietScopes != 0) throw new InvalidDataException("Host close retained a quiet timer or lease.");
        if (failure is not null) throw new InvalidOperationException("Host work retirement failed.", failure);
        Console.WriteLine("Canonical quiet leases recycled across 40 grace periods and drained on close.");
    }
}
