using System.Globalization;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Bounded, declarative stylesheet. No scripts, imports or runtime reflection.</summary>
public sealed class NssStyleSheet
{
    private sealed record Rule(string Selector, int Specificity, IReadOnlyList<NxmlProperty> Properties);
    private readonly Rule[] _rules;
    // Positions in _rules, by what a selector can match. An element matches a handful of the
    // product's rules; testing every rule built two strings per rule for every element compiled.
    private readonly Dictionary<string, int[]> _bySelector, _byClass, _byKey;
    private static readonly Lazy<NssStyleSheet> Shared = new(() => Parse(NxmlComponentLibrary.ReadFile(
        Path.Combine(AppContext.BaseDirectory, "Ui", "Styles", "Renderer.nss"))));
    public static NssStyleSheet Default => Shared.Value;
    public static NssStyleSheet Empty { get; } = new([]);
    private NssStyleSheet(Rule[] rules)
    {
        _rules = rules;
        Dictionary<string, List<int>> selectors = new(StringComparer.Ordinal), classes = new(StringComparer.Ordinal), keys = new(StringComparer.Ordinal);
        for (int index = 0; index < rules.Length; index++)
        {
            string selector = rules[index].Selector;
            Add(selectors, selector, index);
            if (selector.StartsWith('.')) Add(classes, selector[1..], index);
            else if (selector.StartsWith('#')) Add(keys, selector[1..], index);
        }
        _bySelector = Freeze(selectors); _byClass = Freeze(classes); _byKey = Freeze(keys);

        static void Add(Dictionary<string, List<int>> index, string name, int position)
        {
            if (!index.TryGetValue(name, out List<int>? positions)) index.Add(name, positions = []);
            positions.Add(position);
        }
        static Dictionary<string, int[]> Freeze(Dictionary<string, List<int>> index)
        {
            Dictionary<string, int[]> frozen = new(index.Count, StringComparer.Ordinal);
            foreach (var (name, positions) in index) frozen.Add(name, [.. positions]);
            return frozen;
        }
    }

    /// <summary>
    /// The rules that apply to one element, as ascending positions: the universal selector, its
    /// type, its key and each of its classes. Ascending positions are specificity order, then
    /// source order, exactly as a scan of every rule would visit them.
    /// </summary>
    private List<int> Match(NxmlElement element)
    {
        string type = element.FindProperty("Kind")?.Text ?? element.Name;
        string? key = element.FindProperty("Key")?.Text;
        List<int> matched = [];
        int sources = 0;
        Take(_bySelector, "*");
        Take(_bySelector, type);
        if (key is not null) Take(_byKey, key);
        string classes = element.FindProperty("Class")?.Text ?? "";
        var byClass = _byClass.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (Range range in classes.AsSpan().Split(' '))
        {
            ReadOnlySpan<char> name = classes.AsSpan(range);
            if (!name.IsEmpty && byClass.TryGetValue(name, out int[]? positions)) { matched.AddRange(positions); sources++; }
        }
        if (sources > 1)
        {
            // One rule can be reached twice (a repeated class, a type spelled like a selector).
            matched.Sort();
            int kept = 0;
            for (int index = 0; index < matched.Count; index++)
                if (kept == 0 || matched[kept - 1] != matched[index]) matched[kept++] = matched[index];
            matched.RemoveRange(kept, matched.Count - kept);
        }
        return matched;

        void Take(Dictionary<string, int[]> index, string name)
        {
            if (!index.TryGetValue(name, out int[]? positions)) return;
            matched.AddRange(positions); sources++;
        }
    }
    public NssStyleSheet With(NssStyleSheet overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return new(_rules.Concat(overlay._rules).OrderBy(rule => rule.Specificity).ToArray());
    }

    public static NssStyleSheet Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > 1024 * 1024) throw new NxmlCompileException("NSS character budget exceeded.");
        List<Rule> rules = [];
        int position = 0;
        while (position < source.Length)
        {
            if (char.IsWhiteSpace(source[position])) { position++; continue; }
            if (source.AsSpan(position).StartsWith("/*", StringComparison.Ordinal))
            {
                int end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (end < 0) throw new NxmlCompileException("Unclosed NSS comment.");
                position = end + 2; continue;
            }
            int open = source.IndexOf('{', position), close = open < 0 ? -1 : source.IndexOf('}', open);
            if (open < 0 || close < 0) throw new NxmlCompileException("Unclosed NSS rule.");
            string selectors = source[position..open];
            List<NxmlProperty> properties = [];
            foreach (string declaration in source[(open + 1)..close].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int colon = declaration.IndexOf(':');
                if (colon <= 0) throw new NxmlCompileException("Invalid NSS declaration.");
                string name = declaration[..colon].Trim(), value = declaration[(colon + 1)..].Trim();
                if ((!IsVisual(name) && !IsLayout(name)) || value.Length == 0 || value.Contains('{', StringComparison.Ordinal))
                    throw new NxmlCompileException($"Unsupported NSS declaration '{name}'.");
                // Validate every visual value, even if its selector currently matches nothing.
                if (IsVisual(name)) SetVisual(new(), name, value);
                else ValidateLayout(name, value);
                properties.Add(new(name, NxmlValue.Literal(value)));
                if (properties.Count > 128) throw new NxmlCompileException("NSS declaration budget exceeded.");
            }
            foreach (string selector in selectors.Split(',', StringSplitOptions.TrimEntries))
            {
                string identifier = selector.TrimStart('.', '#');
                if (selector != "*" && (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
                    throw new NxmlCompileException($"Unsupported NSS selector '{selector}'.");
                rules.Add(new(selector, selector.StartsWith('#') ? 100 : selector.StartsWith('.') ? 10 : selector == "*" ? 0 : 1, properties.AsReadOnly()));
                if (rules.Count > 4096) throw new NxmlCompileException("NSS rule budget exceeded.");
            }
            position = close + 1;
        }
        return new([.. rules.OrderBy(rule => rule.Specificity)]);
    }

    internal IReadOnlyList<NxmlStyleDeclaration> DescribeSource(NxmlElement element)
    {
        List<NxmlStyleDeclaration> result = [];
        foreach (NxmlProperty property in element.Attributes)
            if (element.DefaultProperties?.Contains(property.Name) == true && (IsVisual(property.Name) || IsLayout(property.Name)))
                result.Add(new(property.Name, property.Value.Text, NxmlStyleOrigin.ComponentDefault));
        foreach (int position in Match(element))
            foreach (NxmlProperty property in _rules[position].Properties)
                result.Add(new(property.Name, property.Value.Text, NxmlStyleOrigin.HostStyleSheet));
        foreach (NxmlProperty property in element.Attributes)
            if (element.DefaultProperties?.Contains(property.Name) != true && (IsVisual(property.Name) || IsLayout(property.Name)))
                result.Add(new(property.Name, property.Value.Text, NxmlStyleOrigin.ExplicitMarkup));
        return Array.AsReadOnly(result.ToArray());
    }

    public NxmlElement Apply(NxmlElement element)
    {
        var properties = new Dictionary<string, NxmlProperty>(StringComparer.Ordinal);
        foreach (NxmlProperty property in element.Attributes)
            if (element.DefaultProperties?.Contains(property.Name) == true) properties[property.Name] = property;
        foreach (int position in Match(element))
            foreach (NxmlProperty property in _rules[position].Properties) properties[property.Name] = property;
        foreach (NxmlProperty property in element.Attributes)
            if (element.DefaultProperties?.Contains(property.Name) != true) properties[property.Name] = property;
        return new(element.Name, properties.Values.ToArray(), element.Children);
    }

    public XsrUiVisualStyle CreateVisual(string className)
    {
        var node = Apply(new NxmlElement("Node", [new("Class", NxmlValue.Literal(className))], []));
        XsrUiVisualStyle visual = new();
        foreach (NxmlProperty property in node.Attributes)
            if (IsVisual(property.Name)) SetVisual(visual, property.Name, property.Value.Text);
        return visual;
    }

    internal static bool IsVisual(string name) => name is "Background" or "Foreground" or "Border" or "Hover"
        or "HoverExpand" or "NavigationLayout" or "WrapText" or "Surface" or "Opacity" or "CornerRadius"
        or "BorderWidth" or "BlurRadius" or "FontSize" or "FontWeight" or "TextAlignment";
    internal static bool IsLayout(string name) => name is "Width" or "Height" or "MinWidth" or "MaxWidth" or "MinHeight" or "MaxHeight"
        or "Weight" or "HorizontalAlignment" or "VerticalAlignment" or "Margin" or "Padding" or "Spacing" or "Orientation"
        or "TransitionOffsetX" or "TransitionOffsetY";


    internal static void ValidateLayout(string name, string value)
    {
        if (name is "HorizontalAlignment" or "VerticalAlignment")
        {
            if (!Enum.TryParse(value, out XsrUiAlignment parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != value)
                throw new NxmlCompileException($"Invalid NSS alignment '{value}'.");
            return;
        }
        if (name == "Orientation")
        {
            if (value is not ("Horizontal" or "Vertical")) throw new NxmlCompileException($"Invalid NSS orientation '{value}'.");
            return;
        }
        string[] parts = value.Split(',');
        if (name is "Margin" or "Padding" ? parts.Length is not (1 or 4) : parts.Length != 1)
            throw new NxmlCompileException($"Invalid NSS numeric shape for '{name}'.");
        foreach (string part in parts)
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number)
                || Math.Abs(number) > 100000 || number < 0 && name is not ("Margin" or "TransitionOffsetX" or "TransitionOffsetY"))
                throw new NxmlCompileException($"Invalid NSS layout value '{value}' for '{name}'.");
    }

    internal static void SetVisual(XsrUiVisualStyle visual, string name, string value)
    {
        static double Number(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < 0)
                throw new NxmlCompileException($"Invalid NSS number '{value}'.");
            return result;
        }
        static bool Boolean(string value) => bool.TryParse(value, out bool result) ? result : throw new NxmlCompileException($"Invalid NSS boolean '{value}'.");
        switch (name)
        {
            case "Background": visual.Background = Color(value); break;
            case "Foreground": visual.Foreground = Color(value); break;
            case "Border": visual.Border = Color(value); break;
            case "Hover": visual.Hover = Color(value); break;
            case "HoverExpand": visual.HoverExpand = Boolean(value); break;
            case "NavigationLayout": visual.NavigationLayout = Boolean(value); break;
            case "WrapText": visual.WrapText = Boolean(value); break;
            case "Opacity": visual.Opacity = Number(value); if (visual.Opacity > 1) throw new NxmlCompileException("NSS opacity exceeds 1."); break;
            case "CornerRadius": visual.CornerRadius = Number(value); break;
            case "BorderWidth": visual.BorderWidth = Number(value); break;
            case "BlurRadius": visual.BlurRadius = Number(value); break;
            case "FontSize": visual.FontSize = Number(value); break;
            case "FontWeight": visual.FontWeight = Number(value); break;
            case "Surface":
                if (!Enum.TryParse(value, out XsrUiSurfaceKind surface) || !Enum.IsDefined(surface)) throw new NxmlCompileException("Invalid NSS surface.");
                visual.Surface = surface; break;
            case "TextAlignment":
                if (!Enum.TryParse(value, out XsrUiTextAlignment alignment) || !Enum.IsDefined(alignment)) throw new NxmlCompileException("Invalid NSS text alignment.");
                visual.TextAlignment = alignment; break;
            default: throw new NxmlCompileException($"Unsupported NSS property '{name}'.");
        }
    }

    public static XsrUiColor Color(string value)
    {
        if (value == "transparent") return XsrUiColor.Transparent;
        if (value.Length is not (7 or 9) || value[0] != '#' || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
            throw new NxmlCompileException($"Invalid NSS color '{value}'. Expected #RRGGBB or #RRGGBBAA.");
        return value.Length == 7 ? new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex)
            : new((byte)(hex >> 24), (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
    }
}
