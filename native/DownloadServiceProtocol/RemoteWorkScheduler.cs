using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Scheduling;

#if NEXA_MINECRAFT_HTTP_BRIDGE
namespace Nexa.Native.Minecraft.Http;
#else
namespace Nexa.Native.Network.Services;
#endif

internal sealed class RemoteWorkScheduler(ServiceEndpoint endpoint) : IWorkScheduler, IAsyncWorkResourceFeedback, IAsyncWorkQuietScheduler, IAsyncDisposable
{
    private readonly AsyncLocal<WorkPriority?> _priority = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, Task> _releases = [];
    private Exception? _failure;
    private bool _closed;
    private long _nextRelease;
    public WorkPriority CurrentPriority => _priority.Value ?? WorkPriority.Interactive;
    public IDisposable UsePriority(WorkPriority priority)
    {
        if ((uint)priority > (uint)WorkPriority.Idle) throw new ArgumentOutOfRangeException(nameof(priority));
        var previous = _priority.Value; _priority.Value = priority; return new PriorityScope(_priority, previous);
    }
    private sealed class PriorityScope(AsyncLocal<WorkPriority?> priority, WorkPriority? previous) : IDisposable
    { public void Dispose() => priority.Value = previous; }
    public IWorkQuietLease EnterQuiet() => throw new NotSupportedException("Download scopes do not enter host quiet mode.");
    public async ValueTask<IWorkQuietLease> EnterQuietAsync(CancellationToken token = default)
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        using var request = NetworkProtocol.Request(DownloadProtocol.AcquireQuiet);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        long handle;
        using (var reader = new ServiceWireReader(bytes))
        { handle = reader.Int64(); reader.End(); if (handle <= 0) throw new InvalidDataException("Invalid host quiet lease."); }
        try
        {
            using var claim = NetworkProtocol.Request(DownloadProtocol.ClaimQuiet); claim.Int64(handle);
            byte[] confirmed = await endpoint.InvokeAsync(claim.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(confirmed);
            if (!reader.Boolean()) throw new InvalidOperationException("Host quiet admission retired before claim.");
            reader.End(); return new QuietLease(this, handle);
        }
        catch { await QuietCommandAsync(DownloadProtocol.ReleaseQuiet, handle, null).ConfigureAwait(false); throw; }
    }
    private sealed class QuietLease(RemoteWorkScheduler owner, long handle) : IWorkQuietLease
    {
        private readonly object _gate = new();
        private Task _pending = Task.CompletedTask;
        private bool _disposed, _grace;
        public void ReleaseAfter(TimeSpan period)
        {
            if (period < TimeSpan.Zero || period > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(period));
            lock (_gate)
            {
                if (_disposed || _grace) return;
                _grace = true; _pending = SendAfterAsync(_pending, DownloadProtocol.GraceQuiet, period); owner.Track(_pending);
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; _pending = SendAfterAsync(_pending, DownloadProtocol.ReleaseQuiet, null); owner.Track(_pending);
            }
        }
        private async Task SendAfterAsync(Task previous, int operation, TimeSpan? grace)
        {
            try { await previous.ConfigureAwait(false); }
            finally { await owner.QuietCommandAsync(operation, handle, grace).ConfigureAwait(false); }
        }
    }
    private async Task QuietCommandAsync(int operation, long handle, TimeSpan? grace)
    {
        using var request = NetworkProtocol.Request(operation); request.Int64(handle); if (grace is { } value) request.TimeSpan(value);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish()).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }

    public async ValueTask ObserveDiskWriteAsync(int bytes, TimeSpan elapsed, CancellationToken token = default)
    {
        if (bytes < 0 || elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bytes));
        using var request = NetworkProtocol.Request(DownloadProtocol.ObserveDiskWrite);
        request.Int32(bytes); request.TimeSpan(elapsed);
        byte[] reply = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(reply); reader.End();
    }

    public async ValueTask<IDisposable> AcquireAsync(WorkPriority priority, WorkResource resource, CancellationToken token = default)
    {
        if ((uint)priority > (uint)WorkPriority.Idle || (uint)resource is 0 or > 7) throw new ArgumentOutOfRangeException(nameof(resource));
        lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        using var request = NetworkProtocol.Request(DownloadProtocol.AcquireWork);
        request.Int32((int)priority); request.Int32((int)resource);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        long handle;
        using (var reader = new ServiceWireReader(bytes))
        { handle = reader.Int64(); reader.End(); if (handle <= 0) throw new InvalidDataException("Invalid host work lease."); }
        try
        {
            using var claim = NetworkProtocol.Request(DownloadProtocol.ClaimWork); claim.Int64(handle);
            byte[] confirmed = await endpoint.InvokeAsync(claim.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(confirmed);
            if (!reader.Boolean()) throw new InvalidOperationException("Host work admission retired before claim.");
            reader.End(); return new Lease(this, handle);
        }
        catch { await ReleaseAsync(handle).ConfigureAwait(false); throw; }
    }
    private sealed class Lease(RemoteWorkScheduler owner, long handle) : IDisposable
    {
        private RemoteWorkScheduler? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(handle);
    }
    private void Release(long handle)
        => Track(ReleaseAsync(handle));
    private void Track(Task retirement)
    {
        lock (_gate)
        {
            foreach (var completed in _releases.Where(item => item.Value.IsCompleted).ToArray())
            {
                if (completed.Value.IsFaulted) _failure ??= completed.Value.Exception;
                _releases.Remove(completed.Key);
            }
            _releases.Add(checked(++_nextRelease), retirement);
        }
    }
    private async Task ReleaseAsync(long handle)
    {
        using var request = NetworkProtocol.Request(DownloadProtocol.ReleaseWork); request.Int64(handle);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish()).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    internal async Task FlushAsync()
    {
        Task[] pending;
        lock (_gate) pending = [.. _releases.Values];
        await Task.WhenAll(pending).ConfigureAwait(false);
        if (_failure is not null) throw new IOException("Host work lease retirement failed.", _failure);
    }
    public async ValueTask DisposeAsync()
    { lock (_gate) _closed = true; await FlushAsync().ConfigureAwait(false); }
}
