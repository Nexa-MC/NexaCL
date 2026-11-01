using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    // The platform notification adapters own one native action target per window.
    private readonly NativeUiAsyncResources<NotificationAction> _notifications = new(1);

    private async Task<bool> NotifyWithActionAsync(string title, string message, long callback, CancellationToken token)
    {
        if (callback <= 0) throw new InvalidDataException("Invalid notification callback handle.");
        long id = _notifications.Register(new());
        ObjectDisposedException.ThrowIf(!_notifications.Claim(id), this);
        try
        {
            bool delivered = await _actions.NotifyNativeWithActionAsync(title, message, () =>
            {
                using var borrow = _notifications.TryAcquire(id);
                if (borrow is null) return;
                try
                {
                    using var writer = WindowControlProtocol.Write((int)WindowControlCallback.Notification, callback: true);
                    writer.Int64(callback);
                    if (_hostUi.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCallback.Notification, writer.Finish())).Length != 0)
                        throw new InvalidDataException("Invalid notification acknowledgement.");
                }
                catch (Exception error) { Report(error); }
            }, token).ConfigureAwait(true);
            // The Linux adapter posts activation before returning. Drain that queued
            // dispatcher turn before retiring the callback lease.
            await NativeContext.RunAsync(() => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);
            return delivered;
        }
        finally { await _notifications.ReleaseAsync(id).ConfigureAwait(false); }
    }

    private sealed class NotificationAction : IAsyncDisposable
    { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
