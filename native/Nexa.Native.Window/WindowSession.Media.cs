using Nexa.Core.Media;
using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private readonly NativeUiAsyncResources<MediaPlayer> _media = new(8);

    private async Task<byte[]> ExecuteMediaAsync(UiWireReader reader, CancellationToken token)
    {
        var operation = (WindowMediaOperation)reader.Int32();
        if (operation == WindowMediaOperation.Create)
        {
            bool video = reader.Boolean(); reader.End(); token.ThrowIfCancellationRequested();
            long handle = 0;
            var resource = new MediaPlayer(video, state => PublishMedia(handle, WindowMediaEvent.State, WindowValueChannel.EncodeMediaState(state)),
                frame => PublishMedia(handle, WindowMediaEvent.Frame, WindowValueChannel.EncodeMediaFrame(frame)));
            try
            {
                handle = _media.Register(resource);
                return WindowMediaResult.Encode(new(handle, resource.Player.State));
            }
            catch
            {
                if (handle == 0) await resource.DisposeAsync().ConfigureAwait(false);
                else await _media.RejectProvisionalAsync(handle).ConfigureAwait(false);
                throw;
            }
        }
        if (operation == WindowMediaOperation.Validate)
        {
            string path = Required(reader.String()); bool video = reader.Boolean(); reader.End();
            token.ThrowIfCancellationRequested();
            var image = await AvaloniaUiLocalMediaPlayer.ValidateThemeMediaAsync(path, video, token).ConfigureAwait(false);
            using var validated = WindowControlProtocol.Write((int)WindowCommand.Media);
            validated.PngImage(image); return validated.Finish();
        }
        long id = reader.Int64();
        if (id <= 0) throw new InvalidDataException("Invalid native media handle.");
        if (operation == WindowMediaOperation.Release)
        { reader.End(); await _media.ReleaseAsync(id).ConfigureAwait(false); return []; }
        using var borrowed = _media.Acquire(id);
        var player = borrowed.Resource.Player;
        using var result = WindowControlProtocol.Write((int)WindowCommand.Media);
        switch (operation)
        {
            case WindowMediaOperation.Configure:
                long generation = reader.Int64(); string path = Required(reader.String());
                int volume = reader.Int32(); bool play = reader.Boolean(); reader.End();
                token.ThrowIfCancellationRequested();
                await player.ConfigureGenerationAsync(generation, path, volume, play, token).ConfigureAwait(false);
                result.Buffer(WindowValueChannel.EncodeMediaState(player.State)); break;
            case WindowMediaOperation.ReadState:
                reader.End(); token.ThrowIfCancellationRequested();
                result.Buffer(WindowValueChannel.EncodeMediaState(player.State)); break;
            case WindowMediaOperation.WarmUp:
                reader.End(); token.ThrowIfCancellationRequested();
                result.PngImage(await player.WarmUpAsync(token).ConfigureAwait(false));
                result.Buffer(WindowValueChannel.EncodeMediaState(player.State)); break;
            default: throw new InvalidDataException("Unknown native media operation.");
        }
        return result.Finish();
    }

    private void PublishMedia(long handle, WindowMediaEvent kind, byte[] payload)
    {
        using var borrowed = _media.TryAcquire(handle);
        if (borrowed is null) return;
        try
        {
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.Media, callback: true);
            writer.Int64(handle); writer.Int32((int)kind); writer.Buffer(payload);
            if (_hostControl.Exchange(writer.Finish()).Length != 0) throw new InvalidDataException("Invalid media event acknowledgement.");
        }
        catch (Exception error) { Report(error); _ = _media.ReleaseAsync(handle); }
    }

    private sealed class MediaPlayer : IAsyncDisposable
    {
        private readonly Action<AvaloniaUiMediaState> _changed;
        internal AvaloniaUiLocalMediaPlayer Player { get; }
        internal MediaPlayer(bool video, Action<AvaloniaUiMediaState> changed, Action<AvaloniaUiMediaFrame> frame)
        {
            _changed = changed; Player = new(video, frame: null, stampedFrame: frame);
            Player.Changed += changed;
        }
        public async ValueTask DisposeAsync()
        { Player.Changed -= _changed; await Player.DisposeAsync().ConfigureAwait(false); }
    }
}
