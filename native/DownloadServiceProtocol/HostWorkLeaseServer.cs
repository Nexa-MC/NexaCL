using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Scheduling;

#if NEXA_MINECRAFT_HTTP_BRIDGE
namespace Nexa.Native.Minecraft.Http;
#else
namespace Nexa.Native.Network.Services;
#endif

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
    private readonly NativeUiAsyncResources<Quiet> _quiet = new(32);
    private readonly object _graceGate = new();
    private sealed record GraceFlight(CancellationTokenSource Stop, Task Work);
    private readonly Dictionary<long, GraceFlight> _grace = [];
    private readonly CancellationTokenSource _stop = new();
    private bool _closed;
    private Task? _retirement;
    private sealed class Quiet(IWorkQuietLease lease) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private bool _disposed, _grace;
        internal bool Grace(TimeSpan period)
        {
            if (period < TimeSpan.Zero || period > TimeSpan.FromMinutes(2)) throw new InvalidDataException("Invalid quiet grace period.");
            lock (_gate)
            {
                if (_disposed || _grace) return false;
                _grace = true; return true;
            }
        }
        public ValueTask DisposeAsync() { lock (_gate) { if (_disposed) return ValueTask.CompletedTask; _disposed = true; lease.Dispose(); } return ValueTask.CompletedTask; }
    }

    internal async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = NetworkProtocol.Read(bytes, out int operation);
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case DownloadProtocol.AcquireQuiet:
                reader.End();
                if (scheduler is null) throw new InvalidOperationException("Quiet scopes require the canonical host scheduler.");
                var quietLease = scheduler is IAsyncWorkQuietScheduler asynchronousQuiet
                    ? await asynchronousQuiet.EnterQuietAsync(token).ConfigureAwait(false) : scheduler.EnterQuiet();
                try { token.ThrowIfCancellationRequested(); writer.Int64(_quiet.Register(new(quietLease))); quietLease = null!; }
                finally { quietLease?.Dispose(); }
                break;
            case DownloadProtocol.ClaimQuiet:
                long quietClaim = reader.Int64(); reader.End(); writer.Boolean(_quiet.Claim(quietClaim)); break;
            case DownloadProtocol.ReleaseQuiet:
                long quietRelease = reader.Int64(); reader.End(); await CancelGraceAsync(quietRelease).ConfigureAwait(false);
                await _quiet.ReleaseAsync(quietRelease).ConfigureAwait(false); break;
            case DownloadProtocol.GraceQuiet:
                long quietHandle = reader.Int64(); TimeSpan grace = reader.TimeSpan(); reader.End();
                using (var borrow = _quiet.TryAcquire(quietHandle))
                    if (borrow is not null && borrow.Resource.Grace(grace)) ScheduleGrace(quietHandle, grace);
                break;
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
        if (operation is not (DownloadProtocol.AcquireWork or DownloadProtocol.AcquireQuiet)) return;
        using var response = new ServiceWireReader(result); long handle = response.Int64(); response.End();
        _ = RejectAsync(handle, operation == DownloadProtocol.AcquireQuiet);
    }
    private async Task RejectAsync(long handle, bool quiet)
    { try { await (quiet ? _quiet.RejectProvisionalAsync(handle) : _leases.RejectProvisionalAsync(handle)).ConfigureAwait(false); } catch (Exception error) { failure(error); } }
    private void ScheduleGrace(long handle, TimeSpan period)
    {
        lock (_graceGate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            foreach (long completed in _grace.Where(item => item.Value.Work.IsCompleted).Select(item => item.Key).ToArray())
            { _grace[completed].Stop.Dispose(); _grace.Remove(completed); }
            if (_grace.Count >= 32) throw new InvalidOperationException("Quiet grace budget exceeded.");
            var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _grace.Add(handle, new(stop, ReleaseAfterAsync(handle, period, stop.Token)));
        }
    }
    private async Task CancelGraceAsync(long handle)
    {
        GraceFlight? flight; lock (_graceGate) _grace.Remove(handle, out flight);
        if (flight is null) return;
        try { await flight.Stop.CancelAsync().ConfigureAwait(false); await flight.Work.ConfigureAwait(false); }
        finally { flight.Stop.Dispose(); }
    }
    private async Task ReleaseAfterAsync(long handle, TimeSpan period, CancellationToken token)
    {
        try { await Task.Delay(period, token).ConfigureAwait(false); await _quiet.ReleaseAsync(handle).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { failure(error); throw; }
    }
    public ValueTask DisposeAsync()
    { lock (_graceGate) return new(_retirement ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        Task[] pending;
        lock (_graceGate) { _closed = true; pending = _grace.Values.Select(item => item.Work).ToArray(); }
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pending).ConfigureAwait(false);
        foreach (var flight in _grace.Values) flight.Stop.Dispose();
        await _quiet.DisposeAsync().ConfigureAwait(false); await _leases.DisposeAsync().ConfigureAwait(false); _stop.Dispose();
    }
}
