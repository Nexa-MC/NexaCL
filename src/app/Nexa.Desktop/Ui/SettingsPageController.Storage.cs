using System.Collections.Concurrent;
using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId MoveStorage = XsrSemanticId.Parse("ui.settings.storage.move");
    private static readonly XsrSemanticId CleanStorageTemporary = XsrSemanticId.Parse("ui.settings.storage.cleanup-temp");
    private static readonly XsrSemanticId CleanStorageTasks = XsrSemanticId.Parse("ui.settings.storage.cleanup-tasks");
    private static readonly XsrSemanticId CancelStorageMigration = XsrSemanticId.Parse("ui.settings.storage.cancel");
    private static bool IsStoragePreferencesIntent(XsrSemanticId command) => command == MoveStorage || command == CleanStorageTemporary || command == CleanStorageTasks || command == CancelStorageMigration;
    private sealed record StoragePreview(StorageMigrationPreview? Migration, StorageCleanupPreview? Cleanup, string? CancelTransaction = null);
    private Func<CancellationToken, Task<string?>>? _chooseStorageDirectory;
    private Action? _closeForStorageMigration;
    private StoragePreferencesStatus? _storageStatus;
    private Task<XsrResult<StoragePreferencesStatus>>? _storageStatusRead;
    private Task<StoragePreview?>? _storagePreviewRead;
    private Task<XsrResult>? _storageApply;
    private bool _storageApplyingMove;
    private bool _storageApplyingCancel;
    private bool _storageStatusUnavailable;
    private bool _storageRebuildPending;
    private XsrUiEntityId _storageLocationPanel, _storageCleanupPanel;
    private CancellationTokenSource? _storageStop;
    private long _storageGeneration;
    private Guid _storageDialog;
    private readonly ConcurrentQueue<(long Generation, bool Accepted, StoragePreview Preview)> _storageDecisions = new();
    private bool StoragePreferencesBusy => _storagePreviewRead is not null || _storageApply is not null || _storageDialog != Guid.Empty;

    internal void ConfigureStoragePreferences(Func<CancellationToken, Task<string?>> chooseDirectory, Action queuedClose)
    { _chooseStorageDirectory = chooseDirectory; _closeForStorageMigration = queuedClose; }

    private Dictionary<string, string> StorageLocationProjection()
    {
        bool locked = _storageStatus?.LocationLocked == true, pending = _storageStatus?.MigrationPending == true;
        return new()
        {
            ["loadingWeight"] = LoadingWeight(_storageStatus is null),
            ["loading"] = Flag(_storageStatus is null),
            ["loaded"] = Flag(_storageStatus is not null),
            ["path"] = _storageStatus?.DataDirectory ?? "",
            ["locked"] = Flag(locked),
            ["pending"] = Flag(!locked && pending),
            ["movable"] = Flag(!locked && !pending),
            ["moveEnabled"] = Flag(!StoragePreferencesBusy && _storageStatus is { LocationLocked: false, MigrationPending: false }
                && _chooseStorageDirectory is not null && _closeForStorageMigration is not null
                && _queries.TryResolve(StoragePreferencesContract.MigrationPreview, out _) && _commands.TryResolve(StoragePreferencesContract.Migrate, out _)),
            ["cancelVisible"] = Flag(pending),
            ["cancelEnabled"] = Flag(!StoragePreferencesBusy && _storageStatus?.PendingTransaction is not null
                && _commands.TryResolve(StoragePreferencesContract.CancelMigration, out _)),
        };
    }

    private Dictionary<string, string> StorageCleanupProjection() => new()
    {
        ["enabled"] = Flag(!StoragePreferencesBusy && _queries.TryResolve(StoragePreferencesContract.CleanupPreview, out _)
            && _commands.TryResolve(StoragePreferencesContract.Cleanup, out _)),
    };

    private void BuildStoragePreferences()
    {
        if (_instanceDirectory is not null || _selected != "storage") return;
        _storageRebuildPending = false;
        _storageLocationPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsStorageLocation.d.nxml", StorageLocationProjection()));
        _storageCleanupPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsStorageCleanup.d.nxml", StorageCleanupProjection()));
    }

    private void RefreshStoragePreferencesPresentation()
    {
        if (_instanceDirectory is not null || _selected != "storage" || !_shell.Tree.IsAlive(_storageLocationPanel) || !_shell.Tree.IsAlive(_storageCleanupPanel)) return;
        _dynamic.Reload(_storageLocationPanel, DesktopMarkup.Dynamic("SettingsStorageLocation.d.nxml", StorageLocationProjection()));
        _dynamic.Reload(_storageCleanupPanel, DesktopMarkup.Dynamic("SettingsStorageCleanup.d.nxml", StorageCleanupProjection()));
        _storageRebuildPending = false;
    }

    private void HandleStoragePreferences(XsrSemanticId command, XsrUiEntityId source)
    {
        if (_instanceDirectory is not null || _selected != "storage" || StoragePreferencesBusy || !_shell.Tree.IsAlive(source)) return;
        string name = command == MoveStorage ? "SettingsStorageMove" : command == CancelStorageMigration ? "SettingsStorageCancel"
            : command == CleanStorageTemporary ? "SettingsStorageCleanTemporary" : "SettingsStorageCleanTasks";
        var owner = command == MoveStorage || command == CancelStorageMigration ? _storageLocationPanel : _storageCleanupPanel;
        if (!_shell.Tree.IsAlive(owner) || !CatalogNodes(owner).TryGetValue(name, out var current) || source != current
            || _shell.Tree.GetComponent<XsrUiInput>(source)?.Enabled != true) return;
        CancelStoragePreferences(); _storageStop = new();
        _storagePreviewRead = ReadStoragePreviewAsync(command, _storageStop.Token);
        ObserveTransfer(_storagePreviewRead); RefreshStoragePreferencesPresentation();
    }

    private async Task<StoragePreview?> ReadStoragePreviewAsync(XsrSemanticId command, CancellationToken token)
    {
        if (command == CancelStorageMigration) return new(null, null, _storageStatus?.PendingTransaction);
        if (command == MoveStorage)
        {
            string? selected = await _chooseStorageDirectory!(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (selected is null) return null;
            if (!_queries.TryResolve(StoragePreferencesContract.MigrationPreview, out var route)) throw new IOException(ProductPresentationText.Raw("settings.storage.migrationUnavailable"));
            var preview = await _queries.QueryAsync<StorageMigrationQuery, StorageMigrationPreview>(route, new(selected), cancellationToken: token).ConfigureAwait(false);
            if (!preview.IsSuccess) throw new IOException(preview.Error?.Message ?? ProductPresentationText.Raw("settings.storage.migrationPreviewFailed"));
            return new(preview.Value, null);
        }
        if (!_queries.TryResolve(StoragePreferencesContract.CleanupPreview, out var cleanupRoute)) throw new IOException(ProductPresentationText.Raw("settings.storage.cleanupUnavailable"));
        StorageCleanupKind kind = command == CleanStorageTemporary ? StorageCleanupKind.TemporaryFiles : StorageCleanupKind.FinishedTasks;
        var cleanup = await _queries.QueryAsync<StorageCleanupQuery, StorageCleanupPreview>(cleanupRoute, new(kind), cancellationToken: token).ConfigureAwait(false);
        if (!cleanup.IsSuccess) throw new IOException(cleanup.Error?.Message ?? ProductPresentationText.Raw("settings.storage.cleanupPreviewFailed"));
        return new(null, cleanup.Value);
    }

    // Run this before OnFrame's visibility return so a committed queue still closes the host after navigation.
    private void UpdateStoragePreferences()
    {
        bool changed = false;
        if (_instanceDirectory is null && _selected == "storage" && _storageStatus is null && _storageStatusRead is null
            && !_storageStatusUnavailable
            && _queries.TryResolve(StoragePreferencesContract.Status, out var statusRoute))
        {
            _storageStatusRead = _queries.QueryAsync<StoragePreferencesQuery, StoragePreferencesStatus>(statusRoute, new()).AsTask();
            ObserveTransfer(_storageStatusRead);
        }
        if (_storageStatusRead is { IsCompleted: true } status)
        {
            _storageStatusRead = null;
            if (PendingQuery.Succeeded(status)) { _storageStatus = status.Result.Value; changed = true; }
            else
            {
                _storageStatusUnavailable = true;
                _feedback.Error(status.IsCompletedSuccessfully ? status.Result.Error?.Message ?? ProductPresentationText.Raw("settings.storage.locationReadFailed") : ProductPresentationText.Raw("settings.storage.locationReadFailed"));
            }
        }
        if (_storagePreviewRead is { IsCompleted: true } reading)
        {
            _storagePreviewRead = null; changed = true;
            if (reading.IsFaulted) _feedback.Error(reading.Exception?.GetBaseException().Message ?? ProductPresentationText.Raw("settings.storage.previewFailed"));
            else if (reading.IsCompletedSuccessfully && reading.Result is { } preview)
            {
                if (preview.Cleanup is { Entries.Count: 0 }) _feedback.Info(ProductPresentationText.Raw("settings.storage.cleanupEmpty"));
                else
                {
                    long generation = _storageGeneration;
                    string message = preview.CancelTransaction is not null ? ProductPresentationText.Term("settings.storage.cancelMessage", _shell.Renderer.LocalizeText) : preview.Migration is { } migration
                        ? ProductPresentationText.Format("SettingsStorageMigration", _shell.Renderer.LocalizeText,
                            ("files", migration.Files.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("bytes", migration.Bytes.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                            ("source", migration.SourceDirectory), ("destination", migration.DestinationDirectory))
                        : ProductPresentationText.Format("SettingsStorageCleanup", _shell.Renderer.LocalizeText,
                            ("count", preview.Cleanup!.Entries.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("bytes", preview.Cleanup.Entries.Sum(entry => entry.Bytes).ToString(System.Globalization.CultureInfo.CurrentCulture)),
                            ("identities", string.Join('\n', preview.Cleanup.Entries.Take(12).Select(entry => entry.Identity))));
                    _storageDialog = _feedback.ShowDialog("settings.storage", ProductPresentationText.Raw(preview.CancelTransaction is not null ? "settings.storage.cancelTitle" : preview.Migration is not null ? "settings.storage.migrationTitle" : "settings.storage.cleanupTitle"), message,
                        ProductPresentationText.Raw(preview.CancelTransaction is not null ? "settings.storage.cancelAction" : preview.Migration is not null ? "settings.storage.migrationAction" : "settings.storage.cleanupAction"), ProductPresentationText.Raw("settings.action.back"),
                        accepted => { if (Volatile.Read(ref _storageGeneration) == generation) _storageDecisions.Enqueue((generation, accepted, preview)); }, localizeMessage: false);
                }
            }
        }
        while (_storageDecisions.TryDequeue(out var decision))
        {
            if (decision.Generation != _storageGeneration || _storageStop?.IsCancellationRequested != false) continue;
            _storageDialog = default; changed = true;
            if (!decision.Accepted) continue;
            _storageApplyingMove = decision.Preview.Migration is not null;
            _storageApplyingCancel = decision.Preview.CancelTransaction is not null;
            if (!_commands.TryResolve(decision.Preview.CancelTransaction is not null ? StoragePreferencesContract.CancelMigration
                : _storageApplyingMove ? StoragePreferencesContract.Migrate : StoragePreferencesContract.Cleanup, out var route))
            { _feedback.Error(ProductPresentationText.Raw("settings.storage.operationUnavailable")); continue; }
            _storageApply = decision.Preview.CancelTransaction is { } transaction
                ? _commands.Dispatch(route, new StorageMigrationCancelCommand(transaction), cancellationToken: _storageStop.Token).Completion
                : decision.Preview.Migration is { } migration
                ? _commands.Dispatch(route, new StorageMigrationCommand(migration.DestinationDirectory, migration.Revision), cancellationToken: _storageStop.Token).Completion
                : _commands.Dispatch(route, new StorageCleanupCommand(decision.Preview.Cleanup!.Kind, decision.Preview.Cleanup.Revision), cancellationToken: _storageStop.Token).Completion;
            ObserveTransfer(_storageApply);
        }
        if (_storageApply is { IsCompleted: true } applying)
        {
            _storageApply = null; _storageStatus = null; _storageStatusUnavailable = false; changed = true;
            if (PendingQuery.Succeeded(applying))
            {
                if (_storageApplyingMove) _closeForStorageMigration?.Invoke();
                else _feedback.Info(ProductPresentationText.Raw(_storageApplyingCancel ? "settings.storage.migrationCancelled" : "settings.storage.cleanupComplete"));
            }
            else if (!applying.IsCanceled) _feedback.Error(applying.IsCompletedSuccessfully ? applying.Result.Error?.Message ?? ProductPresentationText.Raw("settings.storage.operationIncomplete") : ProductPresentationText.Raw("settings.storage.operationFailed"));
        }
        if (changed) _storageRebuildPending = true;
        // Status completion runs before the intent loop. Consume queued intents before
        // applying changed availability to their retained controls.
        if (_storageRebuildPending && _catalog is not null && _instanceDirectory is null && _selected == "storage"
            && _pending.IsEmpty) RefreshStoragePreferencesPresentation();
    }

    private void CancelStoragePreferences()
    {
        Interlocked.Increment(ref _storageGeneration);
        _storageStop?.Cancel(); _storageStop?.Dispose(); _storageStop = null;
        _storagePreviewRead = null;
        // Keep an already dispatched completion observable: after its commit a queued migration must close the host.
        if (_storageDialog != Guid.Empty) _feedback.DismissDialog(_storageDialog);
        _storageDialog = default;
        while (_storageDecisions.TryDequeue(out _)) { }
        int pending = _pending.Count;
        for (int i = 0; i < pending && _pending.TryDequeue(out var intent); i++)
            if (!IsStoragePreferencesIntent(intent.Command)) _pending.Enqueue(intent);
    }
}
