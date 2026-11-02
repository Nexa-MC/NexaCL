using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private readonly NativeUiAsyncResources<MediaControls> _mediaControls = new(2);

    private async Task<byte[]> ExecuteMediaControlsAsync(UiWireReader reader, CancellationToken token)
    {
        var operation = (WindowMediaControlsOperation)reader.Int32();
        if (operation == WindowMediaControlsOperation.Create)
        {
            var snapshot = WindowMediaControlsSnapshot.Read(reader); reader.End(); token.ThrowIfCancellationRequested();
            var resource = new MediaControls(snapshot); long handle = 0;
            try { handle = _mediaControls.Register(resource); return WindowEffectResult.Encode(handle, kind: 1); }
            catch
            {
                if (handle == 0) await resource.DisposeAsync().ConfigureAwait(false);
                else await _mediaControls.RejectProvisionalAsync(handle).ConfigureAwait(false);
                throw;
            }
        }
        long id = reader.Int64();
        if (id <= 0) throw new InvalidDataException("Invalid system media handle.");
        if (operation == WindowMediaControlsOperation.Release)
        { reader.End(); await _mediaControls.ReleaseAsync(id).ConfigureAwait(false); return []; }
        using var borrowed = _mediaControls.Acquire(id);
        switch (operation)
        {
            case WindowMediaControlsOperation.Start:
                reader.End(); token.ThrowIfCancellationRequested();
                await NativeContext.RunAsync(() =>
                {
                    borrowed.Resource.Start(_actions, (command, value) => PublishMediaControl(id, command, value, report: false),
                        message => PublishMediaControl(id, message, null, report: true));
                    return Task.CompletedTask;
                }, token).ConfigureAwait(false);
                return [];
            case WindowMediaControlsOperation.Update:
                var snapshot = WindowMediaControlsSnapshot.Read(reader); reader.End(); token.ThrowIfCancellationRequested();
                borrowed.Resource.Update(snapshot); return [];
            default: throw new InvalidDataException("Unknown system media operation.");
        }
    }

    private void PublishMediaControl(long id, string command, double? value, bool report)
    {
        using var borrowed = _mediaControls.TryAcquire(id);
        if (borrowed is null) return;
        try
        {
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.MediaControls, callback: true);
            writer.Int64(id); writer.Boolean(report); writer.String(command); writer.Boolean(value.HasValue);
            if (value.HasValue) writer.Double(value.Value);
            if (_hostControl.Exchange(writer.Finish()).Length != 0) throw new InvalidDataException("Invalid system media acknowledgement.");
        }
        catch (Exception error) { Report(error); _ = _mediaControls.ReleaseAsync(id); }
    }

    private sealed class MediaControls(WindowMediaControlsSnapshot snapshot) : IAsyncDisposable
    {
        private WindowMediaControlsSnapshot _snapshot = snapshot;
        private IAsyncDisposable? _native;
        private bool _started;
        internal void Update(WindowMediaControlsSnapshot value) => Volatile.Write(ref _snapshot, value);
        internal void Start(AvaloniaUiPlatformActions actions, Action<string, double?> control, Action<string> report)
        {
            if (_started) throw new InvalidOperationException("System media controls already started.");
            _started = true;
            _native = OperatingSystem.IsLinux()
                ? new AvaloniaUiMprisSession(() => Volatile.Read(ref _snapshot).State, () => Volatile.Read(ref _snapshot).Volume,
                    control, report, () => Volatile.Read(ref _snapshot).Enabled)
                : actions.CreateNativeMediaControls(() => Volatile.Read(ref _snapshot).State,
                    () => Volatile.Read(ref _snapshot).Enabled, command => control(command, null), report);
        }
        public async ValueTask DisposeAsync()
        { if (_native is not null) await _native.DisposeAsync().ConfigureAwait(false); }
    }
}
