using System.Text;
#if NEXA_NATIVE_ENGINE
using Nexa.Native.Markup;
#else
using Nexa.Xsr.Renderer.Nxml;
#endif

namespace Nexa.Native.MarkupWire;

internal static class MarkupProtocol
{
    internal const int InputLimit = 4 * 1024 * 1024, OutputLimit = 16 * 1024 * 1024;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Utf8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
    }
    internal static string Text(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > InputLimit || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid NXML field length.");
        return Utf8.GetString(reader.ReadBytes(length));
    }
    internal static void WriteNode(BinaryWriter writer, NxmlElement node)
    {
        Text(writer, node.Name); writer.Write(node.Attributes.Count);
        foreach (var property in node.Attributes)
        {
            Text(writer, property.Name); writer.Write((int)property.Value.Kind); Text(writer, property.Value.Text);
        }
        writer.Write(node.Children.Count);
        foreach (var child in node.Children) WriteNode(writer, child);
    }
    internal static NxmlElement ReadNode(BinaryReader reader, ref int remaining, int depth = 0)
    {
        if (depth >= 128 || --remaining < 0) throw new InvalidDataException("NXML response exceeds its structural budget.");
        string name = Text(reader);
        int attributes = Count(reader, 128);
        var properties = new NxmlProperty[attributes];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < attributes; i++)
        {
            string key = Text(reader); int kind = reader.ReadInt32();
            if (kind is < 0 or > 3 || !names.Add(key)) throw new InvalidDataException("Invalid NXML property response.");
            properties[i] = new(key, new((NxmlValueKind)kind, Text(reader)));
        }
        int children = Count(reader, remaining);
        var nodes = new NxmlElement[children];
        for (int i = 0; i < children; i++) nodes[i] = ReadNode(reader, ref remaining, depth + 1);
        return new(name, properties, nodes);
    }
    private static int Count(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid NXML collection length.");
        return count;
    }
}
