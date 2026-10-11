using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

/// <summary>Changes visibility only; animation and input lifetimes remain with their owners.</summary>
internal static class DesktopElementVisibility
{
    internal static void Set(XsrUiTree tree, XsrUiEntityId entity, bool visible)
    {
        XsrUiElement element = tree.GetComponent<XsrUiElement>(entity) ?? new XsrUiElement();
        if (element.IsVisible == visible) return;
        element.IsVisible = visible;
        tree.SetComponent(entity, element);
        tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }
}
