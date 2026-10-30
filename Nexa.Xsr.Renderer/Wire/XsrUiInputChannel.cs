using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Wire;

internal static partial class XsrUiInputChannel
{
    private static readonly IReadOnlyDictionary<string, PngImage> EmptyImages = new Dictionary<string, PngImage>();
    internal static UiWireWriter Envelope()
    {
        var writer = new UiWireWriter(EmptyImages);
        writer.Int32(0x3149554e); writer.String(Fingerprint);
        return writer;
    }
    internal static UiWireWriter Request(int operation)
    {
        var writer = Envelope(); writer.Int32(operation); return writer;
    }
    internal static UiWireReader Response(byte[] bytes)
    {
        var reader = new UiWireReader(bytes, EmptyImages);
        try
        {
            if (reader.Int32() != 0x3149554e || reader.String() != Fingerprint)
                throw new InvalidDataException("UI input schema mismatch.");
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }
}
