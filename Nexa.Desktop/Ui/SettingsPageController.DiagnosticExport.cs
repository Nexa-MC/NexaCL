using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private CancellationTokenSource? _diagnosticStop;
    private int _diagnosticWriting;
    private readonly List<(XsrUiEntityId Root, XsrUiEntityId Export, XsrUiEntityId Cancel)> _diagnosticControls = [];
    private readonly Dictionary<XsrUiEntityId, (string Template, bool Enabled)> _diagnosticViews = [];
    internal bool DiagnosticExportInProgress => Volatile.Read(ref _diagnosticWriting) != 0;

    private void BuildPrivacyDiagnosticExport()
    {
        if (ExportDiagnostics is null) return;
        var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsDiagnosticExport.d.nxml"));
        BuildDiagnosticExportControls(CatalogNodes(root)["SettingsDiagnosticExport.Actions"]);
    }

    private void BuildDiagnosticExportControls(XsrUiEntityId parent)
    {
        var row = _dynamic.Append(parent, DesktopMarkup.Dynamic("SettingsDiagnosticActionsIdle.d.nxml", new Dictionary<string, string> { ["enabled"] = "false" }));
        var nodes = CatalogNodes(row);
        var export = nodes["Management.导出诊断包"]; var cancel = nodes["Management.取消诊断包导出"];
        _managementActions[export] = () => WakeOnPlatformCompletion(ExportDiagnosticsAsync());
        _managementActions[cancel] = CancelDiagnosticExport;
        _diagnosticControls.Add((row, export, cancel));
        UpdateDiagnosticExportControls();
    }

    private void UpdateDiagnosticExportControls()
    {
        bool busy = DiagnosticExportInProgress;
        bool cancelling = Volatile.Read(ref _diagnosticStop)?.IsCancellationRequested == true;
        string template = busy ? cancelling ? "SettingsDiagnosticActionsCancelling.d.nxml" : "SettingsDiagnosticActionsBusy.d.nxml" : "SettingsDiagnosticActionsIdle.d.nxml";
        bool enabled = ExportDiagnostics is not null && !busy;
        foreach (var (root, export, cancel) in _diagnosticControls)
        {
            if (!_shell.Tree.IsAlive(root) || !_shell.Tree.IsAlive(export) || !_shell.Tree.IsAlive(cancel)) continue;
            if (_diagnosticViews.TryGetValue(root, out var previous) && previous == (template, enabled)) continue;
            _dynamic.Reload(root, DesktopMarkup.Dynamic(template, new Dictionary<string, string> { ["enabled"] = Flag(enabled) }));
            _diagnosticViews[root] = (template, enabled);
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
