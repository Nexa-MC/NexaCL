using Nexa.Native.Minecraft.Http;
using Nexa.Services.Scheduling;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private HostWorkLeaseServer? _hostWork;
    public void AttachWork(IWorkScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_hostWork is not null) throw new InvalidOperationException("Canonical Minecraft work is already attached.");
            _hostWork = new(scheduler, null, Report);
        }
    }
    private Task<byte[]> ExecuteWorkAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) =>
        (Volatile.Read(ref _hostWork) ?? throw new InvalidOperationException("Canonical Minecraft work is not attached.")).ExecuteAsync(bytes, token);
}
