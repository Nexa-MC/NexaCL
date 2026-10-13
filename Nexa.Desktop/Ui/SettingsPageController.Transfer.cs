using System.Collections.Concurrent;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private const int SettingsDocumentBudget = 1024 * 1024;
    private static readonly XsrSemanticId ImportSettings = XsrSemanticId.Parse("ui.settings.data.import");
    private static readonly XsrSemanticId ExportSettings = XsrSemanticId.Parse("ui.settings.data.export");
    private static readonly XsrSemanticId ResetSettings = XsrSemanticId.Parse("ui.settings.data.reset");
    private static bool IsSettingsTransferIntent(XsrSemanticId command) => command == ImportSettings || command == ExportSettings || command == ResetSettings;
    private sealed record ImportResult(string Document, SettingsImportPreview Preview, bool Reset = false);
    private Func<CancellationToken, Task<string?>>? _readSettingsDocument;
    private Func<string, CancellationToken, Task<bool>>? _saveSettingsDocument;
    private CancellationTokenSource? _settingsTransferStop;
    private Task<ImportResult?>? _settingsImportRead;
    private Task<bool>? _settingsExportWrite;
    private Task<XsrResult>? _settingsImportApply;
    private bool _settingsResetApplying;
    private Guid _settingsImportDialog;
    private long _settingsTransferGeneration;
    private XsrUiEntityId _settingsTransferPanel;
    private string? _settingsTransferPanelInstance;
    private string? _settingsTransferPanelSection;
    private readonly ConcurrentQueue<(long Generation, bool Accepted, ImportResult Result)> _settingsImportDecisions = new();
    private bool SettingsTransferBusy => _settingsImportRead is not null || _settingsExportWrite is not null
        || _settingsImportApply is not null || _settingsImportDialog != Guid.Empty;

    internal void ConfigureSettingsTransfer(Func<CancellationToken, Task<string?>> read,
        Func<string, CancellationToken, Task<bool>> save)
    { _readSettingsDocument = read; _saveSettingsDocument = save; }

    private Dictionary<string, string> SettingsTransferProjection() => new()
    {
        ["importEnabled"] = Flag(_readSettingsDocument is not null && !SettingsTransferBusy),
        ["exportEnabled"] = Flag(_saveSettingsDocument is not null && !SettingsTransferBusy),
        ["resetEnabled"] = Flag(!SettingsTransferBusy && _queries.TryResolve(SettingsPolicyContract.ResetPreviewQuery, out _)
            && _commands.TryResolve(SettingsPolicyContract.ResetCommand, out _)),
        ["global"] = Flag(_instance is null),
        ["instance"] = Flag(_instance is not null),
    };

    private void BuildSettingsTransfer()
    {
        _settingsTransferPanelInstance = _instance; _settingsTransferPanelSection = _selected;
        _settingsTransferPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsTransfer.d.nxml", SettingsTransferProjection()));
    }

    private void RefreshSettingsTransferPresentation()
    {
        if (!_shell.Tree.IsAlive(_settingsTransferPanel) || _settingsTransferPanelInstance != _instance || _settingsTransferPanelSection != _selected) return;
        _dynamic.Reload(_settingsTransferPanel, DesktopMarkup.Dynamic("SettingsTransfer.d.nxml", SettingsTransferProjection()));
    }

    private void HandleSettingsTransfer(XsrSemanticId command, XsrUiEntityId source)
    {
        if (!(_instanceDirectory is null && _selected == "storage" || _instanceDirectory is not null && _selected == "game")) return;
        if (!_shell.Tree.IsAlive(source) || !_shell.Tree.IsAlive(_settingsTransferPanel)
            || _settingsTransferPanelInstance != _instance || _settingsTransferPanelSection != _selected
            || _shell.Tree.GetComponent<XsrUiInput>(source)?.Enabled != true) return;
        string key = command == ImportSettings ? "SettingsImport" : command == ResetSettings ? "SettingsReset" : "SettingsExport";
        if (CatalogNodes(_settingsTransferPanel)[key] != source) return;
        if (SettingsTransferBusy) return;
        if (command == ImportSettings && _readSettingsDocument is null || command == ExportSettings && _saveSettingsDocument is null) return;
        CancelSettingsTransfer();
        _settingsTransferStop = new(); CancellationToken token = _settingsTransferStop.Token;
        string? instance = _instance;
        if (command == ImportSettings) { _settingsImportRead = ReadImportAsync(instance, token); ObserveTransfer(_settingsImportRead); }
        else if (command == ResetSettings) { _settingsImportRead = ReadResetAsync(instance, token); ObserveTransfer(_settingsImportRead); }
        else { _settingsExportWrite = ExportAsync(instance, token); ObserveTransfer(_settingsExportWrite); }
        RefreshSettingsTransferPresentation();
    }

    private async Task<ImportResult?> ReadResetAsync(string? instance, CancellationToken token)
    {
        if (!_queries.TryResolve(SettingsPolicyContract.ResetPreviewQuery, out var route)) throw new InvalidOperationException("Reset is unavailable.");
        var result = await Task.Run(async () => await _queries.QueryAsync<SettingsResetQuery, SettingsResetPreview>(route,
            new(instance), cancellationToken: token).ConfigureAwait(false), token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidDataException("Reset preview failed.");
        var preview = result.Value!;
        return new("", new(preview.Revision, preview.Changes, preview.Errors), Reset: true);
    }

    private async Task<ImportResult?> ReadImportAsync(string? instance, CancellationToken token)
    {
        string? document = await _readSettingsDocument!(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (document is null) return null;
        if (document.Length > SettingsDocumentBudget) throw new InvalidDataException("Document exceeds its budget.");
        if (!_queries.TryResolve(SettingsPolicyContract.ImportPreviewQuery, out var route)) throw new InvalidOperationException("Import is unavailable.");
        var result = await Task.Run(async () => await _queries.QueryAsync<SettingsImportQuery, SettingsImportPreview>(route,
            new(document, instance), cancellationToken: token).ConfigureAwait(false), token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidDataException("Import preview failed.");
        return new(document, result.Value!);
    }

    private async Task<bool> ExportAsync(string? instance, CancellationToken token)
    {
        if (!_queries.TryResolve(SettingsPolicyContract.ExportQuery, out var route)) throw new InvalidOperationException("Export is unavailable.");
        var result = await Task.Run(async () => await _queries.QueryAsync<SettingsExportQuery, string>(route,
            new(instance), cancellationToken: token).ConfigureAwait(false), token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidDataException("Export failed.");
        token.ThrowIfCancellationRequested();
        return await _saveSettingsDocument!(result.Value!, token).ConfigureAwait(false);
    }

    private void ObserveTransfer(Task task)
    {
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        WakeOnPlatformCompletion(task);
    }

    private void UpdateSettingsTransfer()
    {
        bool changed = false;
        if (_settingsImportRead is { IsCompleted: true } reading)
        {
            _settingsImportRead = null; changed = true;
            if (reading.IsFaulted) _feedback.Error(ProductPresentationText.Raw("settings.transfer.previewFailed"));
            else if (reading.IsCompletedSuccessfully && reading.Result is { } imported)
            {
                if (imported.Preview.Errors.Count > 0) _feedback.Error(ProductPresentationText.Raw(imported.Reset ? "settings.transfer.resetInvalid" : "settings.transfer.importInvalid"));
                else if (imported.Preview.Changes.Count == 0) _feedback.Info(ProductPresentationText.Raw(imported.Reset ? "settings.transfer.resetEmpty" : "settings.transfer.importEmpty"));
                else
                {
                    long generation = _settingsTransferGeneration;
                    string labels = string.Join('\n', imported.Preview.Changes.Take(16).Select(change => _shell.Renderer.LocalizeText(DisplayLabel(
                        _catalog!.Entries.FirstOrDefault(entry => entry.SettingKey == change.Key)?.Label ?? ProductPresentationText.Raw("settings.transfer.otherSetting")))));
                    string message = ProductPresentationText.Format(imported.Reset ? "SettingsTransferResetCount" : "SettingsTransferImportCount", _shell.Renderer.LocalizeText,
                        ("count", imported.Preview.Changes.Count.ToString(System.Globalization.CultureInfo.CurrentCulture))) + "\n" + labels;
                    if (imported.Preview.Changes.Count > 16) message += "\n" + ProductPresentationText.Format("SettingsTransferMoreCount", _shell.Renderer.LocalizeText,
                        ("count", (imported.Preview.Changes.Count - 16).ToString(System.Globalization.CultureInfo.CurrentCulture)));
                    if (imported.Preview.Changes.Any(change => SettingsPolicySchema.ByKey[change.Key].Timing == SettingsApplyTiming.Restart))
                        message += "\n" + ProductPresentationText.Term("settings.transfer.restartRequired", _shell.Renderer.LocalizeText);
                    _settingsImportDialog = _feedback.ShowDialog(imported.Reset ? "settings.reset" : "settings.import",
                        ProductPresentationText.Raw(imported.Reset ? (_instance is null ? "settings.transfer.resetGlobalTitle" : "settings.transfer.resetInstanceTitle") : "settings.transfer.importTitle"), message,
                        ProductPresentationText.Raw(imported.Reset ? "settings.action.restore" : "settings.transfer.importAction"), ProductPresentationText.Raw("settings.action.cancel"),
                        accepted =>
                        {
                            if (Volatile.Read(ref _settingsTransferGeneration) == generation)
                                _settingsImportDecisions.Enqueue((generation, accepted, imported));
                        }, localizeMessage: false);
                }
            }
        }
        while (_settingsImportDecisions.TryDequeue(out var decision))
        {
            if (decision.Generation != _settingsTransferGeneration || _settingsTransferStop?.IsCancellationRequested != false) continue;
            _settingsImportDialog = default; changed = true;
            if (!decision.Accepted) continue;
            _settingsResetApplying = decision.Result.Reset;
            if (!_commands.TryResolve(_settingsResetApplying ? SettingsPolicyContract.ResetCommand : SettingsPolicyContract.ImportCommand, out var route))
            { _feedback.Error(ProductPresentationText.Raw("settings.transfer.applyUnavailable")); continue; }
            _settingsImportApply = _settingsResetApplying
                ? _commands.Dispatch(route, new SettingsResetCommand(decision.Result.Preview.Revision, _instance), cancellationToken: _settingsTransferStop.Token).Completion
                : _commands.Dispatch(route, new SettingsImportCommand(decision.Result.Document,
                    decision.Result.Preview.Revision, _instance), cancellationToken: _settingsTransferStop.Token).Completion;
            ObserveTransfer(_settingsImportApply);
        }
        if (_settingsImportApply is { IsCompleted: true } applying)
        {
            _settingsImportApply = null; changed = true;
            if (PendingQuery.Succeeded(applying)) _feedback.Info(ProductPresentationText.Raw(_settingsResetApplying ? "settings.transfer.resetComplete" : "settings.transfer.importComplete"));
            else if (!applying.IsCanceled) _feedback.Error(ProductPresentationText.Raw(_settingsResetApplying ? "settings.transfer.resetFailed" : "settings.transfer.importFailed"));
        }
        if (_settingsExportWrite is { IsCompleted: true } exporting)
        {
            _settingsExportWrite = null; changed = true;
            if (exporting.IsCompletedSuccessfully && exporting.Result) _feedback.Info(ProductPresentationText.Raw("settings.transfer.exportComplete"));
            else if (exporting.IsFaulted) _feedback.Error(ProductPresentationText.Raw("settings.transfer.exportFailed"));
        }
        if (changed) RefreshSettingsTransferPresentation();
    }

    private void CancelSettingsTransfer()
    {
        Interlocked.Increment(ref _settingsTransferGeneration);
        _settingsTransferStop?.Cancel(); _settingsTransferStop?.Dispose(); _settingsTransferStop = null;
        _settingsImportRead = null; _settingsExportWrite = null; _settingsImportApply = null;
        if (_settingsImportDialog != Guid.Empty) _feedback.DismissDialog(_settingsImportDialog);
        _settingsImportDialog = default;
        while (_settingsImportDecisions.TryDequeue(out _)) { }
        int pending = _pending.Count;
        for (int i = 0; i < pending && _pending.TryDequeue(out var intent); i++)
            if (!IsSettingsTransferIntent(intent.Command)) _pending.Enqueue(intent);
    }
}
