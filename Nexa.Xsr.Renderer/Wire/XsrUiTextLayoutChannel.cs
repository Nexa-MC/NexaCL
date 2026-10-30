using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Wire;

/// <summary>Local adapter to a byte-only call gate. Implementations must copy requests before retaining them.</summary>
public interface IXsrUiWireTransport
{
    byte[] Exchange(ReadOnlyMemory<byte> request);
}

/// <summary>Host-side text measurement proxy. The native text provider stays in the window runtime.</summary>
public sealed class XsrUiRemoteTextLayoutProvider(IXsrUiWireTransport transport) : IXsrUiTextLayoutProvider
{
    public long Revision
    {
        get
        {
            using var request = XsrUiTextLayoutChannel.Request(0);
            using var response = XsrUiTextLayoutChannel.Response(transport.Exchange(request.Finish()));
            long result = response.Int64(); response.End(); return result;
        }
    }

    public XsrUiTextLayoutMetrics Measure(XsrUiTextLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var writer = XsrUiTextLayoutChannel.Request(1);
        writer.XsrUiTextLayoutRequest(request);
        using var response = XsrUiTextLayoutChannel.Response(transport.Exchange(writer.Finish()));
        var result = response.XsrUiTextLayoutMetrics() ?? throw new InvalidDataException("Missing UI text metrics.");
        response.End(); return result;
    }
}

/// <summary>Window-side admission. Validate the complete request before calling platform text APIs.</summary>
public sealed class XsrUiTextLayoutEndpoint(IXsrUiTextLayoutProvider provider) : IXsrUiWireTransport
{
    public byte[] Exchange(ReadOnlyMemory<byte> request)
    {
        if (request.Length > UiWireLimits.Frame) throw new InvalidDataException("UI text request exceeds its budget.");
        try
        {
            using var reader = XsrUiTextLayoutChannel.Response(request.ToArray());
            int operation = reader.Int32();
            XsrUiTextLayoutRequest? measure = operation switch
            {
                0 => null,
                1 => reader.XsrUiTextLayoutRequest() ?? throw new InvalidDataException("Missing UI text request."),
                _ => throw new InvalidDataException("Unknown UI text operation."),
            };
            reader.End();
            using var writer = XsrUiTextLayoutChannel.Envelope();
            if (measure is null) writer.Int64(provider.Revision);
            else writer.XsrUiTextLayoutMetrics(provider.Measure(measure));
            return writer.Finish();
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Truncated UI text request.", error); }
    }
}

internal static class XsrUiTextLayoutChannel
{
    private static readonly IReadOnlyDictionary<string, PngImage> EmptyImages = new Dictionary<string, PngImage>();
    internal static UiWireWriter Envelope()
    {
        var writer = new UiWireWriter(EmptyImages);
        writer.Int32(0x3154554e); writer.String(UiWireSchema.Fingerprint);
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
            if (reader.Int32() != 0x3154554e || reader.String() != UiWireSchema.Fingerprint)
                throw new InvalidDataException("UI text schema mismatch.");
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }
}
