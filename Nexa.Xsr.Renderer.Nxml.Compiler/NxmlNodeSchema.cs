using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

// Renderer primitive slots only. Component names and defaults live in .c.nxml files.
internal static class NxmlNodeSchema
{
    internal static readonly IReadOnlyList<NxmlControlPropertyModel> Properties =
    [
        new("Width", NxmlControlValueKind.Double, NxmlIrPropertyTarget.Width, null, XsrUiDirtyKinds.None, false, null),
        new("Height", NxmlControlValueKind.Double, NxmlIrPropertyTarget.Height, null, XsrUiDirtyKinds.None, false, null),
        new("MinWidth", NxmlControlValueKind.Double, NxmlIrPropertyTarget.MinWidth, null, XsrUiDirtyKinds.None, false, null),
        new("MaxWidth", NxmlControlValueKind.Double, NxmlIrPropertyTarget.MaxWidth, null, XsrUiDirtyKinds.None, false, null),
        new("MinHeight", NxmlControlValueKind.Double, NxmlIrPropertyTarget.MinHeight, null, XsrUiDirtyKinds.None, false, null),
        new("MaxHeight", NxmlControlValueKind.Double, NxmlIrPropertyTarget.MaxHeight, null, XsrUiDirtyKinds.None, false, null),
        new("Weight", NxmlControlValueKind.Double, NxmlIrPropertyTarget.Weight, null, XsrUiDirtyKinds.None, false, null),
        new("HorizontalAlignment", NxmlControlValueKind.Alignment, NxmlIrPropertyTarget.HorizontalAlignment, null, XsrUiDirtyKinds.None, false, null),
        new("VerticalAlignment", NxmlControlValueKind.Alignment, NxmlIrPropertyTarget.VerticalAlignment, null, XsrUiDirtyKinds.None, false, null),
        new("Margin", NxmlControlValueKind.Thickness, NxmlIrPropertyTarget.Margin, null, XsrUiDirtyKinds.None, false, null),
        new("Padding", NxmlControlValueKind.Thickness, NxmlIrPropertyTarget.Padding, null, XsrUiDirtyKinds.None, false, null),
        new("IsVisible", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Visibility, XsrUiStateProperty.Visibility, XsrUiDirtyKinds.State, false, null),
        new("Key", NxmlControlValueKind.String, NxmlIrPropertyTarget.Key, null, XsrUiDirtyKinds.None, false, null),
        new("Label", NxmlControlValueKind.String, NxmlIrPropertyTarget.Label, XsrUiStateProperty.SemanticLabel, XsrUiDirtyKinds.Paint, false, null),
        new("Content", NxmlControlValueKind.String, NxmlIrPropertyTarget.Content, XsrUiStateProperty.Text, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint, false, null),
        new("Icon", NxmlControlValueKind.String, NxmlIrPropertyTarget.ImageSource, null, XsrUiDirtyKinds.None, false, null),
        new("Command", NxmlControlValueKind.SemanticId, NxmlIrPropertyTarget.Command, null, XsrUiDirtyKinds.None, false, null),
        new("Focusable", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Focusable, null, XsrUiDirtyKinds.None, false, null),
        new("Clickable", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Clickable, XsrUiStateProperty.Enabled, XsrUiDirtyKinds.State, false, null),
        new("TransitionKey", NxmlControlValueKind.String, NxmlIrPropertyTarget.TransitionKey, XsrUiStateProperty.TransitionKey, XsrUiDirtyKinds.Paint, false, null),
        new("TransitionOffsetX", NxmlControlValueKind.Double, NxmlIrPropertyTarget.TransitionOffsetX, null, XsrUiDirtyKinds.None, false, null),
        new("TransitionOffsetY", NxmlControlValueKind.Double, NxmlIrPropertyTarget.TransitionOffsetY, null, XsrUiDirtyKinds.None, false, null),
        new("Orientation", NxmlControlValueKind.Orientation, NxmlIrPropertyTarget.Orientation, null, XsrUiDirtyKinds.None, false, null),
        new("Spacing", NxmlControlValueKind.Double, NxmlIrPropertyTarget.Spacing, null, XsrUiDirtyKinds.None, false, null),
        new("StretchLastChild", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.StretchLastChild, null, XsrUiDirtyKinds.None, false, null),
        new("Scroll", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Scrollable, null, XsrUiDirtyKinds.None, false, null),
        new("Source", NxmlControlValueKind.String, NxmlIrPropertyTarget.ImageSource, null, XsrUiDirtyKinds.None, false, null),
        new("Value", NxmlControlValueKind.Double, NxmlIrPropertyTarget.Value, XsrUiStateProperty.Value, XsrUiDirtyKinds.State, false, null),
        new("Placeholder", NxmlControlValueKind.String, NxmlIrPropertyTarget.Placeholder, null, XsrUiDirtyKinds.None, false, null),
        new("IsPassword", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.IsPassword, null, XsrUiDirtyKinds.None, false, null),
        new("Enabled", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Enabled, XsrUiStateProperty.Enabled, XsrUiDirtyKinds.State, false, null),
        new("Localize", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Localize, null, XsrUiDirtyKinds.None, false, null),
        new("Selected", NxmlControlValueKind.Boolean, NxmlIrPropertyTarget.Selected, null, XsrUiDirtyKinds.None, false, null),
    ];
}
