using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> WorldEditRoutes => [
        HostRoutes.Typed<WorldRenameRequest, WorldSummary>("Minecraft.Worlds", "Rename", async (c, v) =>
        {
            var world = await FindWorld(c, v.World, c.Options.Cancellation).ConfigureAwait(false);
            if (world.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            if (string.IsNullOrWhiteSpace(v.Name) || v.Name.Length > 256 || v.Name.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            await Confirm(c, "worlds/rename", "Rename world " + world.Summary.Name + " to " + v.Name, WorldRenameRequest.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Command(foundationCommands, InstanceWorldContract.Rename,
                new InstanceWorldRenameCommand(world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name, v.Name, world.Metadata.Revision), c.Options.Cancellation).ConfigureAwait(false);
            return (await FindWorld(c, v.World, c.Options.Cancellation).ConfigureAwait(false)).Summary;
        }),
        HostRoutes.Typed<DataPackSelection, MutationPlan>("Minecraft.Worlds", "PlanSetDataPack", async (c, v) =>
        {
            var world = await FindWorld(c, v.World, c.Options.Cancellation).ConfigureAwait(false);
            if (world.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            if (!world.Metadata.DataPacksSupported) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            if (v.EnabledOrder.Length is < 1 or > 256 || v.EnabledOrder.Distinct().Count() != v.EnabledOrder.Length || v.Packs.Length > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var available = world.Metadata.DataPacks.Where(p => !p.Trashed).ToDictionary(p => Token(c, "datapack:" + v.World.Value + ":" + p.Id), StringComparer.Ordinal);
            foreach (var pack in v.Packs)
                if (pack.Resource.Provider.Value != "nexa:world-datapacks" || !available.ContainsKey(pack.Resource.ProjectId) || pack.VersionId != world.Summary.Revision.Value)
                    throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            var order = v.EnabledOrder.Select(id =>
            {
                const string prefix = "nexa:world-datapacks/";
                if (!id.Value.StartsWith(prefix, StringComparison.Ordinal) || !available.TryGetValue(id.Value[prefix.Length..], out var pack)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                return pack.Id;
            }).ToArray();
            if (!order.Contains("vanilla", StringComparer.Ordinal)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return Plan(c, "worlds/datapacks", DataPackSelection.Write(v), "Set data-pack order for " + world.Summary.Name + ": " + string.Join(", ", order), async token =>
            {
                if ((await FindWorld(c, v.World, token).ConfigureAwait(false)).Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            }, async token =>
            {
                await Command(foundationCommands, InstanceWorldContract.SetDataPackOrder,
                    new InstanceWorldDataPackOrderCommand(world.Content.Snapshot.InstanceDirectory, world.Content.Identity.Name, order, world.Metadata.Revision), token).ConfigureAwait(false);
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Worlds", "ApplyDataPackPlan", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "worlds/datapacks", "nexa.minecraft.worlds.write", Abi100Schemas.Empty)))
    ];
}
