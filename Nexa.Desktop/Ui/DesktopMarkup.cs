using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

/// <summary>Product presentation assets are independent files beside the executable.</summary>
internal static class DesktopMarkup
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NxmlHostIr> StaticFragments = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NxmlDynamicTemplate> DynamicTemplates = new(StringComparer.Ordinal);
    internal static NxmlHostIr Dynamic(string name, IReadOnlyDictionary<string, string>? data = null)
    {
        if (!name.EndsWith(".d.nxml", StringComparison.Ordinal)) throw new ArgumentException("Expected a .d.nxml fragment.", nameof(name));
        var template = DynamicTemplates.GetOrAdd(name, static asset => new NxmlDynamicTemplate(Read(asset)));
        return data is null || data.Count == 0
            ? StaticFragments.GetOrAdd(name, _ => template.Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default, Styles.Value))
            : template.Compile(data, NxmlComponentLibrary.Default, Styles.Value);
    }
    private static readonly Lazy<NssStyleSheet> Styles = new(LoadStyles);
    private static NssStyleSheet LoadStyles()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Ui", "Styles");
        string[] paths = Directory.GetFiles(directory, "*.nss");
        if (paths.Length is 0 or > 128) throw new NxmlCompileException("Invalid product stylesheet bundle.");
        var source = new System.Text.StringBuilder();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            source.AppendLine(NxmlComponentLibrary.ReadFile(path));
            if (source.Length > 1024 * 1024) throw new NxmlCompileException("Product stylesheet budget exceeded.");
        }
        return NssStyleSheet.Parse(source.ToString());
    }
    internal static NxmlHostIr Compile(NxmlDocument document) => NxmlCompiler.Compile(document, NxmlComponentLibrary.Default, Styles.Value);
    internal static Nexa.Xsr.Renderer.XsrUiVisualStyle Visual(string className) => Styles.Value.CreateVisual(className);
    internal static Stream Open(string name) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Read(name)), writable: false);
    internal static NxmlHostIr Load(string name) => Compile(NxmlParser.Parse(Read(name)));
    internal static string Read(string name)
    {
        if (name.StartsWith("Ui.", StringComparison.Ordinal)) name = name[3..];
        if (Path.GetFileName(name) != name || name.Contains(':', StringComparison.Ordinal)
            || !name.EndsWith(".nxml", StringComparison.Ordinal))
            throw new ArgumentException("Expected an NXML asset name.", nameof(name));
        return NxmlComponentLibrary.ReadFile(Path.Combine(AppContext.BaseDirectory, "Ui", name.EndsWith(".c.nxml", StringComparison.Ordinal) ? "Components" : name.EndsWith(".d.nxml", StringComparison.Ordinal) ? "Dynamic" : "", name));
    }
}
