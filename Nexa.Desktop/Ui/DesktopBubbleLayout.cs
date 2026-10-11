using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>One compact bottom-up dock for shell bubbles. Exiting items release their slot immediately.</summary>
internal static class DesktopBubbleLayout
{
    private sealed class Slot(int order, XsrUiThickness margin)
    {
        public int Order { get; } = order;
        public XsrUiThickness BaseMargin { get; } = margin;
        public DateTimeOffset? ExitAt { get; set; }
        public bool DestroyOnExit { get; set; }
        public ITimer? Timer { get; set; }
    }

    private sealed class Registry
    {
        public List<XsrUiEntityId> Entries { get; } = [];
    }

    public static void Register(XsrUiShell shell, XsrUiEntityId entity, int order)
    {
        var registry = shell.Tree.GetComponent<Registry>(shell.Stage.Root);
        if (registry is null) { registry = new(); shell.Tree.SetComponent(shell.Stage.Root, registry); }
        registry.Entries.Add(entity);
        shell.Tree.SetComponent(entity, new Slot(order, shell.Tree.GetComponent<XsrUiElement>(entity)!.Margin));
        shell.Tree.SetComponent(entity, new XsrUiOverlayMotion(XsrUiOverlayMotionKind.Notification));
    }

    public static void SetVisible(XsrUiShell shell, XsrStateStore store, XsrUiEntityId entity, bool visible, bool destroy = false)
    {
        var element = shell.Tree.GetComponent<XsrUiElement>(entity)!;
        var slot = shell.Tree.GetComponent<Slot>(entity)!;
        var motion = shell.Tree.GetComponent<XsrUiOverlayMotion>(entity)!;
        if (visible == element.IsVisible && !motion.IsClosing) return;
        if (!visible && motion.IsClosing) return;
        if (visible)
        {
            slot.Timer?.Dispose(); slot.Timer = null; slot.ExitAt = null;
            motion.IsClosing = false;
            element.IsVisible = true;
            shell.Tree.GetComponent<XsrUiInput>(entity)!.Enabled = true;
        }
        else if (element.IsVisible && !motion.IsClosing)
        {
            motion.IsClosing = true;
            shell.Tree.GetComponent<XsrUiInput>(entity)!.Enabled = false;
            slot.DestroyOnExit = destroy;
            slot.ExitAt = DateTimeOffset.UtcNow.AddMilliseconds(shell.Renderer.ReducedMotion ? 0 : 360);
            if (!shell.Renderer.ReducedMotion)
                slot.Timer = TimeProvider.System.CreateTimer(_ =>
                {
                    try { store.Publish(store.Resolve(TaskBubbleState.Revision), DateTime.UtcNow.Ticks); }
                    catch (ObjectDisposedException) { }
                }, null, TimeSpan.FromMilliseconds(370), Timeout.InfiniteTimeSpan);
        }
        shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    public static void Arrange(XsrUiShell shell)
    {
        var registry = shell.Tree.GetComponent<Registry>(shell.Stage.Root);
        if (registry is null) return;
        registry.Entries.RemoveAll(entity => !shell.Tree.IsAlive(entity));
        double height = 0;
        foreach (var entity in registry.Entries.OrderBy(entity => shell.Tree.GetComponent<Slot>(entity)!.Order).ToArray())
        {
            var slot = shell.Tree.GetComponent<Slot>(entity)!;
            var element = shell.Tree.GetComponent<XsrUiElement>(entity)!;
            if (slot.ExitAt is { } exit && DateTimeOffset.UtcNow >= exit)
            {
                slot.Timer?.Dispose(); slot.Timer = null; slot.ExitAt = null;
                if (slot.DestroyOnExit) { shell.Tree.Destroy(entity); continue; }
                element.IsVisible = false;
                shell.Tree.GetComponent<XsrUiOverlayMotion>(entity)!.IsClosing = false;
                shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
            }
            if (!element.IsVisible || shell.Tree.GetComponent<XsrUiOverlayMotion>(entity)!.IsClosing) continue;
            var basis = slot.BaseMargin;
            var margin = basis with { Bottom = basis.Bottom + height };
            if (element.Margin != margin)
            {
                element.Margin = margin;
                shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Layout);
            }
            height += element.Height.GetValueOrDefault() + basis.Top;
        }
    }
}

