namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    // The icon is 20 DIP, followed by a 6 DIP caption gap and 16 DIP leading inset.
    private static double CapsuleCaptionInset(double height) => (height + 20) / 2 + 6 + 16;

    private bool IsScrollableCapsule(XsrUiEntityId entity) =>
        _tree.GetComponent<XsrUiVisualStyle>(entity)?.HoverExpand == true
        && _tree.GetComponent<XsrUiScroll>(entity) is not null;

    private XsrUiSize CapsuleTextSize(XsrUiEntityId entity)
    {
        if (_tree.GetComponent<XsrUiText>(entity) is not { } text) return default;
        XsrUiVisualStyle? visual = EffectiveVisual(entity);
        return MeasureText(ResolveText(text), visual?.FontSize ?? 0, double.PositiveInfinity, 1,
            GetThemeStyle(entity)?.Style.FontFamily, visual?.FontWeight ?? 400, text.Runs);
    }

    private static XsrUiRect CapsuleCaptionRect(XsrUiRect rect) =>
        new(rect.X + 16, rect.Y, Math.Max(0, rect.Width - CapsuleCaptionInset(rect.Height)), rect.Height);

    private void ArrangeCapsuleScroll(XsrUiEntityId entity, XsrUiRect rect)
    {
        if (!IsScrollableCapsule(entity)) return;
        XsrUiScroll scroll = _tree.GetComponent<XsrUiScroll>(entity)!;
        XsrUiSize text = CapsuleTextSize(entity);
        XsrUiRect caption = CapsuleCaptionRect(rect);
        double contentWidth = text.Width > caption.Width + .01 ? text.Width : caption.Width;
        scroll.MaximumOffsetX = Math.Max(0, contentWidth - caption.Width);
        scroll.MaximumOffsetY = 0;
        scroll.OffsetX = caption.Width <= 0 ? 0 : Math.Clamp(scroll.OffsetX, 0, scroll.MaximumOffsetX);
        scroll.OffsetY = 0;
        _stackContentSizes[entity.Index] = new(contentWidth, caption.Height);
    }

    private bool MoveCapsuleCaptionForKey(XsrUiKey key)
    {
        if (key is not (XsrUiKey.Left or XsrUiKey.Right) || !_focused.IsAssigned
            || !CanUseScroll(_focused) || !IsScrollableCapsule(_focused)
            || _tree.GetComponent<XsrUiScroll>(_focused) is not { MaximumOffsetX: > 0 } scroll) return false;
        scroll.OffsetX = Math.Clamp(scroll.OffsetX + (key == XsrUiKey.Left ? -48 : 48), 0, scroll.MaximumOffsetX);
        _tree.MarkDirty(_focused, XsrUiDirtyKinds.Layout);
        return true;
    }
}
