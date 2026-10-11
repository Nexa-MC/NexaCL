using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private CancellationTokenSource? _diagnosticStop;
    private int _diagnosticWriting;
    private readonly List<(XsrUiEntityId Export, XsrUiEntityId Cancel)> _diagnosticControls = [];
    internal bool DiagnosticExportInProgress => Volatile.Read(ref _diagnosticWriting) != 0;

    private void BuildPrivacyDiagnosticExport()
    {
        if (ExportDiagnostics is null) return;
        var group = FormGroup(_sections, "SettingsDiagnosticExport", "诊断包");
        var hint = Text(group, "导出游戏与加载器版本、模组标识、问题分类及结构化操作日志。不包含账户、完整路径或日志正文。", 12, Muted, height: 64);
        var copy = _shell.Tree.GetComponent<XsrUiText>(hint)!;
        copy.MaxLines = 0;
        copy.TrimOverflow = false;
        _shell.Tree.GetComponent<XsrUiVisualStyle>(hint)!.WrapText = true;
        BuildDiagnosticExportControls(group);
    }

    private void BuildDiagnosticExportControls(XsrUiEntityId parent)
    {
        var row = Stack(parent, "DiagnosticExportActions", XsrUiOrientation.Horizontal, 10);
        var export = ActionButton(row, "Management.导出诊断包", "导出诊断包", ManagementAction, 144);
        var cancel = ActionButton(row, "Management.取消诊断包导出", "取消导出", ManagementAction, 96);
        _managementActions[export] = () => WakeOnPlatformCompletion(ExportDiagnosticsAsync());
        _managementActions[cancel] = CancelDiagnosticExport;
        _diagnosticControls.Add((export, cancel));
        UpdateDiagnosticExportControls();
    }

    private void UpdateDiagnosticExportControls()
    {
        bool busy = DiagnosticExportInProgress;
        bool cancelling = Volatile.Read(ref _diagnosticStop)?.IsCancellationRequested == true;
        foreach (var (export, cancel) in _diagnosticControls)
        {
            if (!_shell.Tree.IsAlive(export) || !_shell.Tree.IsAlive(cancel)) continue;
            var input = _shell.Tree.GetComponent<XsrUiInput>(export)!;
            var text = _shell.Tree.GetComponent<XsrUiText>(export)!;
            var cancelLayout = _shell.Tree.GetComponent<XsrUiElement>(cancel)!;
            var cancelInput = _shell.Tree.GetComponent<XsrUiInput>(cancel)!;
            string label = busy ? cancelling ? "正在取消…" : "正在导出…" : "导出诊断包";
            bool enabled = ExportDiagnostics is not null && !busy;
            if (input.Enabled == enabled && text.Content == label && cancelLayout.IsVisible == busy && cancelInput.Enabled == (busy && !cancelling)) continue;
            input.Enabled = enabled; text.Content = label; cancelLayout.IsVisible = busy; cancelInput.Enabled = busy && !cancelling;
            _shell.Tree.GetComponent<XsrUiSemantic>(export)!.Label = label;
            _shell.Tree.MarkDirty(export, XsrUiDirtyKinds.Paint);
            _shell.Tree.MarkDirty(cancel, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (_disposed || ExportDiagnostics is not { } export || Interlocked.CompareExchange(ref _diagnosticWriting, 1, 0) != 0) return;
        var stop = new CancellationTokenSource();
        Volatile.Write(ref _diagnosticStop, stop);
        try
        {
            if (_disposed) { stop.Cancel(); return; }
            bool saved = await export(stop.Token).ConfigureAwait(false);
            if (!_disposed && !stop.IsCancellationRequested && saved) _feedback.Info("诊断包已导出到所选目录。");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed && !stop.IsCancellationRequested) _feedback.Error("诊断包未导出，请检查保存目录是否可写。"); }
        finally
        {
            Interlocked.CompareExchange(ref _diagnosticStop, null, stop);
            stop.Dispose(); Volatile.Write(ref _diagnosticWriting, 0);
        }
    }

    private void CancelDiagnosticExport()
    {
        try { Volatile.Read(ref _diagnosticStop)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
