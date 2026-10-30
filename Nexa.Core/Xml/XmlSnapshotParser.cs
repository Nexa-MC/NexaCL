#if !NEXA_EXTERNAL_MODULES
using System.Xml;
#if NEXA_NATIVE_ENGINE
namespace Nexa.Native.Markup.Xml;
#else
namespace Nexa.Core.Xml;
#endif

internal static class XmlSnapshotParser
{
    internal static XmlSnapshotDocument ParseBytes(ReadOnlySpan<byte> bytes, XmlSnapshotLimits limits)
    {
        if (bytes.Length > XmlSnapshotProtocol.InputLimit - 32) throw new InvalidDataException("XML byte budget exceeded.");
        byte[] frozen = bytes.ToArray();
        return Parse(() => XmlReader.Create(new MemoryStream(frozen, false), Settings(limits)), limits);
    }
    internal static XmlSnapshotDocument ParseText(string text, XmlSnapshotLimits limits) =>
        Parse(() => XmlReader.Create(new StringReader(text), Settings(limits)), limits);
    private static XmlReaderSettings Settings(XmlSnapshotLimits limits) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumCharacters,
        IgnoreComments = true, CloseInput = true,
    };
    private static XmlSnapshotDocument Parse(Func<XmlReader> create, XmlSnapshotLimits limits)
    {
        using (var probe = create())
        {
            int count = 0, nodes = 0;
            while (probe.Read())
            {
                if (++nodes > 1_000_000) throw new InvalidDataException("XML node budget exceeded.");
                if (probe.NodeType == XmlNodeType.Element &&
                    (++count > limits.MaximumElements || probe.Depth >= limits.MaximumDepth || probe.AttributeCount > 4096))
                    throw new InvalidDataException("XML structure exceeds its budget.");
            }
        }
        using var reader = create();
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        return new(ReadNodes(document));
    }
    private static XmlSnapshotNode[] ReadNodes(XmlNode parent)
    {
        var nodes = new List<XmlSnapshotNode>();
        foreach (XmlNode node in parent.ChildNodes)
        {
            if (node is XmlElement element)
            {
                var attributes = new XmlSnapshotProperty[element.Attributes.Count];
                for (int i = 0; i < attributes.Length; i++)
                {
                    var attribute = element.Attributes[i]!;
                    attributes[i] = new(new(attribute.LocalName, attribute.NamespaceURI), attribute.Value,
                        attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/");
                }
                nodes.Add(new XmlSnapshotElement(new(element.LocalName, element.NamespaceURI), attributes, ReadNodes(element)));
            }
            else if (node is XmlText or XmlCDataSection or XmlSignificantWhitespace)
                nodes.Add(new XmlSnapshotText(node.Value ?? string.Empty));
            else if (node is XmlProcessingInstruction) nodes.Add(new XmlSnapshotProcessingInstruction());
        }
        return [.. nodes];
    }
}
#endif
