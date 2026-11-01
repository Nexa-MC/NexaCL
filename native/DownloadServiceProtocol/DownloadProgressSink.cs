using Nexa.Services.Downloads;

namespace Nexa.Native.Network.Services;

// User callbacks never run on a native publication stack. Each caller retains at
// most its latest pending observation, and its completion drains the local worker.
internal sealed class DownloadProgressSink : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _ready = new(0, 1);
    private readonly Task _worker;
    private readonly Action<DownloadProgress> _callback;
    private DownloadProgress? _pending;
    private bool _closed;
    private Task? _retirement;

    internal DownloadProgressSink(Action<DownloadProgress> callback)
    { _callback = callback; _worker = Task.Run(RunAsync); }
    internal void Post(DownloadProgress progress)
    {
        lock (_gate)
        {
            if (_closed) return;
            _pending = progress; if (_ready.CurrentCount == 0) _ready.Release();
        }
    }
    private async Task RunAsync()
    {
        while (true)
        {
            await _ready.WaitAsync().ConfigureAwait(false);
            DownloadProgress? progress;
            lock (_gate) { progress = _pending; _pending = null; if (_closed && progress is null) return; }
            if (progress is { } value) { try { _callback(value); } catch (Exception) { } }
            lock (_gate) { if (_closed && _pending is null) return; }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_retirement is not null) return new(_retirement);
            _closed = true; if (_ready.CurrentCount == 0) _ready.Release();
            return new(_retirement ??= RetireAsync());
        }
    }
    private async Task RetireAsync() { try { await _worker.ConfigureAwait(false); } finally { _ready.Dispose(); } }
}
