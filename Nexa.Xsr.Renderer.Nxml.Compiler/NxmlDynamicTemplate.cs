namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>External dynamic markup with literal data, never interpolated XML or executable code.</summary>
public sealed class NxmlDynamicTemplate
{
    private readonly NxmlDocument _document;
    public NxmlDynamicTemplate(string source)
    {
        _document = NxmlParser.Parse(source);
        if (_document.Root.Name != "nxml-dynamic" || _document.Root.Attributes.Count != 0 || _document.Root.Children.Count != 1)
            throw new NxmlCompileException("A dynamic template requires one root inside nxml-dynamic.");
    }

    public NxmlHostIr Compile(IReadOnlyDictionary<string, string> data, NxmlComponentLibrary components, NssStyleSheet? styles = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count > 256 || data.Sum(pair => (long)pair.Value.Length) > 1024 * 1024)
            throw new NxmlCompileException("Dynamic data exceeds its budget.");
        long remaining = 1024 * 1024;
        var document = new NxmlDocument(Bind(_document.Root.Children[0], data, ref remaining));
        var compiled = NxmlCompiler.Compile(document, components, styles);
        return new(compiled.Root) { IsDynamic = true };
    }

    private static NxmlElement Bind(NxmlElement node, IReadOnlyDictionary<string, string> data, ref long remaining)
    {
        List<NxmlProperty> attributes = [];
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Value.Kind != NxmlValueKind.DataBinding) { attributes.Add(attribute); continue; }
            if (attribute.Name is "Recipe" or "Role" or "Kind" or "AcceptsChildren" or "Required" or "Layout")
                throw new NxmlCompileException("Dynamic data cannot change primitive metadata.");
            if (!data.TryGetValue(attribute.Value.Text, out string? value))
                throw new NxmlCompileException($"Missing dynamic parameter '{attribute.Value.Text}'.");
            remaining -= value.Length;
            if (remaining < 0) throw new NxmlCompileException("Expanded dynamic data exceeds its budget.");
            attributes.Add(new(attribute.Name, NxmlValue.Literal(value)));
        }
        List<NxmlElement> children = [];
        foreach (var child in node.Children) children.Add(Bind(child, data, ref remaining));
        return new(node.Name, attributes.AsReadOnly(), children.AsReadOnly());
    }
}
