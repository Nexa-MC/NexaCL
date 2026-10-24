namespace Nexa.Xsr.Renderer;

/// <summary>Presentation operations apply to any live node, independently of page controllers.</summary>
public enum XsrUiMountOperation { Append, Prepend, Before, After, Modify, Replace }

/// <summary>
/// Owner-thread presentation graph. Logical nodes, bindings and controller handles remain intact.
/// Mounted content uses the ordinary renderer components and is never a second control system.
/// </summary>
public sealed class XsrUiPresentation
{
    private readonly XsrUiTree _source;
    private readonly List<Mount> _mounts = [];
    private readonly Dictionary<XsrUiEntityId, List<Mount>> _byTarget = [];
    private readonly HashSet<XsrUiEntityId> _ownedRoots = [];
    private readonly Dictionary<XsrUiEntityId, IReadOnlyList<XsrUiEntityId>> _children = [];
    private readonly Dictionary<XsrUiEntityId, XsrUiEntityId> _parents = [];
    private long _revision;
    private long _structure;
    internal XsrUiPresentation(XsrUiTree source) => _source = source;
    internal long Revision => _revision;
    internal long EntityLifetimeRevision => _source.EntityLifetimeRevision + _revision;
    internal int Count => _source.Count;
    internal IReadOnlyList<XsrUiEntityId> LiveEntities => _source.LiveEntities;

    public IDisposable MountContent(XsrUiEntityId target, XsrUiEntityId content, XsrUiMountOperation operation)
    {
        if (!_source.IsAlive(target) || !_source.IsAlive(content) || _source.Parent(content).IsAssigned
            || target == content || IsExtension(target) || _mounts.Any(mount => mount.Content == content))
            throw new InvalidOperationException("A presentation mount requires a host target and an unowned detached content root.");
        if (!Enum.IsDefined(operation) || _mounts.Count >= 256 || _mounts.Count(mount => mount.Target == target) >= 16)
            throw new InvalidOperationException("Presentation mount budget exceeded or operation is invalid.");
        for (var ancestor = target; ancestor.IsAssigned; ancestor = _source.Parent(ancestor))
            if (ancestor == content) throw new InvalidOperationException("A presentation mount cannot contain its target.");
        var mount = new Mount(this, target, content, operation);
        _mounts.Add(mount);
        if (!_byTarget.TryGetValue(target, out var layers)) _byTarget.Add(target, layers = []);
        layers.Add(mount); _ownedRoots.Add(content);
        Changed(target);
        return mount;
    }

    /// <summary>Excludes module-owned nodes from host target discovery, preventing self-matching expansions.</summary>
    public bool IsExtension(XsrUiEntityId entity)
    {
        for (var current = entity; current.IsAssigned && _source.IsAlive(current); current = _source.Parent(current))
            if (_ownedRoots.Contains(current)) return true;
        return false;
    }

    internal void PrepareFrame()
    {
        if (_structure != _source.StructureRevision)
        { _structure = _source.StructureRevision; _children.Clear(); _parents.Clear(); }
        for (int index = _mounts.Count - 1; index >= 0; index--)
        {
            var mount = _mounts[index];
            if (!_source.IsAlive(mount.Target) || !_source.IsAlive(mount.Content)) mount.Dispose();
            else if (_source.HasDirtySubtree(mount.Content)) MarkDirty(mount.Target, XsrUiDirtyKinds.State);
        }
    }

    internal void FinishFrame(XsrUiEntityId root)
    {
        if (_mounts.Count == 0) return;
        _source.Walk(root, entity => { _source.ClearDirty(entity); return true; });
        foreach (var mount in _mounts)
            _source.Walk(mount.Content, entity => { _source.ClearDirty(entity); return true; });
    }

    private void Changed(XsrUiEntityId target)
    {
        _revision++;
        _parents.Clear();
        _children.Clear();
        if (_source.IsAlive(target)) _source.MarkDirty(target, XsrUiDirtyKinds.Structure);
    }

    private sealed class Mount(XsrUiPresentation owner, XsrUiEntityId target, XsrUiEntityId content,
        XsrUiMountOperation operation) : IDisposable
    {
        internal XsrUiEntityId Target { get; } = target;
        internal XsrUiEntityId Content { get; } = content;
        internal XsrUiMountOperation Operation { get; } = operation;
        public void Dispose()
        {
            if (!owner._mounts.Remove(this)) return;
            if (owner._byTarget.TryGetValue(Target, out var layers))
            { layers.Remove(this); if (layers.Count == 0) owner._byTarget.Remove(Target); }
            owner._ownedRoots.Remove(Content); owner.Changed(Target);
        }
    }

    public T? GetComponent<T>(XsrUiEntityId entity) where T : class
    {
        if (_mounts.Count == 0) return _source.GetComponent<T>(entity);
        if (_byTarget.TryGetValue(entity, out var layers))
            for (int index = layers.Count - 1; index >= 0; index--)
            {
                var mount = layers[index];
                if (mount.Operation is not (XsrUiMountOperation.Modify or XsrUiMountOperation.Replace) || !_source.IsAlive(mount.Content)) continue;
                if (_source.GetComponent<T>(mount.Content) is { } value) return value;
                if (mount.Operation == XsrUiMountOperation.Replace) return null;
            }
        return _source.GetComponent<T>(entity);
    }

    internal void SetComponent<T>(XsrUiEntityId entity, T? component) where T : class
    {
        var destination = entity;
        if (_byTarget.TryGetValue(entity, out var layers))
            for (int index = layers.Count - 1; index >= 0; index--)
            {
                var mount = layers[index];
                if (mount.Operation is not (XsrUiMountOperation.Modify or XsrUiMountOperation.Replace) || !_source.IsAlive(mount.Content)) continue;
                if (mount.Operation == XsrUiMountOperation.Replace || _source.GetComponent<T>(mount.Content) is not null)
                { destination = mount.Content; break; }
            }
        _source.SetComponent(destination, component);
        MarkDirty(entity, XsrUiDirtyKinds.Structure);
    }

    internal ComponentView ReadComponents(XsrUiEntityId entity)
    {
        bool overridden = false;
        if (_byTarget.TryGetValue(entity, out var layers))
            foreach (var mount in layers)
                if (mount.Operation is XsrUiMountOperation.Modify or XsrUiMountOperation.Replace) { overridden = true; break; }
        return new(_source.ReadComponents(entity), overridden ? this : null, entity);
    }
    internal XsrUiElement? ReplacedHostElement(XsrUiEntityId entity)
    {
        if (_byTarget.TryGetValue(entity, out var layers))
            foreach (var mount in layers)
                if (mount.Operation == XsrUiMountOperation.Replace) return _source.GetComponent<XsrUiElement>(entity);
        return null;
    }
    internal readonly struct ComponentView(XsrUiTree.ComponentView original, XsrUiPresentation? owner, XsrUiEntityId entity)
    {
        internal T? Get<T>() where T : class => owner is null ? original.Get<T>() : owner.GetComponent<T>(entity);
    }

    internal IReadOnlyList<XsrUiEntityId> Children(XsrUiEntityId entity)
    {
        if (_mounts.Count == 0) return _source.Children(entity);
        if (_children.TryGetValue(entity, out var cached)) return cached;
        _byTarget.TryGetValue(entity, out var own);
        var replacement = own?.LastOrDefault(mount => mount.Operation == XsrUiMountOperation.Replace && _source.IsAlive(mount.Content));
        var original = _source.Children(replacement?.Content ?? entity);
        if (replacement is null && own is null && !original.Any(child => _byTarget.ContainsKey(child)))
        { _children[entity] = original; return original; }
        List<XsrUiEntityId> children = [];
        if (own is not null) foreach (var mount in own) if (mount.Operation == XsrUiMountOperation.Prepend) Add(mount.Content);
        foreach (var child in original)
        {
            _byTarget.TryGetValue(child, out var adjacent);
            if (adjacent is not null) foreach (var mount in adjacent) if (mount.Operation == XsrUiMountOperation.Before) Add(mount.Content);
            Add(child);
            if (adjacent is not null) foreach (var mount in adjacent) if (mount.Operation == XsrUiMountOperation.After) Add(mount.Content);
        }
        if (own is not null) foreach (var mount in own) if (mount.Operation == XsrUiMountOperation.Append) Add(mount.Content);
        _children[entity] = children;
        return children;

        void Add(XsrUiEntityId child)
        {
            if (!_source.IsAlive(child)) return;
            children.Add(child);
            if (_source.Parent(child) != entity) _parents[child] = entity;
        }
    }

    internal int ChildCount(XsrUiEntityId entity) => _mounts.Count == 0 ? _source.ChildCount(entity) : Children(entity).Count;
    internal XsrUiEntityId ChildAt(XsrUiEntityId entity, int index) => _mounts.Count == 0 ? _source.ChildAt(entity, index) : Children(entity)[index];
    internal XsrUiEntityId Parent(XsrUiEntityId entity) => _parents.TryGetValue(entity, out var parent) && _source.IsAlive(parent)
        ? parent : _source.Parent(entity);
    internal string Name(XsrUiEntityId entity) => _source.Name(entity);
    internal bool IsAlive(XsrUiEntityId entity) => _source.IsAlive(entity);
    internal bool IsIndexAlive(int index) => _source.IsIndexAlive(index);
    internal XsrUiDirtyKinds DirtyKinds(XsrUiEntityId entity) => _source.DirtyKinds(entity);
    internal bool HasDirtySubtree(XsrUiEntityId entity) => _source.HasDirtySubtree(entity);
    internal bool HasDirtyLayoutSubtree(XsrUiEntityId entity) => _source.HasDirtyLayoutSubtree(entity);
    internal void ClearDirty(XsrUiEntityId entity) => _source.ClearDirty(entity);
    internal void MarkDirty(XsrUiEntityId entity, XsrUiDirtyKinds kinds)
    {
        _source.MarkDirty(entity, kinds);
        if (_mounts.Count == 0) return;
        for (var current = Parent(entity); current.IsAssigned; current = Parent(current))
            _source.MarkDirty(current, kinds);
    }
    internal void Walk(XsrUiEntityId root, Func<XsrUiEntityId, bool> visitor, Func<XsrUiEntityId, bool>? descend = null)
    {
        if (!_source.IsAlive(root) || !visitor(root) || descend?.Invoke(root) == false) return;
        foreach (var child in Children(root)) Walk(child, visitor, descend);
    }

}
