using System.Globalization;
using System.Text;
using Nexa.Desktop.Ui;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private readonly List<RasterWork> _rasterWork = [];
    internal int ActiveRasterCallbacks { get { lock (_controlGate) return _attached is null ? 0 : Volatile.Read(ref _attached._rasterCallbacks); } }
    internal int PendingRasterControls { get { lock (_controlGate) return _rasterWork.Count; } }
    internal HostRasterActions RasterActions => new(
        (owner, value, token) => { RasterAttachment()._rasterPool.Begin(owner, value, token); return ValueTask.CompletedTask; },
        (owner, value, token) => { RasterAttachment()._rasterPool.Append(owner, value, token); return ValueTask.CompletedTask; },
        (owner, value, token) => RasterAttachment()._rasterPool.SealAsync(owner, value.Id, token),
        ReplaceRastersAsync,
        (owner, value, token) => { RasterAttachment()._rasterPool.Release(owner, value.Id, token); return ValueTask.CompletedTask; });
    private Session RasterAttachment()
    { lock (_controlGate) return _attached ?? throw new InvalidOperationException("Presentation is not attached."); }

    private async ValueTask ReplaceRastersAsync(SidecarHostSession owner, HostRasterSet input, CancellationToken token)
    {
        if (input?.Bindings is null || input.Bindings.Length > 8) throw new ArgumentException("Invalid raster binding count.");
        var values = input.Bindings.ToArray(); var snapshot = owner.CapturePresentation(input.ExpectedRevision);
        List<RasterDraft> drafts = []; HashSet<string> targets = new(StringComparer.Ordinal);
        long arguments = 0;
        foreach (var value in values)
        {
            if (value is null || value.ImageId <= 0 || !double.IsFinite(value.Width) || !double.IsFinite(value.Height)
                || value.Width is < 1 or > 2048 || value.Height is < 1 or > 2048
                || string.IsNullOrWhiteSpace(value.Slot) || value.Slot.Length > 256 || value.Slot.Any(char.IsControl)
                || value.Argument is null || value.Argument.Length > 32768 || string.IsNullOrWhiteSpace(value.Command))
                throw new ArgumentException("Invalid raster binding.");
            arguments += Encoding.UTF8.GetByteCount(value.Argument);
            if (arguments > 256 * 1024) throw new ArgumentException("Raster callback budget exceeded.");
            var document = snapshot.Documents.SingleOrDefault(document => document.Index == value.Document);
            var command = XsrSemanticId.Parse(value.Command);
            if (document is null || document.Source.Operation == SidecarNxmlOperation.OnboardingPage
                || !Runtime.CanDispatchCallback(document.Exposure, command))
                throw new InvalidOperationException("Raster requires an owned document and registered form callback.");
            if (!targets.Add(document.Exposure.ToString("N") + ":" + value.Slot)) throw new ArgumentException("Duplicate raster slot.");
            drafts.Add(new(document.Exposure, value.Slot, value.ImageId, value.Width, value.Height, command, value.Argument));
        }
        RasterWork work;
        lock (_controlGate)
        {
            token.ThrowIfCancellationRequested();
            if (_attached is null) throw new InvalidOperationException("Presentation is not attached.");
            if (_rasterWork.Count >= 64 || _rasterWork.Count(item => item.Owner == owner) >= 8)
                throw new InvalidOperationException("Raster control queue is full.");
            work = new(owner, snapshot, drafts.ToArray(), _attached,
                CancellationTokenSource.CreateLinkedTokenSource(token, snapshot.Retirement, _attached.ControlLifetime));
            _rasterWork.Add(work);
        }
        using var stop = work.Stop; stop.CancelAfter(TimeSpan.FromSeconds(5));
        using var cancelled = stop.Token.Register(() =>
        { lock (_controlGate) _rasterWork.Remove(work); work.Completion.TrySetCanceled(stop.Token); });
        try
        { work.Attachment.Pulse(Interlocked.Increment(ref _controlPulse)); await work.Completion.Task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !snapshot.Retirement.IsCancellationRequested && !work.Attachment.ControlLifetime.IsCancellationRequested)
        { throw new TimeoutException("Raster control expired before presentation."); }
        finally { lock (_controlGate) _rasterWork.Remove(work); }
    }

    private sealed record RasterDraft(Guid Exposure, string Slot, long ImageId, double Width, double Height, XsrSemanticId Command, string Argument);
    private sealed record RasterWork(SidecarHostSession Owner, XsrNxmlControlSnapshot Snapshot, RasterDraft[] Bindings, Session Attachment, CancellationTokenSource Stop)
    { internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class RasterBinding
    {
        internal RasterDraft Value;
        internal DesktopSidecarRasterPool.Resource Resource;
        internal XsrUiEntityId Node;
        internal readonly object InputGate = new();
        internal readonly CancellationTokenSource Stop;
        internal readonly CancellationToken Lifetime;
        internal RasterObservation? Latest;
        internal bool Running, Gap;
        internal RasterBinding(RasterDraft value, DesktopSidecarRasterPool.Resource resource, XsrUiEntityId node, CancellationToken attachment)
        { Value = value; Resource = resource; Node = node; Stop = CancellationTokenSource.CreateLinkedTokenSource(attachment); Lifetime = Stop.Token; }
    }
    private sealed record RasterObservation(string Kind, string Phase, double X, double Y, string Text, CancellationToken Dialog);

    private sealed partial class Session
    {
        internal readonly DesktopSidecarRasterPool _rasterPool = new();
        internal int _rasterCallbacks;
        private readonly Dictionary<SidecarHostSession, RasterBinding[]> _rasters = [];
        private readonly NxmlDynamicContent _rasterContent;
        private void ProcessRasterControls()
        {
            foreach (var (owner, bindings) in _rasters.ToArray())
            {
                var live = bindings.Where(binding => _rasterPool.IsLive(binding.Resource)
                    && _owner.Runtime.CanDispatchCallback(binding.Value.Exposure, binding.Value.Command)).ToArray();
                if (live.Length == 0) _rasters.Remove(owner); else _rasters[owner] = live;
                foreach (var removed in bindings.Except(live)) RemoveRaster(removed);
                foreach (var binding in live)
                    if (!_shell.Tree.IsAlive(binding.Node))
                    {
                        try { binding.Node = CreateRasterNode(binding.Value, binding.Resource, ResolveRasterSlot(binding.Value)); }
                        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                        { _warn("Raster slot is unavailable: " + error.GetType().Name); }
                    }
            }
            RasterWork[] work;
            lock (_owner._controlGate)
            { work = _owner._rasterWork.Where(item => item.Attachment == this).ToArray(); foreach (var item in work) _owner._rasterWork.Remove(item); }
            foreach (var item in work)
            {
                if (item.Completion.Task.IsCompleted) continue;
                try { ApplyRasterSet(item); item.Completion.TrySetResult(); }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { item.Completion.TrySetException(error); }
            }
        }

        private void ApplyRasterSet(RasterWork item)
        {
            item.Stop.Token.ThrowIfCancellationRequested(); _stop.Token.ThrowIfCancellationRequested();
            _ = item.Owner.CapturePresentation(item.Snapshot.Revision); item.Snapshot.Retirement.ThrowIfCancellationRequested();
            var prior = _rasters.GetValueOrDefault(item.Owner) ?? [];
            var others = _rasters.Where(pair => pair.Key != item.Owner).SelectMany(pair => pair.Value).ToArray();
            if (others.Length + item.Bindings.Length > 64) throw new InvalidOperationException("Raster binding count exceeded.");
            var slots = item.Bindings.Select(ResolveRasterSlot).ToArray();
            var resources = _rasterPool.Acquire(item.Owner, item.Bindings.Select(value => value.ImageId).ToArray(), item.Stop.Token);
            List<RasterBinding> created = []; bool committed = false;
            try
            {
                long pixels = 0;
                for (int i = 0; i < resources.Length; i++)
                {
                    var image = resources[i].Image!; var value = item.Bindings[i];
                    if (image.Width != Math.Ceiling(value.Width) || image.Height != Math.Ceiling(value.Height))
                        throw new InvalidOperationException("Raster dimensions do not match.");
                    pixels += (long)image.Width * image.Height;
                }
                if (pixels > 8L * 1024 * 1024 || pixels + others.Sum(binding => (long)Math.Ceiling(binding.Value.Width) * (long)Math.Ceiling(binding.Value.Height)) > 32L * 1024 * 1024)
                    throw new InvalidOperationException("Raster pixel budget exceeded.");
                RasterBinding[] next = new RasterBinding[item.Bindings.Length];
                for (int i = 0; i < next.Length; i++)
                {
                    var value = item.Bindings[i];
                    var reused = prior.FirstOrDefault(binding => _shell.Tree.IsAlive(binding.Node) && binding.Value with { ImageId = value.ImageId } == value);
                    if (reused is not null) next[i] = reused;
                    else { next[i] = new(value, resources[i], CreateRasterNode(value, resources[i], slots[i]), ControlLifetime); created.Add(next[i]); }
                }
                item.Stop.Token.ThrowIfCancellationRequested(); item.Snapshot.Retirement.ThrowIfCancellationRequested();
                var oldResources = prior.Select(binding => binding.Resource).ToArray();
                for (int i = 0; i < next.Length; i++)
                {
                    next[i].Value = item.Bindings[i]; next[i].Resource = resources[i];
                    _shell.Tree.SetComponent(next[i].Node, new XsrUiRasterImage(resources[i].Image!, []) { FitToBounds = true });
                }
                if (next.Length == 0) _rasters.Remove(item.Owner); else _rasters[item.Owner] = next;
                committed = true;
                foreach (var binding in prior.Except(next)) RemoveRasterNode(binding);
                foreach (var resource in oldResources) _rasterPool.Unpin(resource);
            }
            finally
            {
                if (!committed)
                { foreach (var binding in created) RemoveRasterNode(binding); foreach (var resource in resources) _rasterPool.Unpin(resource); }
            }
        }

        private XsrUiEntityId ResolveRasterSlot(RasterDraft value)
        {
            if (!_owner.Runtime.CanDispatchCallback(value.Exposure, value.Command)) throw new InvalidOperationException("Raster document retired.");
            var slots = _shell.Tree.LiveEntities.Where(node => _shell.Tree.Name(node) == value.Slot
                && _shell.Tree.GetComponent<NxmlDynamicSlot>(node) is not null && RasterExposure(node) == value.Exposure).ToArray();
            return slots.Length == 1 ? slots[0] : throw new InvalidOperationException("Raster requires one owned Dynamic slot.");
        }
        private Guid RasterExposure(XsrUiEntityId node)
        {
            if (_coordinator.TryGetExposure(node, out var exposure)) return exposure;
            foreach (var page in _pages) if (page.Value.Owns(node)) return page.Key;
            foreach (var dialog in _dialogs) if (dialog.Value.Document.Owns(node)) return dialog.Key;
            return default;
        }
        private XsrUiEntityId CreateRasterNode(RasterDraft value, DesktopSidecarRasterPool.Resource resource, XsrUiEntityId slot)
        {
            var node = _rasterContent.Append(slot, DesktopMarkup.Dynamic("SidecarRaster.d.nxml", new Dictionary<string, string>
            {
                ["key"] = "SidecarRaster_" + Guid.NewGuid().ToString("N"),
                ["label"] = "Canvas",
                ["width"] = value.Width.ToString("R", CultureInfo.InvariantCulture),
                ["height"] = value.Height.ToString("R", CultureInfo.InvariantCulture)
            }));
            _shell.Tree.SetComponent(node, new XsrUiRasterImage(resource.Image!, []) { FitToBounds = true });
            return node;
        }
        private void RemoveRaster(RasterBinding binding) { RemoveRasterNode(binding); _rasterPool.Unpin(binding.Resource); }
        private void RemoveRasterNode(RasterBinding binding)
        { binding.Stop.Cancel(); binding.Stop.Dispose(); _rasterContent.Remove(binding.Node); }
        private void DisposeRasters()
        {
            _shell.Renderer.InputObserved -= RasterObserved;
            foreach (var binding in _rasters.Values.SelectMany(bindings => bindings)) RemoveRaster(binding);
            _rasters.Clear(); _rasterPool.Dispose();
        }
    }
}
