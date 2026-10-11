using System.Globalization;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>
/// Expands external NXML components and NSS rules into typed renderer primitives.
/// Component definitions remain independently shipped assets; no CLR control types are generated.
/// </summary>
public static class NxmlCompiler
{
    public static NxmlHostIr Compile(NxmlDocument document) => Compile(document, NxmlComponentLibrary.Default, NssStyleSheet.Default);

    public static NxmlHostIr Compile(NxmlDocument document, NxmlComponentLibrary components, NssStyleSheet? styles = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(components);
        HashSet<string> keys = new(StringComparer.Ordinal);
        return new NxmlHostIr(CompileNode(components.Expand(document).Root, keys, styles ?? NssStyleSheet.Empty));
    }

    private static NxmlIrNode CompileNode(NxmlElement element, HashSet<string> keys, NssStyleSheet styles)
    {
        element = styles.Apply(element);
        XsrUiVisualStyle? visual = null;
        string Literal(string name, string fallback) => element.FindProperty(name) is { } property
            ? property.Kind == NxmlValueKind.Literal ? property.Text
                : throw Fail(element.Name, name, "must be literal")
            : fallback;
        if (!Enum.TryParse(Literal("Recipe", "Element"), out NxmlRuntimeRecipe recipe) || !Enum.IsDefined(recipe))
            throw Fail(element.Name, "Recipe", "is not a renderer primitive");
        if (!Enum.TryParse(Literal("Role", "None"), out XsrUiSemanticRole role) || !Enum.IsDefined(role))
            throw Fail(element.Name, "Role", "is not a semantic role");
        if (recipe == NxmlRuntimeRecipe.TextInput && element.FindProperty("Content") is not null)
            throw Fail(element.Name, "Content", "cannot seed text-input drafts or secrets from a template");
        bool allowsChildren = ParseBoolean(element.Name, "AcceptsChildren", Literal("AcceptsChildren", "true"));
        if ((!allowsChildren || recipe is NxmlRuntimeRecipe.Text or NxmlRuntimeRecipe.Image or NxmlRuntimeRecipe.Progress or NxmlRuntimeRecipe.TextInput)
            && element.Children.Count != 0)
            throw Fail(element.Name, null, "does not accept child elements");
        if (recipe == NxmlRuntimeRecipe.DynamicSlot && (element.Children.Count != 0 || element.FindProperty("Key") is null))
            throw Fail(element.Name, "Key", "a Dynamic slot must be named and initially empty");
        NxmlRuntimeRecipe slotLayout = NxmlRuntimeRecipe.StackLayout;
        if (element.FindProperty("Layout") is not null)
        {
            if (recipe != NxmlRuntimeRecipe.DynamicSlot) throw Fail(element.Name, "Layout", "requires Dynamic");
            slotLayout = Literal("Layout", "Stack") switch
            {
                "Stack" => NxmlRuntimeRecipe.StackLayout,
                "Overlay" => NxmlRuntimeRecipe.Element,
                "HorizontalPager" => NxmlRuntimeRecipe.HorizontalPager,
                "VerticalPager" => NxmlRuntimeRecipe.VerticalPager,
                _ => throw Fail(element.Name, "Layout", "is not a supported slot layout"),
            };
        }
        int maximumLength = 2048;
        if (element.FindProperty("MaximumLength") is not null
            && (recipe != NxmlRuntimeRecipe.TextInput || !int.TryParse(Literal("MaximumLength", "2048"), NumberStyles.None, CultureInfo.InvariantCulture, out maximumLength)
                || maximumLength is < 1 or > 32768))
            throw Fail(element.Name, "MaximumLength", "requires a text input and a length from 1 to 32768");
        bool preserveTabs = ParseBoolean(element.Name, "PreserveTabs", Literal("PreserveTabs", "false"));
        if (element.FindProperty("PreserveTabs") is not null && recipe != NxmlRuntimeRecipe.TextInput)
            throw Fail(element.Name, "PreserveTabs", "requires a text input");
        int maxLines = 0;
        if (element.FindProperty("MaxLines") is not null
            && (recipe is not (NxmlRuntimeRecipe.Text or NxmlRuntimeRecipe.CommandInput)
                || !int.TryParse(Literal("MaxLines", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out maxLines)
                || maxLines is < 0 or > 4096))
            throw Fail(element.Name, "MaxLines", "requires text and a line count from 0 to 4096");
        bool trimOverflow = ParseBoolean(element.Name, "TrimOverflow", Literal("TrimOverflow", "false"));
        if (element.FindProperty("TrimOverflow") is not null && recipe is not (NxmlRuntimeRecipe.Text or NxmlRuntimeRecipe.CommandInput))
            throw Fail(element.Name, "TrimOverflow", "requires text");
        T? NamedEnum<T>(string name) where T : struct, Enum
        {
            if (element.FindProperty(name) is null) return null;
            string value = Literal(name, "");
            if (!Enum.TryParse(value, out T parsed) || !Enum.IsDefined(parsed) || value != parsed.ToString())
                throw Fail(element.Name, name, "requires a named finite renderer value");
            return parsed;
        }
        var overlayMotion = NamedEnum<XsrUiOverlayMotionKind>("OverlayMotion");
        var liveSetting = NamedEnum<XsrUiLiveSetting>("LiveSetting");
        XsrSemanticId? dismissCommand = element.FindProperty("DismissCommand") is null ? null
            : ParseSemanticId(element.Name, "DismissCommand", Literal("DismissCommand", ""));
        bool showsIndicator = ParseBoolean(element.Name, "ShowsVerticalIndicator", Literal("ShowsVerticalIndicator", "false"));
        bool stickToEnd = ParseBoolean(element.Name, "StickToEnd", Literal("StickToEnd", "false"));
        bool scrollGesture = ParseBoolean(element.Name, "ScrollGesture", Literal("ScrollGesture", "false"));
        if (element.Attributes.Any(attribute => attribute.Name is "ShowsVerticalIndicator" or "StickToEnd" or "ScrollGesture")
            && (recipe is not (NxmlRuntimeRecipe.StackLayout or NxmlRuntimeRecipe.DynamicSlot)
                || recipe == NxmlRuntimeRecipe.DynamicSlot && slotLayout != NxmlRuntimeRecipe.StackLayout
                || !ParseBoolean(element.Name, "Scroll", Literal("Scroll", "false"))))
            throw Fail(element.Name, "Scroll", "is required by scroll presentation facts");
        bool stableContent = ParseBoolean(element.Name, "StableContent", Literal("StableContent", "false"));
        bool? modalLayer = element.FindProperty("ModalLayer") is null ? null : ParseBoolean(element.Name, "ModalLayer", Literal("ModalLayer", "false"));
        if (element.FindProperty("ModalLayer") is not null
            && recipe != NxmlRuntimeRecipe.Element && !(recipe == NxmlRuntimeRecipe.DynamicSlot && slotLayout == NxmlRuntimeRecipe.Element))
            throw Fail(element.Name, "ModalLayer", "requires an overlay element");
        string? anchorKey = element.FindProperty("Anchor") is null ? null : ParseKey(element.Name, "Anchor", Literal("Anchor", ""));
        double anchorGap = ParseDouble(element.Name, "AnchorGap", Literal("AnchorGap", "4"));
        if (element.FindProperty("AnchorGap") is not null && anchorKey is null || anchorGap < 0)
            throw Fail(element.Name, "AnchorGap", "requires an anchor and a nonnegative gap");
        NxmlControlModel model = new(Literal("Kind", "Node"), "Node", role, recipe, allowsChildren, NxmlNodeSchema.Properties);
        foreach (string required in Literal("Required", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (element.FindProperty(required) is null) throw Fail(element.Name, required, "is required by this component");

        NodeBuilder builder = new(model.Kind, model.Recipe, model.Role);
        List<NxmlIrBinding> bindings = [];

        foreach (NxmlControlPropertyModel property in model.Properties)
        {
            if (property.DefaultValue is not null)
            {
                ApplyLiteral(element.Name, property, property.DefaultValue, builder);
            }
        }

        foreach (NxmlProperty attribute in element.Attributes)
        {
            if (attribute.Value.Kind is NxmlValueKind.DataBinding or NxmlValueKind.DataText)
                throw Fail(element.Name, attribute.Name, "requires dynamic template data");
            if (NssStyleSheet.IsVisual(attribute.Name))
            {
                if (attribute.Value.Kind != NxmlValueKind.Literal) throw Fail(element.Name, attribute.Name, "must be literal");
                NssStyleSheet.SetVisual(visual ??= new(), attribute.Name, attribute.Value.Text);
                continue;
            }
            if (attribute.Name is "Recipe" or "Role" or "Kind" or "AcceptsChildren" or "Required" or "Class" or "Layout" or "MaximumLength" or "PreserveTabs" or "MaxLines" or "TrimOverflow" or "OverlayMotion" or "LiveSetting" or "DismissCommand" or "ShowsVerticalIndicator" or "StickToEnd" or "ScrollGesture" or "StableContent" or "ModalLayer" or "Anchor" or "AnchorGap") continue;
            if (!model.TryGetProperty(attribute.Name, out NxmlControlPropertyModel property))
            {
                throw Fail(element.Name, attribute.Name, "is not declared by this control model");
            }

            bool supported = property.Target switch
            {
                NxmlIrPropertyTarget.Command or NxmlIrPropertyTarget.Clickable or NxmlIrPropertyTarget.Focusable => recipe is NxmlRuntimeRecipe.CommandInput or NxmlRuntimeRecipe.Element or NxmlRuntimeRecipe.StackLayout or NxmlRuntimeRecipe.DynamicSlot,
                NxmlIrPropertyTarget.Content => recipe is NxmlRuntimeRecipe.Text or NxmlRuntimeRecipe.CommandInput,
                NxmlIrPropertyTarget.ImageSource => recipe is NxmlRuntimeRecipe.Image or NxmlRuntimeRecipe.CommandInput,
                NxmlIrPropertyTarget.Orientation or NxmlIrPropertyTarget.Spacing or NxmlIrPropertyTarget.StretchLastChild or NxmlIrPropertyTarget.Scrollable => recipe is NxmlRuntimeRecipe.StackLayout or NxmlRuntimeRecipe.DynamicSlot,
                NxmlIrPropertyTarget.Value => recipe == NxmlRuntimeRecipe.Progress,
                NxmlIrPropertyTarget.Placeholder or NxmlIrPropertyTarget.IsPassword => recipe == NxmlRuntimeRecipe.TextInput,
                NxmlIrPropertyTarget.Selected => recipe == NxmlRuntimeRecipe.CommandInput,
                NxmlIrPropertyTarget.Enabled => recipe is NxmlRuntimeRecipe.TextInput or NxmlRuntimeRecipe.CommandInput or NxmlRuntimeRecipe.Element or NxmlRuntimeRecipe.StackLayout or NxmlRuntimeRecipe.DynamicSlot,
                _ => true,
            };
            if (!supported) throw Fail(element.Name, attribute.Name, "is not supported by this renderer primitive");

            if (attribute.Value.Kind == NxmlValueKind.StateBinding)
            {
                if (property.BindingProperty is not { } bindingProperty)
                {
                    throw Fail(element.Name, attribute.Name, "does not support a state binding");
                }

                bindings.Add(new NxmlIrBinding(
                    ParseSemanticId(element.Name, attribute.Name, attribute.Value.Text),
                    bindingProperty,
                    property.BindingDirtyKinds));
                continue;
            }

            ApplyLiteral(element.Name, property, attribute.Value.Text, builder);
        }

        foreach (NxmlControlPropertyModel property in model.Properties)
        {
            if (property.Required && element.FindProperty(property.Name) is null)
            {
                throw Fail(element.Name, property.Name, "is required by this control model");
            }
        }

        if (builder.Key is { } key && !keys.Add(key))
        {
            throw Fail(element.Name, "Key", $"duplicates the document key '{key}'");
        }

        return builder.Build(
            [.. element.Children.Select(child => CompileNode(child, keys, styles))],
            bindings) with
        { Visual = visual?.Snapshot(), SlotLayout = slotLayout, MaximumLength = maximumLength, PreserveTabs = preserveTabs, MaxLines = maxLines, TrimOverflow = trimOverflow, OverlayMotion = overlayMotion, LiveSetting = liveSetting, DismissCommand = dismissCommand, ShowsVerticalIndicator = showsIndicator, StickToEnd = stickToEnd, ScrollGesture = scrollGesture, StableContent = stableContent, ModalLayer = modalLayer, AnchorKey = anchorKey, AnchorGap = anchorGap };
    }

    private static void ApplyLiteral(
        string elementName,
        NxmlControlPropertyModel property,
        string raw,
        NodeBuilder builder)
    {
        switch (property.ValueKind)
        {
            case NxmlControlValueKind.Double:
                builder.Set(property.Target, ParseDouble(elementName, property.Name, raw));
                break;
            case NxmlControlValueKind.Thickness:
                builder.Set(property.Target, ParseThickness(elementName, property.Name, raw));
                break;
            case NxmlControlValueKind.Boolean:
                builder.Set(property.Target, ParseBoolean(elementName, property.Name, raw));
                break;
            case NxmlControlValueKind.Orientation:
                builder.Set(property.Target, ParseOrientation(elementName, property.Name, raw));
                break;
            case NxmlControlValueKind.String:
                builder.Set(
                    property.Target,
                    property.Target == NxmlIrPropertyTarget.Key
                        ? ParseKey(elementName, property.Name, raw)
                        : raw);
                break;
            case NxmlControlValueKind.SemanticId:
                builder.Set(property.Target, ParseSemanticId(elementName, property.Name, raw));
                break;
            case NxmlControlValueKind.Alignment:
                builder.Set(property.Target, ParseAlignment(elementName, property.Name, raw));
                break;
            default:
                throw new InvalidOperationException(
                    $"The generated control catalog uses unsupported value kind '{property.ValueKind}'.");
        }
    }

    private static double ParseDouble(string elementName, string propertyName, string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            || !double.IsFinite(parsed))
        {
            throw Fail(elementName, propertyName, "needs a finite invariant-culture number");
        }

        return parsed;
    }

    private static XsrUiThickness ParseThickness(string elementName, string propertyName, string raw)
    {
        string[] parts = raw.Split(',');
        if (parts.Length == 1)
        {
            double uniform = ParseDouble(elementName, propertyName, parts[0]);
            return new XsrUiThickness(uniform, uniform, uniform, uniform);
        }

        if (parts.Length == 4)
        {
            return new XsrUiThickness(
                ParseDouble(elementName, propertyName, parts[0]),
                ParseDouble(elementName, propertyName, parts[1]),
                ParseDouble(elementName, propertyName, parts[2]),
                ParseDouble(elementName, propertyName, parts[3]));
        }

        throw Fail(elementName, propertyName, "needs one number or four comma-separated numbers");
    }

    private static bool ParseBoolean(string elementName, string propertyName, string raw) => raw switch
    {
        "true" => true,
        "false" => false,
        _ => throw Fail(elementName, propertyName, "needs the literal 'true' or 'false'"),
    };

    private static XsrUiOrientation ParseOrientation(string elementName, string propertyName, string raw) => raw switch
    {
        "Vertical" => XsrUiOrientation.Vertical,
        "Horizontal" => XsrUiOrientation.Horizontal,
        _ => throw Fail(elementName, propertyName, "needs one of Vertical, Horizontal"),
    };

    private static XsrUiAlignment ParseAlignment(string elementName, string propertyName, string raw) => raw switch
    {
        "Stretch" => XsrUiAlignment.Stretch,
        "Start" => XsrUiAlignment.Start,
        "Center" => XsrUiAlignment.Center,
        "End" => XsrUiAlignment.End,
        _ => throw Fail(elementName, propertyName, "needs one of Stretch, Start, Center, End"),
    };

    private static XsrSemanticId ParseSemanticId(string elementName, string propertyName, string raw)
    {
        // Semantic IDs are validated and parsed here, so the IR carries validated IDs and load
        // time only resolves them through the registry.
        try
        {
            return XsrSemanticId.Parse(raw);
        }
        catch (ArgumentException exception)
        {
            throw Fail(elementName, propertyName, $"needs a valid semantic ID ({exception.Message})");
        }
    }

    private static string ParseKey(string elementName, string propertyName, string raw)
    {
        if (raw.Length is 0 or > 128
            || !string.Equals(raw, raw.Trim(), StringComparison.Ordinal)
            || raw.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw Fail(
                elementName,
                propertyName,
                "needs 1..128 non-whitespace, non-control characters without surrounding whitespace");
        }

        return raw;
    }

    private static NxmlCompileException Fail(string element, string? property, string problem) =>
        new(property is null
            ? $"The element '{element}' {problem}."
            : $"The property '{property}' on '{element}' {problem}.");

    private sealed class NodeBuilder(
        string kind,
        NxmlRuntimeRecipe recipe,
        XsrUiSemanticRole role)
    {
        private double? _width;
        private double? _height;
        private double? _minWidth;
        private double? _maxWidth;
        private double? _minHeight;
        private double? _maxHeight;
        private double _weight;
        private XsrUiAlignment _horizontalAlignment;
        private XsrUiAlignment _verticalAlignment;
        private XsrUiThickness _margin;
        private XsrUiThickness _padding;
        private bool _isVisible = true;
        private XsrUiOrientation _orientation;
        private double _spacing;
        private bool _stretchLastChild;
        private bool _scrollable;
        private string? _content;
        private string? _label;
        private string? _key;
        private bool _focusable;
        private bool _clickable = recipe == NxmlRuntimeRecipe.CommandInput;
        private XsrSemanticId? _command;
        private string? _imageSource;
        private string? _placeholder;
        private bool _isPassword;
        private bool _enabled = true;
        private bool _localize = true;
        private bool? _selected;
        private string? _transitionKey;
        private double _transitionOffsetX;
        private double _transitionOffsetY;
        private double _value;

        public string? Key => _key;

        public void Set(NxmlIrPropertyTarget target, object value)
        {
            switch (target)
            {
                case NxmlIrPropertyTarget.Width:
                    _width = (double)value;
                    break;
                case NxmlIrPropertyTarget.Height:
                    _height = (double)value;
                    break;
                case NxmlIrPropertyTarget.MinWidth:
                    _minWidth = (double)value;
                    break;
                case NxmlIrPropertyTarget.MaxWidth:
                    _maxWidth = (double)value;
                    break;
                case NxmlIrPropertyTarget.MinHeight:
                    _minHeight = (double)value;
                    break;
                case NxmlIrPropertyTarget.MaxHeight:
                    _maxHeight = (double)value;
                    break;
                case NxmlIrPropertyTarget.Weight:
                    _weight = (double)value;
                    break;
                case NxmlIrPropertyTarget.HorizontalAlignment:
                    _horizontalAlignment = (XsrUiAlignment)value;
                    break;
                case NxmlIrPropertyTarget.VerticalAlignment:
                    _verticalAlignment = (XsrUiAlignment)value;
                    break;
                case NxmlIrPropertyTarget.Margin:
                    _margin = (XsrUiThickness)value;
                    break;
                case NxmlIrPropertyTarget.Padding:
                    _padding = (XsrUiThickness)value;
                    break;
                case NxmlIrPropertyTarget.Visibility:
                    _isVisible = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Label:
                    _label = (string)value;
                    break;
                case NxmlIrPropertyTarget.Key:
                    _key = (string)value;
                    break;
                case NxmlIrPropertyTarget.Orientation:
                    _orientation = (XsrUiOrientation)value;
                    break;
                case NxmlIrPropertyTarget.Spacing:
                    _spacing = (double)value;
                    break;
                case NxmlIrPropertyTarget.StretchLastChild:
                    _stretchLastChild = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Scrollable:
                    _scrollable = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Content:
                    _content = (string)value;
                    break;
                case NxmlIrPropertyTarget.Command:
                    _command = (XsrSemanticId)value;
                    break;
                case NxmlIrPropertyTarget.Focusable:
                    _focusable = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Clickable:
                    _clickable = (bool)value;
                    break;
                case NxmlIrPropertyTarget.ImageSource:
                    _imageSource = (string)value;
                    break;
                case NxmlIrPropertyTarget.Placeholder:
                    _placeholder = (string)value;
                    break;
                case NxmlIrPropertyTarget.IsPassword:
                    _isPassword = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Localize:
                    _localize = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Selected:
                    _selected = (bool)value;
                    break;
                case NxmlIrPropertyTarget.Enabled:
                    _enabled = (bool)value;
                    break;
                case NxmlIrPropertyTarget.TransitionKey:
                    _transitionKey = (string)value;
                    break;
                case NxmlIrPropertyTarget.TransitionOffsetX:
                    _transitionOffsetX = (double)value;
                    break;
                case NxmlIrPropertyTarget.TransitionOffsetY:
                    _transitionOffsetY = (double)value;
                    break;
                case NxmlIrPropertyTarget.Value:
                    _value = (double)value;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The generated control catalog uses unsupported IR target '{target}'.");
            }
        }

        public NxmlIrNode Build(
            IReadOnlyList<NxmlIrNode> children,
            IReadOnlyList<NxmlIrBinding> bindings) =>
            new()
            {
                Key = _key,
                Kind = kind,
                Recipe = recipe,
                Children = children,
                Width = _width,
                Height = _height,
                MinWidth = _minWidth,
                MaxWidth = _maxWidth,
                MinHeight = _minHeight,
                MaxHeight = _maxHeight,
                Weight = _weight,
                HorizontalAlignment = _horizontalAlignment,
                VerticalAlignment = _verticalAlignment,
                Margin = _margin,
                Padding = _padding,
                IsVisible = _isVisible,
                Orientation = _orientation,
                Spacing = _spacing,
                StretchLastChild = _stretchLastChild,
                Scrollable = _scrollable,
                Content = _content,
                Label = _label,
                Role = role,
                Focusable = _focusable,
                Clickable = _clickable,
                Command = _command,
                ImageSource = _imageSource,
                Placeholder = _placeholder,
                IsPassword = _isPassword,
                Enabled = _enabled,
                Localize = _localize,
                Selected = _selected,
                TransitionKey = _transitionKey,
                TransitionOffsetX = _transitionOffsetX,
                TransitionOffsetY = _transitionOffsetY,
                Value = _value,
                Bindings = bindings,
            };
    }
}
