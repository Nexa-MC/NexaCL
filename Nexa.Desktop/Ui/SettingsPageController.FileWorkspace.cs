using Nexa.Core;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private string _workspaceArea = "config", _workspaceDirectory = "";
    private string? _workspaceInstance, _workspaceError;
    private Task<XsrResult<InstanceFileListing>>? _workspaceListRead;
    private Task<XsrResult<InstanceFileDocument>>? _workspaceDocumentRead;
    private Task<XsrResult<InstanceFileSavePreview>>? _workspacePreviewRead;
    private InstanceFileReadQuery? _workspaceRequestedFile;
    private InstanceFileSavePreviewQuery? _workspaceRequestedPreview;
    private InstanceFileListing? _workspaceListing;
    private InstanceFileDocument? _workspaceDocument;
    private InstanceFileSavePreview? _workspacePreview;
    private CancellationTokenSource? _workspaceStop;
    private string _workspaceDraft = "";
    private int _workspaceListPage, _workspaceTextPage, _workspaceLine;
    private XsrUiEntityId _workspaceLineEditor, _workspaceLineNumber, _workspaceRoot;
    private Dictionary<string, XsrUiEntityId> _workspaceNodes = [];
    private sealed record WorkspaceRowsProjection(InstanceFileListing Listing, int Page, bool CanRead);
    private WorkspaceRowsProjection? _workspaceRowsProjection;
    private bool WorkspaceVisible => !_disposed && _visible && _selected == "files" && _instance is not null && InstanceDevelopmentMode;

    private void RetireWorkspaceReads()
    {
        _workspaceStop?.Cancel(); _workspaceStop?.Dispose(); _workspaceStop = null;
        ObserveRetiredInstanceToolRead(_workspaceListRead);
        ObserveRetiredInstanceToolRead(_workspaceDocumentRead);
        ObserveRetiredInstanceToolRead(_workspacePreviewRead);
        _workspaceListRead = null; _workspaceDocumentRead = null; _workspacePreviewRead = null;
        _workspaceRequestedFile = null; _workspaceRequestedPreview = null;
    }
    private void CancelFileWorkspace()
    {
        RetireWorkspaceReads();
        _workspaceListing = null; _workspaceDocument = null; _workspacePreview = null; _workspaceError = null;
        _workspaceLineEditor = _workspaceLineNumber = _workspaceRoot = default;
        _workspaceNodes.Clear(); _workspaceRowsProjection = null; _workspaceDraft = "";
    }
    private void InitializeWorkspace()
    {
        if (PathIdentity.Comparer.Equals(_workspaceInstance, _instance)) return;
        CancelFileWorkspace(); _workspaceInstance = _instance; _workspaceArea = "config"; _workspaceDirectory = "";
        _workspaceListPage = _workspaceTextPage = _workspaceLine = 0;
    }
    private void ChangeWorkspace(string area, string relative)
    {
        RetireWorkspaceReads(); _workspaceArea = area; _workspaceDirectory = relative;
        _workspaceListing = null; _workspaceDocument = null; _workspacePreview = null; _workspaceError = null;
        _workspaceDraft = ""; _workspaceListPage = _workspaceTextPage = _workspaceLine = 0; _workspaceRowsProjection = null;
        ProjectWorkspace();
    }
    private void UpdateFileWorkspace()
    {
        if (!WorkspaceVisible)
        { if (_workspaceListRead is not null || _workspaceDocumentRead is not null || _workspacePreviewRead is not null) CancelFileWorkspace(); return; }
        InitializeWorkspace();
        bool changed = false, editorChanged = false;
        if (_workspaceListRead is { IsCompleted: true } list)
        {
            _workspaceListRead = null;
            if (PendingQuery.Succeeded(list) && PathIdentity.Comparer.Equals(list.Result.Value!.InstanceDirectory, _instance)
                && list.Result.Value.Area == _workspaceArea && list.Result.Value.RelativeDirectory == _workspaceDirectory)
                _workspaceListing = list.Result.Value;
            else if (!list.IsCanceled) _workspaceError = "workspace.list-failed";
            changed = true;
        }
        if (_workspaceDocumentRead is { IsCompleted: true } document)
        {
            _workspaceDocumentRead = null;
            if (PendingQuery.Succeeded(document) && document.Result.Value!.File == _workspaceRequestedFile)
            {
                _workspaceDocument = document.Result.Value; _workspaceDraft = _workspaceDocument!.Text;
                _workspaceLine = _workspaceTextPage = 0; editorChanged = true;
            }
            else if (!document.IsCanceled) _workspaceError = "workspace.read-failed";
            _workspaceRequestedFile = null; changed = true;
        }
        if (_workspacePreviewRead is { IsCompleted: true } preview)
        {
            CaptureWorkspaceLine(); _workspacePreviewRead = null;
            if (_workspaceRequestedPreview is not { } requested || requested.Text != _workspaceDraft
                || requested.Original != _workspaceDocument) _workspaceError = "workspace.preview-stale";
            else if (PendingQuery.Succeeded(preview) && preview.Result.Value!.Original == requested.Original
                && preview.Result.Value.Text == requested.Text) _workspacePreview = preview.Result.Value;
            else if (!preview.IsCanceled) _workspaceError = "workspace.preview-failed";
            _workspaceRequestedPreview = null; changed = true;
        }
        if (_workspaceListing is null && _workspaceListRead is null && _workspaceError is null)
        {
            if (_queries.TryResolve(InstanceFileWorkspaceContract.List, out var route))
            {
                _workspaceStop ??= new();
                _workspaceListRead = _queries.QueryAsync<InstanceFileListQuery, InstanceFileListing>(route,
                    new(_instance!, _workspaceArea, _workspaceDirectory), cancellationToken: _workspaceStop.Token).AsTask();
                WakeOnPlatformCompletion(_workspaceListRead);
            }
            else _workspaceError = "workspace.list-failed";
            changed = true;
        }
        if (changed) ProjectWorkspace(editorChanged);
    }
    private void BuildInstanceFiles()
    {
        InitializeWorkspace();
        CaptureWorkspaceLine();
        _workspaceRoot = AppendInstanceTool(_sections, "SettingsInstanceFiles.d.nxml", WorkspaceData());
        _workspaceNodes = CatalogNodes(_workspaceRoot); _workspaceRowsProjection = null;
        _workspaceLineEditor = _workspaceNodes["FileWorkspaceLineEditor"];
        _workspaceLineNumber = _workspaceNodes["FileWorkspaceLineNumber"];
        foreach (string area in new[] { "config", "logs", "crash-reports", "other" })
            BindInstanceToolAction(_workspaceNodes, "FileWorkspace.Area." + area, () => ChangeWorkspace(area, ""));
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.OpenFolder", () =>
        { if (_workspaceListing is { } listing) OpenContentDirectory(listing.Directory); });
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.Up", () => ChangeWorkspace(_workspaceArea,
            _workspaceDirectory.Contains('/') ? _workspaceDirectory[.._workspaceDirectory.LastIndexOf('/')] : ""));
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.NextFiles", () =>
        {
            int pages = Math.Max(1, ((_workspaceListing?.Entries.Count ?? 0) + 24) / 25);
            _workspaceListPage = (_workspaceListPage + 1) % pages; ProjectWorkspace();
        });
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.Back", () =>
        {
            RetireWorkspaceReads(); _workspaceDocument = null; _workspacePreview = null; _workspaceDraft = "";
            _workspaceError = null; ProjectWorkspace();
        });
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.NextText", () =>
        {
            CaptureWorkspaceLine(); int pages = Math.Max(1, (WorkspaceLines(_workspaceDraft).Count + 24) / 25);
            _workspaceTextPage = (_workspaceTextPage + 1) % pages; ProjectWorkspace();
        });
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.SelectLine", SelectWorkspaceLine);
        BindInstanceToolAction(_workspaceNodes, "FileWorkspace.Preview", PreviewWorkspaceSave);
        ProjectWorkspace(editorChanged: true);
    }
    private void OpenWorkspaceEntry(InstanceFileListing listing, InstanceFileEntry entry)
    {
        if (_workspaceListing != listing || !PathIdentity.Comparer.Equals(listing.InstanceDirectory, _instance)) return;
        string relative = listing.RelativeDirectory.Length > 0 ? listing.RelativeDirectory + "/" + entry.Name : entry.Name;
        if (entry.IsDirectory) { ChangeWorkspace(listing.Area, relative); return; }
        if (_workspaceDocumentRead is not null || !_queries.TryResolve(InstanceFileWorkspaceContract.Read, out var route)) return;
        _workspaceError = null; _workspaceStop ??= new();
        _workspaceRequestedFile = new(_instance!, listing.Area, relative, entry.Size ?? 0, entry.ModifiedUtcTicks);
        _workspaceDocumentRead = _queries.QueryAsync<InstanceFileReadQuery, InstanceFileDocument>(route,
            _workspaceRequestedFile, cancellationToken: _workspaceStop.Token).AsTask();
        WakeOnPlatformCompletion(_workspaceDocumentRead); ProjectWorkspace();
    }
    private void SelectWorkspaceLine()
    {
        CaptureWorkspaceLine(); var lines = WorkspaceLines(_workspaceDraft);
        if (!int.TryParse(_shell.Tree.GetComponent<XsrUiTextInput>(_workspaceLineNumber)?.ReadDraft(), out int number)
            || number < 1 || number > lines.Count) { _feedback.Error(InstanceToolTerm("workspace.invalid-line")); return; }
        _workspaceLine = number - 1; _workspacePreview = null;
        ProjectWorkspace(editorChanged: true);
    }
    private void PreviewWorkspaceSave()
    {
        CaptureWorkspaceLine();
        if (_workspacePreviewRead is not null || _workspaceDocument is not { Editable: true } document
            || !_queries.TryResolve(InstanceFileWorkspaceContract.Preview, out var route)) return;
        _workspaceError = null; _workspacePreview = null; _workspaceStop ??= new();
        _workspaceRequestedPreview = new(document, _workspaceDraft);
        _workspacePreviewRead = _queries.QueryAsync<InstanceFileSavePreviewQuery, InstanceFileSavePreview>(route,
            _workspaceRequestedPreview, cancellationToken: _workspaceStop.Token).AsTask();
        WakeOnPlatformCompletion(_workspacePreviewRead); ProjectWorkspace();
    }
    private void CaptureWorkspaceLine()
    {
        if (_workspaceDocument is not { Editable: true } || !_workspaceLineEditor.IsAssigned || !_shell.Tree.IsAlive(_workspaceLineEditor) || _shell.Tree.GetComponent<XsrUiTextInput>(_workspaceLineEditor) is not { } editor) return;
        var lines = WorkspaceLines(_workspaceDraft); if (_workspaceLine >= lines.Count) return;
        var selected = lines[_workspaceLine]; if (selected.Length > 32768) return; string text = editor.ReadDraft();
        if (selected.Length == text.Length && _workspaceDraft.AsSpan(selected.Start, selected.Length).SequenceEqual(text)) return;
        _workspaceDraft = _workspaceDraft[..selected.Start] + text + _workspaceDraft[(selected.Start + selected.Length)..];
    }
    private static List<(int Start, int Length)> WorkspaceLines(string text)
    {
        List<(int, int)> lines = []; int start = 0;
        for (int index = 0; index < text.Length; index++)
            if (text[index] is '\r' or '\n')
            {
                lines.Add((start, index - start)); if (lines.Count == 65536) return lines;
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                start = index + 1;
            }
        lines.Add((start, text.Length - start)); return lines;
    }
}
