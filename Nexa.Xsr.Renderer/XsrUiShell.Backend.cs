namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiShell : IXsrUiBackendShell
{
    IXsrUiBackendRenderer IXsrUiBackendShell.Renderer => Renderer;
    XsrUiEntityId IXsrUiBackendShell.CurrentPage => Stage.Navigation.Current;
    bool IXsrUiBackendShell.HasContentTransition =>
        Tree.GetComponent<XsrUiTransition>(Stage.ContentHost) is { OffsetX: not 0 } or { OffsetY: not 0 };
    event EventHandler? IXsrUiBackendShell.RenderRequested
    {
        add
        {
            Tree.RenderInvalidated += value;
            if (StateBridge is not null) StateBridge.RenderRequested += value;
        }
        remove
        {
            Tree.RenderInvalidated -= value;
            if (StateBridge is not null) StateBridge.RenderRequested -= value;
        }
    }
}
