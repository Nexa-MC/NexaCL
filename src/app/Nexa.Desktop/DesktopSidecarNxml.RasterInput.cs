using System.Globalization;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private sealed partial class Session
    {
        private void RasterObserved(XsrUiEntityId node, XsrUiInputObservation observation)
        {
            if (ControlLifetime.IsCancellationRequested || !_shell.Tree.IsAlive(node)
                || _shell.Tree.GetComponent<XsrUiInput>(node) is not { Enabled: true } || !ModalInputAllowed(node)) return;
            var binding = _rasters.Values.SelectMany(items => items).FirstOrDefault(item => item.Node == node);
            if (binding is null || binding.Lifetime.IsCancellationRequested || !_rasterPool.IsLive(binding.Resource)
                || !_owner.Runtime.CanDispatchCallback(binding.Value.Exposure, binding.Value.Command)) return;
            bool visible = false;
            for (var ancestor = node; ancestor.IsAssigned && _shell.Tree.IsAlive(ancestor); ancestor = _coordinator.GetPresentationParent(ancestor))
            {
                if (_shell.Tree.GetComponent<XsrUiElement>(ancestor) is { IsVisible: false }) return;
                if (ancestor == _shell.Stage.Navigation.Current || _dialogs.Values.Any(dialog => dialog.Open && dialog.Chrome == ancestor)) visible = true;
            }
            if (!visible || observation.Kind is not ("Pointer" or "Key" or "Text" or "Focus")
                || observation.Phase.Length > 64 || !double.IsFinite(observation.X) || !double.IsFinite(observation.Y)) return;
            string text = observation.Text ?? ""; bool truncated = text.Length > 4096;
            if (truncated)
            {
                int length = char.IsHighSurrogate(text[4095]) ? 4095 : 4096;
                text = text[..length];
            }
            var next = new RasterObservation(observation.Kind, observation.Phase, observation.X, observation.Y, text, DialogLifetime(node));
            lock (binding.InputGate)
            {
                binding.Gap |= truncated || binding.Latest is not null;
                binding.Latest = next;
                if (binding.Running) return;
                binding.Running = true;
            }
            Interlocked.Increment(ref _rasterCallbacks);
            _ = Task.Run(() => DrainRasterInput(binding));
        }

        private async Task DrainRasterInput(RasterBinding binding)
        {
            try
            {
                while (true)
                {
                    RasterObservation next; bool gap;
                    lock (binding.InputGate)
                    {
                        if (binding.Lifetime.IsCancellationRequested || binding.Latest is null)
                        { binding.Latest = null; binding.Running = false; return; }
                        next = binding.Latest; binding.Latest = null; gap = binding.Gap; binding.Gap = false;
                    }
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(binding.Lifetime, next.Dialog);
                    var value = binding.Value;
                    Dictionary<string, string> fields = new(StringComparer.Ordinal)
                    {
                        ["kind"] = next.Kind,
                        ["phase"] = next.Phase,
                        ["x"] = next.X.ToString("R", CultureInfo.InvariantCulture),
                        ["y"] = next.Y.ToString("R", CultureInfo.InvariantCulture),
                        ["text"] = next.Text,
                        ["gap"] = gap ? "true" : "false"
                    };
                    var result = await _owner.Runtime.DispatchCallbackAsync(value.Exposure, value.Command, value.Argument, fields, stop.Token).ConfigureAwait(false);
                    if (!result.IsSuccess)
                    {
                        lock (binding.InputGate) binding.Gap = true;
                        if (!stop.IsCancellationRequested && result.Error?.Kind != XsrErrorKind.Cancelled)
                            _warn("Sidecar raster input failed: " + result.Error?.Code.Value);
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            {
                lock (binding.InputGate) { binding.Running = false; binding.Latest = null; binding.Gap = true; }
                if (error is not OperationCanceledException) _warn("Sidecar raster input failed: " + error.GetType().Name);
            }
            finally { Interlocked.Decrement(ref _rasterCallbacks); }
        }
    }
}
