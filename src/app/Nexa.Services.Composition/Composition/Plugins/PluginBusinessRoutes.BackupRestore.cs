using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Setup;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record BackupDestination(PluginIdentity Owner, FileRef File);
    private readonly Dictionary<EntityId, BackupDestination> _backupDestinations = [];
    private string BackupDestinationPath(HostInvocation c, EntityId id)
    {
        BackupDestination destination;
        lock (_planGate)
            if (!_backupDestinations.TryGetValue(id, out destination!) || destination.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        string path = Application.Files.Resolve(c.Identity, destination.File, true);
        if (Path.Exists(path)) throw new AbiHostException(AbiErrors.CONFLICT);
        return path;
    }
    private IEnumerable<HostCapabilityRoute> BackupRestoreRoutes => [
        HostRoutes.Typed<FileRef, EntityId>("Backups", "PrepareRestoreDestination", (c, v) =>
        {
            string path = Application.Files.Resolve(c.Identity, v, true);
            if (Path.Exists(path)) throw new AbiHostException(AbiErrors.CONFLICT);
            var id = new EntityId(Token(c, "backup-destination:" + v.Token));
            lock (_planGate)
            {
                if (!_backupDestinations.ContainsKey(id) && _backupDestinations.Count >= 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                _backupDestinations[id] = new(c.Identity, v);
            }
            return ValueTask.FromResult(id);
        }),
        HostRoutes.Typed<BackupRestore, MutationPlan>("Backups", "PreviewRestore", async (c, v) =>
        {
            var backup = await FindBackup(c, v.Backup, c.Options.Cancellation).ConfigureAwait(false);
            if (backup.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            string destination = BackupDestinationPath(c, v.Destination);
            return Plan(c, "backups/restore", BackupRestore.Write(v), "Restore backup into " + destination, async token =>
            {
                if ((await FindBackup(c, v.Backup, token).ConfigureAwait(false)).Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
                if (BackupDestinationPath(c, v.Destination) != destination) throw new AbiHostException(AbiErrors.CONFLICT);
            }, async token =>
            {
                await Application.Files.MutationGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    BackupDestinationPath(c, v.Destination);
                    await Command(foundationCommands, InstanceBackupContract.Restore, new InstanceBackupRestoreCommand(
                        FindInstance(c, backup.Identity.Instance).Instance.DirectoryPath, backup.Identity.Manifest, destination), token).ConfigureAwait(false);
                }
                finally { Application.Files.MutationGate.Release(); }
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Backups", "Restore", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "backups/restore", "nexa.backups.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<BackupImportRequest, OperationRef>("Backups", "ImportInto", (c, v) => ImportBackup(c, v.Instance, v.File)),
        HostRoutes.Typed<BackupTransfer, OperationRef>("Backups", "Import", async (c, v) =>
        {
            if (v.Backup is null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var anchor = await FindBackup(c, v.Backup, c.Options.Cancellation).ConfigureAwait(false);
            return await ImportBackup(c, anchor.Identity.Instance, v.File).ConfigureAwait(false);
        })
    ];
    private ValueTask<OperationRef> ImportBackup(HostInvocation c, EntityId instanceId, FileRef file)
    {
        var instance = FindInstance(c, instanceId);
        string source = Application.Files.Resolve(c.Identity, file);
        return ValueTask.FromResult(Services.StartHostOperation(c, "nexa.backups.write", Abi100Schemas.Empty, async token =>
        {
            await Confirm(c, "backups/import", "Import backup manifest into " + instance.Summary.Name, BackupImportRequest.Write(new(instanceId, file)), token).ConfigureAwait(false);
            Application.Files.Resolve(c.Identity, file);
            if (FindInstance(c, instanceId).Summary.Revision != instance.Summary.Revision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            await Command(foundationCommands, InstanceBackupContract.Import, new InstanceBackupImportCommand(instance.Instance.DirectoryPath, source), token).ConfigureAwait(false);
            return AbiReply<AbiValue>.Ok(Empty.Write(new()));
        }));
    }
}
