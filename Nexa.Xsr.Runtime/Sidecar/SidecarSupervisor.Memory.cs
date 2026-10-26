using System.Threading.Channels;

namespace Nexa.Xsr.Runtime;

public sealed partial class SidecarSupervisor
{
    private readonly Channel<bool> _memoryObservations = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest, AllowSynchronousContinuations = false });
    private bool _memoryWorkerStarted;
    // Accessed only while holding _controls, including child startup/recovery.
    private bool _memoryConstrained;

    /// <summary>Queues the latest policy decision without blocking the native sampler on IPC.</summary>
    public void ReportMemoryConstraint(bool constrained)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _memoryObservations.Writer.TryWrite(constrained);
            if (_memoryWorkerStarted) return;
            _memoryWorkerStarted = true;
            Track(Task.Run(ObserveMemoryAsync));
        }
    }

    private async Task ObserveMemoryAsync()
    {
        try
        {
            while (await _memoryObservations.Reader.WaitToReadAsync(_lifetime.Token).ConfigureAwait(false))
            {
                await _controls.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                try
                {
                    if (!_memoryObservations.Reader.TryRead(out bool constrained)) continue;
                    _memoryConstrained = constrained;
                    Package[] packages;
                    lock (_gate) packages = _packages.Values.ToArray();
                    foreach (var package in packages)
                    {
                        _lifetime.Token.ThrowIfCancellationRequested();
                        Child? child;
                        lock (_gate) child = package.Child;
                        if (child is null || child.IntentionalStop) continue;
                        bool deactivate = constrained && child.Session.State == SidecarSessionState.Active;
                        bool activate = !constrained && child.MemoryQuiescent && child.Session.State == SidecarSessionState.Ready;
                        if (!deactivate && !activate) continue;
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(3));
                        try
                        {
                            if (deactivate) await child.Session.DeactivateAsync(deadline.Token).ConfigureAwait(false);
                            else await child.Session.ActivateAsync(deadline.Token).ConfigureAwait(false);
                            lock (_gate)
                            {
                                if (!ReferenceEquals(package.Child, child)) continue;
                                child.MemoryQuiescent = constrained;
                                package.Status = constrained ? SidecarPackageStatus.Quiescent : SidecarPackageStatus.Active;
                            }
                            Report(package.Name, constrained ? "quiescent" : "active");
                        }
                        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                        {
                            // The monitor owns teardown and bounded recovery of a failed session.
                            child.Session.Dispose();
                            Report(package.Name, "failed: sidecar.memory_transition");
                        }
                    }
                }
                finally { _controls.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
}
