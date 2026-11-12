using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record WorldBackupIdentity(PluginIdentity Owner, EntityId World, string Snapshot);
    private sealed record WorldDestinationIdentity(PluginIdentity Owner, WorldRestoreDestination Request);
    private readonly Dictionary<EntityId, WorldBackupIdentity> _worldBackups = [];
    private readonly Dictionary<EntityId, WorldDestinationIdentity> _worldDestinations = [];
    private async ValueTask<Page<BackupSummary>> WorldBackups(HostInvocation c, EntityId id, CancellationToken token)
    {
        var world = await FindWorld(c, id, token).ConfigureAwait(false);
        var listing = await Query<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(foundationQueries, InstanceWorldSnapshotContract.List,
            new(world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name), token).ConfigureAwait(false);
        if (listing.Count > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        var values = ImmutableArray.CreateBuilder<BackupSummary>();
        lock (_planGate)
            foreach (var item in listing)
            {
                var backupId = new EntityId(Token(c, "world-backup:" + id.Value + ":" + item.Identity));
                if (!_worldBackups.ContainsKey(backupId) && _worldBackups.Count >= 4096) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                _worldBackups[backupId] = new(c.Identity, id, item.Identity);
                values.Add(new(backupId, world.Summary.Instance, item.CreatedAt, item.Bytes, new(Token(c, "world-backup-revision:" + backupId.Value))));
            }
        var page = new Page<BackupSummary>(values.ToImmutable(), null, new("world-backups"));
        return page with { Revision = ProjectionRevision(page) };
    }
    private IEnumerable<HostCapabilityRoute> WorldRestoreRoutes => [
        HostRoutes.Typed<EntityRequest, Page<BackupSummary>>("Minecraft.Worlds", "ListBackups", (c, v) => WorldBackups(c, v.Id, c.Options.Cancellation)),
        HostRoutes.Typed<WorldRestoreDestination, EntityId>("Minecraft.Worlds", "PrepareRestoreDestination", async (c, v) =>
        {
            if (!MinecraftInstanceReference.IsSafe(v.Name) || v.Name.StartsWith(".nexa-", StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var world = await FindWorld(c, v.World, c.Options.Cancellation).ConfigureAwait(false);
            if (world.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            if (Path.Exists(Path.Combine(world.Content.Snapshot.GameDirectory, "saves", v.Name))) throw new AbiHostException(AbiErrors.CONFLICT);
            var id = new EntityId(Token(c, "world-destination:" + v.World.Value + ":" + v.Name));
            lock (_planGate)
            {
                if (!_worldDestinations.ContainsKey(id) && _worldDestinations.Count >= 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                _worldDestinations[id] = new(c.Identity, v);
            }
            return id;
        }),
        HostRoutes.Typed<BackupRestore, MutationPlan>("Minecraft.Worlds", "PreviewRestore", async (c, v) =>
        {
            WorldBackupIdentity backup; WorldDestinationIdentity destination;
            lock (_planGate)
            {
                if (!_worldBackups.TryGetValue(v.Backup, out backup!) || backup.Owner != c.Identity
                    || !_worldDestinations.TryGetValue(v.Destination, out destination!) || destination.Owner != c.Identity || destination.Request.World != backup.World)
                    throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            }
            var summary = (await WorldBackups(c, backup.World, c.Options.Cancellation).ConfigureAwait(false)).Items.FirstOrDefault(b => b.Id == v.Backup)
                ?? throw new AbiHostException(AbiErrors.NOT_FOUND);
            if (summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            var world = await FindWorld(c, backup.World, c.Options.Cancellation).ConfigureAwait(false);
            return Plan(c, "worlds/restore", BackupRestore.Write(v), "Restore world snapshot as " + destination.Request.Name, async token =>
            {
                if ((await FindWorld(c, backup.World, token).ConfigureAwait(false)).Summary.Revision != destination.Request.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            }, async token =>
            {
                await Command(foundationCommands, InstanceWorldSnapshotContract.Restore, new InstanceWorldSnapshotRestoreCommand(
                    world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name, backup.Snapshot, destination.Request.Name, world.Metadata.Revision), token).ConfigureAwait(false);
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Worlds", "Restore", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "worlds/restore", "nexa.minecraft.worlds.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<EntityRequest, MutationPlan>("Minecraft.Worlds", "PlanDelete", async (c, v) =>
        {
            var world = await FindWorld(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            return Plan(c, "worlds/delete", EntityRequest.Write(v), "Move world " + world.Summary.Name + " to trash", async token =>
            {
                if ((await FindWorld(c, v.Id, token).ConfigureAwait(false)).Summary.Revision != world.Summary.Revision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            }, async token =>
            {
                await Command(foundationCommands, InstanceManagementContract.RemoveContent, new InstanceContentRemoveCommand(world.Content.Snapshot.InstanceDirectory,
                    "saves", world.Content.Identity.Name, true, world.Content.Entry.Size, world.Content.Entry.ModifiedUtcTicks), token).ConfigureAwait(false);
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Worlds", "Delete", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "worlds/delete", "nexa.minecraft.worlds.write", Abi100Schemas.Empty)))
    ];
}
