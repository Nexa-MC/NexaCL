using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Marker created only by the dynamic-slot loader recipe.</summary>
public sealed class NxmlDynamicSlot;

/// <summary>Owns fragments attached to one page slot; operations run on the render thread.</summary>
public sealed class NxmlDynamicContent(XsrUiTree tree, XsrStateStore state)
{
    private readonly Dictionary<XsrUiEntityId, XsrUiEntityId> _owners = [];
    private int _nextPruneAt = 64;

    public XsrUiEntityId Append(XsrUiEntityId slot, NxmlHostIr fragment)
    {
        RequireSlot(slot);
        ArgumentNullException.ThrowIfNull(fragment);
        if (!fragment.IsDynamic) throw new NxmlLoadException("Only .d.nxml content may be inserted into Dynamic.");
        if (_owners.Count >= _nextPruneAt)
        { Prune(); _nextPruneAt = Math.Max(64, _owners.Count * 2); }
        var instance = NxmlUiLoader.Load(fragment, tree, state, slot);
        _owners.Add(instance, slot);
        return instance;
    }

    public XsrUiEntityId Replace(XsrUiEntityId slot, NxmlHostIr fragment)
    {
        var instance = Append(slot, fragment);
        foreach (var previous in _owners.Where(pair => pair.Value == slot && pair.Key != instance).Select(pair => pair.Key).ToArray())
            RemoveCore(previous);
        Prune();
        return instance;
    }

    public bool Remove(XsrUiEntityId instance)
    {
        bool removed = RemoveCore(instance);
        Prune();
        return removed;
    }

    private bool RemoveCore(XsrUiEntityId instance)
    {
        if (!_owners.Remove(instance, out var slot)) return false;
        if (!tree.IsAlive(instance) || tree.Parent(instance) != slot) return false;
        tree.Destroy(instance);
        return true;
    }

    public void Clear(XsrUiEntityId slot)
    {
        RequireSlot(slot);
        foreach (var instance in _owners.Where(pair => pair.Value == slot).Select(pair => pair.Key).ToArray()) RemoveCore(instance);
        Prune();
    }

    private void RequireSlot(XsrUiEntityId slot)
    {
        if (!tree.IsAlive(slot) || tree.GetComponent<NxmlDynamicSlot>(slot) is null)
            throw new NxmlLoadException("The target is not a live Dynamic slot.");
    }

    private void Prune()
    {
        foreach (var instance in _owners.Keys.Where(entity => !tree.IsAlive(entity)).ToArray()) _owners.Remove(instance);
    }
}
