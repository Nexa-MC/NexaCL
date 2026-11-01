using System.Collections.Concurrent;
using Nexa.Native.Window.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private readonly ConcurrentDictionary<long, NativeWindowEffectLease> _effects = new();

    public async Task<byte[]> CropScreenshotAsync(ReadOnlyMemory<byte> png, int x, int y, int width, int height,
        CancellationToken cancellationToken = default)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Effect);
        writer.Int32((int)WindowEffectOperation.CropScreenshot); writer.Buffer(png);
        writer.Int32(x); writer.Int32(y); writer.Int32(width); writer.Int32(height);
        using var reader = WindowControlProtocol.Read(await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false), out int kind);
        if (kind != (int)WindowCommand.Effect) throw new InvalidDataException("Invalid screenshot response.");
        byte[] result = reader.Buffer(); reader.End(); return result;
    }

    public async Task<NativeWindowEffectLease> AcquireWorldEditLeaseAsync(string path, CancellationToken cancellationToken = default)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Effect);
        writer.Int32((int)WindowEffectOperation.AcquireWorldLock); writer.String(path);
        byte[] response = await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
        long provisional = WindowEffectResult.ProvisionalHandle(response);
        try
        {
            long handle = WindowEffectResult.Decode(response);
            byte[] claimed = SendControl(WindowCommand.ClaimEffect, arguments => arguments.Int64(handle));
            if (claimed.Length != 1 || claimed[0] != 1) throw new InvalidOperationException("The native effect could not be claimed.");
            var lease = new NativeWindowEffectLease(this, handle);
            if (!_effects.TryAdd(handle, lease)) throw new InvalidDataException("The native effect handle was reused.");
            cancellationToken.ThrowIfCancellationRequested(); return lease;
        }
        catch { if (provisional != 0) await ReleaseEffectAsync(provisional).ConfigureAwait(false); throw; }
    }

    internal async Task ReleaseEffectAsync(long handle)
    {
        try
        {
            if (Volatile.Read(ref _uiRetired) != 0) return;
            if (Volatile.Read(ref _nativeRetiring) != 0) { await _uiStopped.Task.ConfigureAwait(false); return; }
            using var writer = WindowControlProtocol.Write((int)WindowCommand.ReleaseEffect); writer.Int64(handle);
            try { await _operations.InvokeAsync(writer.Finish()).ConfigureAwait(false); }
            catch (Exception) when (Volatile.Read(ref _nativeRetiring) != 0 || Volatile.Read(ref _uiRetired) != 0)
            { await _uiStopped.Task.ConfigureAwait(false); }
        }
        finally { _effects.TryRemove(handle, out _); }
    }
}

/// <summary>An opaque native platform lease. Async disposal waits for physical release.</summary>
public sealed class NativeWindowEffectLease : IDisposable, IAsyncDisposable
{
    private readonly NativeWindowSession _owner;
    private readonly long _handle;
    private TaskCompletionSource? _release;
    internal NativeWindowEffectLease(NativeWindowSession owner, long handle) { _owner = owner; _handle = handle; }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.CompareExchange(ref _release, completion, null);
        if (previous is not null) return new(previous.Task);
        _ = ReleaseAsync(completion); return new(completion.Task);
    }
    private async Task ReleaseAsync(TaskCompletionSource completion)
    {
        try { await _owner.ReleaseEffectAsync(_handle).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
