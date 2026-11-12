using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> InstanceMutationRoutes => [
        HostRoutes.Typed<InstanceCloneRequest, MutationPlan>("Minecraft.Instances", "PrepareClone", (c, v) =>
        {
            var source = FindInstance(c, v.Source);
            if (source.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            if (!MinecraftInstanceReference.IsSafe(v.Name) || v.Name.StartsWith(".nexa-", StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            string root = Library().Directories.FirstOrDefault(d => Root(c, d.Path) == v.Destination)?.Path ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            string destination = Path.Combine(root, "versions", v.Name);
            if (Path.Exists(destination)) throw new AbiHostException(AbiErrors.CONFLICT);
            var command = new MinecraftLibraryCloneCommand(source.Snapshot.RootDirectory, source.Instance.Id, root, v.Name, source.Snapshot.Revision);
            return ValueTask.FromResult(Plan(c, "instances/clone", InstanceCloneRequest.Write(v), "Clone instance " + source.Summary.Name + " as " + v.Name, token =>
            {
                token.ThrowIfCancellationRequested();
                if (FindInstance(c, v.Source).Summary.Revision != v.ExpectedRevision || !Library().Directories.Any(d => Root(c, d.Path) == v.Destination) || Path.Exists(destination)) throw new AbiHostException(AbiErrors.CONFLICT);
                return ValueTask.CompletedTask;
            }, async token =>
            {
                await Command(libraryCommands, MinecraftLibraryRoutes.Clone, command, token).ConfigureAwait(false);
                return Empty.Write(new());
            }));
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Instances", "CommitClone", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "instances/clone", "nexa.minecraft.instances.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<InstanceDeleteRequest, MutationPlan>("Minecraft.Instances", "PrepareDelete", (c, v) =>
        {
            var item = FindInstance(c, v.Instance);
            if (item.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            var command = new MinecraftLibraryDeleteCommand(item.Snapshot.RootDirectory, item.Instance.Id) { ExpectedRevision = item.Snapshot.Revision, RetainUserData = v.RetainUserData };
            return ValueTask.FromResult(Plan(c, "instances/delete", InstanceDeleteRequest.Write(v), (v.RetainUserData ? "Move instance to the recovery directory: " : "Permanently delete instance files: ") + item.Summary.Name, token =>
            {
                token.ThrowIfCancellationRequested();
                if (FindInstance(c, v.Instance).Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
                return ValueTask.CompletedTask;
            }, async token =>
            {
                await Command(libraryCommands, MinecraftLibraryRoutes.Delete, command, token).ConfigureAwait(false);
                return Empty.Write(new());
            }));
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Instances", "CommitDelete", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "instances/delete", "nexa.minecraft.instances.write", Abi100Schemas.Empty)))
    ];
}
