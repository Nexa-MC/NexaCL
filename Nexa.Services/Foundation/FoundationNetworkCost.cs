using Nexa.Platform;

namespace Nexa.Services.Foundation;

internal sealed class FoundationNetworkCost(IPlatformNetworkCost probe) : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Task? _worker;
    private Task? _shutdown;
    private int _cost;
    private bool _disposed;
    internal PlatformNetworkCost Current => (PlatformNetworkCost)Volatile.Read(ref _cost);

    internal Task StartAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _worker ??= Task.Run(ObserveAsync);
            return _ready.Task;
        }
    }

    internal static bool AllowsBackground(PlatformNetworkCost cost, string policy) => policy switch
    {
        "allow" => true,
        "restrict" => false,
        _ => cost is not (PlatformNetworkCost.Metered or PlatformNetworkCost.Roaming)
    };

    private async Task ObserveAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            do
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                PlatformNetworkCost next = PlatformNetworkCost.Unknown;
                try { next = await probe.ReadAsync(deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException and not OperationCanceledException) { }
                Volatile.Write(ref _cost, (int)next);
                _ready.TrySetResult();
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
        _stop.Dispose();
    }
}
