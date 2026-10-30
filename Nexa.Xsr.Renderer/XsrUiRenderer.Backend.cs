namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    bool IXsrUiBackendRenderer.SetTextSelection(XsrUiEntityId entity, int start, int selectionEnd) =>
        SetTextSelection(entity, start, selectionEnd);
}
