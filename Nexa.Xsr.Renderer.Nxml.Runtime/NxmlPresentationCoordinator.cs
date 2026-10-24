using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml;

public sealed record NxmlPresentationLayer(Guid Exposure, string Target, XsrUiMountOperation Operation,
    NxmlPreparedDocument Document);

public sealed class NxmlPresentationException(Guid exposure, Exception cause)
    : InvalidOperationException("An NXML extension could not be mounted.", cause)
{
    public Guid Exposure { get; } = exposure;
}

/// <summary>Materializes immutable prepared documents at stable node paths, including future pages.</summary>
public sealed class NxmlPresentationCoordinator(XsrUiTree tree, XsrStateStore state) : IDisposable
{
    private readonly Dictionary<(Guid Exposure, XsrUiEntityId Target), Instance> _instances = [];
    private IReadOnlyList<NxmlPresentationLayer> _layers = [];
    private long _structure = -1;

    public void Update(IReadOnlyList<NxmlPresentationLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            if (_instances.Count != 0) Dispose();
            _layers = layers; _structure = tree.StructureRevision; return;
        }
        bool changed = !ReferenceEquals(_layers, layers);
        // LiveEntities includes retained pages; extension roots never become extension targets.
        long structure = tree.StructureRevision;
        if (!changed && _structure == structure) return;
        var nodes = tree.LiveEntities.Where(node => !tree.Presentation.IsExtension(node)
            && !_instances.Values.Any(instance => instance.OwnsContent(node, tree))).ToArray();
        HashSet<(Guid, XsrUiEntityId)> keep = [];
        List<((Guid, XsrUiEntityId) Key, Instance Instance)> created = [];
        List<(Instance Instance, NxmlPreparedDocument Document)> reloads = [];
        Guid exposure = default;
        try
        {
            foreach (var layer in layers)
            {
                exposure = layer.Exposure;
                ValidateTarget(layer.Target);
                foreach (var target in nodes.Where(node => Matches(node, layer.Target)))
                {
                    var key = (layer.Exposure, target); keep.Add(key);
                    if (_instances.TryGetValue(key, out var existing))
                    {
                        if (!ReferenceEquals(existing.Document, layer.Document))
                        {
                            existing.ValidateReload(layer.Document, tree);
                            reloads.Add((existing, layer.Document));
                        }
                        continue;
                    }
                    created.Add((key, Instance.Create(layer, target, tree, state)));
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            foreach (var item in created) item.Instance.Dispose();
            throw new NxmlPresentationException(exposure, error);
        }
        foreach (var reload in reloads) reload.Instance.Reload(reload.Document, tree);
        foreach (var item in created) _instances.Add(item.Key, item.Instance);
        foreach (var key in _instances.Keys.Where(key => !keep.Contains(key)).ToArray())
        { _instances[key].Dispose(); _instances.Remove(key); }
        _layers = layers;
        _structure = tree.StructureRevision;
    }

    public bool TryGetExposure(XsrUiEntityId source, out Guid exposure)
    {
        foreach (var instance in _instances.Values.Reverse())
            if (tree.IsAlive(source) && instance.Owns(source, tree))
            { exposure = instance.Exposure; return true; }
        exposure = default; return false;
    }

    public static void ValidateTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 512 || target.Split('/').Any(part => part.Length < 2
            || part[0] != '#' || part.Skip(1).Any(character => character is '#' or '/' || char.IsWhiteSpace(character))))
            throw new NxmlCompileException("Targets use #Key or an ancestor path such as #Page/#Toolbar.");
    }

    private bool Matches(XsrUiEntityId entity, string selector)
    {
        var parts = selector.Split('/');
        if (tree.Name(entity) != parts[^1][1..]) return false;
        for (int index = parts.Length - 2; index >= 0; index--)
        {
            do { entity = tree.Parent(entity); } while (entity.IsAssigned && tree.Name(entity) != parts[index][1..]);
            if (!entity.IsAssigned) return false;
        }
        return true;
    }

    public void Dispose()
    {
        foreach (var instance in _instances.Values) instance.Dispose();
        _instances.Clear(); _layers = [];
    }

    private sealed class Instance(XsrUiTree tree, XsrUiEntityId root, XsrUiEntityId target, Guid exposure,
        XsrUiMountOperation operation, NxmlPreparedDocument document, NxmlDynamicContent dynamic) : IDisposable
    {
        private readonly NxmlDynamicContent _dynamic = dynamic;
        private IDisposable? _mount;
        private readonly List<XsrUiEntityId> _fragments = [];
        internal Guid Exposure { get; } = exposure;
        internal NxmlPreparedDocument Document { get; private set; } = document;

        internal static Instance Create(NxmlPresentationLayer layer, XsrUiEntityId target, XsrUiTree tree, XsrStateStore state)
        {
            var root = NxmlUiLoader.LoadDetached(layer.Document.Root, tree, state);
            var instance = new Instance(tree, root, target, layer.Exposure, layer.Operation, layer.Document, new(tree, state));
            try
            {
                foreach (var fragment in layer.Document.Fragments)
                {
                    List<XsrUiEntityId> slots = [];
                    tree.Walk(root, node => { if (tree.Name(node) == fragment.Slot && tree.GetComponent<NxmlDynamicSlot>(node) is not null) slots.Add(node); return true; });
                    if (slots.Count != 1) throw new NxmlLoadException("A dynamic extension fragment requires one unambiguous local slot.");
                    instance._fragments.Add(instance._dynamic.Append(slots[0], fragment.Content));
                }
                instance._mount = tree.Presentation.MountContent(target, root, layer.Operation);
                return instance;
            }
            catch { instance.Dispose(); throw; }
        }

        internal bool Owns(XsrUiEntityId entity, XsrUiTree source)
        {
            if (entity == target && operation is XsrUiMountOperation.Modify or XsrUiMountOperation.Replace) return true;
            return OwnsContent(entity, source);
        }

        internal bool OwnsContent(XsrUiEntityId entity, XsrUiTree source)
        {
            for (var node = entity; node.IsAssigned; node = source.Parent(node)) if (node == root) return true;
            return false;
        }

        internal void Reload(NxmlPreparedDocument next, XsrUiTree source)
        {
            NxmlUiLoader.ApplyReload(Document.Root.Root, next.Root.Root, root, source);
            for (int index = 0; index < _fragments.Count; index++) _dynamic.Reload(_fragments[index], next.Fragments[index].Content);
            Document = next;
        }

        internal void ValidateReload(NxmlPreparedDocument next, XsrUiTree source)
        {
            NxmlUiLoader.ValidateReload(Document.Root.Root, next.Root.Root, root, source);
            if (next.Fragments.Count != _fragments.Count) throw new NxmlLoadException("Extension state cannot change document structure.");
            for (int index = 0; index < _fragments.Count; index++)
                NxmlUiLoader.ValidateReload(Document.Fragments[index].Content.Root, next.Fragments[index].Content.Root, _fragments[index], source);
        }

        public void Dispose()
        {
            _mount?.Dispose(); _mount = null;
            if (tree.IsAlive(root)) tree.Destroy(root);
        }
    }
}
