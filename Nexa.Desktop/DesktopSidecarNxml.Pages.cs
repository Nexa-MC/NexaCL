using Nexa.Desktop.Ui;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    internal static bool TryHttpsLink(string value, out Uri? uri)
    {
        uri = null;
        return value.Length is > 0 and <= 2048 && !value.Any(char.IsControl)
            && Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.UserInfo.Length == 0 && uri.Host.Length > 0;
    }
    internal static bool CanCopyText(string value) => value.Length is > 0 and <= 4096 && !value.Any(char.IsControl);

    private sealed partial class Session
    {
        private static readonly XsrSemanticId OpenLink = XsrSemanticId.Parse("ui.sidecar.link.open");
        private static readonly XsrSemanticId OpenPage = XsrSemanticId.Parse("ui.sidecar.page.open");
        private static readonly XsrSemanticId ContinueOnboarding = XsrSemanticId.Parse("ui.sidecar.onboarding.continue");
        private readonly Dictionary<Guid, PageInstance> _pages = [];
        private readonly HashSet<Guid> _completed = [];
        private readonly XsrUiEntityId _onboardingFallback;
        private Guid _onboarding, _opened;
        private XsrUiEntityId _guideStart, _guideEnd;
        private bool _guideBegan, _guideHasPages;
        private XsrUiEntityId _returnPage;
        private XsrSemanticId _returnNavigation;

        private void UpdatePages(IReadOnlyList<XsrNxmlModuleView> modules)
        {
            HashSet<Guid> keep = [];
            foreach (var module in modules.Where(module => module.Source.Operation is SidecarNxmlOperation.Page or SidecarNxmlOperation.OnboardingPage))
            {
                string historyKey = DesktopOnboardingHistory.Identity(module.PackageName, module.Source.Target, module.Source.OnboardingRevision);
                if (_completed.Contains(module.Exposure)) continue;
                keep.Add(module.Exposure);
                try
                {
                    var document = (NxmlPreparedDocument)module.Presentation.Artifact;
                    if (_pages.TryGetValue(module.Exposure, out var existing)) existing.Reload(document);
                    else _pages.Add(module.Exposure, new(_shell.Tree, _store, module.Source.Operation, document, historyKey));
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { _owner.Runtime.RejectPresentation(module.Exposure); _warn("NXML page rejected: " + error.Message); }
            }
            foreach (var key in _pages.Keys.Where(key => !keep.Contains(key)).ToArray()) RetirePage(key);
        }

        private bool TryGetPageExposure(XsrUiEntityId source, out Guid exposure)
        {
            foreach (var page in _pages)
                if (_shell.Stage.Navigation.Current == page.Value.Root && page.Value.Owns(source))
                { exposure = page.Key; return true; }
            exposure = default; return false;
        }

        private bool PageIntent(Guid exposure, DesktopUiIntent intent)
        {
            if (intent.Command.Value == "ui.sidecar.theme.import")
            {
                string path = _shell.Renderer.ReadIntentArgument(intent.Source);
                if (!_importing && _importTheme is not null && path.Length is > 0 and <= 8192
                    && !path.Any(char.IsControl) && Path.IsPathFullyQualified(path) && !path.StartsWith("\\\\", StringComparison.Ordinal)
                    && _pages.TryGetValue(exposure, out var importPage))
                    _ = ImportTheme(path, importPage.Stop.Token);
                return true;
            }
            if (intent.Command.Value == "ui.sidecar.clipboard.copy")
            {
                string text = _shell.Renderer.ReadIntentArgument(intent.Source);
                if (CanCopyText(text) && _copyText is not null) _ = CopyText(text);
                return true;
            }
            if (intent.Command == OpenLink)
            {
                string value = _shell.Renderer.ReadIntentArgument(intent.Source);
                if (TryHttpsLink(value, out var uri) && _openLink is not null)
                {
                    try { _openLink(uri!); }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                    { _warn("Unable to open Sidecar browser link."); }
                }
                return true;
            }
            if (intent.Command == OpenPage)
            {
                string target = _shell.Renderer.ReadIntentArgument(intent.Source);
                if (_owner.Runtime.TryResolvePage(exposure, target, out var module)
                    && _pages.TryGetValue(module!.Exposure, out var page))
                {
                    if (_opened == default || !_pages.TryGetValue(_opened, out var prior) || _shell.Stage.Navigation.Current != prior.Root)
                    { _returnPage = _shell.Stage.Navigation.Current; _returnNavigation = _shell.SelectedNavigationId; }
                    _opened = module.Exposure;
                    _shell.Stage.Navigation.Replace(page.Root);
                    if (_shell.Tree.GetComponent<XsrUiSemantic>(intent.Source)?.Role == XsrUiSemanticRole.NavigationItem)
                        _shell.SelectAuxiliaryNavigation(intent.Source);
                }
                return true;
            }
            if (intent.Command == ContinueOnboarding)
            {
                if (_onboarding == exposure && _pages.TryGetValue(exposure, out var page)
                    && _shell.Stage.Navigation.Current == page.Root)
                {
                    try { OnboardingHistory?.Complete(page.HistoryKey); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { _warn("Unable to save onboarding completion."); return true; }
                    _completed.Add(exposure); RetirePage(exposure); AdvanceOnboarding();
                }
                return true;
            }
            return false;
        }

        private async Task CopyText(string text)
        {
            try { await _copyText!(text).ConfigureAwait(false); }
            catch (Exception error) when (error is InvalidOperationException or IOException or System.Runtime.InteropServices.ExternalException)
            { _warn("Unable to copy extension text to the clipboard."); }
        }

        private async Task ImportTheme(string path, CancellationToken pageStop)
        {
            _importing = true;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, pageStop);
            try
            {
                var result = await _importTheme!(path, stop.Token).ConfigureAwait(false);
                if (!stop.IsCancellationRequested)
                {
                    if (result.IsSuccess) _imported?.Invoke();
                    else _warn(result.Error?.Message ?? "Theme import failed.");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
            { if (!stop.IsCancellationRequested) _warn(error.Message); }
            finally { _importing = false; }
        }
        private void OpenExternal()
        {
            if (!_owner._external.TryPeek(out var request)) return;
            if (Environment.TickCount64 >= request.Expires)
            {
                _owner._external.TryDequeue(out _); _warn("Extension page is not available. Install or enable its Sidecar first."); return;
            }
            var matches = _snapshot?.Modules.Where(module => module.Source.Operation == SidecarNxmlOperation.Page
                && module.Source.Target == "#" + request.Page && _pages.ContainsKey(module.Exposure)).ToArray() ?? [];
            if (matches.Length == 0) return;
            _owner._external.TryDequeue(out _);
            if (matches.Length != 1) { _warn("Ambiguous extension page."); return; }
            var module = matches[0]; var page = _pages[module.Exposure];
            XsrUiCommandBinding? binding = null;
            int count = 0;
            _shell.Tree.Walk(page.Root, entity =>
            {
                if (_shell.Tree.Name(entity) == "ExternalActivation") { binding = _shell.Tree.GetComponent<XsrUiCommandBinding>(entity); count++; }
                return true;
            });
            if (count != 1 || binding is null || _owner.Runtime.ReadCommandRoute(module.Exposure, binding.Command) != XsrNxmlCommandRoute.Sidecar)
            { _warn("Extension page does not accept external activation."); return; }
            if (_shell.Stage.Navigation.Current != page.Root)
            { _returnPage = _shell.Stage.Navigation.Current; _returnNavigation = _shell.SelectedNavigationId; }
            _opened = module.Exposure; _shell.Stage.Navigation.Replace(page.Root);
            _ = Dispatch(module.Exposure, binding.Command, request.Argument, page.Stop.Token);
        }

        private bool GuideIntent(DesktopUiIntent intent)
        {
            bool begin = intent.Command.Value == "ui.onboarding.begin" && _shell.Stage.Navigation.Current == _guideStart;
            bool finish = intent.Command.Value == "ui.onboarding.finish" && _shell.Stage.Navigation.Current == _guideEnd;
            if ((!begin && !finish) || !_shell.Tree.IsAlive(intent.Source)
                || _shell.Tree.GetComponent<XsrUiCommandBinding>(intent.Source)?.Command != intent.Command) return false;
            bool owns = false;
            _shell.Tree.Walk(begin ? _guideStart : _guideEnd, node => { if (node == intent.Source) owns = true; return true; });
            if (!owns) return false;
            try
            {
                if (begin)
                {
                    OnboardingHistory?.BeginBatch();
                    _guideBegan = _guideHasPages = true;
                    AdvanceOnboarding();
                }
                else
                {
                    OnboardingHistory?.FinishBatch();
                    _guideBegan = _guideHasPages = false;
                    _shell.Stage.Navigation.Replace(_onboardingFallback);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _warn("Unable to save onboarding progress."); }
            return true;
        }

        private void AdvanceOnboarding()
        {
            if (!_shell.Tree.IsAlive(_onboardingFallback) || _onboarding != default) return;
            if (_firstRun is not null && (!_firstRun.ExtensionsRequested || !_firstRun.ExtensionsReady)) return;
            var pending = _pages.FirstOrDefault(page => page.Value.Operation == SidecarNxmlOperation.OnboardingPage
                && !_completed.Contains(page.Key) && OnboardingHistory?.IsComplete(page.Value.HistoryKey) != true);
            if (_firstRun is not null)
            {
                if (pending.Value is not null)
                { _onboarding = pending.Key; _shell.Stage.Navigation.Replace(pending.Value.Root); }
                else
                {
                    _firstRun.CompleteExtensions();
                    if (_shell.Stage.Navigation.Current != _onboardingFallback) _shell.Stage.Navigation.Replace(_onboardingFallback);
                }
                return;
            }
            if (OnboardingHistory is not null && !_guideBegan && (pending.Value is not null || OnboardingHistory.HasPendingBatch))
            {
                if (!_shell.Tree.IsAlive(_guideStart)) _guideStart = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("SidecarOnboardingStart.nxml"), _shell.Tree, _store);
                if (_shell.Stage.Navigation.Current != _guideStart) _shell.Stage.Navigation.Replace(_guideStart);
                return;
            }
            if (pending.Value is not null)
            {
                _guideHasPages = true; _onboarding = pending.Key;
                _shell.Stage.Navigation.Replace(pending.Value.Root);
                return;
            }
            if (OnboardingHistory is not null && _guideBegan && _guideHasPages)
            {
                if (!_shell.Tree.IsAlive(_guideEnd)) _guideEnd = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("SidecarOnboardingEnd.nxml"), _shell.Tree, _store);
                if (_shell.Stage.Navigation.Current != _guideEnd) _shell.Stage.Navigation.Replace(_guideEnd);
            }
        }

        private void RetirePage(Guid exposure)
        {
            if (!_pages.Remove(exposure, out var page)) return;
            page.Stop.Cancel();
            if (_shell.Stage.Navigation.Current == page.Root)
            {
                var fallback = exposure == _onboarding ? _onboardingFallback : _returnPage;
                if (_shell.Tree.IsAlive(fallback)) _shell.Stage.Navigation.Replace(fallback);
                if (exposure == _opened) _shell.Select(_returnNavigation);
            }
            if (exposure == _opened) _opened = default;
            if (exposure == _onboarding) _onboarding = default;
            page.Dispose();
            page.Stop.Dispose();
        }

        private void DisposePages()
        {
            foreach (var key in _pages.Keys.ToArray()) RetirePage(key);
            if (_shell.Tree.IsAlive(_guideStart)) _shell.Tree.Destroy(_guideStart);
            if (_shell.Tree.IsAlive(_guideEnd)) _shell.Tree.Destroy(_guideEnd);
        }
    }

    private sealed class PageInstance : NxmlDocumentInstance
    {
        internal PageInstance(XsrUiTree tree, XsrStateStore store, SidecarNxmlOperation operation, NxmlPreparedDocument document, string historyKey)
            : base(tree, store, document)
        {
            HistoryKey = historyKey; Operation = operation;
            List<XsrUiEntityId> observers = [];
            tree.Walk(Root, node => { if (tree.GetComponent<NxmlScrollObservation>(node) is not null) observers.Add(node); return true; });
            ScrollObservers = observers.ToArray();
            XsrUiTransition.ConfigureIndependent(tree, Root, "sidecar-page-" + Guid.NewGuid().ToString("N"), distance: 8);
        }
        internal string HistoryKey { get; }
        internal SidecarNxmlOperation Operation { get; }
        internal IReadOnlyList<XsrUiEntityId> ScrollObservers { get; }
        internal CancellationTokenSource Stop { get; } = new();
    }
}
