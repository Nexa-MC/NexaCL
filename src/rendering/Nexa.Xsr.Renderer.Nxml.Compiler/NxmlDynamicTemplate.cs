namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>External dynamic markup with literal data, never interpolated XML or executable code.</summary>
public sealed class NxmlDynamicTemplate
{
    private readonly NxmlDocument _document;
    public NxmlDynamicTemplate(string source)
        : this(NxmlParser.Parse(source)) { }

    public NxmlDynamicTemplate(NxmlDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (_document.Root.Name != "nxml-dynamic" || _document.Root.Attributes.Count != 0 || _document.Root.Children.Count != 1)
            throw new NxmlCompileException("A dynamic template requires one root inside nxml-dynamic.");
    }

    public NxmlHostIr Compile(IReadOnlyDictionary<string, string> data, NxmlComponentLibrary components, NssStyleSheet? styles = null)
        => CompileCore(data, components, styles, null);

    /// <summary>Localizes format literals before inserting data; parameter names must be preserved.</summary>
    public NxmlHostIr CompileLocalized(IReadOnlyDictionary<string, string> data, NxmlComponentLibrary components,
        Func<string, string> localize, NssStyleSheet? styles = null)
    {
        ArgumentNullException.ThrowIfNull(localize);
        return CompileCore(data, components, styles, localize);
    }

    /// <summary>Compiles one extension template against only the session values it references.</summary>
    public NxmlHostIr CompileScoped(IReadOnlyDictionary<string, string> data, NxmlComponentLibrary components, NssStyleSheet? styles = null)
        => CompileCore(data, components, styles, null, scoped: true);

    private NxmlHostIr CompileCore(IReadOnlyDictionary<string, string> data, NxmlComponentLibrary components,
        NssStyleSheet? styles, Func<string, string>? localize, bool scoped = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        var expanded = components.Expand(new NxmlDocument(_document.Root.Children[0]));
        if (scoped)
        {
            Dictionary<string, string> selected = new(StringComparer.Ordinal);
            Select(expanded.Root);
            data = selected;
            void Select(NxmlElement node)
            {
                foreach (var attribute in node.Attributes)
                {
                    if (attribute.Value.Kind == NxmlValueKind.DataBinding)
                        Add(attribute.Name == "IsVisible" ? attribute.Value.Text.TrimStart('!') : attribute.Value.Text);
                    else if (attribute.Value.Kind == NxmlValueKind.DataText)
                        foreach (var part in NxmlTextFormat.Parse(attribute.Value.Text))
                            if (part.Parameter) Add(part.Value);
                }
                foreach (var child in node.Children) Select(child);
            }
            void Add(string name)
            {
                if (data.TryGetValue(name, out var value)) selected[name] = value;
            }
        }
        if (data.Count > 256 || data.Sum(pair => (long)pair.Value.Length) > 1024 * 1024)
            throw new NxmlCompileException("Dynamic data exceeds its budget.");
        long remaining = 1024 * 1024;
        var document = new NxmlDocument(Bind(expanded.Root, data, localize, ref remaining));
        var compiled = NxmlCompiler.Compile(document, components, styles);
        return new(compiled.Root) { IsDynamic = true };
    }

    private static NxmlElement Bind(NxmlElement node, IReadOnlyDictionary<string, string> data, Func<string, string>? localize, ref long remaining)
    {
        List<NxmlProperty> attributes = [];
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Value.Kind == NxmlValueKind.DataText)
            {
                if (attribute.Name is not ("Content" or "Label" or "Placeholder"))
                    throw new NxmlCompileException("Mixed dynamic data cannot change non-text properties.");
                attributes.Add(new(attribute.Name, NxmlValue.Literal(NxmlTextFormat.Expand(attribute.Value.Text, data, localize, ref remaining))));
                continue;
            }
            if (attribute.Value.Kind != NxmlValueKind.DataBinding) { attributes.Add(attribute); continue; }
            if (attribute.Name is "Recipe" or "Role" or "Kind" or "AcceptsChildren" or "Required" or "Layout")
                throw new NxmlCompileException("Dynamic data cannot change primitive metadata.");
            bool inverted = attribute.Name == "IsVisible" && attribute.Value.Text.StartsWith('!');
            string parameter = inverted ? attribute.Value.Text[1..] : attribute.Value.Text;
            if (!data.TryGetValue(parameter, out string? value))
                throw new NxmlCompileException($"Missing dynamic parameter '{attribute.Value.Text}'.");
            if (inverted)
            {
                if (!bool.TryParse(value, out bool visible)) throw new NxmlCompileException("Inverse visibility requires boolean data.");
                value = visible ? "false" : "true";
            }
            remaining -= value.Length;
            if (remaining < 0) throw new NxmlCompileException("Expanded dynamic data exceeds its budget.");
            attributes.Add(new(attribute.Name, NxmlValue.Literal(value)));
        }
        List<NxmlElement> children = [];
        foreach (var child in node.Children) children.Add(Bind(child, data, localize, ref remaining));
        return new(node.Name, attributes.AsReadOnly(), children.AsReadOnly()) { DefaultProperties = node.DefaultProperties };
    }
}
