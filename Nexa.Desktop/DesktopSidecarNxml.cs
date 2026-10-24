using System.Runtime.CompilerServices;
using Nexa.Desktop.Ui;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

/// <summary>One composition edge for all current and future NXML pages, controls and components.</summary>
internal sealed class DesktopSidecarNxml
{
    private readonly ConditionalWeakTable<SidecarNxmlDocument, NxmlExtensionProgram> _programs = new();
    internal XsrNxmlModuleRuntime Runtime { get; }
    internal XsrStateId State { get; }
    internal DesktopSidecarNxml(XsrStateStore store)
    {
        State = store.Resolve(SidecarUiPresentationState.Nxml);
        Runtime = new(store, State, (source, data) =>
        {
            NxmlPresentationCoordinator.ValidateTarget(source.Target);
            var program = _programs.GetValue(source, item => new(item.EntryPoint, item.Files, styles: DesktopMarkup.SharedStyles));
            var document = program.Compile(data);
            NxmlUiLoader.ValidateBindings(document.Root, store);
            foreach (var fragment in document.Fragments) NxmlUiLoader.ValidateBindings(fragment.Content, store);
            return new(document, document.Commands);
        });
    }

    internal IDisposable Attach(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store,
        Action<string> warn) => new Session(this, shell, intents, store, warn);

    private sealed class Session : IDisposable
    {
        private readonly DesktopSidecarNxml _owner;
        private readonly XsrUiShell _shell;
        private readonly IDisposable _route;
        private readonly XsrStateStore _store;
        private readonly Action<string> _warn;
        private readonly NxmlPresentationCoordinator _coordinator;
        private XsrNxmlModuleSnapshot? _snapshot;
        private IReadOnlyList<NxmlPresentationLayer> _layers = [];
        private readonly CancellationTokenSource _stop = new();

        internal Session(DesktopSidecarNxml owner, XsrUiShell shell, DesktopUiIntentSink intents,
            XsrStateStore store, Action<string> warn)
        {
            _owner = owner; _shell = shell; _store = store; _warn = warn;
            _coordinator = new(shell.Tree, store);
            shell.Renderer.PresentationPreparing += Frame;
            _route = intents.Route(Intent);
        }
        private void Frame(object? sender, EventArgs args)
        {
            var read = _store.Read<XsrNxmlModuleSnapshot>(_owner.State);
            if (read.IsAvailable && read.HasValue && !ReferenceEquals(_snapshot, read.Value))
            {
                _snapshot = read.Value;
                _layers = _snapshot!.Modules.Select(module => new NxmlPresentationLayer(module.Exposure,
                    module.Source.Target, (XsrUiMountOperation)((int)module.Source.Operation - 1),
                    (NxmlPreparedDocument)module.Presentation.Artifact)).ToArray();
            }
            try { _coordinator.Update(_layers); }
            catch (NxmlPresentationException error)
            { _owner.Runtime.RejectPresentation(error.Exposure); _warn("NXML presentation rejected: " + error.InnerException?.Message); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _warn("NXML presentation could not be mounted: " + error.Message); }
        }
        private bool Intent(DesktopUiIntent intent)
        {
            if (!_coordinator.TryGetExposure(intent.Source, out var exposure)) return false;
            var route = _owner.Runtime.ReadCommandRoute(exposure, intent.Command);
            if (route == XsrNxmlCommandRoute.Host) return false;
            if (route == XsrNxmlCommandRoute.Inactive) return true;
            string value = _shell.Renderer.ReadIntentArgument(intent.Source);
            _ = Dispatch(exposure, intent.Command, value);
            return true;
        }
        private async Task Dispatch(Guid exposure, Nexa.Xsr.XsrSemanticId command, string value)
        {
            try { await _owner.Runtime.DispatchAsync(exposure, command, value, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _warn("NXML action failed: " + error.Message); }
        }
        public void Dispose()
        {
            _shell.Renderer.PresentationPreparing -= Frame; _route.Dispose();
            _stop.Cancel(); _coordinator.Dispose(); _stop.Dispose();
        }
    }
}
