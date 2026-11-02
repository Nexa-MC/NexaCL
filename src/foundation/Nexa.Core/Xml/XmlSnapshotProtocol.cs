using System.Text;
#if NEXA_NATIVE_ENGINE
namespace Nexa.Native.Markup.Xml;
#else
namespace Nexa.Core.Xml;
#endif

internal static class XmlSnapshotProtocol
{
    internal const int InputLimit = 64 * 1024 * 1024 + 32, OutputLimit = 128 * 1024 * 1024;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Utf8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
    }
    internal static string Text(BinaryReader reader)
    {
        int size = reader.ReadInt32();
        if (size < 0 || size > InputLimit || size > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid XML string length.");
        return Utf8.GetString(reader.ReadBytes(size));
    }
    internal static void WriteNodes(BinaryWriter writer, IReadOnlyList<XmlSnapshotNode> nodes)
    {
        writer.Write(nodes.Count);
        foreach (var node in nodes)
        {
            if (node is XmlSnapshotElement element)
            {
                writer.Write((byte)1); Text(writer, element.Name.LocalName); Text(writer, element.Name.NamespaceName);
                var attributes = element.Attributes().ToArray(); writer.Write(attributes.Length);
                foreach (var attribute in attributes)
                {
                    Text(writer, attribute.Name.LocalName); Text(writer, attribute.Name.NamespaceName);
                    Text(writer, attribute.Value); writer.Write(attribute.IsNamespaceDeclaration);
                }
                WriteNodes(writer, element.Nodes().ToArray());
            }
            else if (node is XmlSnapshotText text) { writer.Write((byte)2); Text(writer, text.Value); }
            else if (node is XmlSnapshotProcessingInstruction) writer.Write((byte)3);
            else throw new InvalidDataException("Unsupported XML node.");
        }
    }
    internal static XmlSnapshotNode[] ReadNodes(BinaryReader reader, XmlSnapshotLimits limits, ref int elements, ref int nodes, int depth = 0)
    {
        int count = Count(reader, nodes);
        var result = new XmlSnapshotNode[count];
        for (int i = 0; i < count; i++)
        {
            if (--nodes < 0) throw new InvalidDataException("XML node budget exceeded.");
            switch (reader.ReadByte())
            {
                case 1:
                    if (depth >= limits.MaximumDepth || --elements < 0) throw new InvalidDataException("XML element budget exceeded.");
                    var name = new XmlSnapshotName(Text(reader), Text(reader));
                    var attributes = new XmlSnapshotProperty[Count(reader, 4096)];
                    var names = new HashSet<XmlSnapshotName>();
                    for (int a = 0; a < attributes.Length; a++)
                    {
                        var key = new XmlSnapshotName(Text(reader), Text(reader));
                        if (!names.Add(key)) throw new InvalidDataException("Duplicate XML attribute.");
                        attributes[a] = new(key, Text(reader), reader.ReadBoolean());
                    }
                    result[i] = new XmlSnapshotElement(name, attributes, ReadNodes(reader, limits, ref elements, ref nodes, depth + 1));
                    break;
                case 2: result[i] = new XmlSnapshotText(Text(reader)); break;
                case 3: result[i] = new XmlSnapshotProcessingInstruction(); break;
                default: throw new InvalidDataException("Invalid XML node kind.");
            }
        }
        return result;
    }
    private static int Count(BinaryReader reader, int max)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > max || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid XML collection count.");
        return count;
    }
    internal sealed class Output : MemoryStream
    {
        private void Check(int length) { if (length < 0 || Position > OutputLimit - (long)length) throw new InvalidDataException("XML result budget exceeded."); }
        public override void Write(ReadOnlySpan<byte> bytes) { Check(bytes.Length); base.Write(bytes); }
        public override void Write(byte[] bytes, int offset, int count) { Check(count); base.Write(bytes, offset, count); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }
}
