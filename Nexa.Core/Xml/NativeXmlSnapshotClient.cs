#if NEXA_EXTERNAL_MODULES
using Nexa.Native.Interop;

namespace Nexa.Core.Xml;

internal static class NativeXmlSnapshotClient
{
    private static readonly NativeResultClient Client = new("nexa-markup", "nexa_markup_xml", XmlSnapshotProtocol.InputLimit, XmlSnapshotProtocol.OutputLimit);
    internal static XmlSnapshotDocument Parse(ReadOnlySpan<byte> bytes, bool text, XmlSnapshotLimits limits)
    {
        if (bytes.Length > XmlSnapshotProtocol.InputLimit - 32) throw new InvalidDataException("XML byte budget exceeded.");
        using var request = new MemoryStream();
        using (var writer = new BinaryWriter(request, System.Text.Encoding.UTF8, true))
        {
            writer.Write(text); writer.Write(limits.MaximumCharacters); writer.Write(limits.MaximumElements); writer.Write(limits.MaximumDepth);
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        using var reader = new BinaryReader(new MemoryStream(Client.Execute(request.GetBuffer().AsSpan(0, checked((int)request.Length)))));
        if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported XML response version.");
        int status = reader.ReadInt32();
        if (status != 0)
        {
            string message = XmlSnapshotProtocol.Text(reader); End(reader);
            if (status == 1) throw new System.Xml.XmlException(message);
            throw new InvalidDataException(message);
        }
        int elements = limits.MaximumElements, nodes = 1_000_000;
        var result = new XmlSnapshotDocument(XmlSnapshotProtocol.ReadNodes(reader, limits, ref elements, ref nodes));
        End(reader); return result;
    }
    private static void End(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing XML response data.");
    }
}
#endif
