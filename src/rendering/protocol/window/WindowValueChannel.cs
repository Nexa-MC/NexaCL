using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window.Wire;

/// <summary>Versioned control-plane values; complete decoding precedes domain admission.</summary>
internal static class WindowValueChannel
{
    private static UiWireWriter Envelope(int kind)
    {
        var writer = new UiWireWriter(new Dictionary<string, Nexa.Core.Media.PngImage>());
        writer.Int32(0x3157564e); writer.Int32(1);
        writer.String(UiWireSchema.Fingerprint); writer.Int32(kind);
        return writer;
    }

    private static UiWireReader Open(byte[] bytes, int kind)
    {
        var reader = new UiWireReader(bytes, new Dictionary<string, Nexa.Core.Media.PngImage>());
        try
        {
            if (reader.Int32() != 0x3157564e || reader.Int32() != 1
                || reader.String() != UiWireSchema.Fingerprint || reader.Int32() != kind)
                throw new InvalidDataException("Incompatible native window value envelope.");
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }

    internal static byte[] EncodeStartup(AvaloniaUiStartupAppearance value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Snapshot();
        using var writer = Envelope(0);
        writer.AvaloniaUiStartupAppearance(value); return writer.Finish();
    }
    internal static AvaloniaUiStartupAppearance DecodeStartup(byte[] bytes)
    {
        using var reader = Open(bytes, 0);
        var value = reader.AvaloniaUiStartupAppearance() ?? throw new InvalidDataException("Missing native window value.");
        reader.End();
        return value.Snapshot();
    }

    internal static byte[] EncodeTheme(AvaloniaUiThemeAppearance value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Snapshot();
        using var writer = Envelope(1);
        writer.AvaloniaUiThemeAppearance(value); return writer.Finish();
    }
    internal static AvaloniaUiThemeAppearance DecodeTheme(byte[] bytes)
    {
        using var reader = Open(bytes, 1);
        var value = reader.AvaloniaUiThemeAppearance() ?? throw new InvalidDataException("Missing native window value.");
        reader.End();
        return value.Snapshot();
    }

    internal static byte[] EncodeDesktopPolicy(AvaloniaUiDesktopPolicy value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = Envelope(2);
        writer.AvaloniaUiDesktopPolicy(value); return writer.Finish();
    }
    internal static AvaloniaUiDesktopPolicy DecodeDesktopPolicy(byte[] bytes)
    {
        using var reader = Open(bytes, 2);
        var value = reader.AvaloniaUiDesktopPolicy() ?? throw new InvalidDataException("Missing native window value.");
        reader.End();
        return value;
    }

    internal static byte[] EncodeMediaState(AvaloniaUiMediaState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = Envelope(3);
        writer.AvaloniaUiMediaState(value); return writer.Finish();
    }
    internal static AvaloniaUiMediaState DecodeMediaState(byte[] bytes)
    {
        using var reader = Open(bytes, 3);
        var value = reader.AvaloniaUiMediaState() ?? throw new InvalidDataException("Missing native window value.");
        reader.End();
        return value;
    }

    internal static byte[] EncodeMediaFrame(AvaloniaUiMediaFrame value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = Envelope(4);
        writer.AvaloniaUiMediaFrame(value); return writer.Finish();
    }
    internal static AvaloniaUiMediaFrame DecodeMediaFrame(byte[] bytes)
    {
        using var reader = Open(bytes, 4);
        var value = reader.AvaloniaUiMediaFrame() ?? throw new InvalidDataException("Missing native window value.");
        reader.End();
        return value;
    }

    internal static byte[] EncodeRuntimeCapture(AvaloniaUiRuntimeCapture value)
    {
        using var writer = Envelope(5);
        writer.AvaloniaUiRuntimeCapture(value); return writer.Finish();
    }
    internal static AvaloniaUiRuntimeCapture DecodeRuntimeCapture(byte[] bytes)
    {
        using var reader = Open(bytes, 5);
        var value = reader.AvaloniaUiRuntimeCapture();
        reader.End();
        return value;
    }

    internal static byte[] EncodeMaterial(AvaloniaUiThemeMaterialPresentation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = Envelope(6);
        writer.AvaloniaUiThemeMaterialPresentation(value); return writer.Finish();
    }
    internal static AvaloniaUiThemeMaterialPresentation DecodeMaterial(byte[] bytes)
    {
        using var reader = Open(bytes, 6);
        var value = reader.AvaloniaUiThemeMaterialPresentation() ?? throw new InvalidDataException("Missing native window value.");
        reader.End();
        return value;
    }

    internal static byte[] EncodeInputKind(AvaloniaUiInputKind value)
    {
        using var writer = Envelope(7);
        writer.AvaloniaUiInputKind(value); return writer.Finish();
    }
    internal static AvaloniaUiInputKind DecodeInputKind(byte[] bytes)
    {
        using var reader = Open(bytes, 7);
        var value = reader.AvaloniaUiInputKind();
        reader.End();
        return value;
    }

    internal static byte[] EncodePickerKind(AvaloniaUiThemePickerKind value)
    {
        using var writer = Envelope(8);
        writer.AvaloniaUiThemePickerKind(value); return writer.Finish();
    }
    internal static AvaloniaUiThemePickerKind DecodePickerKind(byte[] bytes)
    {
        using var reader = Open(bytes, 8);
        var value = reader.AvaloniaUiThemePickerKind();
        reader.End();
        return value;
    }
}
