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
internal sealed partial class DesktopSidecarNxml
{
    private readonly ConditionalWeakTable<SidecarNxmlDocument, NxmlExtensionProgram> _programs = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Page, string Argument, long Expires)> _external = new();
    internal void EnqueueExternal(string uri)
    {
        if (DesktopActivation.TryExtension(uri, out var page, out var argument) && _external.Count < 16)
            _external.Enqueue((page, argument, Environment.TickCount64 + 30000));
    }
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
            if (source.Operation is SidecarNxmlOperation.Page or SidecarNxmlOperation.OnboardingPage
                && (document.Root.Root.Role != XsrUiSemanticRole.Page || document.Root.Root.Key != source.Target[1..]))
                throw new NxmlCompileException("Standalone pages require a Page root matching their target key.");
            NxmlUiLoader.ValidateBindings(document.Root, store);
            foreach (var fragment in document.Fragments) NxmlUiLoader.ValidateBindings(fragment.Content, store);
            return new(document, document.Commands);
        });
    }

    internal IDisposable Attach(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store,
        Action<string> warn, XsrUiEntityId onboardingFallback = default, Action<Uri>? openLink = null, Func<string, CancellationToken, Task<XsrResult>>? importTheme = null, Action? imported = null, Func<string, Task>? copyText = null, DesktopOnboardingHistory? onboarding = null)
        => new Session(this, shell, intents, store, warn, onboardingFallback, openLink, importTheme, imported, copyText, onboarding);

    private sealed partial class Session : IDisposable
    {
        private readonly DesktopSidecarNxml _owner;
        private readonly XsrUiShell _shell;
        private readonly IDisposable _route;
        private readonly XsrStateStore _store;
        private readonly Action<string> _warn;
        private readonly Action<Uri>? _openLink;
        private readonly Func<string, Task>? _copyText;
        private readonly DesktopOnboardingHistory? _onboardingHistory;
        private readonly Func<string, CancellationToken, Task<XsrResult>>? _importTheme;
        private readonly Action? _imported;
        private bool _importing;
        private readonly NxmlPresentationCoordinator _coordinator;
        private XsrNxmlModuleSnapshot? _snapshot;
        private IReadOnlyList<NxmlPresentationLayer> _layers = [];
        private readonly CancellationTokenSource _stop = new();
        private long _styledStructure = -1;

        internal Session(DesktopSidecarNxml owner, XsrUiShell shell, DesktopUiIntentSink intents,
            XsrStateStore store, Action<string> warn, XsrUiEntityId onboardingFallback, Action<Uri>? openLink, Func<string, CancellationToken, Task<XsrResult>>? importTheme, Action? imported, Func<string, Task>? copyText, DesktopOnboardingHistory? onboarding)
        {
            _onboardingHistory = onboarding; _copyText = copyText; _importTheme = importTheme; _imported = imported; _owner = owner; _shell = shell; _store = store; _warn = warn; _openLink = openLink;
            _onboardingFallback = onboardingFallback;
            _coordinator = new(shell.Tree, store);
            shell.Renderer.PresentationPreparing += Frame;
            shell.StyleChanged += StyleChanged;
            _route = intents.Route(Intent);
        }
        private void Frame(object? sender, EventArgs args)
        {
            var read = _store.Read<XsrNxmlModuleSnapshot>(_owner.State);
            if (read.IsAvailable && read.HasValue && !ReferenceEquals(_snapshot, read.Value))
            {
                _snapshot = read.Value;
                _styledStructure = -1;
                UpdatePages(_snapshot!.Modules);
                _layers = _snapshot.Modules.Where(module => module.Source.Operation <= SidecarNxmlOperation.Replace).Select(module => new NxmlPresentationLayer(module.Exposure,
                    module.Source.Target, (XsrUiMountOperation)((int)module.Source.Operation - 1),
                    (NxmlPreparedDocument)module.Presentation.Artifact)).ToArray();
            }
            try
            {
                _coordinator.Update(_layers);
                if (_styledStructure != _shell.Tree.StructureRevision)
                {
                    foreach (var node in _shell.Tree.LiveEntities)
                        if (_shell.Tree.GetComponent<XsrUiSemantic>(node)?.Role == XsrUiSemanticRole.NavigationItem
                            && _coordinator.TryGetExposure(node, out _)) _shell.PrepareAuxiliaryNavigation(node);
                    _styledStructure = _shell.Tree.StructureRevision;
                }
            }
            catch (NxmlPresentationException error)
            { _owner.Runtime.RejectPresentation(error.Exposure); _warn("NXML presentation rejected: " + error.InnerException?.Message); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _warn("NXML presentation could not be mounted: " + error.Message); }
            AdvanceOnboarding();
            ObserveScroll();
            OpenExternal();
        }
        private void StyleChanged(object? sender, EventArgs args) => _styledStructure = -1;
        private bool Intent(DesktopUiIntent intent)
        {
            if (GuideIntent(intent)) return true;
            if (!_coordinator.TryGetExposure(intent.Source, out var exposure) && !TryGetPageExposure(intent.Source, out exposure)) return false;
            var route = _owner.Runtime.ReadCommandRoute(exposure, intent.Command);
            if (route == XsrNxmlCommandRoute.Inactive) return true;
            if (PageIntent(exposure, intent)) return true;
            if (route == XsrNxmlCommandRoute.Host) return false;
            string value = _shell.Renderer.ReadIntentArgument(intent.Source);
            var pageStop = _pages.TryGetValue(exposure, out var page) ? page.Stop.Token : _stop.Token;
            _ = Dispatch(exposure, intent.Command, value, pageStop);
            return true;
        }
        private async Task Dispatch(Guid exposure, Nexa.Xsr.XsrSemanticId command, string value, CancellationToken pageStop)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, pageStop);
            try { await _owner.Runtime.DispatchAsync(exposure, command, value, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _warn("NXML action failed: " + error.Message); }
        }
        public void Dispose()
        {
            _shell.Renderer.PresentationPreparing -= Frame; _route.Dispose();
            _shell.StyleChanged -= StyleChanged;
            _stop.Cancel(); _coordinator.Dispose(); _stop.Dispose();
            DisposePages();
        }
    }
}
