using System.Globalization;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Immutable, prevalidated NSS/1 program. Matching never parses source or accesses resources.</summary>
public sealed class NssThemeStyleProgram : IXsrUiThemeStyleProgram
{
    private readonly ThemeRule[] _rules;
    internal NssThemeStyleProgram(long revision, ThemeRule[] rules, IReadOnlyList<NssThemeResourceReference> resources)
    { Revision = revision; _rules = rules; Resources = resources; }
    public long Revision { get; }
    public int RuleCount => _rules.Length;
    public IReadOnlyList<NssThemeResourceReference> Resources { get; }
    public XsrUiComputedStyle Compute(XsrUiThemeStyleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        XsrUiComputedStyle computed = XsrUiComputedStyle.Empty;
        foreach (var rule in _rules)
            if (rule.Conditions.All(condition => condition.Matches(context)) && rule.Selector.Matches(context))
                foreach (var declaration in rule.Declarations) computed = declaration.Apply(computed);
        return computed;
    }
}

internal sealed record ThemeRule(ThemeSelector Selector, IReadOnlyList<ThemeDeclaration> Declarations, IReadOnlyList<ThemeCondition> Conditions);
internal sealed record ThemeDeclaration(string Name, object Value)
{
    public static ThemeDeclaration Compile(string name, string value,
        IReadOnlyDictionary<string, IReadOnlyDictionary<XsrUiStyleAnimationProperty, IReadOnlyList<XsrUiStyleKeyframe>>> keyframes)
    {
        object parsed = name switch
        {
            "Background" or "Foreground" or "Border" or "Hover" => NssStyleSheet.Color(value),
            "Opacity" => NssThemeCompiler.Number(value, 0, 1),
            "FontWeight" => NssThemeCompiler.Number(value, 100, 900),
            "FontSize" => NssThemeCompiler.Number(value, 1, 512),
            "CornerRadius" or "BorderWidth" or "BlurRadius" => NssThemeCompiler.Number(value, 0, 4096),
            "Width" or "Height" or "MinWidth" or "MaxWidth" or "MinHeight" or "MaxHeight" or "Weight" or "Spacing" or "ColumnGap" or "RowGap" => NssThemeCompiler.Number(value),
            "TranslateX" or "TranslateY" or "Rotation" => NssThemeCompiler.Number(value, -100000),
            "ScaleX" or "ScaleY" => NssThemeCompiler.Number(value, .01, 100),
            "Left" or "Top" or "Right" or "Bottom" => NssThemeCompiler.Number(value, -100000),
            "Margin" => Thickness(value, true),
            "Padding" => Thickness(value, false),
            "HorizontalAlignment" or "VerticalAlignment" => FiniteEnum<XsrUiAlignment>(value),
            "Orientation" or "Direction" => FiniteEnum<XsrUiOrientation>(value),
            "Surface" => FiniteEnum<XsrUiSurfaceKind>(value),
            "TextAlignment" => FiniteEnum<XsrUiTextAlignment>(value),
            "Position" => FiniteEnum<XsrUiThemePosition>(value),
            "WrapText" => value is "true" or "false" ? value == "true" : throw NssThemeCompiler.Error("Invalid boolean value."),
            "Order" => Integer(value, -32768, 32767),
            "GridColumn" => Integer(value, 0, 63),
            "GridRow" => Integer(value, 0, 4095),
            "GridColumnSpan" => Integer(value, 1, 64),
            "GridRowSpan" => Integer(value, 1, 256),
            "GridColumns" => Tracks(value, 64),
            "GridRows" => Tracks(value, 256),
            "Transform" => Transform(value),
            "Shadow" => Shadow(value),
            "ItemHeight" or "ItemWidth" => NssThemeCompiler.Number(value, 1),
            "ItemSpacing" => NssThemeCompiler.Number(value),
            "ItemColumns" => Integer(value, 1, 64),
            "BackgroundGradient" => Gradient(value),
            "FontFamily" => FontFamily(value),
            "BackgroundImage" => new XsrUiBackgroundImage(Resource(value)),
            "ImageSource" or "IconSource" => Resource(value),
            "BackgroundImageFit" => value is "cover" or "contain" or "stretch" or "tile" ? value : throw NssThemeCompiler.Error("Invalid background image fit."),
            "BackgroundImageOpacity" => NssThemeCompiler.Number(value, 0, 1),
            "Animation" => Animations(value, keyframes),
            _ => throw NssThemeCompiler.Error($"Unsupported or protected property '{name}'."),
        };
        return new(name, parsed);
    }

    public XsrUiComputedStyle Apply(XsrUiComputedStyle style) => Name switch
    {
        "Width" => style with { Width = (double)Value },
        "Height" => style with { Height = (double)Value },
        "MinWidth" => style with { MinWidth = (double)Value },
        "MaxWidth" => style with { MaxWidth = (double)Value },
        "MinHeight" => style with { MinHeight = (double)Value },
        "MaxHeight" => style with { MaxHeight = (double)Value },
        "Weight" => style with { Weight = (double)Value },
        "Spacing" => style with { Spacing = (double)Value },
        "Margin" => style with { Margin = (XsrUiThickness)Value },
        "Padding" => style with { Padding = (XsrUiThickness)Value },
        "HorizontalAlignment" => style with { HorizontalAlignment = (XsrUiAlignment)Value },
        "VerticalAlignment" => style with { VerticalAlignment = (XsrUiAlignment)Value },
        "Orientation" or "Direction" => style with { Direction = (XsrUiOrientation)Value },
        "Background" => style with { Background = (XsrUiColor)Value },
        "Foreground" => style with { Foreground = (XsrUiColor)Value },
        "Border" => style with { Border = (XsrUiColor)Value },
        "Hover" => style with { Hover = (XsrUiColor)Value },
        "Surface" => style with { Surface = (XsrUiSurfaceKind)Value },
        "Opacity" => style with { Opacity = (double)Value },
        "CornerRadius" => style with { CornerRadius = (double)Value },
        "BorderWidth" => style with { BorderWidth = (double)Value },
        "BlurRadius" => style with { BlurRadius = (double)Value },
        "FontSize" => style with { FontSize = (double)Value },
        "FontWeight" => style with { FontWeight = (double)Value },
        "FontFamily" => style with { FontFamily = (string)Value },
        "TextAlignment" => style with { TextAlignment = (XsrUiTextAlignment)Value },
        "WrapText" => style with { WrapText = (bool)Value },
        "Order" => style with { Order = (int)Value },
        "Position" => style with { Position = (XsrUiThemePosition)Value },
        "Left" => style with { Left = (double)Value },
        "Top" => style with { Top = (double)Value },
        "Right" => style with { Right = (double)Value },
        "Bottom" => style with { Bottom = (double)Value },
        "GridColumn" => style with { GridColumn = (int)Value },
        "GridRow" => style with { GridRow = (int)Value },
        "GridColumnSpan" => style with { GridColumnSpan = (int)Value },
        "GridRowSpan" => style with { GridRowSpan = (int)Value },
        "GridColumns" => style with { Grid = (style.Grid ?? new XsrUiGridLayout([new(XsrUiGridUnit.Auto)])) with { Columns = (IReadOnlyList<XsrUiGridTrack>)Value } },
        "GridRows" => style with { Grid = (style.Grid ?? new XsrUiGridLayout([new(XsrUiGridUnit.Auto)])) with { Rows = (IReadOnlyList<XsrUiGridTrack>)Value } },
        "ColumnGap" => style with { Grid = (style.Grid ?? new XsrUiGridLayout([new(XsrUiGridUnit.Auto)])) with { ColumnGap = (double)Value } },
        "RowGap" => style with { Grid = (style.Grid ?? new XsrUiGridLayout([new(XsrUiGridUnit.Auto)])) with { RowGap = (double)Value } },
        "TranslateX" => style with { Transform = (style.Transform ?? new XsrUiStyleTransform(0, 0, 1, 1, 0)) with { TranslateX = (double)Value } },
        "TranslateY" => style with { Transform = (style.Transform ?? new XsrUiStyleTransform(0, 0, 1, 1, 0)) with { TranslateY = (double)Value } },
        "ScaleX" => style with { Transform = (style.Transform ?? new XsrUiStyleTransform(0, 0, 1, 1, 0)) with { ScaleX = (double)Value } },
        "ScaleY" => style with { Transform = (style.Transform ?? new XsrUiStyleTransform(0, 0, 1, 1, 0)) with { ScaleY = (double)Value } },
        "Rotation" => style with { Transform = (style.Transform ?? new XsrUiStyleTransform(0, 0, 1, 1, 0)) with { Rotation = (double)Value } },
        "Transform" => style with { Transform = (XsrUiStyleTransform)Value },
        "Shadow" => style with { Shadow = (XsrUiShadow)Value },
        "Animation" => style with { Animations = (IReadOnlyList<XsrUiStyleAnimation>)Value },
        "BackgroundImage" => style with { BackgroundImage = (XsrUiBackgroundImage)Value with { Fit = style.BackgroundImage?.Fit ?? "cover", Opacity = style.BackgroundImage?.Opacity ?? 1 } },
        "BackgroundImageFit" => style with { BackgroundImage = (style.BackgroundImage ?? new XsrUiBackgroundImage("")) with { Fit = (string)Value } },
        "BackgroundImageOpacity" => style with { BackgroundImage = (style.BackgroundImage ?? new XsrUiBackgroundImage("")) with { Opacity = (double)Value } },
        "ImageSource" or "IconSource" => style with { ImageSource = (string)Value },
        "BackgroundGradient" => style with { BackgroundGradient = (XsrUiLinearGradient)Value },
        "ItemHeight" => style with { VirtualItemHeight = (double)Value },
        "ItemWidth" => style with { VirtualItemWidth = (double)Value },
        "ItemSpacing" => style with { VirtualItemSpacing = (double)Value },
        "ItemColumns" => style with { VirtualItemColumns = (int)Value },
        _ => throw new InvalidOperationException("A compiled declaration has no presentation mapping."),
    };

    private static T FiniteEnum<T>(string value) where T : struct, Enum
    {
        // CSS spellings are accepted without admitting numeric enum values.
        foreach (T candidate in Enum.GetValues<T>())
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase)) return candidate;
        throw NssThemeCompiler.Error("Invalid finite enum value.");
    }
    private static int Integer(string value, int minimum, int maximum) => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) && number >= minimum && number <= maximum
        ? number : throw NssThemeCompiler.Error("Invalid bounded integer value.");
    private static XsrUiThickness Thickness(string value, bool signed)
    {
        bool legacyOrder = value.Contains(',');
        string[] parts = legacyOrder ? NssThemeCompiler.Split(value, ',') : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (1 or 2 or 4)) throw NssThemeCompiler.Error("Thickness requires one, two or four values.");
        double[] numbers = parts.Select(part => NssThemeCompiler.Number(part, signed ? -100000 : 0)).ToArray();
        return parts.Length switch
        {
            1 => new(numbers[0], numbers[0], numbers[0], numbers[0]),
            2 => new(numbers[1], numbers[0], numbers[1], numbers[0]),
            _ => legacyOrder ? new(numbers[0], numbers[1], numbers[2], numbers[3]) : new(numbers[3], numbers[0], numbers[1], numbers[2])
        };
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<XsrUiGridTrack> Tracks(string value, int maximum)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Length > maximum) throw NssThemeCompiler.Error("Grid track budget exceeded.");
        return Array.AsReadOnly(parts.Select(part => part == "auto" ? new XsrUiGridTrack(XsrUiGridUnit.Auto)
            : part.EndsWith("fr", StringComparison.Ordinal) ? new(XsrUiGridUnit.Fraction, NssThemeCompiler.Number(part[..^2], .001, 10000))
            : new(XsrUiGridUnit.Pixels, NssThemeCompiler.Number(part))).ToArray());
    }
    private static XsrUiStyleTransform Transform(string value)
    {
        XsrUiStyleTransform result = new(0, 0, 1, 1, 0); int position = 0, functions = 0; HashSet<string> seen = new(StringComparer.Ordinal);
        while (position < value.Length)
        {
            while (position < value.Length && char.IsWhiteSpace(value[position])) position++;
            if (position == value.Length) break;
            int open = value.IndexOf('(', position), close = open < 0 ? -1 : value.IndexOf(')', open);
            if (open < 0 || close < 0 || ++functions > 3) throw NssThemeCompiler.Error("Invalid finite transform.");
            string name = value[position..open].Trim();
            if (!seen.Add(name)) throw NssThemeCompiler.Error("Duplicate transform operation.");
            string[] parts = NssThemeCompiler.Split(value[(open + 1)..close], ',');
            if (parts.Length is < 1 or > 2) throw NssThemeCompiler.Error("Invalid transform arguments.");
            if (name == "translate") result = result with { TranslateX = NssThemeCompiler.Number(parts[0], -100000), TranslateY = parts.Length == 2 ? NssThemeCompiler.Number(parts[1], -100000) : 0 };
            else if (name == "scale") result = result with { ScaleX = NssThemeCompiler.Number(parts[0], .01, 100), ScaleY = NssThemeCompiler.Number(parts[^1], .01, 100) };
            else if (name == "rotate" && parts.Length == 1) result = result with { Rotation = NssThemeCompiler.Number(parts[0], -36000, 36000) };
            else throw NssThemeCompiler.Error("Unsupported transform operation.");
            position = close + 1;
        }
        if (functions == 0) throw NssThemeCompiler.Error("Empty transform.");
        return result;
    }
    private static XsrUiShadow Shadow(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) throw NssThemeCompiler.Error("Shadow requires color, X, Y and blur.");
        return new(NssStyleSheet.Color(parts[0]), NssThemeCompiler.Number(parts[1], -100000), NssThemeCompiler.Number(parts[2], -100000), NssThemeCompiler.Number(parts[3], 0, 4096));
    }
    private static XsrUiLinearGradient Gradient(string value)
    {
        if (!NssThemeCompiler.Call(value, "linear-gradient", out string arguments)) throw NssThemeCompiler.Error("Gradient requires linear-gradient().");
        string[] parts = NssThemeCompiler.Split(arguments, ',');
        if (parts.Length is < 3 or > 17) throw NssThemeCompiler.Error("Gradient requires angle and 2..16 stops.");
        string angleText = parts[0].EndsWith("deg", StringComparison.Ordinal) ? parts[0][..^3] : parts[0];
        double angle = NssThemeCompiler.Number(angleText, -36000, 36000);
        List<XsrUiGradientStop> stops = [];
        for (int i = 1; i < parts.Length; i++)
        {
            string[] stop = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (stop.Length is not (1 or 2)) throw NssThemeCompiler.Error("Invalid gradient stop.");
            double position = stop.Length == 1 ? (double)(i - 1) / (parts.Length - 2)
                : stop[1].EndsWith('%') ? NssThemeCompiler.Number(stop[1][..^1], 0, 100) / 100 : NssThemeCompiler.Number(stop[1], 0, 1);
            if (stops.Count != 0 && position < stops[^1].Position) throw NssThemeCompiler.Error("Gradient stops must be ordered.");
            stops.Add(new(position, NssStyleSheet.Color(stop[0])));
        }
        return new(angle, Array.AsReadOnly(stops.ToArray()));
    }

    private static string FontFamily(string value)
    {
        string literal = NssThemeCompiler.UnquoteText(value);
        if (literal.Length is 0 or > 256 || literal.Any(char.IsControl) || literal.Contains(';') || literal.Contains('{')) throw NssThemeCompiler.Error("Invalid font identity.");
        // Resource expressions are admitted only by the earlier expression resolver.
        if (value == literal && literal.Contains('(') && !(NssThemeCompiler.Call(literal, "asset", out _) || NssThemeCompiler.Call(literal, "file", out _) || NssThemeCompiler.Call(literal, "folder", out _)))
            throw NssThemeCompiler.Error("Unsupported font expression.");
        return literal;
    }
    private static string Resource(string value)
    {
        value = NssThemeCompiler.Unquote(value);
        if (value.Length is 0 or > 4096 || value.Any(char.IsControl) || value.Contains(';') || value.Contains('{')) throw NssThemeCompiler.Error("Invalid prepared resource identity.");
        if (value.Contains('(') && !(NssThemeCompiler.Call(value, "asset", out _) || NssThemeCompiler.Call(value, "file", out _) || NssThemeCompiler.Call(value, "folder", out _)))
            throw NssThemeCompiler.Error("Unsupported resource expression.");
        return value;
    }
    private static IReadOnlyList<XsrUiStyleAnimation> Animations(string value,
        IReadOnlyDictionary<string, IReadOnlyDictionary<XsrUiStyleAnimationProperty, IReadOnlyList<XsrUiStyleKeyframe>>> keyframes)
    {
        if (value == "none") return Array.Empty<XsrUiStyleAnimation>();
        List<XsrUiStyleAnimation> result = [];
        foreach (string animation in NssThemeCompiler.Split(value, ','))
        {
            string[] parts = animation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 2 or > 4 || !keyframes.TryGetValue(parts[0], out var frames)) throw NssThemeCompiler.Error("Unknown animation or invalid shorthand.");
            string duration = parts[1];
            double milliseconds = duration.EndsWith("ms", StringComparison.Ordinal) ? NssThemeCompiler.Number(duration[..^2], 1, 600000)
                : duration.EndsWith('s') ? NssThemeCompiler.Number(duration[..^1], .001, 600) * 1000 : throw NssThemeCompiler.Error("Animation duration requires s or ms.");
            bool repeat = false, alternate = false;
            foreach (string modifier in parts.Skip(2))
                if (modifier == "infinite" && !repeat) repeat = true;
                else if (modifier == "alternate" && !alternate) alternate = true;
                else throw NssThemeCompiler.Error("Unsupported animation modifier.");
            foreach (var frame in frames) result.Add(new(frame.Key, TimeSpan.FromMilliseconds(milliseconds), frame.Value, repeat, alternate));
            if (result.Count > 16) throw NssThemeCompiler.Error("Animation count budget exceeded.");
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
