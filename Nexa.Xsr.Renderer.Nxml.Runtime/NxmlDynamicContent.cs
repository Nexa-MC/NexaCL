using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Marker created only by the dynamic-slot loader recipe.</summary>
public sealed class NxmlDynamicSlot;

/// <summary>Owns fragments attached to one page slot; operations run on the render thread.</summary>
public sealed class NxmlDynamicContent(XsrUiTree tree, XsrStateStore state)
{
    private readonly Dictionary<XsrUiEntityId, OwnedFragment> _owners = [];
    private int _nextPruneAt = 64;
    private sealed record OwnedFragment(XsrUiEntityId Slot, NxmlHostIr Fragment);

    public XsrUiEntityId Append(XsrUiEntityId slot, NxmlHostIr fragment)
    {
        RequireSlot(slot);
        ArgumentNullException.ThrowIfNull(fragment);
        if (!fragment.IsDynamic) throw new NxmlLoadException("Only .d.nxml content may be inserted into Dynamic.");
        if (_owners.Count >= _nextPruneAt)
        { Prune(); _nextPruneAt = Math.Max(64, _owners.Count * 2); }
        var instance = NxmlUiLoader.Load(fragment, tree, state, slot);
        _owners.Add(instance, new(slot, fragment));
        return instance;
    }

    /// <summary>Reprojects template data without resetting focus, drafts, animation or nested slots.</summary>
    public void Reload(XsrUiEntityId instance, NxmlHostIr fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_owners.TryGetValue(instance, out var owner) || !tree.IsAlive(instance)
            || tree.Parent(instance) != owner.Slot || !fragment.IsDynamic)
            throw new NxmlLoadException("Reload requires a live owned dynamic instance.");
        RequireSlot(owner.Slot);
        NxmlUiLoader.ValidateReload(owner.Fragment.Root, fragment.Root, instance, tree);
        NxmlUiLoader.ApplyReload(owner.Fragment.Root, fragment.Root, instance, tree);
        _owners[instance] = owner with { Fragment = fragment };
    }

    /// <summary>Reorders only this owner's complete set of live direct fragments, without resetting nested state.</summary>
    public void Reorder(XsrUiEntityId slot, IReadOnlyList<XsrUiEntityId> order)
    {
        RequireSlot(slot); ArgumentNullException.ThrowIfNull(order);
        var requested = order.ToArray();
        if (requested.Length != tree.Children(slot).Count || requested.Distinct().Count() != requested.Length
            || requested.Any(instance => !_owners.TryGetValue(instance, out var owner) || owner.Slot != slot
                || !tree.IsAlive(instance) || tree.Parent(instance) != slot))
            throw new NxmlLoadException("Reorder requires this owner's complete permutation of live slot fragments.");
        tree.ReorderChildren(slot, requested);
    }

    /// <summary>Moves an anchor-free fragment between slots of the same owned workspace, retaining its live state.</summary>
    public void Move(XsrUiEntityId instance, XsrUiEntityId destination)
    {
        RequireSlot(destination);
        if (!_owners.TryGetValue(instance, out var owner) || !tree.IsAlive(instance)
            || tree.Parent(instance) != owner.Slot)
            throw new NxmlLoadException("Move requires a live owned dynamic instance.");
        RequireSlot(owner.Slot);
        for (var node = destination; node.IsAssigned; node = tree.Parent(node))
            if (node == instance) throw new NxmlLoadException("Move cannot create an ownership cycle.");
        if (Workspace(instance) != Workspace(destination))
            throw new NxmlLoadException("Move requires slots in the same owned workspace.");
        bool anchored = false;
        tree.Walk(instance, node => { anchored |= tree.GetComponent<XsrUiAnchoredOverlay>(node) is not null; return true; });
        if (anchored) throw new NxmlLoadException("Move cannot relocate anchor contracts.");
        if (owner.Slot == destination) return;
        tree.Attach(instance, destination);
        _owners[instance] = owner with { Slot = destination };
    }

    private XsrUiEntityId Workspace(XsrUiEntityId entity)
    {
        XsrUiEntityId root = default;
        for (var node = entity; node.IsAssigned; node = tree.Parent(node))
            if (_owners.ContainsKey(node)) root = node;
        return root;
    }

    public XsrUiEntityId Replace(XsrUiEntityId slot, NxmlHostIr fragment)
    {
        var instance = Append(slot, fragment);
        foreach (var previous in _owners.Where(pair => pair.Value.Slot == slot && pair.Key != instance).Select(pair => pair.Key).ToArray())
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
        if (!_owners.Remove(instance, out var owner)) return false;
        if (!tree.IsAlive(instance) || tree.Parent(instance) != owner.Slot) return false;
        tree.Destroy(instance);
        return true;
    }

    public void Clear(XsrUiEntityId slot)
    {
        RequireSlot(slot);
        foreach (var instance in _owners.Where(pair => pair.Value.Slot == slot).Select(pair => pair.Key).ToArray()) RemoveCore(instance);
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
