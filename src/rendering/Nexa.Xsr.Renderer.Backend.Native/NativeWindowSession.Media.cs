using System.Collections.Concurrent;
using Nexa.Core.Media;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private readonly ConcurrentDictionary<long, NativeWindowMediaPlayer> _media = new();

    public async Task<NativeWindowMediaPlayer> CreateMediaPlayerAsync(bool video, CancellationToken cancellationToken = default)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Media);
        writer.Int32((int)WindowMediaOperation.Create); writer.Boolean(video);
        byte[] response = await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
        long provisional = WindowMediaResult.ProvisionalHandle(response);
        try
        {
            var descriptor = WindowMediaResult.Decode(response);
            var player = new NativeWindowMediaPlayer(this, descriptor.Handle, descriptor.State);
            if (!_media.TryAdd(descriptor.Handle, player)) throw new InvalidDataException("The native media handle was reused.");
            byte[] claimed = SendControl(WindowCommand.ClaimMedia, arguments => arguments.Int64(descriptor.Handle));
            if (claimed.Length != 1 || claimed[0] != 1) throw new InvalidOperationException("The native media could not be claimed.");
            cancellationToken.ThrowIfCancellationRequested(); return player;
        }
        catch { if (provisional != 0) await ReleaseMediaAsync(provisional).ConfigureAwait(false); throw; }
    }

    public async Task<PngImage?> ValidateThemeMediaAsync(string path, bool video, CancellationToken cancellationToken = default)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Media);
        writer.Int32((int)WindowMediaOperation.Validate); writer.String(path); writer.Boolean(video);
        using var response = WindowControlProtocol.Read(await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false), out int kind);
        if (kind != (int)WindowCommand.Media) throw new InvalidDataException("Invalid media validation response.");
        var image = response.PngImage(); response.End(); return image;
    }

    internal async Task<(PngImage? Image, AvaloniaUiMediaState State)> MediaOperationAsync(long handle,
        WindowMediaOperation operation, Action<UiWireWriter>? arguments, CancellationToken token)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Media);
        writer.Int32((int)operation); writer.Int64(handle); arguments?.Invoke(writer);
        using var response = WindowControlProtocol.Read(await _operations.InvokeAsync(writer.Finish(), token).ConfigureAwait(false), out int kind);
        if (kind != (int)WindowCommand.Media) throw new InvalidDataException("Invalid media response.");
        var image = operation == WindowMediaOperation.WarmUp ? response.PngImage() : null;
        var state = WindowValueChannel.DecodeMediaState(response.Buffer()); response.End(); return (image, state);
    }

    private byte[] ReceiveMedia(UiWireReader reader)
    {
        long handle = reader.Int64(); var kind = (WindowMediaEvent)reader.Int32(); byte[] payload = reader.Buffer(); reader.End();
        if (handle <= 0) throw new InvalidDataException("Invalid native media event handle.");
        switch (kind)
        {
            case WindowMediaEvent.State:
                var state = WindowValueChannel.DecodeMediaState(payload);
                if (_media.TryGetValue(handle, out var player)) player.Publish(state);
                break;
            case WindowMediaEvent.Frame:
                var frame = WindowValueChannel.DecodeMediaFrame(payload);
                if (_media.TryGetValue(handle, out var video)) video.Publish(frame);
                break;
            default: throw new InvalidDataException("Unknown native media event.");
        }
        return [];
    }

    internal async Task ReleaseMediaAsync(long handle)
    {
        try
        {
            if (Volatile.Read(ref _uiRetired) != 0) return;
            if (Volatile.Read(ref _nativeRetiring) != 0) { await _uiStopped.Task.ConfigureAwait(false); return; }
            using var writer = WindowControlProtocol.Write((int)WindowCommand.Media);
            writer.Int32((int)WindowMediaOperation.Release); writer.Int64(handle);
            try { await _operations.InvokeAsync(writer.Finish()).ConfigureAwait(false); }
            catch (Exception) when (Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0)
            { await _uiStopped.Task.ConfigureAwait(false); }
        }
        finally { _media.TryRemove(handle, out _); }
    }
}

/// <summary>A local lease on a native decoder; callbacks contain copied values and retire before async disposal completes.</summary>
public sealed class NativeWindowMediaPlayer : IDisposable, IAsyncDisposable
{
    private readonly NativeWindowSession _owner;
    private readonly long _handle;
    private AvaloniaUiMediaState _state;
    private TaskCompletionSource? _release;
    public AvaloniaUiMediaState State => Volatile.Read(ref _state);
    public event Action<AvaloniaUiMediaState>? Changed;
    public event Action<AvaloniaUiMediaFrame>? Frame;

    internal NativeWindowMediaPlayer(NativeWindowSession owner, long handle, AvaloniaUiMediaState state)
    { _owner = owner; _handle = handle; _state = state; }

    public Task ConfigureAsync(string localPath, int volume, bool play, CancellationToken token = default)
        => ConfigureGenerationAsync(0, localPath, volume, play, token);
    public async Task ConfigureGenerationAsync(long generation, string localPath, int volume, bool play, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is not null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0); ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);
        await _owner.MediaOperationAsync(_handle, WindowMediaOperation.Configure, writer =>
        { writer.Int64(generation); writer.String(localPath); writer.Int32(volume); writer.Boolean(play); }, token).ConfigureAwait(false);
    }
    public async Task<PngImage?> WarmUpAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is not null, this);
        return (await _owner.MediaOperationAsync(_handle, WindowMediaOperation.WarmUp, null, token).ConfigureAwait(false)).Image;
    }
    internal void Publish(AvaloniaUiMediaState state)
    {
        if (Volatile.Read(ref _release) is not null) return;
        Volatile.Write(ref _state, state); Changed?.Invoke(state);
    }
    internal void Publish(AvaloniaUiMediaFrame frame)
    { if (Volatile.Read(ref _release) is null) Frame?.Invoke(frame); }
    public void Dispose() => _ = BeginRelease();
    public ValueTask DisposeAsync() => new(BeginRelease());
    private Task BeginRelease()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.CompareExchange(ref _release, completion, null);
        if (previous is not null) return previous.Task;
        _ = ReleaseAsync(completion); return completion.Task;
    }
    private async Task ReleaseAsync(TaskCompletionSource completion)
    {
        try { await _owner.ReleaseMediaAsync(_handle).ConfigureAwait(false); Changed = null; Frame = null; completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
