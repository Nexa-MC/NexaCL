using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> InstanceCreateRoutes => [
        HostRoutes.Typed<InstanceCreateRequest, MutationPlan>("Minecraft.Instances", "PrepareCreate", async (c, v) =>
        {
            if (v.Template.Value != "nexa:instance/java") return await PrepareTemplate(c, v).ConfigureAwait(false);
            if ( !MinecraftInstanceReference.IsSafe(v.Name) || !MinecraftInstanceReference.IsSafe(v.GameVersion)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            string root = Library().Directories.FirstOrDefault(d => Root(c, d.Path) == v.Root)?.Path ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            string path = Path.Combine(root, "versions", v.Name);
            if (Path.Exists(path)) throw new AbiHostException(AbiErrors.CONFLICT);
            var games = await ReadInstallCatalog("", null, c.Options.Cancellation).ConfigureAwait(false);
            if (!games.Versions.Any(p => p.Id == v.GameVersion)) throw new AbiHostException(AbiErrors.NOT_FOUND);
            InstallLoader? loader = v.Loader is { } selected ? Loader(selected) : null;
            if (loader is { } addon && InstallLoaderFacts.IsAddon(addon)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            string? build = null;
            if (loader is { } target)
            {
                var builds = await ReadInstallCatalog(v.GameVersion, target, c.Options.Cancellation).ConfigureAwait(false);
                build = (builds.Versions.FirstOrDefault(b => b.Stable) ?? (builds.Versions.Count == 0 ? null : builds.Versions[0]))?.Id ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            }
            var command = new MinecraftInstallCommand(root, v.GameVersion, loader, build, InstanceName: v.Name);
            return Plan(c, "instances/create", InstanceCreateRequest.Write(v), "Create instance " + v.Name + " (" + v.GameVersion + (build is null ? "" : ", " + loader + " " + build) + ")", token =>
            {
                token.ThrowIfCancellationRequested();
                if (!Library().Directories.Any(d => Root(c, d.Path) == v.Root) || Path.Exists(path)) throw new AbiHostException(AbiErrors.CONFLICT);
                return ValueTask.CompletedTask;
            }, async token =>
            {
                await Command(_installCommands, MinecraftInstallRoutes.Run, command, token).ConfigureAwait(false);
                await Command(libraryCommands, MinecraftLibraryRoutes.Refresh, new MinecraftLibraryRefreshCommand(), token).ConfigureAwait(false);
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Instances", "CommitCreate", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "instances/create", "nexa.minecraft.instances.write", Abi100Schemas.Empty)))
    ];
}
