using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

public sealed partial class NxmlUiLoader
{
    private static void ResolveSegments(NxmlIrNode root, XsrUiEntityId entity, XsrUiTree tree)
    {
        Dictionary<string, XsrUiEntityId> keys = new(StringComparer.Ordinal);
        List<(XsrUiEntityId Track, string Key)> tracks = [];
        Visit(root, entity);
        HashSet<XsrUiEntityId> thumbs = [];
        List<(XsrUiEntityId Track, XsrUiEntityId Thumb)> admitted = [];
        foreach (var (track, key) in tracks)
        {
            if (!keys.TryGetValue(key, out var thumb) || thumb == track
                || tree.GetComponent<XsrUiTransition>(thumb) is null || !thumbs.Add(thumb))
                throw new NxmlLoadException("A segmented track requires its own declared local transition thumb.");
            admitted.Add((track, thumb));
        }
        foreach (var (track, thumb) in admitted)
        {
            var selection = new XsrUiSegmentedTrack(thumb);
            foreach (var child in tree.Children(track))
                if (tree.GetComponent<XsrUiSelection>(child)?.IsSelected == true)
                {
                    selection.Selected = child;
                    break;
                }
            tree.SetComponent(track, selection);
        }

        void Visit(NxmlIrNode node, XsrUiEntityId current)
        {
            if (node.Key is { } key && !keys.TryAdd(key, current))
                throw new NxmlLoadException("The local segmented key is ambiguous.");
            if (node.SegmentThumbKey is { } thumb) tracks.Add((current, thumb));
            var children = tree.Children(current);
            for (int index = 0; index < node.Children.Count; index++)
                Visit(node.Children[index], children[index]);
        }
    }
}
