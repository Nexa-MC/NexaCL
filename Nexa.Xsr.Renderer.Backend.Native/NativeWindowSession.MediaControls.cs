using System.Collections.Concurrent;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private readonly ConcurrentDictionary<long, NativeWindowMediaControls> _mediaControls = new();

    public async Task<NativeWindowMediaControls> CreateMediaControlsAsync(AvaloniaUiMediaState state, double volume, bool enabled,
        Action<string, double?> control, Action<string> report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(control); ArgumentNullException.ThrowIfNull(report);
        var snapshot = new WindowMediaControlsSnapshot(state, volume, enabled);
        using var writer = WindowControlProtocol.Write((int)WindowCommand.MediaControls);
        writer.Int32((int)WindowMediaControlsOperation.Create); snapshot.Write(writer);
        byte[] response = await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
        long provisional = WindowEffectResult.ProvisionalHandle(response, kind: 1);
        try
        {
            long handle = WindowEffectResult.Decode(response, kind: 1);
            var resource = new NativeWindowMediaControls(this, handle, control, report);
            if (!_mediaControls.TryAdd(handle, resource)) throw new InvalidDataException("The system media handle was reused.");
            byte[] claimed = SendControl(WindowCommand.ClaimMediaControls, arguments => arguments.Int64(handle));
            if (claimed.Length != 1 || claimed[0] != 1) throw new InvalidOperationException("System media controls could not be claimed.");
            await MediaControlsOperationAsync(handle, WindowMediaControlsOperation.Start, null, cancellationToken).ConfigureAwait(false);
            return resource;
        }
        catch { if (provisional != 0) await ReleaseMediaControlsAsync(provisional).ConfigureAwait(false); throw; }
    }

    internal async Task MediaControlsOperationAsync(long handle, WindowMediaControlsOperation operation,
        WindowMediaControlsSnapshot? snapshot, CancellationToken token)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.MediaControls);
        writer.Int32((int)operation); writer.Int64(handle); snapshot?.Write(writer);
        if ((await _operations.InvokeAsync(writer.Finish(), token).ConfigureAwait(false)).Length != 0)
            throw new InvalidDataException("Invalid system media response.");
    }

    private byte[] ReceiveMediaControls(UiWireReader reader)
    {
        long id = reader.Int64(); bool report = reader.Boolean();
        string command = reader.String() ?? throw new InvalidDataException("Missing system media command.");
        double? value = reader.Boolean() ? reader.Double() : null; reader.End();
        if (id <= 0 || value.HasValue && (!double.IsFinite(value.Value) || value is < 0 or > 1))
            throw new InvalidDataException("Invalid system media event.");
        if (_mediaControls.TryGetValue(id, out var controls)) controls.Publish(command, value, report);
        return [];
    }

    internal async Task ReleaseMediaControlsAsync(long handle)
    {
        try
        {
            if (Volatile.Read(ref _uiRetired) != 0) return;
            if (Volatile.Read(ref _nativeRetiring) != 0) { await _uiStopped.Task.ConfigureAwait(false); return; }
            try { await MediaControlsOperationAsync(handle, WindowMediaControlsOperation.Release, null, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) when (Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0)
            { await _uiStopped.Task.ConfigureAwait(false); }
        }
        finally { _mediaControls.TryRemove(handle, out _); }
    }
}

/// <summary>System media integration consumes snapshots locally; native OS callbacks publish bounded control values.</summary>
public sealed class NativeWindowMediaControls : IDisposable, IAsyncDisposable
{
    private readonly NativeWindowSession _owner;
    private readonly long _handle;
    private readonly Action<string, double?> _control;
    private readonly Action<string> _report;
    private TaskCompletionSource? _release;
    internal NativeWindowMediaControls(NativeWindowSession owner, long handle, Action<string, double?> control, Action<string> report)
    { _owner = owner; _handle = handle; _control = control; _report = report; }
    public Task UpdateAsync(AvaloniaUiMediaState state, double volume, bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is not null, this);
        return _owner.MediaControlsOperationAsync(_handle, WindowMediaControlsOperation.Update, new(state, volume, enabled), cancellationToken);
    }
    internal void Publish(string command, double? value, bool report)
    { if (Volatile.Read(ref _release) is null) { if (report) _report(command); else _control(command, value); } }
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
        try { await _owner.ReleaseMediaControlsAsync(_handle).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
