using System.Collections.Concurrent;
using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId LauncherMigrationAction = XsrSemanticId.Parse("ui.settings.launcher-migration.action");
    private readonly Dictionary<XsrUiEntityId, (string Kind, string? Instance)> _launcherMigrationActions = [];
    private readonly ConcurrentQueue<(long Generation, bool Accepted, string Kind, string[] Selected)> _launcherMigrationDecisions = new();
    private readonly HashSet<string> _launcherMigrationSelected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XsrUiEntityId> _launcherMigrationRows = new(StringComparer.Ordinal);
    private readonly Dictionary<int, XsrUiEntityId> _launcherMigrationSourceRows = [];
    private static readonly LauncherMigrationSourceKind[] LauncherMigrationKinds = Enum.GetValues<LauncherMigrationSourceKind>();
    private Func<CancellationToken, Task<string?>>? _chooseLauncherMigrationDirectory;
    private XsrCommandRouter? _launcherMigrationLibrary;
    private LauncherMigrationSourceKind _launcherMigrationKind;
    private string? _launcherMigrationSource, _launcherMigrationDestination, _launcherMigrationMessage;
    private LauncherMigrationPreview? _launcherMigrationPreview;
    private LauncherMigrationDiscoveryResult? _launcherMigrationDiscovery;
    private LauncherMigrationReceipt? _launcherMigrationReceipt, _launcherMigrationCleanupReceipt;
    private readonly ConcurrentQueue<(LauncherMigrationReceipt Receipt, bool Complete)> _launcherMigrationCleanupCompletions = new();
    private Task<LauncherMigrationResult>? _launcherMigrationTask;
    private CancellationTokenSource? _launcherMigrationStop;
    private long _launcherMigrationGeneration;
    private int _launcherMigrationCleanupActive;
    private bool _launcherMigrationPresentationDirty;
    private Guid _launcherMigrationDialog;
    private XsrUiEntityId _launcherMigrationPanel, _launcherMigrationStatus, _launcherMigrationWarnings;
    private bool LauncherMigrationLoading => _launcherMigrationTask is not null || Volatile.Read(ref _launcherMigrationCleanupActive) != 0;
    private bool LauncherMigrationBusy => LauncherMigrationLoading || _launcherMigrationDialog != Guid.Empty;
    private sealed record LauncherMigrationResult(string Kind, string? Path = null,
        LauncherMigrationPreview? Preview = null, LauncherMigrationReceipt? Receipt = null, bool Registered = false,
        LauncherMigrationDiscoveryResult? Discovery = null);

    internal void ConfigureLauncherMigration(Func<CancellationToken, Task<string?>> chooseDirectory, XsrCommandRouter libraryCommands)
    { _chooseLauncherMigrationDirectory = chooseDirectory; _launcherMigrationLibrary = libraryCommands; }

    private void BuildLauncherMigration()
    {
        _launcherMigrationActions.Clear(); _launcherMigrationRows.Clear(); _launcherMigrationSourceRows.Clear();
        _launcherMigrationPanel = _launcherMigrationStatus = _launcherMigrationWarnings = default;
        if (_instanceDirectory is not null || _selected != "storage" || _chooseLauncherMigrationDirectory is null) return;
        _launcherMigrationPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsLauncherMigration.d.nxml", LauncherMigrationProjection()));
        RefreshLauncherMigrationPresentation();
    }

    private void HandleLauncherMigration(DesktopUiIntent intent)
    {
        if (!_visible || _instanceDirectory is not null || _selected != "storage" || !_shell.Tree.IsAlive(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true
            || !_launcherMigrationActions.TryGetValue(intent.Source, out var action)) return;
        if (action.Kind == "cancel")
        {
            CancelLauncherMigration(); _launcherMigrationMessage = MigrationCopy("cancelled");
            RefreshLauncherMigrationPresentation(); return;
        }
        if (LauncherMigrationBusy || _launcherMigrationCleanupReceipt is not null && action.Kind != "cleanup") return;
        if (action.Kind == "kind")
        {
            _launcherMigrationKind = LauncherMigrationKinds[(Array.IndexOf(LauncherMigrationKinds, _launcherMigrationKind) + 1) % LauncherMigrationKinds.Length];
            _launcherMigrationDiscovery = null;
            ClearLauncherMigrationPreview(); RefreshLauncherMigrationPresentation(); return;
        }
        if (action.Kind == "detected")
        {
            if (!int.TryParse(action.Instance, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                || _launcherMigrationDiscovery is not { } discovery || index < 0 || index >= discovery.Sources.Count) return;
            var source = discovery.Sources[index];
            _launcherMigrationKind = source.SourceKind; _launcherMigrationSource = source.Directory;
            ClearLauncherMigrationPreview(); RefreshLauncherMigrationPresentation(); return;
        }
        if (action.Kind == "select")
        {
            if (_launcherMigrationPreview?.Instances.Any(instance => instance.Id == action.Instance && instance.CanImport) != true) return;
            if (!_launcherMigrationSelected.Add(action.Instance!)) _launcherMigrationSelected.Remove(action.Instance!);
            RefreshLauncherMigrationPresentation(); return;
        }
        if (action.Kind == "undo" && _launcherMigrationReceipt is { } undoReceipt && !LauncherMigrationHasRemainingLibraryRoot(undoReceipt))
        { _launcherMigrationMessage = MigrationCopy("lastRoot"); RefreshLauncherMigrationPresentation(); return; }
        if (action.Kind == "apply" || action.Kind == "undo")
        {
            string[] selected = _launcherMigrationSelected.Order(StringComparer.Ordinal).ToArray();
            if (action.Kind == "apply" && (_launcherMigrationPreview is null || selected.Length == 0) || action.Kind == "undo" && _launcherMigrationReceipt is null) return;
            CancelLauncherMigration(); _launcherMigrationStop = new();
            long generation = _launcherMigrationGeneration;
            string message = action.Kind == "undo"
                ? MigrationFormat("UndoConfirmation", ("destination", _launcherMigrationReceipt!.DestinationDirectory))
                : MigrationFormat("Confirmation", ("destination", _launcherMigrationPreview!.Query.DestinationDirectory),
                    ("instances", string.Join('\n', _launcherMigrationPreview.Instances.Where(instance => selected.Contains(instance.Id, StringComparer.Ordinal)).Select(instance => instance.Name))));
            _launcherMigrationDialog = _feedback.ShowDialog("settings.launcher-migration", MigrationCopy("confirmTitle"), message,
                MigrationCopy(action.Kind == "undo" ? "undo" : "apply"), MigrationCopy("back"),
                accepted => { if (Volatile.Read(ref _launcherMigrationGeneration) == generation) _launcherMigrationDecisions.Enqueue((generation, accepted, action.Kind, selected)); }, localizeMessage: false);
            RefreshLauncherMigrationPresentation(); return;
        }
        StartLauncherMigrationOperation(action.Kind, []);
    }

    private void StartLauncherMigrationOperation(string kind, string[] selected)
    {
        CancelLauncherMigration(); _launcherMigrationStop = new();
        var token = _launcherMigrationStop.Token;
        _launcherMigrationTask = RunLauncherMigrationAsync(kind, selected, _launcherMigrationPreview, kind == "cleanup" ? _launcherMigrationCleanupReceipt : _launcherMigrationReceipt,
            _launcherMigrationKind, _launcherMigrationSource, _launcherMigrationDestination, token);
        ObserveTransfer(_launcherMigrationTask); RefreshLauncherMigrationPresentation();
    }

    private void UpdateLauncherMigration()
    {
        bool changed = ReadLauncherMigrationCleanupCompletions() || _launcherMigrationPresentationDirty;
        if (!_visible || _instanceDirectory is not null || _selected != "storage")
        { if (LauncherMigrationBusy) CancelLauncherMigration(); return; }
        if (_pending.Any(intent => intent.Command == LauncherMigrationAction)) return;
        while (_launcherMigrationDecisions.TryDequeue(out var decision))
        {
            if (decision.Generation != _launcherMigrationGeneration || _launcherMigrationStop?.IsCancellationRequested != false) continue;
            _launcherMigrationDialog = default; changed = true;
            if (decision.Accepted) StartLauncherMigrationOperation(decision.Kind, decision.Selected);
        }
        if (_launcherMigrationTask is { IsCompleted: true } task)
        {
            _launcherMigrationTask = null; changed = true;
            if (task.IsFaulted)
            {
                _launcherMigrationMessage = task.Exception!.GetBaseException().Message;
                _feedback.Error(_launcherMigrationMessage, localizeMessage: false);
            }
            else if (task.IsCompletedSuccessfully)
            {
                var result = task.Result;
                if (result.Kind == "source" && result.Path is not null) { _launcherMigrationSource = result.Path; ClearLauncherMigrationPreview(); }
                if (result.Kind == "destination" && result.Path is not null) { _launcherMigrationDestination = result.Path; ClearLauncherMigrationPreview(); }
                if (result.Discovery is { } discovery)
                {
                    _launcherMigrationDiscovery = discovery;
                    _launcherMigrationMessage = MigrationCopy(discovery.Sources.Count == 0 ? "discoveryEmpty" : "discoveryReady");
                }
                if (result.Preview is { } preview)
                {
                    _launcherMigrationPreview = preview; _launcherMigrationReceipt = null; _launcherMigrationSelected.Clear();
                    foreach (var instance in preview.Instances.Where(instance => instance.CanImport)) _launcherMigrationSelected.Add(instance.Id);
                    _launcherMigrationMessage = MigrationCopy(preview.Instances.Count == 0 ? "empty" : "previewReady");
                }
                if (result.Receipt is { } receipt)
                {
                    _launcherMigrationReceipt = receipt; _launcherMigrationDestination = receipt.DestinationDirectory;
                    _launcherMigrationPreview = null; _launcherMigrationSelected.Clear();
                    _launcherMigrationMessage = MigrationCopy(result.Registered ? "complete" : result.Kind == "recover" ? "recovered" : "registrationFailed");
                }
            }
        }
        changed |= ReadLauncherMigrationCleanupCompletions();
        if (changed) RefreshLauncherMigrationPresentation();
    }

    // Library cleanup follows an already committed disk removal and must survive navigation.
    // This ledger records that maintenance result without publishing a retired page's preview.
    private bool ReadLauncherMigrationCleanupCompletions()
    {
        bool changed = false;
        while (_launcherMigrationCleanupCompletions.TryDequeue(out var completion))
        {
            _launcherMigrationCleanupReceipt = completion.Complete ? null : completion.Receipt;
            if (_launcherMigrationReceipt?.Identity == completion.Receipt.Identity) _launcherMigrationReceipt = null;
            _launcherMigrationMessage = MigrationCopy(completion.Complete ? "undone" : "cleanupFailed");
            if (completion.Complete && _launcherMigrationDestination == completion.Receipt.DestinationDirectory)
                _launcherMigrationDestination = null;
            changed = true; _launcherMigrationPresentationDirty = true;
        }
        return changed;
    }

    private void ClearLauncherMigrationPreview()
    { _launcherMigrationPreview = null; _launcherMigrationReceipt = null; _launcherMigrationSelected.Clear(); _launcherMigrationMessage = null; }

    private void CancelLauncherMigration()
    {
        Interlocked.Increment(ref _launcherMigrationGeneration);
        _launcherMigrationStop?.Cancel(); _launcherMigrationStop?.Dispose(); _launcherMigrationStop = null;
        if (_launcherMigrationTask is { } retired) ObserveTransfer(retired);
        _launcherMigrationTask = null;
        if (_launcherMigrationDialog != Guid.Empty) _feedback.DismissDialog(_launcherMigrationDialog);
        _launcherMigrationDialog = default;
        while (_launcherMigrationDecisions.TryDequeue(out _)) { }
    }
}
