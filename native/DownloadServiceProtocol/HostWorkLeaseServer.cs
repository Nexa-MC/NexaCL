using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Scheduling;

namespace Nexa.Native.Network.Services;

// The caller runtime owns real scheduler leases. Their handles have a provisional
// stage because cancellation can reject a successful asynchronous acquisition.
internal sealed class HostWorkLeaseServer(IWorkScheduler? scheduler,
    Func<bool>? backgroundEnabled, Action<Exception> failure) : IAsyncDisposable
{
    private sealed class Lease(IDisposable value) : IAsyncDisposable
    {
        private IDisposable? _value = value;
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _value, null)?.Dispose(); return ValueTask.CompletedTask; }
    }
    private readonly NativeUiAsyncResources<Lease> _leases = new(128);

    internal async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = NetworkProtocol.Read(bytes, out int operation);
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case DownloadProtocol.AcquireWork:
                var priority = (WorkPriority)reader.Int32(); var resources = (WorkResource)reader.Int32(); reader.End();
                if ((uint)priority > (uint)WorkPriority.Idle || (uint)resources is 0 or > 7) throw new InvalidDataException("Invalid host work admission.");
                if (scheduler is null) throw new InvalidOperationException("Native downloads require the canonical host scheduler.");
                IDisposable admitted = await scheduler.AcquireAsync(priority, resources, token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (priority >= WorkPriority.Background && backgroundEnabled?.Invoke() == false)
                        throw new InvalidOperationException("Background downloads are disabled by the committed host policy.");
                    var lease = new Lease(admitted); long handle = _leases.Register(lease); admitted = null!;
                    writer.Int64(handle);
                }
                finally { admitted?.Dispose(); }
                break;
            case DownloadProtocol.ClaimWork:
                long claim = reader.Int64(); reader.End(); writer.Boolean(_leases.Claim(claim)); break;
            case DownloadProtocol.ReleaseWork:
                long release = reader.Int64(); reader.End(); await _leases.ReleaseAsync(release).ConfigureAwait(false); break;
            case DownloadProtocol.ObserveDiskWrite:
                int count = reader.Int32(); TimeSpan elapsed = reader.TimeSpan(); reader.End();
                if (count < 0 || elapsed < TimeSpan.Zero) throw new InvalidDataException("Invalid host disk observation.");
                if (scheduler is IAsyncWorkResourceFeedback asynchronous)
                    await asynchronous.ObserveDiskWriteAsync(count, elapsed, token).ConfigureAwait(false);
                else if (scheduler is IWorkResourceFeedback feedback) feedback.ObserveDiskWrite(count, elapsed);
                break;
            default: throw new InvalidDataException("Unknown host work operation.");
        }
        return writer.Finish();
    }
    internal void Reject(long id, ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> result)
    {
        using var reader = NetworkProtocol.Read(request, out int operation);
        if (operation != DownloadProtocol.AcquireWork) return;
        using var response = new ServiceWireReader(result); long handle = response.Int64(); response.End();
        _ = RejectAsync(handle);
    }
    private async Task RejectAsync(long handle)
    { try { await _leases.RejectProvisionalAsync(handle).ConfigureAwait(false); } catch (Exception error) { failure(error); } }
    public ValueTask DisposeAsync() => _leases.DisposeAsync();
}
