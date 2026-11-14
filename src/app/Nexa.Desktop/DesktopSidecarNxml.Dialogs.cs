using Nexa.Desktop.Ui;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private sealed partial class Session
    {
        private static readonly XsrSemanticId CloseDialogCommand = XsrSemanticId.Parse("ui.sidecar.dialog.close");
        private readonly Dictionary<Guid, DialogInstance> _dialogs = [];

        private void UpdateDialogs(IReadOnlyList<XsrNxmlModuleView> modules)
        {
            var sources = modules.Where(module => module.Source.Operation == SidecarNxmlOperation.Dialog).ToArray();
            foreach (var exposure in _dialogs.Keys.Where(key => !sources.Any(module => module.Exposure == key)).ToArray())
                RetireDialog(exposure);
            foreach (var module in sources)
            {
                try
                {
                    var compiled = (NxmlPreparedDocument)module.Presentation.Artifact;
                    if (_dialogs.TryGetValue(module.Exposure, out var current))
                    {
                        if (!ReferenceEquals(current.Compiled, compiled))
                        { current.Document.Reload(compiled.AsFragment()); current.Compiled = compiled; }
                    }
                    else _dialogs.Add(module.Exposure, CreateDialog(module, compiled));
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { RetireDialog(module.Exposure); _owner.Runtime.RejectPresentation(module.Exposure); _warn("NXML modal document rejected: " + error.Message); }
            }
        }

        private DialogInstance CreateDialog(XsrNxmlModuleView module, NxmlPreparedDocument compiled)
        {
            string suffix = module.Exposure.ToString("N");
            XsrUiEntityId chrome = default;
            NxmlDocumentInstance? document = null;
            try
            {
                var chromeProgram = new NxmlExtensionProgram("dialog.nxml",
                    new Dictionary<string, string> { ["dialog.nxml"] = DesktopMarkup.Read("SidecarDialog.d.nxml") }, styles: DesktopMarkup.SharedStyles);
                var chromeTemplate = chromeProgram.Compile(new Dictionary<string, string>
                {
                    ["chrome"] = "SidecarDialog_" + suffix,
                    ["close"] = "SidecarDialogClose_" + suffix,
                    ["body"] = "SidecarDialogBody_" + suffix,
                    ["owner"] = module.PackageName
                });
                chrome = NxmlUiLoader.LoadDetached(chromeTemplate.Root, _shell.Tree, _store);
                XsrUiEntityId body = default, close = default;
                _shell.Tree.Walk(chrome, node =>
                {
                    if (_shell.Tree.Name(node) == "SidecarDialogBody_" + suffix) body = node;
                    if (_shell.Tree.Name(node) == "SidecarDialogClose_" + suffix) close = node;
                    return true;
                });
                if (!body.IsAssigned || !close.IsAssigned) throw new InvalidOperationException("Modal chrome is incomplete.");
                document = new(_shell.Tree, _store, compiled.AsFragment()); document.Mount(body);
                return new(document, compiled, chrome, close);
            }
            catch
            {
                document?.Dispose(); if (_shell.Tree.IsAlive(chrome)) _shell.Tree.Destroy(chrome);
                throw;
            }
        }

        private bool DialogCloseIntent(DesktopUiIntent intent)
        {
            foreach (var dialog in _dialogs.Values)
            {
                if (intent.Source != dialog.Close) continue;
                if (dialog.Open && intent.Command == CloseDialogCommand && CanSubmit(intent)) CloseDialog(dialog);
                return true;
            }
            return false;
        }

        private bool TryGetDialogExposure(XsrUiEntityId source, out Guid exposure)
        {
            foreach (var dialog in _dialogs)
                if (dialog.Value.Open && dialog.Value.Document.Owns(source)) { exposure = dialog.Key; return true; }
            exposure = default; return false;
        }

        private bool ModalInputAllowed(XsrUiEntityId source)
        {
            var modal = _shell.Stage.Overlays.LastOrDefault(node => _shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true);
            bool insideModal = !modal.IsAssigned;
            for (var node = source; node.IsAssigned && _shell.Tree.IsAlive(node); node = _coordinator.GetPresentationParent(node))
            {
                if (_dialogs.Values.Any(dialog => dialog.Chrome == node && (!dialog.Open || !_shell.Stage.Overlays.Contains(dialog.Chrome)))) return false;
                if (node == modal) insideModal = true;
            }
            return insideModal;
        }

        private CancellationToken DialogLifetime(XsrUiEntityId source)
        {
            for (var node = source; node.IsAssigned && _shell.Tree.IsAlive(node); node = _coordinator.GetPresentationParent(node))
            {
                var dialog = _dialogs.Values.FirstOrDefault(value => value.Chrome == node);
                if (dialog is not null) return dialog.Stop.Token;
            }
            return default;
        }

        private void ObserveClosedDialogs()
        {
            foreach (var dialog in _dialogs.Values)
                if (dialog.Open && !_shell.Stage.Overlays.Contains(dialog.Chrome)) CloseDialog(dialog);
        }

        private void CloseDialog(DialogInstance dialog)
        {
            if (!dialog.Open) return;
            dialog.Open = false; _shell.Stage.Dismiss(dialog.Chrome);
            dialog.Stop.Cancel(); dialog.Stop.Dispose(); dialog.Stop = new();
        }

        private void RetireDialog(Guid exposure)
        {
            if (!_dialogs.Remove(exposure, out var dialog)) return;
            CloseDialog(dialog); dialog.Stop.Cancel(); dialog.Stop.Dispose(); dialog.Document.Dispose();
            if (_shell.Tree.IsAlive(dialog.Chrome)) _shell.Tree.Destroy(dialog.Chrome);
        }
    }

    private sealed class DialogInstance(NxmlDocumentInstance document, NxmlPreparedDocument compiled, XsrUiEntityId chrome, XsrUiEntityId close)
    {
        internal NxmlDocumentInstance Document { get; } = document;
        internal NxmlPreparedDocument Compiled { get; set; } = compiled;
        internal XsrUiEntityId Chrome { get; } = chrome;
        internal XsrUiEntityId Close { get; } = close;
        internal Guid OwnerSession { get; set; }
        internal bool Open { get; set; }
        internal CancellationTokenSource Stop { get; set; } = new();
    }
}
