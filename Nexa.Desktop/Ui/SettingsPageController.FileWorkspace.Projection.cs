using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private InstanceFileDocument? _workspaceCaptionDocument;
    private XsrUiEntityId _workspacePreviewRoot;
    private Dictionary<string, string> WorkspaceData()
    {
        var document = _workspaceDocument; var listing = _workspaceListing;
        var lines = WorkspaceLines(_workspaceDraft);
        int listPages = Math.Max(1, ((listing?.Entries.Count ?? 0) + 24) / 25);
        int textPages = Math.Max(1, (lines.Count + 24) / 25);
        _workspaceListPage = Math.Clamp(_workspaceListPage, 0, listPages - 1);
        _workspaceTextPage = Math.Clamp(_workspaceTextPage, 0, textPages - 1);
        _workspaceLine = Math.Clamp(_workspaceLine, 0, Math.Max(0, lines.Count - 1));
        bool editable = document?.Editable == true;
        bool shortLine = lines[_workspaceLine].Length <= 32768;
        var data = new Dictionary<string, string>
        {
            ["hasError"] = Flag(_workspaceError is not null),
            ["error"] = _workspaceError is null ? "" : InstanceToolTerm(_workspaceError),
            ["loadingList"] = Flag(listing is null && _workspaceError is null),
            ["loadingFile"] = Flag(_workspaceDocumentRead is not null),
            ["showList"] = Flag(listing is not null && document is null),
            ["directory"] = listing?.Directory ?? "", ["hasParent"] = Flag(_workspaceDirectory.Length > 0),
            ["incomplete"] = Flag(listing is { Complete: false }), ["empty"] = Flag(listing?.Entries.Count == 0),
            ["listPaging"] = Flag(listPages > 1), ["listPage"] = InstanceNumber(_workspaceListPage + 1), ["listPages"] = InstanceNumber(listPages),
            ["hasDocument"] = Flag(document is not null), ["editable"] = Flag(editable),
            ["manyLines"] = Flag(lines.Count == 65536), ["textPaging"] = Flag(textPages > 1),
            ["textPage"] = InstanceNumber(_workspaceTextPage + 1), ["textPages"] = InstanceNumber(textPages),
            ["shortLine"] = Flag(shortLine), ["longLine"] = Flag(!shortLine),
            ["canPreview"] = Flag(editable && _workspacePreviewRead is null && _managementWrite is null),
            ["previewPending"] = Flag(_workspacePreviewRead is not null)
        };
        foreach (var (area, key) in new[] { ("config", "config"), ("logs", "logs"), ("crash-reports", "crash"), ("other", "other") })
        {
            data[key] = Flag(_workspaceArea == area);
            data[key + "Class"] = "settings-catalog-action instance-tools-area" + (_workspaceArea == area ? " instance-tools-area-selected" : "");
        }
        return data;
    }
    private void ClearWorkspaceRows(string key)
    {
        var slot = _workspaceNodes[key];
        _shell.Tree.Walk(slot, entity => { _managementActions.Remove(entity); _instanceToolViews.Remove(entity); return true; });
        _dynamic.Clear(slot);
    }
    private void ProjectWorkspace(bool editorChanged = false)
    {
        if (!_shell.Tree.IsAlive(_workspaceRoot)) return;
        ReloadInstanceTool(_workspaceRoot, WorkspaceData());
        ProjectWorkspaceFiles();
        ProjectWorkspaceDocument();
        if (editorChanged && _workspaceDocument is { Editable: true })
        {
            var lines = WorkspaceLines(_workspaceDraft); var selected = lines[_workspaceLine];
            _shell.Renderer.SetTextInputValue(_workspaceLineNumber, InstanceNumber(_workspaceLine + 1));
            _shell.Renderer.SetTextInputValue(_workspaceLineEditor, selected.Length <= 32768
                ? _workspaceDraft.Substring(selected.Start, selected.Length) : "");
        }
        if (_workspacePreview is not { } preview)
        { ClearWorkspaceRows("FileWorkspace.PreviewResult"); _workspacePreviewRoot = default; return; }
        var data = new Dictionary<string, string>
        {
            ["previousBytes"] = InstanceNumber(preview.PreviousBytes), ["updatedBytes"] = InstanceNumber(preview.UpdatedBytes),
            ["previousLines"] = InstanceNumber(preview.PreviousLines), ["updatedLines"] = InstanceNumber(preview.UpdatedLines),
            ["canSave"] = Flag(_managementWrite is null)
        };
        if (!_shell.Tree.IsAlive(_workspacePreviewRoot))
        {
            _workspacePreviewRoot = AppendInstanceTool(_workspaceNodes["FileWorkspace.PreviewResult"], "SettingsInstanceFilePreview.d.nxml", data);
            BindInstanceToolAction(CatalogNodes(_workspacePreviewRoot), "FileWorkspace.Save", () =>
            {
                CaptureWorkspaceLine();
                if (_workspacePreview != preview || _workspaceDraft != preview.Text)
                {
                    _workspacePreview = null; _workspaceError = "workspace.preview-stale"; ProjectWorkspace();
                    _feedback.Error(InstanceToolTerm("workspace.preview-stale")); return;
                }
                DispatchWorld(InstanceFileWorkspaceContract.Save, new InstanceFileSaveCommand(preview));
                ProjectWorkspace();
            });
        }
        else ReloadInstanceTool(_workspacePreviewRoot, data);
    }
    private void ProjectWorkspaceFiles()
    {
        if (_workspaceListing is not { } listing)
        { ClearWorkspaceRows("FileWorkspace.Rows"); _workspaceRowsProjection = null; return; }
        bool canRead = _workspaceDocumentRead is null;
        var projection = new WorkspaceRowsProjection(listing, _workspaceListPage, canRead);
        if (_workspaceRowsProjection == projection) return;
        ClearWorkspaceRows("FileWorkspace.Rows"); _workspaceRowsProjection = projection;
        foreach (var entry in listing.Entries.Skip(_workspaceListPage * 25).Take(25))
        {
            string identity = InstanceToolIdentity(listing.Area + "/" + listing.RelativeDirectory + "/" + entry.Name);
            var row = _dynamic.Append(_workspaceNodes["FileWorkspace.Rows"],
                DesktopMarkup.Dynamic("SettingsInstanceFile" + (entry.IsDirectory ? "Directory" : "Text") + "Row.d.nxml", new Dictionary<string, string>
                {
                    ["row"] = "FileWorkspaceRow." + identity, ["action"] = "FileWorkspace.Open." + identity,
                    ["name"] = entry.Name, ["size"] = InstanceToolSize(entry.Size ?? 0), ["enabled"] = Flag(canRead)
                }, _shell.Renderer.LocalizeText));
            BindInstanceToolAction(CatalogNodes(row), "FileWorkspace.Open." + identity, () => OpenWorkspaceEntry(listing, entry));
        }
    }
    private void ProjectWorkspaceDocument()
    {
        if (_workspaceCaptionDocument != _workspaceDocument || _workspaceCaptionDocument is not null && _shell.Tree.Children(_workspaceNodes["FileWorkspace.Caption"]).Count == 0)
        {
            ClearWorkspaceRows("FileWorkspace.Caption"); _workspaceCaptionDocument = _workspaceDocument;
            if (_workspaceDocument is { } document)
                _dynamic.Append(_workspaceNodes["FileWorkspace.Caption"],
                    DesktopMarkup.Dynamic("SettingsInstanceFile" + (document.Editable ? "Editable" : "ReadOnly") + "Caption.d.nxml",
                        new Dictionary<string, string> { ["path"] = document.File.RelativePath, ["encoding"] = document.Encoding }, _shell.Renderer.LocalizeText));
        }
        ClearWorkspaceRows("FileWorkspace.Lines");
        if (_workspaceDocument is null) return;
        var lines = WorkspaceLines(_workspaceDraft);
        int number = _workspaceTextPage * 25 + 1;
        foreach (var line in lines.Skip(_workspaceTextPage * 25).Take(25))
        {
            _dynamic.Append(_workspaceNodes["FileWorkspace.Lines"], DesktopMarkup.Dynamic(
                line.Length > 120 ? "SettingsInstanceFileLongLine.d.nxml" : "SettingsInstanceFileLine.d.nxml",
                new Dictionary<string, string>
                {
                    ["row"] = "FileWorkspace.Line." + InstanceNumber(number), ["number"] = InstanceNumber(number++),
                    ["content"] = _workspaceDraft.Substring(line.Start, Math.Min(line.Length, 120))
                }));
        }
    }
}
