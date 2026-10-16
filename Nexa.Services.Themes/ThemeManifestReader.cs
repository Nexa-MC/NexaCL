using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexa.Services.Themes;

internal static partial class ThemeManifestReader
{
    internal const int ManifestLimit = 64 * 1024;
    internal static ThemeManifest Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ManifestLimit) throw new InvalidDataException("Theme manifest exceeds 64 KiB.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        Fields(root, "format", "id", "version", "name", "styleApi", "nss", "styles", "requiredCapabilities", "optionalCapabilities", "options", "appearance", "optionsVersion");
        string format = Text(root, "format"), id = Text(root, "id"), version = Text(root, "version"), nss = Text(root, "nss");
        int styleApi = root.GetProperty("styleApi").GetInt32();
        if (format != "NTP/1" || styleApi != 1 || nss != "NSS/1" || !Identifier().IsMatch(id) || !Version().IsMatch(version))
            throw new InvalidDataException("Unsupported theme format, identity, version or style API.");
        var styles = Strings(root.GetProperty("styles"), 64);
        if (styles.Count == 0 || styles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != styles.Count) throw new InvalidDataException("Styles must be a nonempty ordered unique list.");
        foreach (string path in styles) { ThemePaths.Relative(path); if (!path.EndsWith(".nss", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only NSS style files are allowed."); }
        var required = root.TryGetProperty("requiredCapabilities", out var r) ? Strings(r, 128) : [];
        foreach (string capability in required) Capability(capability);
        var optional = new List<ThemeCapability>();
        if (root.TryGetProperty("optionalCapabilities", out var o))
        {
            Array(o, 128);
            foreach (var item in o.EnumerateArray())
            {
                Fields(item, "name", "fallback"); string name = Text(item, "name"); Capability(name);
                optional.Add(new(name, Text(item, "fallback")));
            }
        }
        if (required.Concat(optional.Select(c => c.Name)).Distinct(StringComparer.Ordinal).Count() != required.Count + optional.Count)
            throw new InvalidDataException("Capabilities must be unique.");
        var options = new List<ThemeOptionDefinition>();
        if (root.TryGetProperty("options", out var optionsNode))
        {
            Array(optionsNode, 256);
            foreach (var item in optionsNode.EnumerateArray()) options.Add(Option(item));
        }
        if (options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != options.Count) throw new InvalidDataException("Option IDs must be unique.");
        var optionDefaults = options.ToDictionary(option => option.Id, option => option.Default, StringComparer.Ordinal);
        foreach (var option in options) ThemeOptionCondition.Evaluate(option.VisibleWhen, optionDefaults);
        var resources = new Dictionary<string, ThemeAppearanceResource>(StringComparer.Ordinal);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        string? accent = null, material = null, motion = null;
        if (root.TryGetProperty("appearance", out var appearance))
        {
            Fields(appearance, "resources", "accentColor", "windowMaterial", "splashMotion", "properties");
            accent = Optional(appearance, "accentColor"); material = Optional(appearance, "windowMaterial"); motion = Optional(appearance, "splashMotion");
            if (accent is not null && !Color().IsMatch(accent)) throw new InvalidDataException("Invalid appearance accent color.");
            if (material is not null && material is not ("none" or "solid" or "blur" or "acrylic" or "mica")) throw new InvalidDataException("Unknown window material.");
            if (motion is not null && motion is not ("none" or "fade" or "breathe" or "draw")) throw new InvalidDataException("Unknown splash motion.");
            if (appearance.TryGetProperty("resources", out var resourceNodes))
            {
                Object(resourceNodes); if (resourceNodes.EnumerateObject().Count() > 64) throw new InvalidDataException("Too many appearance resources.");
                foreach (var property in resourceNodes.EnumerateObject())
                {
                    if (property.Name is not ("launcherIcon" or "windowIcon" or "trayIcon" or "splashIcon" or "background" or "music" or "video" or "font" or "logo" or "icon-light" or "icon-dark")) throw new InvalidDataException("Unknown appearance resource slot.");
                    var item = property.Value; Fields(item, "expression", "kind", "required", "fallback");
                    if (!Enum.TryParse<ThemeResourceKind>(Text(item, "kind"), true, out var kind) || !Enum.IsDefined(kind)) throw new InvalidDataException("Unknown resource kind.");
                    bool matchesSlot = property.Name switch
                    {
                        "background" => kind == ThemeResourceKind.Image,
                        "music" => kind == ThemeResourceKind.Audio,
                        "video" => kind == ThemeResourceKind.Video,
                        "font" => kind == ThemeResourceKind.Font,
                        _ => kind is ThemeResourceKind.Icon or ThemeResourceKind.Image
                    };
                    if (!matchesSlot) throw new InvalidDataException("Appearance resource kind does not match its slot.");
                    string expression = Text(item, "expression"); ThemePaths.Expression(expression);
                    bool isRequired = item.TryGetProperty("required", out var req) && req.GetBoolean();
                    string? fallback = Optional(item, "fallback"); if (fallback is not null && fallback != "none") ThemePaths.Expression(fallback);
                    if (!isRequired && fallback is null) throw new InvalidDataException("Optional appearance resources must declare a fallback.");
                    resources.Add(property.Name, new(expression, kind, isRequired, fallback));
                }
            }
            if (appearance.TryGetProperty("properties", out var appearanceProperties))
            {
                Object(appearanceProperties);
                foreach (var property in appearanceProperties.EnumerateObject())
                {
                    if (property.Name is not ("background-fit" or "background-opacity" or "background-color" or "window-opacity" or "window-blur" or "video.enabled" or "video.auto-pause" or "video.fit" or "music.enabled" or "music.startup" or "music.autoplay" or "music.shuffle" or "music.volume" or "music.media-controls" or "music.auto-pause" or "animation-fps" or "resize-policy")) throw new InvalidDataException("Unknown appearance property.");
                    string value = property.Value.ValueKind == JsonValueKind.String ? String(property.Value) : property.Value.GetRawText();
                    if (value.Length > 4096 || !properties.TryAdd(property.Name, value)) throw new InvalidDataException("Invalid appearance property.");
                    if (value.StartsWith("option(", StringComparison.Ordinal))
                    {
                        string optionId = value.EndsWith(')') ? value[7..^1] : "";
                        if (!options.Any(option => option.Id == optionId)) throw new InvalidDataException("Appearance property references an unknown option.");
                    }
                }
            }
        }
        int optionsVersion = root.TryGetProperty("optionsVersion", out var schema) ? schema.GetInt32() : 1;
        if (optionsVersion is < 1 or > 65535) throw new InvalidDataException("Invalid options schema version.");
        var appearanceResult = new ThemeAppearance(new System.Collections.ObjectModel.ReadOnlyDictionary<string, ThemeAppearanceResource>(resources), accent, material, motion, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(properties));
        ValidateAppearance(appearanceResult, optionDefaults);
        return new(format, id, version, Localized(root.GetProperty("name")), styleApi, nss, styles.AsReadOnly(), required.AsReadOnly(), optional.AsReadOnly(), options.AsReadOnly(), appearanceResult, optionsVersion);
    }

    private static ThemeOptionDefinition Option(JsonElement item)
    {
        Fields(item, "id", "kind", "default", "label", "description", "group", "visibleWhen", "min", "max", "step", "choices", "resourceKind", "control");
        string id = Text(item, "id"); if (!Identifier().IsMatch(id)) throw new InvalidDataException("Invalid option ID.");
        if (!Enum.TryParse<ThemeOptionKind>(Text(item, "kind"), true, out var kind) || !Enum.IsDefined(kind)) throw new InvalidDataException("Unknown option kind.");
        ThemeOptionControl control = ThemeOptionControl.Automatic;
        if (item.TryGetProperty("control", out var controlNode)
            && (!Enum.TryParse(String(controlNode), true, out control) || !Enum.IsDefined(control)
                || !String(controlNode).Equals(Enum.GetName(control), StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unknown option control.");
        double? min = Number(item, "min"), max = Number(item, "max"), step = Number(item, "step");
        if (min.HasValue && max.HasValue && min > max || step is <= 0) throw new InvalidDataException("Invalid option numeric limits.");
        if (kind == ThemeOptionKind.Range || kind == ThemeOptionKind.Number && control == ThemeOptionControl.Slider)
        {
            if (kind == ThemeOptionKind.Number && (min is null || max is null)) throw new InvalidDataException("A numeric slider requires explicit finite bounds.");
            min ??= 0; max ??= 100; double span = max.Value - min.Value; step ??= Math.Min(1, span / 100);
            if (min >= max || Math.Abs(min.Value) > 1e12 || Math.Abs(max.Value) > 1e12 || step <= 0 || step > span || span / step > 1e9) throw new InvalidDataException("Range option cannot be projected by the finite slider contract.");
        }
        var choices = new List<ThemeOptionChoice>();
        if (item.TryGetProperty("choices", out var nodes))
        {
            Array(nodes, 256); foreach (var choice in nodes.EnumerateArray()) { Fields(choice, "value", "label"); choices.Add(new(Text(choice, "value"), Localized(choice.GetProperty("label")))); }
        }
        if (choices.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count() != choices.Count || kind == ThemeOptionKind.Select && choices.Count == 0) throw new InvalidDataException("Invalid option choices.");
        bool validControl = control switch
        {
            ThemeOptionControl.Automatic => true,
            ThemeOptionControl.Switch or ThemeOptionControl.CheckBox => kind == ThemeOptionKind.Boolean,
            ThemeOptionControl.Slider => kind is ThemeOptionKind.Number or ThemeOptionKind.Range || kind == ThemeOptionKind.Select && choices.Count >= 2,
            ThemeOptionControl.Segmented => kind is ThemeOptionKind.Boolean or ThemeOptionKind.Select,
            ThemeOptionControl.Input => kind is ThemeOptionKind.Number or ThemeOptionKind.Range or ThemeOptionKind.Text or ThemeOptionKind.Color,
            _ => false,
        };
        if (!validControl) throw new InvalidDataException("Option control does not match its value kind.");
        ThemeResourceKind? resourceKind = null;
        if (item.TryGetProperty("resourceKind", out var resource))
        { if (!Enum.TryParse<ThemeResourceKind>(String(resource), true, out var parsed) || !Enum.IsDefined(parsed)) throw new InvalidDataException("Invalid option resource kind."); resourceKind = parsed; }
        if (kind is ThemeOptionKind.File or ThemeOptionKind.Files or ThemeOptionKind.Directory && resourceKind is null) throw new InvalidDataException("File options require a resource kind.");
        var value = ReadValue(kind, item.GetProperty("default"));
        var result = new ThemeOptionDefinition(id, kind, value, Localized(item.GetProperty("label")), item.TryGetProperty("description", out var description) ? Localized(description) : new Dictionary<string, string>(),
            Optional(item, "group"), Optional(item, "visibleWhen"), min, max, step, choices.AsReadOnly(), resourceKind)
        { Control = control };
        ValidateValue(result, value); return result;
    }

    internal static ThemeOptionValue ReadValue(ThemeOptionKind kind, JsonElement node)
    {
        if (kind == ThemeOptionKind.Files) return new(kind, "", Strings(node, 256).AsReadOnly());
        if (kind == ThemeOptionKind.Boolean && node.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || kind is ThemeOptionKind.Number or ThemeOptionKind.Range && node.ValueKind != JsonValueKind.Number
            || kind is not (ThemeOptionKind.Boolean or ThemeOptionKind.Number or ThemeOptionKind.Range) && node.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Option default JSON type does not match its declared kind.");
        string value = node.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.String => String(node),
            _ => throw new InvalidDataException("Invalid option value type.")
        };
        return new(kind, value);
    }

    internal static void ValidateValue(ThemeOptionDefinition option, ThemeOptionValue value)
    {
        if (value.Kind != option.Kind || value.Value.Length > 4096 || value.Value.Contains('\0') || value.Value.Any(char.IsControl) && option.Kind != ThemeOptionKind.Text || value.Values?.Any(v => v.Length > 4096 || v.Any(char.IsControl)) == true
            || option.Kind != ThemeOptionKind.Files && value.Values is not null)
            throw new InvalidDataException("Option value does not match its schema.");
        switch (option.Kind)
        {
            case ThemeOptionKind.Boolean: if (value.Value is not ("true" or "false")) throw new InvalidDataException("Invalid boolean option."); break;
            case ThemeOptionKind.Number:
            case ThemeOptionKind.Range:
                if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < option.Minimum || number > option.Maximum)
                    throw new InvalidDataException("Numeric option is outside its range.");
                if (option.Step is { } step)
                {
                    double quotient = (number - (option.Minimum ?? 0)) / step;
                    if (!double.IsFinite(quotient) || Math.Abs(quotient - Math.Round(quotient)) > 1e-7) throw new InvalidDataException("Numeric option violates its step.");
                }
                break;
            case ThemeOptionKind.Color: if (!Color().IsMatch(value.Value)) throw new InvalidDataException("Invalid color option."); break;
            case ThemeOptionKind.Select: if (!option.Choices.Any(c => c.Value == value.Value)) throw new InvalidDataException("Invalid select option."); break;
            case ThemeOptionKind.Files: if (value.Value.Length != 0 || value.Values is null || value.Values.Count > 256) throw new InvalidDataException("Invalid files option."); break;
        }
    }

    internal static void ValidateAppearance(ThemeAppearance appearance, IReadOnlyDictionary<string, ThemeOptionValue> values)
    {
        if (appearance.Properties is null) return;
        foreach (var property in appearance.Properties)
        {
            string value = property.Value; ThemeOptionKind? kind = null;
            if (value.StartsWith("option(", StringComparison.Ordinal))
            {
                string id = value.EndsWith(')') ? value[7..^1] : "";
                if (!values.TryGetValue(id, out var option)) throw new InvalidDataException("Unknown appearance option reference.");
                value = option.Value; kind = option.Kind;
            }
            switch (property.Key)
            {
                case "background-opacity":
                case "window-opacity":
                case "music.volume":
                case "animation-fps":
                    double minimum = property.Key == "window-opacity" ? 40 : property.Key == "animation-fps" ? 1 : 0;
                    double maximum = property.Key == "animation-fps" ? 240 : 100;
                    if (kind is not (null or ThemeOptionKind.Number or ThemeOptionKind.Range) || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < minimum || number > maximum) throw new InvalidDataException("Appearance numeric property is outside its range."); break;
                case "background-fit":
                case "video.fit":
                    if (kind is not (null or ThemeOptionKind.Text or ThemeOptionKind.Select) || value is not ("cover" or "contain" or "stretch")) throw new InvalidDataException("Invalid appearance fit property."); break;
                case "background-color":
                    if (kind is not (null or ThemeOptionKind.Color or ThemeOptionKind.Text or ThemeOptionKind.Select) || value != "auto" && !Color().IsMatch(value)) throw new InvalidDataException("Invalid appearance color property."); break;
                case "resize-policy":
                    if (kind is not (null or ThemeOptionKind.Text or ThemeOptionKind.Select) || value is not ("auto" or "immediate" or "deferred" or "animated" or "instant")) throw new InvalidDataException("Invalid appearance resize policy."); break;
                default:
                    if (kind is not (null or ThemeOptionKind.Boolean) || value is not ("true" or "false")) throw new InvalidDataException("Invalid appearance boolean property."); break;
            }
        }
    }

    internal static void Fields(JsonElement node, params string[] allowed)
    {
        Object(node); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject()) if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException("Unknown or duplicate manifest field: " + property.Name);
    }
    internal static void Object(JsonElement node) { if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected an object."); }
    internal static void Array(JsonElement node, int count) { if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() > count) throw new InvalidDataException("Array exceeds its budget."); }
    internal static string String(JsonElement node) { string value = node.GetString() ?? throw new InvalidDataException("Expected a string."); if (value.Length > 4096 || value.Any(c => c == '\0')) throw new InvalidDataException("String exceeds its budget."); return value; }
    internal static string Text(JsonElement node, string key) => String(node.GetProperty(key));
    internal static string? Optional(JsonElement node, string key) => node.TryGetProperty(key, out var value) ? String(value) : null;
    internal static List<string> Strings(JsonElement node, int count) { Array(node, count); return node.EnumerateArray().Select(String).ToList(); }
    private static double? Number(JsonElement node, string key) { if (!node.TryGetProperty(key, out var value)) return null; double number = value.GetDouble(); if (!double.IsFinite(number)) throw new InvalidDataException("Expected a finite number."); return number; }
    private static System.Collections.ObjectModel.ReadOnlyDictionary<string, string> Localized(JsonElement node)
    {
        Object(node); var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in node.EnumerateObject()) { if (map.Count >= 64 || property.Name.Length is < 1 or > 64 || !map.TryAdd(property.Name, String(property.Value))) throw new InvalidDataException("Invalid localized text."); }
        if (map.Count == 0) throw new InvalidDataException("Localized text must not be empty.");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(map);
    }
    private static void Capability(string name) { if (!Identifier().IsMatch(name)) throw new InvalidDataException("Invalid capability name."); }
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex Identifier();
    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+){1,7}(?:\\.[a-z]+\\.[0-9]+)?$", RegexOptions.CultureInvariant)] private static partial Regex Version();
    [GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant)] private static partial Regex Color();
}
