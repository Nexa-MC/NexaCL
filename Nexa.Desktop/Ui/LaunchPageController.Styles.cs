








using Nexa.Xsr.Renderer;




namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{

    /// <summary>
    /// Applies the legacy experimental launch-home styling that the PXML control vocabulary
    /// cannot express: card surfaces, section typography, the badge, the picker row, and the
    /// accent launch button. PXML keys name internal handles; semantic labels remain human text.
    /// </summary>
    private void StyleLaunchPage(XsrUiEntityId page, Dictionary<string, XsrUiEntityId> entities)
    {
        // Visual rules are loaded from Styles/Desktop.nss by the NXML compiler.
        if (entities.TryGetValue("VersionName", out XsrUiEntityId versionName))
            DesktopLiteralText.Preserve(_shell.Tree, versionName);
    }

    private void StyleCard(
        Dictionary<string, XsrUiEntityId> entities,
        string label,
        double cornerRadius)
    {
        if (entities.TryGetValue(label, out XsrUiEntityId card))
        {
            ApplyVisual(card, CardBackground, PrimaryText, cornerRadius, border: CardBorder);
        }
    }


    private void StyleText(Dictionary<string, XsrUiEntityId> entities, string label, XsrUiColor foreground, double fontSize, double weight = 400)
    {
        if (entities.TryGetValue(label, out XsrUiEntityId entity))
        {
            StyleText(entity, foreground, fontSize, weight);
        }
    }


    private void StyleText(XsrUiEntityId entity, XsrUiColor foreground, double fontSize, double weight = 400)
    {
        XsrUiVisualStyle visual = RequireVisual(entity);
        visual.Foreground = foreground;
        visual.FontSize = fontSize;
        visual.FontWeight = weight;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint | XsrUiDirtyKinds.Layout);
    }


    private void AlignText(Dictionary<string, XsrUiEntityId> entities, string key, XsrUiTextAlignment alignment)
    {
        if (entities.TryGetValue(key, out XsrUiEntityId entity))
        {
            AlignText(entity, alignment);
        }
    }


    private void AlignText(XsrUiEntityId entity, XsrUiTextAlignment alignment)
    {
        RequireVisual(entity).TextAlignment = alignment;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }


    private void ApplyVisual(
        XsrUiEntityId entity,
        XsrUiColor background,
        XsrUiColor foreground,
        double cornerRadius,
        XsrUiColor? border = null,
        XsrUiColor? hover = null,
        bool hoverExpand = false)
    {
        XsrUiVisualStyle visual = RequireVisual(entity);
        visual.Background = background;
        visual.Foreground = foreground;
        visual.Border = border ?? XsrUiColor.Transparent;
        visual.BorderWidth = border is null ? 0 : 1;
        visual.Hover = hover ?? XsrUiColor.Transparent;
        visual.HoverExpand = hoverExpand;
        visual.Surface = XsrUiSurfaceKind.Solid;
        visual.CornerRadius = cornerRadius;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }


    private XsrUiVisualStyle RequireVisual(XsrUiEntityId entity)
    {
        XsrUiVisualStyle? visual = _shell.Tree.GetComponent<XsrUiVisualStyle>(entity);
        if (visual is null)
        {
            visual = new XsrUiVisualStyle();
            _shell.Tree.SetComponent(entity, visual);
        }

        return visual;
    }


    private static string ReadEmbeddedResource(string suffix)
    {
        return DesktopMarkup.Read(suffix);
    }

}
