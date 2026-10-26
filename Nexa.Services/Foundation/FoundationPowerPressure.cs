using Nexa.Platform;
using Nexa.Services.Scheduling;

namespace Nexa.Services.Foundation;

internal sealed class FoundationPowerPressure(IPlatformPowerPressure probe, IWorkScheduler work) : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private Task? _shutdown;
    private IWorkQuietLease? _quiet;
    private int _recovery;
    private PlatformPowerPressure _previous;
    private bool _disposed;

    internal Task StartAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _worker ??= Task.Run(ObserveAsync);
            return _ready.Task;
        }
    }

    internal void Observe(PlatformPowerPressure sample)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (sample != _previous) _recovery = 0;
            _previous = sample;
            if (sample == PlatformPowerPressure.Constrained)
            {
                _recovery = 0;
                _quiet ??= work.EnterQuiet();
            }
            else if (++_recovery >= 3)
            {
                _quiet?.Dispose(); _quiet = null; _recovery = 0;
            }
        }
    }

    private async Task ObserveAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var next = PlatformPowerPressure.Unknown;
                try { next = await probe.ReadAsync(deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException and not OperationCanceledException) { }
                Observe(next); _ready.TrySetResult();
            } while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _ready.TrySetResult(); }
    }

    public void Dispose() => _ = DisposeAsync().AsTask().ContinueWith(static completed =>
    { _ = completed.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            return new(_shutdown ??= Task.Run(ShutdownAsync));
        }
    }

    private async Task ShutdownAsync()
    {
        _stop.Cancel();
        if (_worker is not null) await _worker.ConfigureAwait(false);
        _quiet?.Dispose(); _quiet = null;
        _stop.Dispose();
    }
}
