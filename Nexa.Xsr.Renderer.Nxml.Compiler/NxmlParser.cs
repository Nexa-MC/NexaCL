using System.Globalization;
using System.Xml;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>
/// Reports one deterministic NXML parse failure. The message carries the failing construct;
/// no exception details leak beyond the grammar rule.
/// </summary>
public sealed class NxmlParseException(string message) : InvalidOperationException(message)
{
}

/// <summary>
/// Parses NXML authoring text into the structural DOM. The parser enforces grammar only:
/// exactly one root, the NXML namespace, unique attribute names, attribute-only content (no
/// mixed text), and the `{state path}` binding grammar. Element and attribute semantics are
/// the compiler's job.
/// </summary>
public static class NxmlParser
{
    private const int MaximumCharacters = 1024 * 1024;
    public static NxmlDocument Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new NxmlParseException("The NXML document is empty.");
        }
        if (text.Length > MaximumCharacters) throw new NxmlParseException("The NXML document exceeds the character budget.");

        XmlDocument document = new()
        {
            XmlResolver = null,
        };
        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumCharacters,
            };
            using (var probeSource = new StringReader(text))
            using (var probe = XmlReader.Create(probeSource, settings))
            {
                int nodes = 0;
                int elements = 0;
                while (probe.Read())
                {
                    if (++nodes > 65536
                        || (probe.NodeType == XmlNodeType.Element && (probe.Depth >= 128 || ++elements > 16384 || probe.AttributeCount > 128)))
                        throw new NxmlParseException("The NXML document exceeds the structural budget.");
                }
            }
            using StringReader source = new(text);
            using XmlReader reader = XmlReader.Create(source, settings);
            document.Load(reader);
        }
        catch (XmlException)
        {
            throw new NxmlParseException("The NXML document is not well-formed XML or uses a prohibited DTD.");
        }

        XmlElement root = document.DocumentElement
            ?? throw new NxmlParseException("The NXML document has no root element.");
        ValidateNamespace(root);
        return new NxmlDocument(ReadElement(root));
    }

    private static void ValidateNamespace(XmlElement element)
    {
        string namespaceUri = element.NamespaceURI;
        if (!string.IsNullOrEmpty(namespaceUri) && namespaceUri != NxmlWellKnown.Namespace)
        {
            throw new NxmlParseException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The element '{element.LocalName}' declares namespace '{namespaceUri}' but NXML requires '{NxmlWellKnown.Namespace}'."));
        }

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
            {
                ValidateNamespace(childElement);
            }
        }
    }

    private static NxmlElement ReadElement(XmlElement element)
    {
        string name = element.LocalName;
        List<NxmlProperty> attributes = ReadAttributes(element);
        List<NxmlElement> children = ReadChildren(element);
        return new NxmlElement(name, attributes, children);
    }

    private static List<NxmlProperty> ReadAttributes(XmlElement element)
    {
        List<NxmlProperty> attributes = [];
        HashSet<string> seen = [];
        foreach (XmlAttribute attribute in element.Attributes)
        {
            if (attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/")
            {
                continue;
            }

            if (!string.IsNullOrEmpty(attribute.NamespaceURI) || !string.IsNullOrEmpty(attribute.Prefix))
            {
                throw new NxmlParseException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The attribute '{attribute.Name}' on '{element.LocalName}' uses a namespace; NXML properties must be unqualified."));
            }

            if (!seen.Add(attribute.LocalName))
            {
                throw new NxmlParseException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The element '{element.LocalName}' declares attribute '{attribute.LocalName}' twice."));
            }

            attributes.Add(new NxmlProperty(attribute.LocalName, ReadValue(element.LocalName, attribute.LocalName, attribute.Value)));
        }

        return attributes;
    }

    private static List<NxmlElement> ReadChildren(XmlElement element)
    {
        List<NxmlElement> children = [];
        foreach (XmlNode node in element.ChildNodes)
        {
            switch (node)
            {
                case XmlElement child:
                    children.Add(ReadElement(child));
                    break;
                case XmlText or XmlCDataSection or XmlSignificantWhitespace:
                    throw new NxmlParseException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The element '{element.LocalName}' carries text content; NXML elements are attribute-only."));
                case XmlComment or XmlWhitespace:
                    break;
                default:
                    throw new NxmlParseException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The element '{element.LocalName}' contains unsupported XML node '{node.NodeType}'."));
            }
        }

        return children;
    }

    private static NxmlValue ReadValue(string elementName, string attributeName, string raw)
    {
        const string dataPrefix = "{data ";
        if (raw.StartsWith(dataPrefix, StringComparison.Ordinal) && raw.EndsWith('}') && raw.IndexOf('}') == raw.Length - 1)
        {
            string name = raw[dataPrefix.Length..^1];
            if (name.Length is 0 or > 128 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
                throw new NxmlParseException("A data binding needs a simple parameter name.");
            return new(NxmlValueKind.DataBinding, name);
        }
        if (raw.Contains(dataPrefix, StringComparison.Ordinal))
        {
            if (attributeName is not ("Content" or "Label" or "Placeholder"))
                throw new NxmlParseException("Mixed dynamic text is only supported on text properties.");
            NxmlTextFormat.Parse(raw);
            return new(NxmlValueKind.DataText, raw);
        }
        const string statePrefix = "{state ";
        if (!raw.StartsWith(statePrefix, StringComparison.Ordinal))
        {
            if (raw.Contains('{', StringComparison.Ordinal) || raw.Contains('}', StringComparison.Ordinal))
            {
                throw new NxmlParseException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The attribute '{attributeName}' of '{elementName}' contains braces but is not a {{state ...}} binding."));
            }

            return NxmlValue.Literal(raw);
        }

        if (!raw.EndsWith('}'))
        {
            throw new NxmlParseException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The state binding on '{elementName}.{attributeName}' is not closed."));
        }

        string path = raw[statePrefix.Length..^1];
        if (path.Length == 0 || path.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new NxmlParseException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The state binding on '{elementName}.{attributeName}' needs one non-empty path without whitespace."));
        }

        return NxmlValue.StateBinding(path);
    }
}
