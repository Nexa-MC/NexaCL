#if NEXA_NATIVE_ENGINE
namespace Nexa.Native.Markup.Xml;
#else
namespace Nexa.Core.Xml;
#endif

/// <summary>Bounded immutable XML data; DTD and external resolution are always prohibited.</summary>
public static class XmlSnapshotReader
{
    public static XmlSnapshotDocument ParseBytes(ReadOnlySpan<byte> bytes, XmlSnapshotLimits limits)
    {
        limits.Validate();
#if NEXA_EXTERNAL_MODULES
        return NativeXmlSnapshotClient.Parse(bytes, false, limits);
#else
        return XmlSnapshotParser.ParseBytes(bytes, limits);
#endif
    }
    public static XmlSnapshotDocument ParseText(string text, XmlSnapshotLimits limits)
    {
        ArgumentNullException.ThrowIfNull(text); limits.Validate();
        if (text.Length > limits.MaximumCharacters) throw new System.Xml.XmlException("XML exceeds its character budget.");
#if NEXA_EXTERNAL_MODULES
        byte[] bytes;
        try { bytes = XmlSnapshotProtocol.Utf8.GetBytes(text); }
        catch (System.Text.EncoderFallbackException) { throw new System.Xml.XmlException("XML contains invalid Unicode."); }
        return NativeXmlSnapshotClient.Parse(bytes, true, limits);
#else
        return XmlSnapshotParser.ParseText(text, limits);
#endif
    }
}
