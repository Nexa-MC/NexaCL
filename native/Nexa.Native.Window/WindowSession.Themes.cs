using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;
using UiWireReader = Nexa.Native.Window.Wire.UiWireReader;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private readonly NativeUiResources<AvaloniaUiPreparedThemeAppearance> _themes = new(8, 512L * 1048576);
    private NativeUiResources<AvaloniaUiPreparedThemeAppearance>.Borrow? _activeTheme;
    private long _activeThemeHandle;

    private async Task<byte[]> PrepareThemeAsync(UiWireReader reader, CancellationToken token)
    {
        byte[] bytes = reader.Buffer(); reader.End();
        var appearance = WindowValueChannel.DecodeTheme(bytes);
        byte[] result = [];
        await NativeContext.RunAsync(async () =>
        {
            var prepared = await _actions.PrepareThemeAppearanceAsync(appearance, token);
            long handle = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                handle = _themes.Register(prepared, checked(bytes.LongLength + prepared.DecodedBitmapBytes));
                result = WindowThemeResult.Encode(new(handle, prepared.IsNativePrepared, prepared.DecodedBitmapBytes, prepared.StartupSnapshot));
            }
            catch
            {
                if (handle == 0) prepared.Dispose(); else _themes.RejectProvisional(handle);
                throw;
            }
        }, token).ConfigureAwait(false);
        return result;
    }

    private byte[] ExchangeTheme(ReadOnlyMemory<byte> request)
    {
        using var reader = WindowControlProtocol.Read(request, out int command);
        if (command != (int)WindowCommand.Theme) throw new InvalidDataException("Invalid native theme request.");
        int operation = reader.Int32(); long value = reader.Int64();
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Theme);
        switch (operation)
        {
            case 0: // Commit: hold ownership through the complete active generation.
                reader.End();
                var next = _themes.Acquire(value);
                try { _actions.CommitThemeAppearance(next.Resource); }
                catch { next.Dispose(); throw; }
                long previous = _activeThemeHandle;
                var old = _activeTheme; _activeTheme = next; _activeThemeHandle = value;
                old?.Dispose(); writer.Int64(previous); break;
            case 1: // Clear at the same frame boundary as renderer styles.
                reader.End(); _actions.ClearThemeAppearance(value);
                long cleared = _activeThemeHandle; _activeThemeHandle = 0;
                _activeTheme?.Dispose(); _activeTheme = null;
                writer.Int64(cleared); break;
            case 2:
                reader.End(); _actions.FlushRetiredThemeAppearanceResources(value); break;
            case 3:
                byte[] measurement = reader.Buffer(); reader.End();
                using (var borrowed = _themes.Acquire(value))
                {
                    var provider = borrowed.Resource.TextLayout ?? throw new InvalidOperationException("Theme text layout is unavailable.");
                    writer.Buffer(new XsrUiTextLayoutEndpoint(provider).Exchange(measurement));
                }
                break;
            default: throw new InvalidDataException("Unknown native theme operation.");
        }
        return writer.Finish();
    }
}
