using System.Collections.Concurrent;
using Nexa.Services.Logging;
using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ContentWorkspaceAction = XsrSemanticId.Parse("ui.settings.storage.workspace");
    private readonly Dictionary<XsrUiEntityId, (string Kind, string? Identity)> _contentWorkspaceActions = [];
    private readonly ConcurrentQueue<(long Generation, bool Accepted, Func<CancellationToken, Task<ContentWorkspaceResult>> Action)> _contentWorkspaceDecisions = new();
    private DesktopContentWorkspaceRoutes? _contentBackups, _legacyMigration, _diagnosticHistory;
    private Func<CancellationToken, Task<string?>>? _contentChooseDirectory, _contentChooseFile;
    private Func<string>? _diagnosticRawCapture;
    private IReadOnlyList<ContentBackupManifest> _contentBackupRows = [];
    private ContentStoreStatistics? _contentStoreStatistics;
    private ContentBackupRetentionPolicy _contentRetention = new();
    private IReadOnlyList<DurableDiagnosticEntry> _durableDiagnosticRows = [];
    private Task<ContentWorkspaceResult>? _contentWorkspaceTask;
    private CancellationTokenSource? _contentWorkspaceStop;
    private Guid _contentWorkspaceDialog;
    private long _contentWorkspaceGeneration;
    private long _contentAiPolicyRevision = -1;
    private bool _contentWorkspaceLoaded;
    private string? _contentWorkspaceMessage;
    private XsrUiEntityId _contentWorkspacePanel, _contentMigrationPanel, _contentDiagnosticPanel, _contentSummaryView, _contentRetentionView, _contentMessageView;
    private bool _contentSummaryStatistics;
    private readonly Dictionary<string, XsrUiEntityId> _contentBackupViews = new(StringComparer.Ordinal);
    private readonly List<XsrUiEntityId> _contentHistoryViews = [];
    private sealed record DiagnosticRawConfirmation(DiagnosticRawPreview Preview, string Destination);
    private sealed record ContentWorkspaceResult(CustomText? Message = null, ContentStoreStatistics? Statistics = null,
        IReadOnlyList<ContentBackupManifest>? Backups = null, IReadOnlyList<DurableDiagnosticEntry>? History = null,
        CustomText? Confirmation = null, Func<CancellationToken, Task<ContentWorkspaceResult>>? Apply = null, ContentBackupRetentionPolicy? Retention = null,
        DiagnosticAiConfirmation? AiConfirmation = null, DiagnosticAiSuggestion? AiSuggestion = null, DiagnosticRawConfirmation? RawConfirmation = null);
    private bool ContentWorkspaceBusy => _contentWorkspaceTask is not null || _contentWorkspaceDialog != Guid.Empty;

    internal void ConfigureContentWorkspace(Func<CancellationToken, Task<string?>> chooseDirectory,
        Func<CancellationToken, Task<string?>> chooseFile, Func<string> captureRaw)
    {
        _contentBackups = _legacyMigration = _diagnosticHistory = new(_queries, _commands);
        _contentChooseDirectory = chooseDirectory; _contentChooseFile = chooseFile; _diagnosticRawCapture = captureRaw;
    }

    private void BuildContentWorkspace()
    {
        _contentWorkspaceActions.Clear(); _contentBackupViews.Clear(); _contentHistoryViews.Clear();
        _contentWorkspacePanel = _contentMigrationPanel = _contentDiagnosticPanel = _contentSummaryView = _contentRetentionView = _contentMessageView = default;
        if (_instanceDirectory is not null || _selected != "storage" || _contentBackups is null) return;
        var availability = new Dictionary<string, string> { ["enabled"] = Flag(!ContentWorkspaceBusy) };
        _contentWorkspacePanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsContentWorkspace.d.nxml",
            new Dictionary<string, string>(availability) { ["truncated"] = Flag(_contentBackupRows.Count > 16) }));
        _contentMigrationPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsLegacyMigration.d.nxml", availability));
        _contentDiagnosticPanel = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsDurableDiagnostics.d.nxml", availability));
        BuildDiagnosticAiWorkspace();
        RefreshContentWorkspacePresentation();
    }

    private void RefreshContentWorkspacePresentation()
    {
        if (!_shell.Tree.IsAlive(_contentWorkspacePanel) || _instanceDirectory is not null || _selected != "storage") return;
        var availability = new Dictionary<string, string> { ["enabled"] = Flag(!ContentWorkspaceBusy) };
        _dynamic.Reload(_contentWorkspacePanel, DesktopMarkup.Dynamic("SettingsContentWorkspace.d.nxml",
            new Dictionary<string, string>(availability) { ["truncated"] = Flag(_contentBackupRows.Count > 16) }));
        _dynamic.Reload(_contentMigrationPanel, DesktopMarkup.Dynamic("SettingsLegacyMigration.d.nxml", availability));
        _dynamic.Reload(_contentDiagnosticPanel, DesktopMarkup.Dynamic("SettingsDurableDiagnostics.d.nxml", availability));
        var nodes = CatalogNodes(_contentWorkspacePanel);
        var summary = _contentStoreStatistics is { } stats
            ? DesktopMarkup.Dynamic("SettingsContentStats.d.nxml", new Dictionary<string, string>
            {
                ["backups"] = stats.Backups.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["objects"] = stats.Objects.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["physical"] = stats.PhysicalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["logical"] = stats.LogicalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reclaimableBytes"] = stats.ReclaimableBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["savedBytes"] = Math.Max(0, stats.LogicalBytes - stats.ReferencedBytes).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reclaimable"] = Flag(stats.ReclaimableBytes > 0),
                ["clean"] = Flag(stats.ReclaimableBytes == 0)
            }, _shell.Renderer.LocalizeText)
            : DesktopMarkup.Dynamic("SettingsContentIntro.d.nxml");
        if (_shell.Tree.IsAlive(_contentSummaryView) && _contentSummaryStatistics == (_contentStoreStatistics is not null)) _dynamic.Reload(_contentSummaryView, summary);
        else _contentSummaryView = _dynamic.Replace(nodes["ContentWorkspace.Summary"], summary);
        _contentSummaryStatistics = _contentStoreStatistics is not null;
        foreach (var (name, kind) in new[] { ("Refresh", "refresh"), ("Capture", "capture"), ("Import", "import"), ("Prune", "prune") }) RegisterWorkspaceAction(nodes, name, kind);
        var retention = DesktopMarkup.Dynamic(_contentRetention.Enabled ? "SettingsContentRetentionOn.d.nxml" : "SettingsContentRetentionOff.d.nxml", availability);
        if (_shell.Tree.IsAlive(_contentRetentionView)) _dynamic.Reload(_contentRetentionView, retention);
        else _contentRetentionView = _dynamic.Append(nodes["ContentWorkspace.RetentionSlot"], retention);
        RegisterWorkspaceAction(CatalogNodes(_contentRetentionView), "Retention", "retention");
        var backups = _contentBackupRows.Take(16).ToArray(); var live = backups.Select(manifest => manifest.Identity).ToHashSet(StringComparer.Ordinal);
        foreach (string identity in _contentBackupViews.Keys.Where(identity => !live.Contains(identity)).ToArray())
        {
            var retired = _contentBackupViews[identity];
            foreach (var action in CatalogNodes(retired).Values) _contentWorkspaceActions.Remove(action);
            _dynamic.Remove(retired); _contentBackupViews.Remove(identity);
        }
        foreach (var manifest in backups)
        {
            var fragment = DesktopMarkup.Dynamic("SettingsContentBackup.d.nxml", new Dictionary<string, string>(availability)
            {
                ["row"] = "ContentWorkspace.Backup." + manifest.Identity,
                ["actions"] = "ContentWorkspace.Actions." + manifest.Identity,
                ["name"] = manifest.Label,
                ["files"] = manifest.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["created"] = manifest.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                ["verify"] = "ContentWorkspace.Verify." + manifest.Identity,
                ["restore"] = "ContentWorkspace.Restore." + manifest.Identity,
                ["export"] = "ContentWorkspace.Export." + manifest.Identity
            }, _shell.Renderer.LocalizeText);
            if (_contentBackupViews.TryGetValue(manifest.Identity, out var row)) _dynamic.Reload(row, fragment);
            else _contentBackupViews[manifest.Identity] = row = _dynamic.Append(nodes["ContentWorkspace.Backups"], fragment);
            var actions = CatalogNodes(row);
            foreach (var (name, kind) in new[] { ("Verify", "verify"), ("Restore", "restore"), ("Export", "export") }) RegisterWorkspaceAction(actions, name + "." + manifest.Identity, kind, manifest.Identity);
        }
        _dynamic.Reorder(nodes["ContentWorkspace.Backups"], backups.Select(manifest => _contentBackupViews[manifest.Identity]).ToArray());
        RegisterWorkspaceAction(CatalogNodes(_contentMigrationPanel), "Migrate", "migrate");
        var diagnostics = CatalogNodes(_contentDiagnosticPanel); RegisterWorkspaceAction(diagnostics, "Raw", "raw");
        var history = _durableDiagnosticRows.Take(12).ToArray();
        while (_contentHistoryViews.Count > history.Length)
        { _dynamic.Remove(_contentHistoryViews[^1]); _contentHistoryViews.RemoveAt(_contentHistoryViews.Count - 1); }
        for (int index = 0; index < history.Length; index++)
        {
            var entry = history[index];
            var fragment = DesktopMarkup.Dynamic("SettingsDurableDiagnosticRow.d.nxml", new Dictionary<string, string>
            {
                ["timestamp"] = entry.Timestamp.ToLocalTime().ToString("MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                ["module"] = entry.Module,
                ["operation"] = entry.Operation,
                ["stage"] = entry.Stage,
                ["outcome"] = entry.Outcome.ToString()
            }, _shell.Renderer.LocalizeText);
            if (index < _contentHistoryViews.Count) _dynamic.Reload(_contentHistoryViews[index], fragment);
            else _contentHistoryViews.Add(_dynamic.Append(diagnostics["DurableDiagnosticWorkspace.History"], fragment));
        }
        if (_contentWorkspaceMessage is { } message)
        {
            var fragment = DesktopMarkup.Dynamic("SettingsContentMessage.d.nxml", new Dictionary<string, string> { ["text"] = message });
            if (_shell.Tree.IsAlive(_contentMessageView)) _dynamic.Reload(_contentMessageView, fragment);
            else _contentMessageView = _dynamic.Append(nodes["ContentWorkspace.Message"], fragment);
        }
        RefreshDiagnosticAiWorkspace();
    }

    private void RegisterWorkspaceAction(Dictionary<string, XsrUiEntityId> nodes, string name, string kind, string? identity = null)
        => _contentWorkspaceActions[nodes["ContentWorkspace." + name]] = (kind, identity);

    private void HandleContentWorkspace(DesktopUiIntent intent)
    {
        if (!_visible || _instanceDirectory is not null || _selected != "storage" || ContentWorkspaceBusy || _contentBackups is null
            || !_shell.Tree.IsAlive(intent.Source) || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true
            || !_contentWorkspaceActions.TryGetValue(intent.Source, out var action)) return;
        CaptureDiagnosticAiDraft();
        var aiDraft = action.Kind is "ai-facts" or "ai-raw" ? ReadDiagnosticAiRequest(action.Kind) : null;
        if (action.Kind is "ai-facts" or "ai-raw" && aiDraft is null) return;
        CancelContentWorkspace(); _contentWorkspaceStop = new();
        if (aiDraft is not null) _contentAiPolicyRevision = _store.Read<long>(_revisionId).Value;
        // Raw capture reads the current local log snapshot only on this explicit UI action.
        string? raw = action.Kind is "raw" or "ai-raw" ? _diagnosticRawCapture?.Invoke() : null;
        _contentWorkspaceTask = aiDraft is not null ? PreviewDiagnosticAiAsync(aiDraft, raw, _contentWorkspaceStop.Token)
            : RunContentWorkspaceAsync(action.Kind, action.Identity, raw, _contentWorkspaceStop.Token);
        ObserveTransfer(_contentWorkspaceTask); RefreshContentWorkspacePresentation();
    }

    private async Task<ContentWorkspaceResult> RefreshContentWorkspaceAsync(CustomText? message, CancellationToken token) => new(message,
        await _contentBackups!.StatisticsAsync(token).ConfigureAwait(false), await _contentBackups.ListAsync(token).ConfigureAwait(false),
        await _diagnosticHistory!.ReadAsync(token).ConfigureAwait(false), Retention: await _contentBackups.ReadRetentionAsync(token).ConfigureAwait(false));

    private async Task<ContentWorkspaceResult> RunContentWorkspaceAsync(string kind, string? identity, string? raw, CancellationToken token)
    {
        if (kind == "refresh") return await RefreshContentWorkspaceAsync(null, token).ConfigureAwait(false);
        if (kind == "retention")
        {
            var policy = await _contentBackups!.ReadRetentionAsync(token).ConfigureAwait(false);
            var changed = policy with { Enabled = !policy.Enabled };
            return new(Confirmation: changed.Enabled ? CustomFormatted("Retention", ("count", CustomNumber(policy.KeepCount)), ("days", CustomNumber(policy.KeepDays))) : CustomTerm("content.retentionOff"),
                Apply: async cancellation => { await _contentBackups.SetRetentionAsync(changed, cancellation).ConfigureAwait(false); return await RefreshContentWorkspaceAsync(CustomTerm("content.retentionSaved"), cancellation).ConfigureAwait(false); });
        }
        if (kind == "verify")
        {
            var report = await _contentBackups!.VerifyOfflineAsync(identity!, token).ConfigureAwait(false);
            return new(report.Ready ? CustomFormatted("BackupReady", ("count", CustomNumber(report.VerifiedFiles))) : CustomFormatted("BackupNotReady", ("count", CustomNumber(report.MissingOrCorruptPaths.Count)), ("paths", string.Join('\n', report.MissingOrCorruptPaths.Take(8)))));
        }
        if (kind == "prune")
        {
            var policy = await _contentBackups!.ReadRetentionAsync(token).ConfigureAwait(false);
            var preview = await _contentBackups.PreviewPruneAsync(policy.KeepCount, policy.KeepDays, token).ConfigureAwait(false);
            return new(Confirmation: CustomFormatted("Prune", ("count", CustomNumber(policy.KeepCount)), ("days", CustomNumber(policy.KeepDays)), ("backups", CustomNumber(preview.BackupIdentities.Count)), ("objects", CustomNumber(preview.Objects)), ("bytes", CustomNumber(preview.Bytes))),
                Apply: async cancellation => { await _contentBackups.ApplyPruneAsync(policy.KeepCount, policy.KeepDays, preview.Revision, cancellation).ConfigureAwait(false); return await RefreshContentWorkspaceAsync(CustomTerm("content.pruned"), cancellation).ConfigureAwait(false); });
        }
        if (kind == "import")
        {
            string? file = await _contentChooseFile!(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (file is null) return new();
            return new(Confirmation: CustomTerm("content.importPreview"),
                Apply: async cancellation => { await _contentBackups!.ImportThinAsync(file, cancellation).ConfigureAwait(false); return await RefreshContentWorkspaceAsync(CustomTerm("content.imported"), cancellation).ConfigureAwait(false); });
        }
        if (kind == "migrate")
        {
            string? source = await _contentChooseDirectory!(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (source is null) return new();
            string? parent = await _contentChooseDirectory(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (parent is null) return new();
            var preview = await _legacyMigration!.PreviewAsync(source, Path.Combine(parent, "NexaCL-import"), token).ConfigureAwait(false);
            return new(Confirmation: CustomFormatted("Migrate", ("settings", CustomNumber(preview.Settings)), ("offline", CustomNumber(preview.OfflineAccounts)), ("online", CustomNumber(preview.ReauthenticationAccounts)), ("destination", preview.DestinationDirectory), ("warnings", string.Join('\n', preview.Warnings))),
                Apply: async cancellation => { await _legacyMigration.ApplyAsync(preview, cancellation).ConfigureAwait(false); return new(CustomTerm("content.migrated")); });
        }
        string? directory = await _contentChooseDirectory!(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (directory is null) return new();
        if (kind == "capture")
            return new(Confirmation: CustomFormatted("Capture", ("directory", directory)),
                Apply: async cancellation => { await _contentBackups!.CaptureAsync(directory, Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) is { Length: > 0 } name ? name : CustomRaw("content.backupName"), cancellation).ConfigureAwait(false); return await RefreshContentWorkspaceAsync(CustomTerm("content.captured"), cancellation).ConfigureAwait(false); });
        if (kind == "restore")
        {
            string destination = Path.Combine(directory, "restore-" + identity);
            return new(Confirmation: CustomFormatted("Restore", ("destination", destination)),
                Apply: async cancellation => { await _contentBackups!.RestoreAsync(identity!, destination, cancellation).ConfigureAwait(false); return new(CustomFormatted("Restored", ("destination", destination))); });
        }
        if (kind == "export")
        {
            string destination = Path.Combine(directory, "nexa-thin-" + identity + ".json");
            return new(Confirmation: CustomFormatted("Export", ("destination", destination)),
                Apply: async cancellation => { await _contentBackups!.ExportThinAsync(identity!, destination, cancellation).ConfigureAwait(false); return new(CustomTerm("content.exported")); });
        }
        if (kind == "raw")
        {
            var preview = await _contentBackups!.PreviewRawAsync(raw ?? "", token).ConfigureAwait(false); string destination = Path.Combine(directory, "nexa-raw-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
            return new(RawConfirmation: new(preview, destination),
                Apply: async cancellation => { await _contentBackups.ExportRawAsync(destination, preview, preview.Revision, cancellation).ConfigureAwait(false); return new(CustomTerm("content.rawExported")); });
        }
        return new();
    }

    private void UpdateContentWorkspace()
    {
        CaptureDiagnosticAiDraft();
        bool visible = _visible && _instanceDirectory is null && _selected == "storage";
        if (!visible) { if (ContentWorkspaceBusy) CancelContentWorkspace(); ClearDiagnosticAiSecret(); return; }
        if (_contentAiPolicyRevision >= 0 && (!DiagnosticAiEnabled || _store.Read<long>(_revisionId).Value != _contentAiPolicyRevision))
        { CancelContentWorkspace(); ClearDiagnosticAiSecret(); _feedback.Info(CustomCopy("ai.policyChanged")); }
        // The current controls must consume their queued intent before refresh completion replaces them.
        if (_pending.Any(intent => intent.Command == Choice || intent.Command == ContentWorkspaceAction)) return;
        if (_diagnosticAiWorkspaceEnabled is { } builtEnabled && builtEnabled != DiagnosticAiEnabled)
        {
            if (!DiagnosticAiEnabled) ClearDiagnosticAiSecret();
            RefreshContentWorkspacePresentation();
        }
        if (_contentBackups is null) return;
        if (_contentWorkspaceActions.Count == 0) RefreshContentWorkspacePresentation();
        if (!_contentWorkspaceLoaded && _contentWorkspaceTask is null)
        {
            _contentWorkspaceLoaded = true; _contentWorkspaceStop = new();
            _contentWorkspaceTask = RefreshContentWorkspaceAsync(null, _contentWorkspaceStop.Token); ObserveTransfer(_contentWorkspaceTask);
            RefreshContentWorkspacePresentation();
        }
        if (_contentWorkspaceTask is { IsCompleted: true } completed)
        {
            _contentWorkspaceTask = null;
            if (completed.IsFaulted) _feedback.Error(completed.Exception!.GetBaseException().Message);
            else if (completed.IsCompletedSuccessfully)
            {
                var result = completed.Result;
                if (result.Apply is null) _contentAiPolicyRevision = -1;
                if (result.Statistics is not null) _contentStoreStatistics = result.Statistics;
                if (result.Backups is not null) _contentBackupRows = result.Backups;
                if (result.History is not null) _durableDiagnosticRows = result.History;
                if (result.Retention is not null) _contentRetention = result.Retention;
                if (result.Message is not null) { _contentWorkspaceMessage = ProjectCustomText(result.Message); _feedback.Info(_contentWorkspaceMessage, localizeMessage: false); }
                if (result.AiSuggestion is { } suggestion) PresentDiagnosticAiSuggestion(suggestion);
                string? confirmation = result.AiConfirmation is { } ai ? FormatDiagnosticAiConfirmation(ai)
                    : result.RawConfirmation is { } raw ? FormatDiagnosticRawConfirmation(raw) : result.Confirmation is { } text ? ProjectCustomText(text) : null;
                if (result.Apply is { } apply && confirmation is not null)
                {
                    long generation = _contentWorkspaceGeneration;
                    _contentWorkspaceDialog = _feedback.ShowDialog("settings.content-workspace", CustomCopy("content.confirmTitle"), confirmation, CustomCopy("content.confirm"), CustomCopy("content.back"),
                        accepted => { if (Volatile.Read(ref _contentWorkspaceGeneration) == generation) _contentWorkspaceDecisions.Enqueue((generation, accepted, apply)); },
                        localizeMessage: false);
                }
            }
            RefreshContentWorkspacePresentation();
        }
        while (_contentWorkspaceDecisions.TryDequeue(out var decision))
        {
            if (decision.Generation != _contentWorkspaceGeneration || _contentWorkspaceStop?.IsCancellationRequested != false) continue;
            _contentWorkspaceDialog = default;
            if (decision.Accepted) { _contentWorkspaceTask = decision.Action(_contentWorkspaceStop.Token); ObserveTransfer(_contentWorkspaceTask); }
            RefreshContentWorkspacePresentation();
        }
    }

    private string FormatDiagnosticRawConfirmation(DiagnosticRawConfirmation confirmation)
        => CustomFormat("RawConfirmation", new()
        {
            ["bytes"] = confirmation.Preview.Utf8Bytes.ToString(System.Globalization.CultureInfo.CurrentCulture),
            ["destination"] = confirmation.Destination,
            ["preview"] = confirmation.Preview.RedactedText,
        });

    private void CancelContentWorkspace()
    {
        Interlocked.Increment(ref _contentWorkspaceGeneration); _contentWorkspaceStop?.Cancel(); _contentWorkspaceStop?.Dispose(); _contentWorkspaceStop = null;
        _contentAiPolicyRevision = -1;
        _contentWorkspaceTask = null; _contentWorkspaceActions.Clear();
        if (_contentWorkspaceDialog != Guid.Empty) _feedback.DismissDialog(_contentWorkspaceDialog); _contentWorkspaceDialog = default;
        if (_diagnosticAiResultDialog != Guid.Empty) _feedback.DismissDialog(_diagnosticAiResultDialog); _diagnosticAiResultDialog = default;
        while (_contentWorkspaceDecisions.TryDequeue(out _)) { }
    }
}
