namespace Nexa.Services.Minecraft.Process;

public sealed partial class MinecraftProcessSession
{
    private IAsyncDisposable? _nativeLease;
    private Task _nativeRelease = Task.CompletedTask;

    internal void OwnNativeLease(IAsyncDisposable lease)
    {
        lock (_gate)
        {
            if (_nativeLease is not null) throw new InvalidOperationException("Native process lease already owned.");
            _nativeLease = lease;
            if (_disposed != 0 || _snapshot.State is not (MinecraftProcessState.Created or MinecraftProcessState.Running))
                _ = ReleaseNativeLeaseAsync();
        }
    }

    private Task ReleaseNativeLeaseAsync()
    {
        lock (_gate)
        {
            if (_nativeLease is { } lease)
            {
                _nativeLease = null;
                _nativeRelease = Task.Run(async () => await lease.DisposeAsync().ConfigureAwait(false));
                _ = _nativeRelease.ContinueWith(static completed => { _ = completed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return _nativeRelease;
        }
    }
}
