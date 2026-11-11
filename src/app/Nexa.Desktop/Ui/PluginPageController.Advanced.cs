using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class PluginPageController
{
    private readonly Dictionary<EntityId, (PluginDialog State, NxmlDocumentInstance Document, XsrUiEntityId Chrome)> _dialogs = [];
    private readonly List<(PluginPage State, NxmlDocumentInstance Document)> _workspaceExtensions = [];
    private readonly Dictionary<XsrUiEntityId, (PluginPage State, string Action)> _commandButtons = [];
    private static PluginPage EmptyPage(PluginIdentity owner) => new(owner,
        new(PluginApplication.WorkspaceView, new(Guid.Empty, owner.LoadInstanceId, "package"), Abi100Schemas.Empty, []),
        owner.PluginId, DesktopMarkup.Read("PluginContributionHost.nxml"), Empty.Write(new()), new("workspace"));
    private string AdvancedStamp() => string.Join('|', Application.AdvancedUi().Registrations.Select(r => r.Registration.Handle));
    private NxmlPreparedDocument ApplyContributions(PluginPage page, NxmlPreparedDocument document)
    {
        var snapshot = Application.AdvancedUi();
        var registrations = snapshot.Registrations.Where(r => r.Owner == page.Owner).ToArray();
        var replacement = registrations.Where(r => r.Kind == "replacements").Select(r => UIViewReplacement.Read(r.Value)).SingleOrDefault(r => r.View == page.Page.Id);
        if (replacement is not null)
        {
            var target = Application.Snapshot().Pages.Single(p => p.Owner == page.Owner && p.Page.Id == replacement.Page);
            document = Compile(target, component: true);
        }
        var contributions = registrations.Where(r => r.Kind == "contributions").Select(r => UIContribution.Read(r.Value)).Where(r => r.View == page.Page.Id).ToList();
        var ordered = new List<UIContribution>();
        while (contributions.Count != 0)
        {
            var next = contributions.OrderBy(r => r.Id.Value, StringComparer.Ordinal).FirstOrDefault(r =>
                !contributions.Any(other => other.Id != r.Id && (r.After.Contains(other.Id) || other.Before.Contains(r.Id))));
            if (next is null) throw new AbiHostException(AbiErrors.CONFLICT);
            ordered.Add(next); contributions.Remove(next);
        }
        var fragments = new List<NxmlDocumentContribution>();
        foreach (var contribution in ordered)
        {
            if (!snapshot.Components.TryGetValue(contribution.Component, out var source)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            var component = Compile(page with { Source = source }, component: true);
            var mode = contribution.Mode switch
            {
                UIContributionMode.Append => NxmlContributionMode.Append,
                UIContributionMode.Prepend => NxmlContributionMode.Prepend,
                UIContributionMode.Hide => NxmlContributionMode.Hide,
                UIContributionMode.Replace => NxmlContributionMode.Replace,
                UIContributionMode.Wrap => NxmlContributionMode.Wrap,
                _ => throw new AbiHostException(AbiErrors.SCHEMA_INVALID)
            };
            fragments.Add(new(component, mode));
        }
        return document.Compose(fragments);
    }
    private void RenderWorkspaceExtensions(XsrUiEntityId workspace)
    {
        foreach (var owner in Application.AdvancedUi().Registrations.Select(r => r.Owner).Distinct())
        {
            try
            {
                var page = EmptyPage(owner);
                var prepared = Compile(page);
                var document = new NxmlDocumentInstance(_shell.Tree, _store, prepared.AsFragment());
                _workspaceExtensions.Add((page, document)); document.Mount(Names(workspace)["PluginExtensions"]);
                foreach (var command in Application.AdvancedUi().Registrations.Where(r => r.Owner == owner && r.Kind == "commands").Select(r => UICommand.Read(r.Value)))
                {
                    var row = _dynamic.Append(Names(workspace)["PluginExtensions"], DesktopMarkup.Dynamic("PluginLink.d.nxml", new Dictionary<string, string> { ["name"] = command.Title.MessageKey }));
                    _commandButtons[Names(row)["PluginOpenPage"]] = (page, command.Action.Value);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _feedback.Warn(Copy("plugin.page-rejected") + ": " + owner.PluginId); }
        }
    }
    private void ClearWorkspaceExtensions()
    { foreach (var item in _workspaceExtensions) item.Document.Dispose(); _workspaceExtensions.Clear(); _commandButtons.Clear(); }
    private static AbiValue.Map ReadFields(NxmlDocumentInstance document, AbiValue model, XsrUiTree tree)
    {
        var fields = model is AbiValue.Map map ? map.Value.ToBuilder() : ImmutableDictionary.CreateBuilder<string, AbiValue>(StringComparer.Ordinal);
        tree.Walk(document.Root, node =>
        {
            if (tree.GetComponent<XsrUiTextInput>(node) is { } input)
            {
                string name = tree.Name(node);
                if (fields.Count >= 128 || name.Length > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                fields[name] = new AbiValue.Utf8String(input.ReadDraft());
            }
            return true;
        });
        return new AbiValue.Map(fields.ToImmutable());
    }
    private AbiValue.Map ReadFields(NxmlDocumentInstance document, AbiValue model) => ReadFields(document, model, _shell.Tree);
    private bool AdvancedIntent(DesktopUiIntent intent)
    {
        foreach (var item in _dialogs.Values)
        {
            if (intent.Command.Value == "ui.plugins.dialog-close" && Names(item.Chrome).GetValueOrDefault("PluginDialogClose") == intent.Source)
            { Application.ClosePluginDialog(item.State.Owner, item.State.Reference.Id); return true; }
            if (item.Document.Owns(intent.Source)) return GuestIntent(item.State.Page, item.Document, intent);
        }
        if (_shell.Stage.Navigation.Current != Page) return false;
        if (_commandButtons.TryGetValue(intent.Source, out var command))
        {
            if (Enabled(intent.Source, Page) && _work is not { IsCompleted: false } && intent.Command.Value == "ui.plugins.manage")
                Start(() => InvokeAction(command.State, command.Action));
            return true;
        }
        foreach (var item in _workspaceExtensions)
            if (item.Document.Owns(intent.Source)) return GuestIntent(item.State, item.Document, intent);
        return false;
    }
    private bool GuestIntent(PluginPage page, NxmlDocumentInstance document, DesktopUiIntent intent)
    {
        if (_work is { IsCompleted: false } || !Enabled(intent.Source, document.Root)) return true;
        if (intent.Command.Value == "ui.plugin.action")
        {
            string action = _shell.Renderer.ReadIntentArgument(intent.Source);
            if (action.StartsWith(page.Owner.PluginId + ":", StringComparison.Ordinal))
                Start(() => InvokeAction(page, action, ReadFields(document, page.Model)));
        }
        return true;
    }
    private void FrameDialogs()
    {
        var dialogs = Application.AdvancedUi().Dialogs;
        foreach (var id in _dialogs.Keys.Where(id => !dialogs.Any(d => d.Reference.Id == id)).ToArray())
        {
            var item = _dialogs[id]; _shell.Stage.Dismiss(item.Chrome); item.Document.Dispose();
            if (_shell.Tree.IsAlive(item.Chrome)) _shell.Tree.Destroy(item.Chrome); _dialogs.Remove(id);
        }
        foreach (var dialog in dialogs)
        {
            if (!_dialogs.TryGetValue(dialog.Reference.Id, out var current) || current.State.Page.Revision == dialog.Page.Revision) continue;
            current.Document.Reload(Compile(dialog.Page).AsFragment());
            _dialogs[dialog.Reference.Id] = (dialog, current.Document, current.Chrome);
        }
        // Host confirmations always retain the top modal position.
        if (_feedback.Snapshot().Dialog is not null) return;
        foreach (var dialog in dialogs.Where(d => !_dialogs.ContainsKey(d.Reference.Id)))
        {
            NxmlDocumentInstance? document = null; XsrUiEntityId chrome = default;
            try
            {
                document = new(_shell.Tree, _store, Compile(dialog.Page).AsFragment());
                var chromeProgram = new NxmlExtensionProgram("dialog.nxml", new Dictionary<string, string> { ["dialog.nxml"] = DesktopMarkup.Read("PluginDialog.d.nxml") }, styles: DesktopMarkup.SharedStyles);
                chrome = NxmlUiLoader.LoadDetached(chromeProgram.Compile(new Dictionary<string, string> { ["owner"] = dialog.Owner.PluginId }).Root, _shell.Tree, _store);
                document.Mount(Names(chrome)["PluginDialogBody"]);
                _shell.Stage.Show(chrome, modal: true); _dialogs.Add(dialog.Reference.Id, (dialog, document, chrome));
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            {
                document?.Dispose(); if (chrome.IsAssigned && _shell.Tree.IsAlive(chrome)) _shell.Tree.Destroy(chrome);
                Application.ClosePluginDialog(dialog.Owner, dialog.Reference.Id); _feedback.Warn(Copy("plugin.page-rejected") + ": " + dialog.Owner.PluginId);
            }
        }
    }
    internal bool HandleShortcut(string gesture)
    {
        if (_closed || _application is null || _work is { IsCompleted: false } || _feedback.Snapshot().Dialog is not null ||
            _shell.Stage.Overlays.Any(overlay => _shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal == true) || _shell.Tree.IsAlive(_shell.Renderer.Focused) && _shell.Tree.GetComponent<XsrUiTextInput>(_shell.Renderer.Focused) is not null) return false;
        var page = _pages.Values.FirstOrDefault(p => p.Document.Root == _shell.Stage.Navigation.Current).State;
        QualifiedId? scope = _shell.Stage.Navigation.Current == Page ? PluginApplication.WorkspaceView : page?.Page.Id;
        if (scope is null) return false;
        var registrations = Application.AdvancedUi().Registrations;
        foreach (var item in registrations.Where(r => r.Kind == "shortcuts").OrderBy(r => r.Registration.DefinitionId.Value, StringComparer.Ordinal))
        {
            var shortcut = UIShortcut.Read(item.Value);
            if (shortcut.Scope != scope || !string.Equals(shortcut.Gesture, gesture, StringComparison.OrdinalIgnoreCase)) continue;
            var command = registrations.SingleOrDefault(r => r.Kind == "commands" && r.Owner == item.Owner && r.Registration.DefinitionId == shortcut.Command);
            if (command is null) continue;
            Start(() => InvokeAction(page ?? EmptyPage(item.Owner), UICommand.Read(command.Value).Action.Value, Empty.Write(new()))); return true;
        }
        return false;
    }
    private void DisposeAdvancedUi()
    {
        foreach (var item in _dialogs.Values) { _shell.Stage.Dismiss(item.Chrome); item.Document.Dispose(); if (_shell.Tree.IsAlive(item.Chrome)) _shell.Tree.Destroy(item.Chrome); }
        _dialogs.Clear(); ClearWorkspaceExtensions(); _drawingNodes.Clear(); _drawingRejected.Clear();
    }
}
