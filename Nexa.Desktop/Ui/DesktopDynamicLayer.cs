using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Owns a nonmodal external slot on the shell stage; children never leave that slot.</summary>
internal sealed class DesktopDynamicLayer : IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly XsrUiEntityId _slot;
    private readonly NxmlDynamicContent _content;
    public DesktopDynamicLayer(XsrUiShell shell, XsrStateStore store, string page)
    {
        _shell = shell;
        _content = new(shell.Tree, store);
        _slot = NxmlUiLoader.LoadDetached(DesktopMarkup.Load(page), shell.Tree, store);
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(_slot) is null)
        { shell.Tree.Destroy(_slot); throw new NxmlLoadException("An overlay layer must declare a Dynamic root."); }
        shell.Stage.Show(_slot);
    }
    public XsrUiEntityId Append(string fragment, IReadOnlyDictionary<string, string>? data = null)
    {
        var entity = _content.Append(_slot, DesktopMarkup.Dynamic(fragment, data));
        _shell.Tree.SetComponent(entity, new XsrUiOverlayLayer(isModal: false));
        return entity;
    }
    public void Reload(XsrUiEntityId entity, string fragment, IReadOnlyDictionary<string, string> data) =>
        _content.Reload(entity, DesktopMarkup.Dynamic(fragment, data));
    public void Remove(XsrUiEntityId entity) => _content.Remove(entity);
    public void Dispose()
    {
        if (!_shell.Tree.IsAlive(_slot)) return;
        _content.Clear(_slot); _shell.Stage.Dismiss(_slot); _shell.Tree.Destroy(_slot);
    }
}
