using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

public sealed partial class NxmlUiLoader
{
    // Validate the full projection before applying anything. A nested slot owns its own
    // children; their lifetime and live state are not part of the containing fragment.
    internal static void ValidateReload(NxmlIrNode before, NxmlIrNode after, XsrUiEntityId entity, XsrUiTree tree)
    {
        var allowed = before with
        {
            Width = after.Width,
            Height = after.Height,
            MinWidth = after.MinWidth,
            MaxWidth = after.MaxWidth,
            MinHeight = after.MinHeight,
            MaxHeight = after.MaxHeight,
            Weight = after.Weight,
            HorizontalAlignment = after.HorizontalAlignment,
            VerticalAlignment = after.VerticalAlignment,
            Margin = after.Margin,
            Padding = after.Padding,
            IsVisible = after.IsVisible,
            Spacing = after.Spacing,
            Content = after.Content,
            Label = after.Label,
            ImageSource = after.ImageSource,
            Command = after.Command,
            Enabled = after.Enabled,
            Localize = after.Localize,
            Selected = after.Selected,
            MaxLines = after.MaxLines,
            TrimOverflow = after.TrimOverflow,
            Value = after.Value,
            Visual = after.Visual,
            LiveSetting = after.LiveSetting,
            Children = after.Children,
            Bindings = after.Bindings,
        };
        if (allowed != after || !before.Bindings.SequenceEqual(after.Bindings) || !tree.IsAlive(entity)
            || tree.Name(entity) != (before.Key ?? before.Kind) || before.Children.Count != after.Children.Count)
            throw new NxmlLoadException("Reload cannot change fragment shape, identity, bindings or input/transition contracts; use Replace.");
        if (before.Recipe == NxmlRuntimeRecipe.DynamicSlot) return;
        var children = tree.Children(entity);
        if (children.Count != before.Children.Count) throw new NxmlLoadException("The fragment structure changed outside its owner.");
        for (int i = 0; i < children.Count; i++) ValidateReload(before.Children[i], after.Children[i], children[i], tree);
    }

    internal static void ApplyReload(NxmlIrNode before, NxmlIrNode after, XsrUiEntityId entity, XsrUiTree tree)
    {
        XsrUiDirtyKinds dirty = XsrUiDirtyKinds.None;
        if (before.Width != after.Width || before.Height != after.Height || before.MinWidth != after.MinWidth
            || before.MaxWidth != after.MaxWidth || before.MinHeight != after.MinHeight || before.MaxHeight != after.MaxHeight
            || before.Weight != after.Weight || before.HorizontalAlignment != after.HorizontalAlignment
            || before.VerticalAlignment != after.VerticalAlignment || before.Margin != after.Margin
            || before.Padding != after.Padding || before.IsVisible != after.IsVisible)
        {
            var box = tree.GetComponent<XsrUiElement>(entity);
            if (box is null) { box = new(); tree.SetComponent(entity, box); }
            box.Width = after.Width; box.Height = after.Height; box.MinWidth = after.MinWidth; box.MaxWidth = after.MaxWidth;
            box.MinHeight = after.MinHeight; box.MaxHeight = after.MaxHeight; box.Weight = after.Weight;
            box.HorizontalAlignment = after.HorizontalAlignment; box.VerticalAlignment = after.VerticalAlignment;
            box.Margin = after.Margin; box.Padding = after.Padding; box.IsVisible = after.IsVisible;
            dirty |= XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint;
        }
        if (before.Spacing != after.Spacing && tree.GetComponent<XsrUiStackPanel>(entity) is { } stack)
        { stack.Spacing = after.Spacing; dirty |= XsrUiDirtyKinds.Layout; }
        if (before.Content != after.Content)
        {
            if (tree.GetComponent<XsrUiText>(entity) is { } text) text.Content = after.Content ?? string.Empty;
            else if (after.Content is not null) tree.SetComponent(entity, new XsrUiText(after.Content) { Localize = after.Localize, MaxLines = after.MaxLines, TrimOverflow = after.TrimOverflow });
            dirty |= XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint;
        }
        if ((before.MaxLines != after.MaxLines || before.TrimOverflow != after.TrimOverflow)
            && tree.GetComponent<XsrUiText>(entity) is { } clipped)
        {
            clipped.MaxLines = after.MaxLines; clipped.TrimOverflow = after.TrimOverflow;
            dirty |= XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint;
        }
        if (before.Label != after.Label)
        {
            if (tree.GetComponent<XsrUiSemantic>(entity) is { } semantic) semantic.Label = after.Label;
            else tree.SetComponent(entity, new XsrUiSemantic(after.Role, after.Label) { Localize = after.Localize });
            dirty |= XsrUiDirtyKinds.Paint;
        }
        if (before.ImageSource != after.ImageSource)
        {
            if (tree.GetComponent<XsrUiImage>(entity) is { } image) image.Source = after.ImageSource ?? string.Empty;
            else if (after.ImageSource is not null) tree.SetComponent(entity, new XsrUiImage(after.ImageSource));
            dirty |= XsrUiDirtyKinds.Paint;
        }
        if (before.Command != after.Command)
        {
            if (after.Command is { } command)
            {
                if (tree.GetComponent<XsrUiCommandBinding>(entity) is { } binding) binding.Command = command;
                else tree.SetComponent(entity, new XsrUiCommandBinding(command));
            }
            else tree.SetComponent<XsrUiCommandBinding>(entity, null);
        }
        if (before.Enabled != after.Enabled && tree.GetComponent<XsrUiInput>(entity) is { } input)
        { input.Enabled = after.Enabled; dirty |= XsrUiDirtyKinds.Paint; }
        if (before.Value != after.Value && tree.GetComponent<XsrUiProgress>(entity) is { } progress)
        { progress.SetTarget(after.Value); dirty |= XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint; }
        if (before.Localize != after.Localize)
        {
            if (tree.GetComponent<XsrUiText>(entity) is { } literalText) literalText.Localize = after.Localize;
            if (tree.GetComponent<XsrUiSemantic>(entity) is { } literalSemantic) literalSemantic.Localize = after.Localize;
            dirty |= XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint;
        }
        if (before.Selected != after.Selected)
        {
            if (after.Selected is { } selected)
            {
                if (tree.GetComponent<XsrUiSelection>(entity) is { } selection) selection.IsSelected = selected;
                else tree.SetComponent(entity, new XsrUiSelection { IsSelected = selected });
            }
            else tree.SetComponent<XsrUiSelection>(entity, null);
            dirty |= XsrUiDirtyKinds.Paint;
        }
        if (before.LiveSetting != after.LiveSetting)
        {
            if (tree.GetComponent<XsrUiLiveRegion>(entity) is { } live && after.LiveSetting is { } setting) live.Setting = setting;
            else tree.SetComponent(entity, after.LiveSetting is { } value ? new XsrUiLiveRegion(value) : null);
            dirty |= XsrUiDirtyKinds.Paint;
        }
        if (before.Visual != after.Visual)
        {
            tree.SetComponent(entity, after.Visual is { } visual ? CreateVisual(visual) : null);
            dirty |= XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint;
        }
        if (dirty != XsrUiDirtyKinds.None) tree.MarkDirty(entity, dirty);
        if (before.Recipe == NxmlRuntimeRecipe.DynamicSlot) return;
        var children = tree.Children(entity);
        for (int i = 0; i < children.Count; i++) ApplyReload(before.Children[i], after.Children[i], children[i], tree);
    }

    private static XsrUiVisualStyle CreateVisual(XsrUiVisualStyleSnapshot visual) => new()
    {
        Background = visual.Background,
        Foreground = visual.Foreground,
        Border = visual.Border,
        Hover = visual.Hover,
        HoverExpand = visual.HoverExpand,
        NavigationLayout = visual.NavigationLayout,
        WrapText = visual.WrapText,
        Surface = visual.Surface,
        Opacity = visual.Opacity,
        CornerRadius = visual.CornerRadius,
        BorderWidth = visual.BorderWidth,
        BlurRadius = visual.BlurRadius,
        FontSize = visual.FontSize,
        FontWeight = visual.FontWeight,
        TextAlignment = visual.TextAlignment,
    };
}
