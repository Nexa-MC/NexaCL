using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    internal Action? OpenLogDirectory { get; set; }
    internal Func<CancellationToken, Task<bool>>? ExportLogs { get; set; }
    private CancellationTokenSource? _logExportStop;
    private int _logExportWriting;
    private bool _logExportUiBusy;
    private XsrUiEntityId _diskLogActions;

    private void BuildDiskLogActions()
    {
        _logExportUiBusy = Volatile.Read(ref _logExportWriting) != 0;
        if (OpenLogDirectory is null && ExportLogs is null) return;
        _diskLogActions = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsDiskLogActions.d.nxml", DiskLogProjection()));
        var nodes = CatalogNodes(_diskLogActions);
        _managementActions[nodes[ProductPresentationText.Raw("settings.logs.openKey")]] = OpenDiskLogDirectory;
        _managementActions[nodes[ProductPresentationText.Raw("settings.logs.exportKey")]] = () => WakeOnPlatformCompletion(ExportDiskLogsAsync());
        _managementActions[nodes[ProductPresentationText.Raw("settings.logs.cancelKey")]] = CancelDiskLogExport;
    }

    private Dictionary<string, string> DiskLogProjection() => new()
    {
        ["canOpen"] = Flag(OpenLogDirectory is not null),
        ["canExport"] = Flag(ExportLogs is not null),
        ["exportEnabled"] = Flag(ExportLogs is not null && !_logExportUiBusy),
        ["cancelVisible"] = Flag(ExportLogs is not null && _logExportUiBusy),
    };

    private void UpdateDiskLogActions()
    {
        if (_instanceDirectory is not null || _selected != "privacy" || !_shell.Tree.IsAlive(_diskLogActions)) return;
        bool busy = Volatile.Read(ref _logExportWriting) != 0;
        if (_logExportUiBusy == busy) return;
        _logExportUiBusy = busy;
        _dynamic.Reload(_diskLogActions, DesktopMarkup.Dynamic("SettingsDiskLogActions.d.nxml", DiskLogProjection()));
    }

    private void OpenDiskLogDirectory()
    {
        if (_disposed) return;
        try { OpenLogDirectory?.Invoke(); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { _feedback.Error(ProductPresentationText.Raw("settings.logs.openFailed")); }
    }

    private async Task ExportDiskLogsAsync()
    {
        if (_disposed || ExportLogs is not { } export || Interlocked.CompareExchange(ref _logExportWriting, 1, 0) != 0) return;
        var stop = new CancellationTokenSource();
        Volatile.Write(ref _logExportStop, stop);
        try
        {
            if (_disposed) { stop.Cancel(); return; }
            bool saved = await export(stop.Token).ConfigureAwait(false);
            if (!_disposed && !stop.IsCancellationRequested && saved) _feedback.Success(ProductPresentationText.Raw("settings.logs.exportComplete"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed && !stop.IsCancellationRequested) _feedback.Error(ProductPresentationText.Raw("settings.logs.exportFailed")); }
        finally
        {
            Interlocked.CompareExchange(ref _logExportStop, null, stop);
            stop.Dispose(); Volatile.Write(ref _logExportWriting, 0);
        }
    }

    private void CancelDiskLogExport()
    {
        try { Volatile.Read(ref _logExportStop)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
