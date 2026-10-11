using System.Collections.ObjectModel;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Immutable external component bundle. Expanded before any UI tree mutation.</summary>
public sealed class NxmlComponentLibrary
{
    private const int MaxComponents = 256;
    private const int MaxExpandedNodes = 16384;
    private const int MaxDepth = 128;
    private readonly ReadOnlyDictionary<string, NxmlElement> _components;
    private static readonly Lazy<NxmlComponentLibrary> Shared = new(() => LoadDirectory(
        Path.Combine(AppContext.BaseDirectory, "Ui", "Components")));

    public static NxmlComponentLibrary Default => Shared.Value;
    public IReadOnlyList<string> Names { get; }

    public NxmlComponentLibrary(IReadOnlyDictionary<string, string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count > MaxComponents) throw new NxmlCompileException("Too many NXML components.");
        Dictionary<string, NxmlElement> components = new(StringComparer.Ordinal);
        long characters = 0;
        foreach ((string name, string source) in sources)
        {
            if (!IsName(name) || name is "Node" or "nxml-component" or "nxml-page")
                throw new NxmlCompileException($"Invalid component name '{name}'.");
            characters += source.Length;
            if (characters > 8 * 1024 * 1024) throw new NxmlCompileException("NXML bundle exceeds its budget.");
            NxmlElement root = NxmlParser.Parse(source).Root;
            if (root.Name != "nxml-component" || root.Children.Count != 1 || root.Attributes.Count != 0)
                throw new NxmlCompileException($"Component '{name}' must contain one root inside nxml-component.");
            components.Add(name, root.Children[0]);
        }
        _components = new ReadOnlyDictionary<string, NxmlElement>(components);
        Names = Array.AsReadOnly(components.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public static NxmlComponentLibrary LoadDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"NXML component directory is missing: {directory}");
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*.c.nxml").Order(StringComparer.Ordinal))
        {
            if (sources.Count >= MaxComponents) throw new NxmlCompileException("Too many NXML components.");
            sources.Add(Path.GetFileName(file)[..^7], ReadFile(file));
        }
        if (sources.Count == 0) throw new NxmlCompileException("NXML component directory is empty.");
        return new(sources);
    }

    public static string ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024) throw new NxmlCompileException("NXML asset exceeds its file budget.");
        using var reader = new StreamReader(stream);
        var buffer = new char[1024 * 1024 + 1];
        int length = 0, read;
        while (length < buffer.Length && (read = reader.Read(buffer, length, buffer.Length - length)) > 0) length += read;
        if (length > 1024 * 1024) throw new NxmlCompileException("NXML asset exceeds its character budget.");
        return new string(buffer, 0, length);
    }

    public NxmlDocument Expand(NxmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        NxmlElement root = document.Root;
        if (root.Name is "nxml-page" or "nxml-component")
        {
            if (root.Attributes.Count != 0 || root.Children.Count != 1)
                throw new NxmlCompileException("An NXML document must contain one visual root.");
            root = root.Children[0];
        }
        int remaining = MaxExpandedNodes;
        return new(Expand(root, [], 0, ref remaining));
    }

    private NxmlElement Expand(NxmlElement source, HashSet<string> path, int depth, ref int remaining)
    {
        if (depth >= MaxDepth || --remaining < 0) throw new NxmlCompileException("NXML expansion budget exceeded.");
        if (source.Name == "Node")
        {
            List<NxmlElement> children = [];
            foreach (NxmlElement child in source.Children) children.Add(Expand(child, path, depth + 1, ref remaining));
            return new("Node", source.Attributes, children.AsReadOnly()) { DefaultProperties = source.DefaultProperties };
        }
        if (!_components.TryGetValue(source.Name, out NxmlElement? template))
            throw new NxmlCompileException($"Unknown NXML component '{source.Name}'.");
        if (!path.Add(source.Name)) throw new NxmlCompileException($"Circular NXML component reference '{source.Name}'.");
        NxmlElement expanded = Expand(template, path, depth + 1, ref remaining);
        path.Remove(source.Name);
        // Caller-owned children are not part of the definition's cycle graph.
        var attributes = expanded.Attributes.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (NxmlProperty attribute in source.Attributes)
        {
            if (attribute.Name is "Recipe" or "Role" or "Kind" or "AcceptsChildren" or "Required")
                throw new NxmlCompileException($"Component '{source.Name}' cannot override primitive metadata '{attribute.Name}'.");
            attributes[attribute.Name] = attribute;
        }
        List<NxmlElement> appended = [.. expanded.Children];
        foreach (NxmlElement child in source.Children) appended.Add(Expand(child, path, depth + 1, ref remaining));
        var explicitNames = source.Attributes.Select(attribute => attribute.Name).ToHashSet(StringComparer.Ordinal);
        return new("Node", attributes.Values.ToArray(), appended.AsReadOnly())
        {
            DefaultProperties = expanded.Attributes.Select(attribute => attribute.Name).Where(name => !explicitNames.Contains(name)).ToHashSet(StringComparer.Ordinal),
        };
    }

    private static bool IsName(string name) => name.Length is > 0 and <= 128
        && char.IsAsciiLetter(name[0])
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
