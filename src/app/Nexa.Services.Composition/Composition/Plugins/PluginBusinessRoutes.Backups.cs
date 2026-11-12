using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Setup;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record BackupIdentity(PluginIdentity Owner, EntityId Instance, string Manifest);
    private readonly Dictionary<EntityId, BackupIdentity> _backupIdentities = [];
    private Revision BackupRevision(HostInvocation c, EntityId instance, string revision) => new(Token(c, "backups:" + instance.Value + ":" + revision));
    private ValueTask<InstanceBackupSnapshot> BackupScope(HostInvocation c, EntityId instance, CancellationToken token) =>
        Query<InstanceBackupQuery, InstanceBackupSnapshot>(foundationQueries, InstanceBackupContract.Read,
            new(FindInstance(c, instance).Instance.DirectoryPath), token);
    private BackupSummary Backup(HostInvocation c, EntityId instance, ContentBackupManifest manifest)
    {
        var id = new EntityId(Token(c, "backup:" + instance.Value + ":" + manifest.Identity));
        lock (_planGate)
        {
            if (!_backupIdentities.ContainsKey(id) && _backupIdentities.Count >= 4096) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            _backupIdentities[id] = new(c.Identity, instance, manifest.Identity);
        }
        return new(id, instance, manifest.CreatedAt, manifest.Files.Sum(f => f.Bytes), new(Token(c, "backup-manifest:" + id.Value)));
    }
    private async ValueTask<(BackupIdentity Identity, BackupSummary Summary)> FindBackup(HostInvocation c, EntityId id, CancellationToken token)
    {
        BackupIdentity identity;
        lock (_planGate)
            if (!_backupIdentities.TryGetValue(id, out identity!) || identity.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        var scope = await BackupScope(c, identity.Instance, token).ConfigureAwait(false);
        var manifest = scope.Backups.FirstOrDefault(p => p.Identity == identity.Manifest) ?? throw new AbiHostException(AbiErrors.NOT_FOUND);
        return (identity, Backup(c, identity.Instance, manifest));
    }
    private static ContentBackupRetentionPolicy BackupPolicy(RetentionPolicy policy)
    {
        if (policy.KeepCount is < 1 or > 1024 || policy.KeepDays is < 1 or > 3650 || policy.MaximumBytes < 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        if (policy.MaximumBytes != 0) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return new(false, (int)policy.KeepCount, (int)policy.KeepDays);
    }
    private async ValueTask RequireBackupRevision(HostInvocation c, EntityId instance, Revision expected, CancellationToken token)
    {
        if (BackupRevision(c, instance, (await BackupScope(c, instance, token).ConfigureAwait(false)).Revision) != expected)
            throw new AbiHostException(AbiErrors.CONFLICT);
    }
    private IEnumerable<HostCapabilityRoute> BackupRoutes => [
        HostRoutes.Typed<EntityRequest, Page<BackupSummary>>("Backups", "List", async (c, v) =>
        {
            var scope = await BackupScope(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            return new(scope.Backups.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Identity, StringComparer.Ordinal).Select(p => Backup(c, v.Id, p)).ToImmutableArray(), null, BackupRevision(c, v.Id, scope.Revision));
        }),
        HostRoutes.Typed<EntityRequest, BackupSummary>("Backups", "Read", async (c, v) => (await FindBackup(c, v.Id, c.Options.Cancellation).ConfigureAwait(false)).Summary),
        HostRoutes.Typed<EntityRequest, BackupStatistics>("Backups", "Statistics", async (c, v) =>
        {
            var scope = await BackupScope(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            return new(v.Id, scope.Backups.Count, scope.Backups.Sum(p => p.Files.Sum(f => f.Bytes)));
        }),
        HostRoutes.Typed<BackupCapture, MutationPlan>("Backups", "PrepareCapture", (c, v) =>
        {
            if (v.Target is not null || v.Include.Length != 0 || v.Exclude.Length != 0) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var instance = FindInstance(c, v.Instance);
            return ValueTask.FromResult(Plan(c, "backups/capture", BackupCapture.Write(v), "Back up instance " + instance.Summary.Name,
                token => { token.ThrowIfCancellationRequested(); if (FindInstance(c, v.Instance).Summary.Revision != instance.Summary.Revision) throw new AbiHostException(AbiErrors.CONFLICT); return ValueTask.CompletedTask; },
                async token =>
                {
                    await Command(foundationCommands, InstanceBackupContract.Capture, new InstanceBackupCaptureCommand(instance.Instance.DirectoryPath, "Plugin capture"), token).ConfigureAwait(false);
                    return Empty.Write(new());
                }));
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Backups", "Capture", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "backups/capture", "nexa.backups.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<EntityRequest, IntegrityReport>("Backups", "VerifyOffline", async (c, v) =>
        {
            var backup = await FindBackup(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            var report = await Query<InstanceBackupOfflineQuery, OfflineReadinessReport>(foundationQueries, InstanceBackupContract.Offline,
                new(FindInstance(c, backup.Identity.Instance).Instance.DirectoryPath, backup.Identity.Manifest), c.Options.Cancellation).ConfigureAwait(false);
            return new(v.Id, report.Ready, report.MissingOrCorruptPaths.Take(256).Select(p => Issue("BACKUP_INTEGRITY", p, v.Id.Value)).ToImmutableArray(), backup.Summary.Revision);
        }),
        HostRoutes.Typed<BackupTransfer, OperationRef>("Backups", "Export", async (c, v) =>
        {
            if (v.Backup is not { } id) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var backup = await FindBackup(c, id, c.Options.Cancellation).ConfigureAwait(false);
            string destination = Application.Files.Resolve(c.Identity, v.File, true);
            if (Path.Exists(destination)) throw new AbiHostException(AbiErrors.CONFLICT);
            return Services.StartHostOperation(c, "nexa.backups.write", Abi100Schemas.FileRef, async token =>
            {
                await Confirm(c, "backups/export", "Export backup to " + destination, BackupTransfer.Write(v), token).ConfigureAwait(false);
                Application.Files.Resolve(c.Identity, v.File, true);
                await FindBackup(c, id, token).ConfigureAwait(false);
                string stage = Path.Combine(Path.GetDirectoryName(destination)!, ".nexa-backup-export-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    await Command(foundationCommands, InstanceBackupContract.Export, new InstanceBackupExportCommand(
                        FindInstance(c, backup.Identity.Instance).Instance.DirectoryPath, backup.Identity.Manifest, stage), token).ConfigureAwait(false);
                    await Application.Files.MutationGate.WaitAsync(token).ConfigureAwait(false);
                    try { Application.Files.Resolve(c.Identity, v.File, true); token.ThrowIfCancellationRequested(); File.Move(stage, destination, false); }
                    finally { Application.Files.MutationGate.Release(); }
                    return AbiReply<AbiValue>.Ok(FileRef.Write(v.File));
                }
                finally { if (File.Exists(stage)) File.Delete(stage); }
            });
        }),
        HostRoutes.Typed<BackupPrune, MutationPlan>("Backups", "PreviewPrune", async (c, v) =>
        {
            var policy = BackupPolicy(v.Retention);
            string directory = FindInstance(c, v.Instance).Instance.DirectoryPath;
            await RequireBackupRevision(c, v.Instance, v.ExpectedRevision, c.Options.Cancellation).ConfigureAwait(false);
            var preview = await Query<InstanceBackupPruneQuery, ContentPrunePreview>(foundationQueries, InstanceBackupContract.PrunePreview,
                new(directory, policy.KeepCount, policy.KeepDays), c.Options.Cancellation).ConfigureAwait(false);
            return Plan(c, "backups/prune", BackupPrune.Write(v), $"Remove {preview.BackupIdentities.Count} backups and reclaim {preview.Bytes} bytes",
                token => RequireBackupRevision(c, v.Instance, v.ExpectedRevision, token),
                async token =>
                {
                    await Command(foundationCommands, InstanceBackupContract.Prune, new InstanceBackupPruneCommand(directory, policy.KeepCount, policy.KeepDays, preview.Revision), token).ConfigureAwait(false);
                    return Empty.Write(new());
                });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Backups", "Prune", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "backups/prune", "nexa.backups.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<EntityRequest, RetentionPolicy>("Backups", "ReadRetention", async (c, v) =>
        {
            var policy = (await BackupScope(c, v.Id, c.Options.Cancellation).ConfigureAwait(false)).Retention;
            return new(policy.KeepCount, policy.KeepDays, 0);
        }),
        HostRoutes.Typed<BackupRetention, RetentionPolicy>("Backups", "SetRetention", async (c, v) =>
        {
            var policy = BackupPolicy(v.Policy);
            var instance = FindInstance(c, v.Instance);
            var scope = await BackupScope(c, v.Instance, c.Options.Cancellation).ConfigureAwait(false);
            if (BackupRevision(c, v.Instance, scope.Revision) != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            await Confirm(c, "backups/retention", $"Set backup retention for {instance.Summary.Name}: {policy.KeepCount} copies, {policy.KeepDays} days", BackupRetention.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await RequireBackupRevision(c, v.Instance, v.ExpectedRevision, c.Options.Cancellation).ConfigureAwait(false);
            await Command(foundationCommands, InstanceBackupContract.SetRetention, new InstanceBackupRetentionCommand(instance.Instance.DirectoryPath, policy, scope.Revision), c.Options.Cancellation).ConfigureAwait(false);
            return v.Policy;
        })
    ];
}
