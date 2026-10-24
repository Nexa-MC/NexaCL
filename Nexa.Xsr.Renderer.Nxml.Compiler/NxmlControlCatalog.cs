using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>
/// The complete NXML-visible control names expanded from the configured build-time directory.
/// </summary>
public static class NxmlControlCatalog
{
    public static IReadOnlyList<string> Names => NxmlComponentLibrary.Default.Names;
}

internal enum NxmlControlValueKind
{
    Double = 1,
    Thickness = 2,
    Boolean = 3,
    Orientation = 4,
    String = 5,
    SemanticId = 6,
    Alignment = 7,
}

internal enum NxmlIrPropertyTarget
{
    Width = 1,
    Height = 2,
    Margin = 3,
    Padding = 4,
    Visibility = 5,
    Label = 6,
    Orientation = 7,
    Spacing = 8,
    Scrollable = 9,
    Content = 10,
    Command = 11,
    Focusable = 12,
    Clickable = 13,
    ImageSource = 14,
    StretchLastChild = 15,
    MinWidth = 16,
    MaxWidth = 17,
    MinHeight = 18,
    MaxHeight = 19,
    Weight = 20,
    HorizontalAlignment = 21,
    VerticalAlignment = 22,
    Key = 23,
    Placeholder = 24,
    IsPassword = 25,
    Enabled = 26,
    TransitionKey = 27,
    TransitionOffsetX = 28,
    TransitionOffsetY = 29,
    Value = 30,
    Localize = 31,
    Selected = 32,
    Argument = 33,
    ArgumentFrom = 34,
}

internal sealed class NxmlControlPropertyModel
{
    public NxmlControlPropertyModel(
        string name,
        NxmlControlValueKind valueKind,
        NxmlIrPropertyTarget target,
        XsrUiStateProperty? bindingProperty,
        XsrUiDirtyKinds bindingDirtyKinds,
        bool required,
        string? defaultValue)
    {
        Name = name;
        ValueKind = valueKind;
        Target = target;
        BindingProperty = bindingProperty;
        BindingDirtyKinds = bindingDirtyKinds;
        Required = required;
        DefaultValue = defaultValue;
    }

    public string Name { get; }

    public NxmlControlValueKind ValueKind { get; }

    public NxmlIrPropertyTarget Target { get; }

    public XsrUiStateProperty? BindingProperty { get; }

    public XsrUiDirtyKinds BindingDirtyKinds { get; }

    public bool Required { get; }

    public string? DefaultValue { get; }
}

internal sealed class NxmlControlModel
{
    public NxmlControlModel(
        string kind,
        string name,
        XsrUiSemanticRole role,
        NxmlRuntimeRecipe recipe,
        bool allowsChildren,
        IReadOnlyList<NxmlControlPropertyModel> properties)
    {
        Kind = kind;
        Name = name;
        Role = role;
        Recipe = recipe;
        AllowsChildren = allowsChildren;
        Properties = properties;
    }

    public string Kind { get; }

    public string Name { get; }

    public XsrUiSemanticRole Role { get; }

    public NxmlRuntimeRecipe Recipe { get; }

    public bool AllowsChildren { get; }

    public IReadOnlyList<NxmlControlPropertyModel> Properties { get; }

    public bool TryGetProperty(string name, out NxmlControlPropertyModel property)
    {
        foreach (NxmlControlPropertyModel candidate in Properties)
        {
            if (candidate.Name == name)
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }
}
