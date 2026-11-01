using System.Collections.Concurrent;
using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private readonly ConcurrentDictionary<long, NativeWindowTheme> _themes = new();
    private int _nativeRetiring;

    public async Task<NativeWindowTheme> PrepareThemeAsync(AvaloniaUiThemeAppearance appearance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        appearance = appearance.Snapshot();
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Theme);
        writer.Buffer(WindowValueChannel.EncodeTheme(appearance));
        byte[] bytes = await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
        long provisional = WindowThemeResult.ProvisionalHandle(bytes);
        try
        {
            var prepared = WindowThemeResult.Decode(bytes);
            byte[] claimed = SendControl(WindowCommand.ClaimTheme, arguments => arguments.Int64(prepared.Handle));
            if (claimed.Length != 1 || claimed[0] != 1) throw new InvalidOperationException("The native theme could not be claimed.");
            var theme = new NativeWindowTheme(this, prepared.Handle, appearance, prepared.NativePrepared,
                prepared.DecodedBitmapBytes, prepared.Startup);
            if (!_themes.TryAdd(prepared.Handle, theme)) throw new InvalidDataException("The native theme handle was reused.");
            return theme;
        }
        catch { if (provisional != 0) await ReleaseThemeAsync(provisional).ConfigureAwait(false); throw; }
    }

    public Task<NativeWindowTheme?> CommitThemeAsync(NativeWindowTheme theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme); theme.CheckOwner(this);
        return ChangeThemeAsync(0, theme.Handle, cancellationToken);
    }

    public Task<NativeWindowTheme?> ClearThemeAsync(long generation, CancellationToken cancellationToken = default)
        => ChangeThemeAsync(1, generation, cancellationToken);

    private async Task<NativeWindowTheme?> ChangeThemeAsync(int operation, long value, CancellationToken token)
    {
        NativeWindowTheme? previous = null;
        await _context.RunAsync(() =>
        {
            using var response = ThemeOperation(operation, value);
            long handle = response.Int64(); response.End();
            if (handle != 0) _themes.TryGetValue(handle, out previous);
            return Task.CompletedTask;
        }, token).ConfigureAwait(false);
        return previous;
    }

    private UiWireReader ThemeOperation(int operation, long value, ReadOnlyMemory<byte>? payload = null)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Theme);
        writer.Int32(operation); writer.Int64(value); if (payload.HasValue) writer.Buffer(payload.Value);
        var reader = WindowControlProtocol.Read(_ui.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCommand.Theme, writer.Finish())), out int kind);
        if (kind == (int)WindowCommand.Theme) return reader;
        reader.Dispose(); throw new InvalidDataException("Invalid native theme response.");
    }

    internal IXsrUiTextLayoutProvider ThemeTextLayout(NativeWindowTheme theme)
        => new XsrUiRemoteTextLayoutProvider(new ThemeTextTransport(this, theme));

    internal async Task ReleaseThemeAsync(long handle)
    {
        _themes.TryRemove(handle, out _);
        int delay = 1;
        while (Volatile.Read(ref _nativeRetiring) == 0)
        {
            try { SendControl(WindowCommand.ReleaseTheme, writer => writer.Int64(handle)); return; }
            catch (NativeUiCallBusyException)
            { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
            catch (InvalidOperationException) when (Volatile.Read(ref _nativeRetiring) != 0) { return; }
        }
    }

    private sealed class ThemeTextTransport(NativeWindowSession owner, NativeWindowTheme theme) : IXsrUiWireTransport
    {
        public byte[] Exchange(ReadOnlyMemory<byte> request)
        {
            theme.CheckOwner(owner);
            using var response = owner.ThemeOperation(3, theme.Handle, request);
            byte[] result = response.Buffer(); response.End(); return result;
        }
    }
}

/// <summary>Host-owned lease on native prepared resources; its text provider exchanges bounded messages.</summary>
public sealed class NativeWindowTheme : IDisposable, IAsyncDisposable
{
    private readonly NativeWindowSession _owner;
    private TaskCompletionSource? _release;
    internal long Handle { get; }
    public AvaloniaUiThemeAppearance Appearance { get; }
    public bool IsNativePrepared { get; }
    public long DecodedBitmapBytes { get; }
    public AvaloniaUiThemeStartupSnapshot? StartupSnapshot { get; }
    public IXsrUiTextLayoutProvider TextLayoutProvider { get; }
    public bool IsDisposed => Volatile.Read(ref _release) is not null;

    internal NativeWindowTheme(NativeWindowSession owner, long handle, AvaloniaUiThemeAppearance appearance,
        bool nativePrepared, long decodedBitmapBytes, AvaloniaUiThemeStartupSnapshot? startup)
    {
        _owner = owner; Handle = handle; Appearance = appearance; IsNativePrepared = nativePrepared;
        DecodedBitmapBytes = decodedBitmapBytes; StartupSnapshot = startup; TextLayoutProvider = owner.ThemeTextLayout(this);
    }
    internal void CheckOwner(NativeWindowSession owner)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!ReferenceEquals(_owner, owner)) throw new ArgumentException("The theme belongs to another native session.");
    }
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
        try { await _owner.ReleaseThemeAsync(Handle).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
