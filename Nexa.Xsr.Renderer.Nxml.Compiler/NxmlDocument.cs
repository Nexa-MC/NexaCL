namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>
/// The NXML authoring namespace. Documents that declare a namespace must use exactly this one.
/// </summary>
public static class NxmlWellKnown
{
    public const string Namespace = "https://nexa.dev/nxml/2026";
}

/// <summary>
/// Classifies one attribute value: a literal, or a state binding path wrapped in `{state ...}`.
/// </summary>
public enum NxmlValueKind
{
    Literal = 0,
    StateBinding = 1,
    DataBinding = 2,
}

/// <summary>
/// One attribute value. <see cref="Text"/> carries the literal text or the binding path.
/// </summary>
public readonly record struct NxmlValue(NxmlValueKind Kind, string Text)
{
    public static NxmlValue Literal(string text) => new(NxmlValueKind.Literal, text);

    public static NxmlValue StateBinding(string path) => new(NxmlValueKind.StateBinding, path);
}

/// <summary>
/// One attribute of a NXML element. Attribute names are unique per element.
/// </summary>
public sealed record NxmlProperty(string Name, NxmlValue Value);

/// <summary>
/// One element of a NXML document. The DOM is structural: element and attribute semantics are
/// resolved later by the compiler.
/// </summary>
public sealed class NxmlElement
{
    public NxmlElement(string name, IReadOnlyList<NxmlProperty> attributes, IReadOnlyList<NxmlElement> children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(children);

        Name = name;
        Attributes = attributes;
        Children = children;
    }

    internal IReadOnlySet<string>? DefaultProperties { get; init; }

    public string Name { get; }

    public IReadOnlyList<NxmlProperty> Attributes { get; }

    public IReadOnlyList<NxmlElement> Children { get; }

    public NxmlValue? FindProperty(string name)
    {
        foreach (NxmlProperty attribute in Attributes)
        {
            if (attribute.Name == name)
            {
                return attribute.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// One parsed NXML document with exactly one root element.
/// </summary>
public sealed class NxmlDocument(NxmlElement root)
{
    public NxmlElement Root { get; } = root ?? throw new ArgumentNullException(nameof(root));
}
