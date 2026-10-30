using System.Text;
#if NEXA_NATIVE_ENGINE
namespace Nexa.Native.Markup.Xml;
#else
namespace Nexa.Core.Xml;
#endif

public readonly record struct XmlSnapshotName(string LocalName, string NamespaceName)
{
    public string Namespace => NamespaceName;
    public override string ToString() => NamespaceName.Length == 0 ? LocalName : "{" + NamespaceName + "}" + LocalName;
}
public sealed record XmlSnapshotProperty(XmlSnapshotName Name, string Value, bool IsNamespaceDeclaration);
public abstract class XmlSnapshotNode
{
    public XmlSnapshotElement? Parent { get; internal set; }
    public abstract string Value { get; }
}
public sealed class XmlSnapshotText(string value) : XmlSnapshotNode
{
    public override string Value { get; } = value;
}
public sealed class XmlSnapshotProcessingInstruction : XmlSnapshotNode
{
    public override string Value => string.Empty;
}
public sealed class XmlSnapshotElement : XmlSnapshotNode
{
    private readonly XmlSnapshotProperty[] _attributes;
    private readonly XmlSnapshotNode[] _nodes;
    internal XmlSnapshotElement(XmlSnapshotName name, XmlSnapshotProperty[] attributes, XmlSnapshotNode[] nodes)
    {
        Name = name; _attributes = attributes; _nodes = nodes;
        foreach (var node in nodes) node.Parent = this;
    }
    public XmlSnapshotName Name { get; }
    public bool HasElements => _nodes.Any(node => node is XmlSnapshotElement);
    public IEnumerable<XmlSnapshotProperty> Attributes() => _attributes;
    public XmlSnapshotProperty? Attribute(string name) => _attributes.FirstOrDefault(a => a.Name.NamespaceName.Length == 0 && a.Name.LocalName == name);
    public IEnumerable<XmlSnapshotNode> Nodes() => _nodes;
    public IEnumerable<XmlSnapshotElement> Elements() => _nodes.OfType<XmlSnapshotElement>();
    public IEnumerable<XmlSnapshotElement> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent) yield return node;
    }
    public IEnumerable<XmlSnapshotElement> AncestorsAndSelf()
    {
        yield return this; foreach (var ancestor in Ancestors()) yield return ancestor;
    }
    public IEnumerable<XmlSnapshotElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Elements()) foreach (var node in child.DescendantsAndSelf()) yield return node;
    }
    public override string Value
    {
        get
        {
            var result = new StringBuilder();
            Append(this);
            return result.ToString();
            void Append(XmlSnapshotElement element)
            {
                foreach (var node in element._nodes)
                    if (node is XmlSnapshotElement child) Append(child);
                    else if (node is XmlSnapshotText text) result.Append(text.Value);
            }
        }
    }
}
public sealed class XmlSnapshotDocument
{
    private readonly XmlSnapshotNode[] _nodes;
    internal XmlSnapshotDocument(XmlSnapshotNode[] nodes) => _nodes = nodes;
    public XmlSnapshotElement? Root => _nodes.OfType<XmlSnapshotElement>().FirstOrDefault();
    public IEnumerable<XmlSnapshotNode> DescendantNodes()
    {
        foreach (var node in _nodes)
        {
            yield return node;
            if (node is XmlSnapshotElement element)
                foreach (var child in Descendants(element)) yield return child;
        }
        static IEnumerable<XmlSnapshotNode> Descendants(XmlSnapshotElement element)
        {
            foreach (var node in element.Nodes())
            {
                yield return node;
                if (node is XmlSnapshotElement child)
                    foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
    public IEnumerable<XmlSnapshotElement> Descendants() => DescendantNodes().OfType<XmlSnapshotElement>();
    public IEnumerable<XmlSnapshotElement> Descendants(string name) => DescendantNodes().OfType<XmlSnapshotElement>()
        .Where(element => element.Name.NamespaceName.Length == 0 && element.Name.LocalName == name);
    internal XmlSnapshotNode[] AllNodes => _nodes;
}
public readonly record struct XmlSnapshotLimits(int MaximumCharacters, int MaximumElements = 65536, int MaximumDepth = 128)
{
    internal void Validate()
    {
        if (MaximumCharacters is < 1 or > 16 * 1024 * 1024 || MaximumElements is < 1 or > 65536 || MaximumDepth is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(XmlSnapshotLimits));
    }
}
