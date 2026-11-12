using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private WorldSummary World(HostInvocation c, ContentRecord record, InstanceWorldMetadata metadata) => new(record.Public.Id, record.Identity.Instance,
        metadata.LevelName, Sessions().Items.Any(p => p.InstanceDirectory == record.Snapshot.InstanceDirectory && p.State is MinecraftProcessState.Created or MinecraftProcessState.Running),
        new(Token(c, "world:" + record.Public.Id.Value + ":" + metadata.Revision)));
    private async ValueTask<(ContentRecord Content, InstanceWorldMetadata Metadata, WorldSummary Summary)> FindWorld(HostInvocation c, EntityId id, CancellationToken token)
    {
        var record = await FindContent(c, id, token).ConfigureAwait(false);
        if (record.Identity.Page != "saves" || !record.Entry.IsDirectory) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        var metadata = await Query<InstanceWorldMetadataQuery, InstanceWorldMetadata>(foundationQueries, InstanceWorldContract.Read,
            new(record.Snapshot.InstanceDirectory, record.Identity.Name), token).ConfigureAwait(false);
        return (record, metadata, World(c, record, metadata));
    }
    private IEnumerable<HostCapabilityRoute> WorldRoutes => [
        HostRoutes.Typed<EntityRequest, Page<WorldSummary>>("Minecraft.Worlds", "List", async (c, v) =>
        {
            var snapshot = await Management(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            var content = snapshot.Contents.FirstOrDefault(p => p.PageId == "saves");
            if (content is null || !content.Complete) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            if (content.Entries.Count > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var items = ImmutableArray.CreateBuilder<WorldSummary>();
            foreach (var entry in content.Entries.Where(e => e.IsDirectory))
            {
                var item = Content(c, v.Id, "saves", entry);
                var metadata = entry.World ?? await Query<InstanceWorldMetadataQuery, InstanceWorldMetadata>(foundationQueries, InstanceWorldContract.Read,
                    new(snapshot.InstanceDirectory, entry.Name), c.Options.Cancellation).ConfigureAwait(false);
                items.Add(World(c, new(new(c.Identity, v.Id, "saves", entry.Name), snapshot, entry, item), metadata));
            }
            var result = new Page<WorldSummary>(items.ToImmutable(), null, new("pending")); return result with { Revision = ProjectionRevision(result) };
        }),
        HostRoutes.Typed<EntityRequest, WorldSummary>("Minecraft.Worlds", "Read", async (c, v) => (await FindWorld(c, v.Id, c.Options.Cancellation).ConfigureAwait(false)).Summary),
        HostRoutes.Typed<EntityRequest, WorldMetadata>("Minecraft.Worlds", "ReadMetadata", async (c, v) =>
        {
            var world = await FindWorld(c, v.Id, c.Options.Cancellation).ConfigureAwait(false); var m = world.Metadata;
            return new(v.Id, new("nexa:minecraft/java-world"), [
                new("name", new AbiValue.Utf8String(m.LevelName)), new("gameVersion", new AbiValue.Utf8String(m.GameVersion)),
                new("dataVersion", m.DataVersion is { } data ? new AbiValue.Int64(data) : new AbiValue.Null()),
                new("seed", m.Seed is { } seed ? new AbiValue.Int64(seed) : new AbiValue.Null()),
                new("gameMode", m.GameMode is { } gameMode ? new AbiValue.Int64(gameMode) : new AbiValue.Null()),
                new("difficulty", m.Difficulty is { } difficulty ? new AbiValue.Int64(difficulty) : new AbiValue.Null()),
                new("hardcore", new AbiValue.Bool(m.Hardcore)), new("locked", new AbiValue.Bool(m.Locked)),
                new("size", new AbiValue.Int64(m.Size)), new("sizeComplete", new AbiValue.Bool(m.SizeComplete)),
                new("lastPlayed", m.LastPlayed is { } played ? new AbiValue.Utf8String(played.ToString("O")) : new AbiValue.Null())]);
        }),
        HostRoutes.Typed<WorldCopyRequest, OperationRef>("Minecraft.Worlds", "Copy", async (c, v) =>
        {
            var world = await FindWorld(c, v.World, c.Options.Cancellation).ConfigureAwait(false);
            if (v.Destination != world.Summary.Instance) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            if (v.ExpectedRevision != world.Summary.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
            return Services.StartHostOperation(c, "nexa.minecraft.worlds.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "worlds/copy", "Copy world " + world.Summary.Name + " to " + v.Name, WorldCopyRequest.Write(v), token).ConfigureAwait(false);
                await Command(foundationCommands, InstanceWorldContract.Copy, new InstanceWorldCopyCommand(world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name, v.Name, world.Metadata.Revision), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        }),
        HostRoutes.Typed<EntityRequest, Page<ContentItem>>("Minecraft.Worlds", "ListDataPacks", async (c, v) =>
        {
            var world = await FindWorld(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            return new(world.Metadata.DataPacks.Where(p => !p.Trashed).Select(p => new ContentItem(new(Token(c, "datapack:" + v.Id.Value + ":" + p.Id)),
                world.Summary.Instance, new("nexa:content/datapacks"), p.Name,
                new(new(new("nexa:world-datapacks"), Token(c, "datapack:" + v.Id.Value + ":" + p.Id)), world.Summary.Revision.Value), p.Enabled, world.Summary.Revision)).ToImmutableArray(), null, world.Summary.Revision);
        }),
        HostRoutes.Typed<EntityRequest, OperationRef>("Minecraft.Worlds", "Backup", async (c, v) =>
        {
            var world = await FindWorld(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            return Services.StartHostOperation(c, "nexa.minecraft.worlds.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "worlds/backup", "Back up world " + world.Summary.Name, EntityRequest.Write(v), token).ConfigureAwait(false);
                await Command(foundationCommands, InstanceWorldSnapshotContract.Capture, new InstanceWorldSnapshotCaptureCommand(world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name, world.Metadata.Revision), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        }),
        HostRoutes.Typed<EntityRequest, IntegrityReport>("Minecraft.Worlds", "ReadHealth", async (c, v) =>
        {
            var world = await FindWorld(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            var health = await Query<InstanceWorldHealthQuery, InstanceWorldHealth>(foundationQueries, InstanceWorldHealthContract.Read,
                new(world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name), c.Options.Cancellation).ConfigureAwait(false);
            var issues = health.Issues.Take(256).Select(t => Issue("WORLD_HEALTH", t, v.Id.Value)).ToImmutableArray();
            if (!health.Complete) issues = issues.Add(Issue("INCOMPLETE", "The world scan is incomplete.", v.Id.Value));
            return new(v.Id, health.Healthy, issues, world.Summary.Revision);
        })
    ];
}
