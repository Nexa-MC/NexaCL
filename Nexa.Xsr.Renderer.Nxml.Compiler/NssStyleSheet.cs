using System.Globalization;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Bounded, declarative stylesheet. No scripts, imports or runtime reflection.</summary>
public sealed class NssStyleSheet
{
    private sealed record Rule(string Selector, int Specificity, IReadOnlyList<NxmlProperty> Properties);
    private readonly Rule[] _rules;
    private static readonly Lazy<NssStyleSheet> Shared = new(() => Parse(NxmlComponentLibrary.ReadFile(
        Path.Combine(AppContext.BaseDirectory, "Ui", "Styles", "Renderer.nss"))));
    public static NssStyleSheet Default => Shared.Value;
    public static NssStyleSheet Empty { get; } = new([]);
    private NssStyleSheet(Rule[] rules) => _rules = rules;

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

    public NxmlElement Apply(NxmlElement element)
    {
        var properties = new Dictionary<string, NxmlProperty>(StringComparer.Ordinal);
        foreach (NxmlProperty property in element.Attributes)
            if (element.DefaultProperties?.Contains(property.Name) == true) properties[property.Name] = property;
        string type = element.FindProperty("Kind")?.Text ?? element.Name;
        string? key = element.FindProperty("Key")?.Text;
        string[] classes = (element.FindProperty("Class")?.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (Rule rule in _rules)
            if (rule.Selector == "*" || rule.Selector == type || rule.Selector == "#" + key
                || rule.Selector.StartsWith('.') && classes.Contains(rule.Selector[1..], StringComparer.Ordinal))
                foreach (NxmlProperty property in rule.Properties) properties[property.Name] = property;
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
    private static bool IsLayout(string name) => name is "Width" or "Height" or "MinWidth" or "MaxWidth" or "MinHeight" or "MaxHeight"
        or "Weight" or "HorizontalAlignment" or "VerticalAlignment" or "Margin" or "Padding" or "Spacing" or "Orientation"
        or "TransitionOffsetX" or "TransitionOffsetY";

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
