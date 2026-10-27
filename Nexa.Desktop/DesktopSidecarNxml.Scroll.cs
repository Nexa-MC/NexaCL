using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private sealed partial class Session
    {
        private void ObserveScroll()
        {
            foreach (var (exposure, page) in _pages)
            {
                if (_shell.Stage.Navigation.Current != page.Root) continue;
                foreach (var entity in page.ScrollObservers)
                {
                    bool visible = true;
                    for (var node = entity; node.IsAssigned; node = _shell.Tree.Parent(node))
                        if (_shell.Tree.GetComponent<XsrUiElement>(node) is { IsVisible: false }) { visible = false; break; }
                    if (!visible) continue;
                    var observer = _shell.Tree.GetComponent<NxmlScrollObservation>(entity);
                    if (observer is null || _owner.Runtime.ReadCommandRoute(exposure, observer.Command) != XsrNxmlCommandRoute.Sidecar
                        || !_shell.Renderer.TryGetScrollSnapshot(entity, out var scroll)
                        || !observer.TryBegin(scroll.OffsetY, scroll.ViewportHeight, scroll.ContentHeight, out var argument)) continue;
                    _ = DispatchScroll(exposure, observer, argument, page.Stop.Token);
                }
            }
        }
        private async Task DispatchScroll(Guid exposure, NxmlScrollObservation observer, string argument, CancellationToken stop)
        {
            try { await Dispatch(exposure, observer.Command, argument, stop).ConfigureAwait(false); }
            finally { observer.Complete(); }
        }
    }
}
