using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private readonly List<ShortcutWork> _shortcutWork = [];
    internal int PendingShortcutControls { get { lock (_controlGate) return _shortcutWork.Count; } }
    internal int ActiveShortcutCallbacks { get { lock (_controlGate) return _attached?.ActiveShortcutCallbacks ?? 0; } }
    internal bool HandleShortcut(string gesture)
    {
        Session? attachment;
        lock (_controlGate) attachment = _attached;
        return attachment?.HandleShortcut(gesture) == true;
    }

    internal async ValueTask ReplaceShortcutsAsync(SidecarHostSession session, HostShortcutSet input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Bindings is null || input.Bindings.Length > 64) throw new ArgumentException("Invalid shortcut count.");
        var source = input.Bindings.ToArray();
        var snapshot = session.CapturePresentation(input.ExpectedRevision);
        List<ShortcutDraft> bindings = [];
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var item in source)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Gesture) || item.Gesture.Length > 64 || item.Gesture.Any(char.IsControl)
                || item.Argument is null || item.Argument.Length > 32768 || string.IsNullOrWhiteSpace(item.Command))
                throw new ArgumentException("Invalid shortcut binding.");
            bytes += Encoding.UTF8.GetByteCount(item.Argument);
            if (bytes > 256 * 1024) throw new ArgumentException("Shortcut argument budget exceeded.");
            var document = snapshot.Documents.SingleOrDefault(document => document.Index == item.Document);
            var command = XsrSemanticId.Parse(item.Command);
            if (document is null || document.Source.Operation is SidecarNxmlOperation.OnboardingPage or SidecarNxmlOperation.Dialog
                || !Runtime.CanDispatchCallback(document.Exposure, command))
                throw new InvalidOperationException("Shortcuts require an owned ordinary document and registered form callback.");
            string gesture = item.Gesture.Trim();
            if (!unique.Add(document.Exposure.ToString("N") + gesture)) throw new ArgumentException("Duplicate document gesture.");
            bindings.Add(new(document.Exposure, gesture, command, item.Argument));
        }
        ShortcutWork work;
        lock (_controlGate)
        {
            token.ThrowIfCancellationRequested();
            if (_attached is null) throw new InvalidOperationException("Presentation is not attached.");
            if (_shortcutWork.Count >= 64 || _shortcutWork.Count(item => item.Owner == session) >= 8)
                throw new InvalidOperationException("Shortcut control queue is full.");
            work = new(session, snapshot, bindings.ToArray(), _attached,
                CancellationTokenSource.CreateLinkedTokenSource(token, snapshot.Retirement, _attached.ControlLifetime));
            _shortcutWork.Add(work);
        }
        using var stop = work.Stop;
        stop.CancelAfter(TimeSpan.FromSeconds(5));
        using var cancelled = stop.Token.Register(() =>
        {
            lock (_controlGate) _shortcutWork.Remove(work);
            work.Completion.TrySetCanceled(stop.Token);
        });
        try
        {
            work.Attachment.Pulse(Interlocked.Increment(ref _controlPulse));
            await work.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !snapshot.Retirement.IsCancellationRequested && !work.Attachment.ControlLifetime.IsCancellationRequested)
        { throw new TimeoutException("Shortcut control expired before presentation."); }
        finally { lock (_controlGate) _shortcutWork.Remove(work); }
    }

    private sealed record ShortcutDraft(Guid Exposure, string Gesture, XsrSemanticId Command, string Argument);
    private sealed record ShortcutWork(SidecarHostSession Owner, XsrNxmlControlSnapshot Snapshot, ShortcutDraft[] Bindings,
        Session Attachment, CancellationTokenSource Stop)
    { internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class ShortcutBinding(ShortcutDraft value, CancellationToken attachment) : IDisposable
    {
        internal ShortcutDraft Value { get; } = value;
        internal CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(attachment);
        internal int Running;
        public void Dispose() { Stop.Cancel(); Stop.Dispose(); }
    }

    private sealed partial class Session
    {
        private readonly Dictionary<SidecarHostSession, ShortcutBinding[]> _shortcuts = [];
        private int _shortcutCalls;
        internal int ActiveShortcutCallbacks => Volatile.Read(ref _shortcutCalls);
        private void PruneShortcuts()
        {
            foreach (var (owner, bindings) in _shortcuts.ToArray())
            {
                var live = bindings.Where(item => owner.State == SidecarSessionState.Active
                    && _owner.Runtime.CanDispatchCallback(item.Value.Exposure, item.Value.Command)).ToArray();
                if (live.Length == 0) _shortcuts.Remove(owner); else _shortcuts[owner] = live;
                foreach (var removed in bindings.Except(live)) removed.Dispose();
            }
        }
        private void ProcessShortcutControls()
        {
            PruneShortcuts();
            ShortcutWork[] work;
            lock (_owner._controlGate)
            {
                work = _owner._shortcutWork.Where(item => item.Attachment == this).ToArray();
                foreach (var item in work) _owner._shortcutWork.Remove(item);
            }
            foreach (var item in work)
            {
                if (item.Completion.Task.IsCompleted) continue;
                try
                {
                    item.Stop.Token.ThrowIfCancellationRequested();
                    _ = item.Owner.CapturePresentation(item.Snapshot.Revision);
                    item.Snapshot.Retirement.ThrowIfCancellationRequested(); _stop.Token.ThrowIfCancellationRequested();
                    if (item.Bindings.Any(binding => !_owner.Runtime.CanDispatchCallback(binding.Exposure, binding.Command)))
                        throw new InvalidOperationException("Shortcut source retired.");
                    var prior = _shortcuts.GetValueOrDefault(item.Owner) ?? [];
                    if (_shortcuts.Values.Sum(bindings => bindings.Length) - prior.Length + item.Bindings.Length > 512)
                        throw new InvalidOperationException("Shortcut binding quota exceeded.");
                    var next = item.Bindings.Select(value => prior.FirstOrDefault(binding => Same(binding.Value, value))
                        ?? new ShortcutBinding(value, _stop.Token)).ToArray();
                    if (next.Length == 0) _shortcuts.Remove(item.Owner); else _shortcuts[item.Owner] = next;
                    foreach (var removed in prior.Except(next)) removed.Dispose();
                    item.Completion.TrySetResult();
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { item.Completion.TrySetException(error); }
            }
            static bool Same(ShortcutDraft a, ShortcutDraft b) => a.Exposure == b.Exposure && a.Command == b.Command
                && a.Argument == b.Argument && string.Equals(a.Gesture, b.Gesture, StringComparison.OrdinalIgnoreCase);
        }

        internal bool HandleShortcut(string gesture)
        {
            if (_stop.IsCancellationRequested || string.IsNullOrWhiteSpace(gesture) || gesture.Length > 64
                || _onboarding != default || _firstRun is { Completed: false }
                || _shell.Stage.Navigation.Current == _guideStart || _shell.Stage.Navigation.Current == _guideEnd
                || _shell.Stage.Overlays.Any(overlay => _shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal == true)
                || _shell.Tree.IsAlive(_shell.Renderer.Focused) && _shell.Tree.GetComponent<XsrUiTextInput>(_shell.Renderer.Focused) is not null)
                return false;
            PruneShortcuts();
            var matches = _shortcuts.OrderBy(pair => pair.Key.PluginName, StringComparer.Ordinal).ThenBy(pair => pair.Key.SessionId)
                .SelectMany(pair => pair.Value).Where(binding => string.Equals(binding.Value.Gesture, gesture.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) return false;
            var visible = VisibleShortcutScopes();
            foreach (var binding in matches)
            {
                if (!visible.Contains(binding.Value.Exposure)) continue;
                if (Interlocked.CompareExchange(ref binding.Running, 1, 0) != 0) return true;
                if (Interlocked.Increment(ref _shortcutCalls) > 16)
                { Interlocked.Decrement(ref _shortcutCalls); Volatile.Write(ref binding.Running, 0); return true; }
                _ = DispatchShortcut(binding);
                return true;
            }
            return false;
        }

        private HashSet<Guid> VisibleShortcutScopes()
        {
            HashSet<Guid> visible = [];
            var current = _shell.Stage.Navigation.Current;
            foreach (var node in _shell.Tree.LiveEntities)
            {
                if (!_coordinator.TryGetExposure(node, out var exposure) && !TryGetPageExposure(node, out exposure)) continue;
                if (visible.Contains(exposure)) continue;
                for (var ancestor = node; ancestor.IsAssigned; ancestor = _coordinator.GetPresentationParent(ancestor))
                {
                    if (!_shell.Tree.IsAlive(ancestor) || _shell.Tree.GetComponent<XsrUiElement>(ancestor) is { IsVisible: false }) break;
                    if (ancestor == current) { visible.Add(exposure); break; }
                }
            }
            return visible;
        }

        private async Task DispatchShortcut(ShortcutBinding binding)
        {
            var stop = binding.Stop.Token;
            try
            {
                var value = binding.Value;
                var result = await _owner.Runtime.DispatchCallbackAsync(value.Exposure, value.Command, value.Argument, stop).ConfigureAwait(false);
                if (!result.IsSuccess && !stop.IsCancellationRequested && result.Error?.Kind != XsrErrorKind.Cancelled)
                    _warn("Sidecar shortcut failed: " + result.Error?.Code.Value);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _warn("Sidecar shortcut failed: " + error.GetType().Name); }
            finally { Interlocked.Decrement(ref _shortcutCalls); Volatile.Write(ref binding.Running, 0); }
        }
        private void DisposeShortcuts()
        {
            foreach (var binding in _shortcuts.Values.SelectMany(bindings => bindings)) binding.Dispose();
            _shortcuts.Clear();
        }
    }
}
