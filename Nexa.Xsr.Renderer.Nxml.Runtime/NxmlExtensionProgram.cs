using Nexa.Xsr;

namespace Nexa.Xsr.Renderer.Nxml;

public sealed record NxmlPreparedFragment(string Slot, NxmlHostIr Content);
public sealed record NxmlPreparedDocument(NxmlHostIr Root, IReadOnlyList<NxmlPreparedFragment> Fragments)
{
    public IReadOnlyList<XsrSemanticId> Commands => Enumerate(Root.Root)
        .Concat(Fragments.SelectMany(fragment => Enumerate(fragment.Content.Root)))
        .Where(node => node.Command is not null).Select(node => node.Command!.Value).Distinct().ToArray();

    private static IEnumerable<NxmlIrNode> Enumerate(NxmlIrNode root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in Enumerate(child)) yield return descendant;
    }
}

/// <summary>Compiles host and extension files through the same component, style and dynamic pipeline.</summary>
public sealed class NxmlExtensionProgram
{
    private readonly NxmlComponentLibrary _components;
    private readonly NssStyleSheet _styles;
    private readonly NxmlDynamicTemplate _root;
    private readonly (string Slot, NxmlDynamicTemplate Template)[] _fragments;

    public NxmlExtensionProgram(string entryPoint, IReadOnlyDictionary<string, string> files,
        NxmlComponentLibrary? components = null, NssStyleSheet? styles = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!files.TryGetValue(entryPoint, out var entry)) throw new NxmlCompileException("Missing extension entry point.");
        _components = (components ?? NxmlComponentLibrary.Default).WithComponents(files
            .Where(file => file.Key.EndsWith(".c.nxml", StringComparison.Ordinal))
            .ToDictionary(file => Path.GetFileName(file.Key)[..^7], file => file.Value, StringComparer.Ordinal));
        _styles = (styles ?? NssStyleSheet.Default).With(NssStyleSheet.Parse(string.Join('\n', files
            .Where(file => file.Key.EndsWith(".nss", StringComparison.Ordinal)).OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => file.Value))));
        _root = Template(entry);
        _fragments = files.Where(file => file.Key != entryPoint && file.Key.EndsWith(".d.nxml", StringComparison.Ordinal))
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => (Slot(file.Key), new NxmlDynamicTemplate(file.Value))).ToArray();
    }

    public NxmlPreparedDocument Compile(IReadOnlyDictionary<string, string> data)
    {
        var root = _root.Compile(data, _components, _styles);
        var fragments = _fragments.Select(fragment => new NxmlPreparedFragment(fragment.Slot,
            fragment.Template.Compile(data, _components, _styles))).ToArray();
        Dictionary<string, int> slots = new(StringComparer.Ordinal);
        int count = 0;
        Visit(root.Root);
        foreach (var fragment in fragments)
        {
            if (!slots.TryGetValue(fragment.Slot, out int matches) || matches != 1)
                throw new NxmlCompileException("Each dynamic file requires one existing unambiguous local Dynamic slot.");
            Visit(fragment.Content.Root);
        }
        return new(new(root.Root), fragments);

        void Visit(NxmlIrNode node)
        {
            if (++count > 16384) throw new NxmlCompileException("Extension expanded-node budget exceeded.");
            if (node.Recipe == NxmlRuntimeRecipe.DynamicSlot && node.Key is { } key) slots[key] = slots.GetValueOrDefault(key) + 1;
            foreach (var child in node.Children) Visit(child);
        }
    }

    private NxmlDynamicTemplate Template(string source)
    {
        var document = NxmlParser.Parse(source);
        if (document.Root.Name == "nxml-dynamic") return new(document);
        var expanded = _components.Expand(document);
        return new(new NxmlDocument(new NxmlElement("nxml-dynamic", [], [expanded.Root])));
    }

    private static string Slot(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 1 ? parts[^2] : parts[^1][..^7];
    }
}
