using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private readonly NativeUiAsyncResources<NotificationCallback> _notificationCallbacks = new(1);

    public async Task<bool> NotifyNativeWithActionAsync(string title, string message, Action activate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activate); cancellationToken.ThrowIfCancellationRequested();
        long id = _notificationCallbacks.Register(new(activate));
        try
        {
            ObjectDisposedException.ThrowIf(!_notificationCallbacks.Claim(id), this);
            using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.NotifyWithAction, writer =>
            { writer.String(title); writer.String(message); writer.Int64(id); }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.NotifyWithAction);
            bool delivered = result.Boolean(); result.End(); return delivered;
        }
        finally { await _notificationCallbacks.ReleaseAsync(id).ConfigureAwait(false); }
    }

    private byte[] ReceiveNotification(ReadOnlyMemory<byte> message)
    {
        using var reader = WindowControlProtocol.Read(message, out int kind, callback: true);
        if (kind != (int)WindowControlCallback.Notification) throw new InvalidDataException("Invalid notification envelope.");
        long id = reader.Int64(); reader.End();
        if (id <= 0) throw new InvalidDataException("Invalid notification callback handle.");
        using var borrow = _notificationCallbacks.TryAcquire(id);
        borrow?.Resource.Activate(); return [];
    }

    private sealed class NotificationCallback(Action activate) : IAsyncDisposable
    { internal void Activate() => activate(); public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
