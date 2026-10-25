using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Owns a standalone prepared document and reloads its data while retaining local UI state.</summary>
public class NxmlDocumentInstance : IDisposable
{
    private readonly XsrUiTree _tree;
    private readonly NxmlDynamicContent _dynamic;
    private readonly List<XsrUiEntityId> _fragments = [];
    private NxmlPreparedDocument _document;
    public XsrUiEntityId Root { get; }

    public NxmlDocumentInstance(XsrUiTree tree, XsrStateStore store, NxmlPreparedDocument document)
    {
        _tree = tree; _document = document;
        Root = NxmlUiLoader.LoadDetached(document.Root, tree, store); _dynamic = new(tree, store);
        try
        {
            foreach (var fragment in document.Fragments)
            {
                List<XsrUiEntityId> slots = [];
                tree.Walk(Root, node => { if (tree.Name(node) == fragment.Slot && tree.GetComponent<NxmlDynamicSlot>(node) is not null) slots.Add(node); return true; });
                if (slots.Count != 1) throw new NxmlLoadException("Page fragments require one local dynamic slot.");
                _fragments.Add(_dynamic.Append(slots[0], fragment.Content));
            }
        }
        catch { Dispose(); throw; }
    }

    public bool Owns(XsrUiEntityId source)
    {
        if (!_tree.IsAlive(source)) return false;
        for (var node = source; node.IsAssigned; node = _tree.Parent(node)) if (node == Root) return true;
        return false;
    }

    public void Reload(NxmlPreparedDocument document)
    {
        if (ReferenceEquals(document, _document)) return;
        NxmlUiLoader.ValidateReload(_document.Root.Root, document.Root.Root, Root, _tree);
        if (document.Fragments.Count != _fragments.Count) throw new NxmlLoadException("Page structure cannot change during state refresh.");
        for (int index = 0; index < _fragments.Count; index++)
            NxmlUiLoader.ValidateReload(_document.Fragments[index].Content.Root, document.Fragments[index].Content.Root, _fragments[index], _tree);
        NxmlUiLoader.ApplyReload(_document.Root.Root, document.Root.Root, Root, _tree);
        for (int index = 0; index < _fragments.Count; index++) _dynamic.Reload(_fragments[index], document.Fragments[index].Content);
        _document = document;
    }

    public void Dispose() { if (_tree.IsAlive(Root)) _tree.Destroy(Root); GC.SuppressFinalize(this); }
}
