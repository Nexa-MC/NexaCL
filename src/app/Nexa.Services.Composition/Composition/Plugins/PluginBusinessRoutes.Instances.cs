using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> InstanceRoutes => [
        HostRoutes.Typed<EntityRequest, InstanceDetails>("Minecraft.Instances", "Read", (c, v) =>
        {
            var item = FindInstance(c, v.Id);
            bool running = Sessions().Items.Any(p => p.InstanceDirectory == item.Instance.DirectoryPath && p.State is MinecraftProcessState.Running or MinecraftProcessState.Created);
            return ValueTask.FromResult(new InstanceDetails(item.Summary, Application.Files.GrantPath(c.Identity, item.Instance.DirectoryPath), running, item.Instance.Metadata.Tags.Select(tag => new QualifiedId("nexa:instance-tags/" + Token(c, "tag:" + tag))).ToImmutableArray()));
        }),
        HostRoutes.Typed<EntityRequest, Empty>("Minecraft.Instances", "OpenDirectory", (c, v) =>
        { Platform.Reveal(FindInstance(c, v.Id).Instance.DirectoryPath); return ValueTask.FromResult(new Empty()); }),
        HostRoutes.Typed<RootRegistration, RootRegistration>("Minecraft.Instances", "AddRoot", async (c, v) =>
        {
            string path = Application.Files.Resolve(c.Identity, new(v.Root.Token, v.Root.LoadInstanceId, "selected-write"), true);
            if (!Directory.Exists(path) || v.DisplayName.Length > 128) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            await Confirm(c, "instances/add-root", "Add game directory " + path, RootRegistration.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Command(libraryCommands, MinecraftLibraryRoutes.Directory, new MinecraftLibraryDirectoryCommand(path, true), c.Options.Cancellation).ConfigureAwait(false);
            await Command(libraryCommands, MinecraftLibraryRoutes.Rename, new MinecraftLibraryRenameCommand(path, v.DisplayName), c.Options.Cancellation).ConfigureAwait(false);
            return new(Root(c, path), v.DisplayName);
        }),
        HostRoutes.Typed<RootRequest, Empty>("Minecraft.Instances", "RemoveRoot", async (c, v) =>
        {
            var directory = Library().Directories.FirstOrDefault(d => Root(c, d.Path) == v.Root) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            await Confirm(c, "instances/remove-root", "Forget game directory " + directory.Path, RootRequest.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Command(libraryCommands, MinecraftLibraryRoutes.Forget, new MinecraftLibraryForgetCommand(directory.Path), c.Options.Cancellation).ConfigureAwait(false); return new Empty();
        }),
        HostRoutes.Typed<InstanceRenameRequest, InstanceSummary>("Minecraft.Instances", "Rename", async (c, v) =>
        {
            var item = FindInstance(c, v.Instance);
            if (item.Summary.Revision != v.ExpectedRevision || string.IsNullOrWhiteSpace(v.Name) || v.Name.Length > 128 || v.Name.Any(char.IsControl)) throw new AbiHostException(AbiErrors.CONFLICT);
            var identity = await Query<InstanceIdentityQuery, InstanceIdentitySnapshot>(foundationQueries, InstanceIdentityContract.Query, new(item.Instance.DirectoryPath), c.Options.Cancellation).ConfigureAwait(false);
            await Command(foundationCommands, InstanceIdentityContract.Save, new InstanceIdentitySaveCommand(item.Instance.DirectoryPath, identity.Revision, identity.Fields with { DisplayName = v.Name }, identity.Server), c.Options.Cancellation).ConfigureAwait(false);
            await Command(libraryCommands, MinecraftLibraryRoutes.Refresh, new MinecraftLibraryRefreshCommand(), c.Options.Cancellation).ConfigureAwait(false);
            return FindInstance(c, v.Instance).Summary;
        })
    ];
}
