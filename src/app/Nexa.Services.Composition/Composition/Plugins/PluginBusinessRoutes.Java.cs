using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Settings;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IJavaRuntimeLocator? _javaLocator;
    private Func<string?, bool, CancellationToken, ValueTask<JavaRuntimeCandidate?>>? _javaSelect;
    private Func<string, CancellationToken, ValueTask<JavaRequirementResolution>>? _javaRequirements;
    public void BindJava(Nexa.Services.Foundation.FoundationHost host)
    {
        _javaLocator = host.JavaLocator;
#if NEXA_NATIVE_SERVICES
        _javaSelect = host.NativeMinecraft.SelectPluginJavaAsync;
        _javaRequirements = host.NativeMinecraft.ReadPluginJavaRequirementsAsync;
#else
        _javaRequirements = Nexa.Services.Minecraft.Launch.MinecraftPluginJavaPolicy.ReadAsync;
        _javaSelect = async (instance, versionLocal, token) =>
        {
            var selected = await new JavaSelectionService(host.JavaLocator).SelectAsync(instance is null ? JavaRequirementResolution.Valid(JavaVersionRange.Any) : await _javaRequirements(instance, token).ConfigureAwait(false),
                versionLocal ? new InstanceJavaPreference(instance!) : new AutoSelectJavaPreference(), token).ConfigureAwait(false);
            return selected.SelectedJava;
        };
#endif
    }
    private IJavaRuntimeLocator JavaLocator => _javaLocator ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    private ValueTask<JavaRuntimeInventorySnapshot> JavaInventory(CancellationToken token) => Query<JavaRuntimeInventoryQuery, JavaRuntimeInventorySnapshot>(foundationQueries, JavaRuntimeInventoryContract.Query, new(), token);
    private JavaRuntime ProjectJava(HostInvocation c, JavaRuntimeCandidate candidate, JavaRuntimeInventorySnapshot inventory)
    {
        var java = candidate.Installation;
        return new(new(Token(c, "java:" + java.JavaExecutablePath)), java.Version.ToString(), java.Architecture.ToString(), java.Brand.ToString(),
            Application.Files.GrantPath(c.Identity, java.JavaExecutablePath), inventory.ManagedRuntimes.Any(m => Nexa.Core.PathIdentity.Comparer.Equals(m.Executable, java.JavaExecutablePath)));
    }
    private async ValueTask<JavaRuntime> ResolveJava(HostInvocation c, JavaSelection request)
    {
        if (request.VersionLocal && (request.Instance is null || request.Runtime is not null)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string? instance = request.Instance is { } id ? FindInstance(c, id).Instance.DirectoryPath : null;
        var inventory = await JavaInventory(c.Options.Cancellation).ConfigureAwait(false);
        if (request.Runtime is { } runtime)
        {
            var candidate = inventory.Runtimes.FirstOrDefault(j => new EntityId(Token(c, "java:" + j.Installation.JavaExecutablePath)) == runtime && j.IsAvailable && j.IsEnabled) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (instance is not null)
            {
                var requirement = await JavaRequirementsFor(instance, c.Options.Cancellation).ConfigureAwait(false);
                if (!requirement.Range.Contains(candidate.Installation.Version)) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            return ProjectJava(c, candidate, inventory);
        }
        var select = _javaSelect ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var candidateResult = await select(instance, request.VersionLocal, c.Options.Cancellation).ConfigureAwait(false);
        if (candidateResult is null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return ProjectJava(c, candidateResult, inventory);
    }
    private async ValueTask<JavaRuntime> SelectJava(HostInvocation c, JavaSelection request, bool global)
    {
        if (global && (request.Instance is not null || request.VersionLocal) || !global && request.Instance is null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string? instance = request.Instance is { } id ? FindInstance(c, id).Instance.DirectoryPath : null;
        var before = await ReadSettings(instance, c.Options.Cancellation).ConfigureAwait(false);
        if (request.ExpectedRevision != PolicyRevision(c, before.Revision)) throw new AbiHostException(AbiErrors.CONFLICT);
        var selected = await ResolveJava(c, request).ConfigureAwait(false);
        string path = Application.Files.Resolve(c.Identity, selected.File);
        var value = request.VersionLocal ? new SettingsOverride(SettingsOverrideMode.Custom, SettingsPolicySchema.InstanceJavaRuntime) : request.Runtime is null
            ? new SettingsOverride(SettingsOverrideMode.Auto) : new SettingsOverride(SettingsOverrideMode.Custom, path);
        await Confirm(c, "java/select", "Select Java " + selected.Version + " (" + selected.Vendor + ")", JavaSelection.Write(request), c.Options.Cancellation).ConfigureAwait(false);
        await Command(foundationCommands, SettingsPolicyContract.BatchCommand, new SettingsBatchCommand([new("java.runtime", global ? SettingsLayer.Global : SettingsLayer.Instance, value, instance)], before.Revision), c.Options.Cancellation).ConfigureAwait(false);
        return selected;
    }
    private async ValueTask<JavaRequirementResolution> JavaRequirementsFor(string instance, CancellationToken token)
    {
        var read = _javaRequirements ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var result = await read(instance, token).ConfigureAwait(false);
        if (!result.Success) throw new AbiHostException(AbiErrors.CONFLICT);
        return result;
    }
    private IEnumerable<HostCapabilityRoute> JavaRoutes => [
        HostRoutes.Typed<EntityRequest, JavaRequirements>("Minecraft.Java", "ReadRequirements", async (c, v) =>
        {
            var requirement = await JavaRequirementsFor(FindInstance(c, v.Id).Instance.DirectoryPath, c.Options.Cancellation).ConfigureAwait(false);
            static int Major(Version version) => version.Major == 1 ? version.Minor : version.Major;
            return new(v.Id, Major(requirement.Range.Minimum), Major(requirement.Range.Maximum), System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        }),
        HostRoutes.Typed<RootRequest, OperationRef>("Minecraft.Java", "Discover", (c, v) =>
        {
            if (!Library().Directories.Any(d => Root(c, d.Path) == v.Root)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return ValueTask.FromResult(Services.StartHostOperation(c, "nexa.minecraft.java.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "java/discover", "Refresh the launcher Java inventory", RootRequest.Write(v), token).ConfigureAwait(false);
                await Query<JavaRuntimeInventoryQuery, JavaRuntimeInventorySnapshot>(foundationQueries, JavaRuntimeInventoryContract.Query, new(true), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            }));
        }),
        HostRoutes.Typed<PageRequest, Page<JavaRuntime>>("Minecraft.Java", "List", async (c, v) =>
        {
            var inventory = await JavaInventory(c.Options.Cancellation).ConfigureAwait(false);
            return Page(c, v, inventory.Runtimes.Where(j => j.IsAvailable).Select(j => ProjectJava(c, j, inventory)).ToImmutableArray(), "java", inventory.RegistryRevision);
        }),
        HostRoutes.Typed<FileRef, JavaRuntime>("Minecraft.Java", "Inspect", async (c, v) =>
        {
            string path = Application.Files.Resolve(c.Identity, v);
            await Confirm(c, "java/inspect", "Inspect Java executable " + path, FileRef.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            Application.Files.Resolve(c.Identity, v);
            var candidate = await JavaLocator.InspectAsync(path, c.Options.Cancellation).ConfigureAwait(false) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return ProjectJava(c, candidate, await JavaInventory(c.Options.Cancellation).ConfigureAwait(false));
        }),
        HostRoutes.Typed<JavaSelection, JavaRuntime>("Minecraft.Java", "ResolveSelection", ResolveJava),
        HostRoutes.Typed<JavaSelection, JavaRuntime>("Minecraft.Java", "SelectGlobal", (c, v) => SelectJava(c, v, true)),
        HostRoutes.Typed<JavaSelection, JavaRuntime>("Minecraft.Java", "SelectForInstance", (c, v) => SelectJava(c, v, false)),
        HostRoutes.Typed<EntityRequest, OperationRef>("Minecraft.Java", "Remove", async (c, v) =>
        {
            var inventory = await JavaInventory(c.Options.Cancellation).ConfigureAwait(false);
            var candidate = inventory.Runtimes.FirstOrDefault(j => new EntityId(Token(c, "java:" + j.Installation.JavaExecutablePath)) == v.Id) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            string path = candidate.Installation.JavaExecutablePath;
            var managed = inventory.ManagedRuntimes.FirstOrDefault(m => Nexa.Core.PathIdentity.Comparer.Equals(m.Executable, path));
            return Services.StartHostOperation(c, "nexa.minecraft.java.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "java/remove", "Remove Java " + path, EntityRequest.Write(v), token).ConfigureAwait(false);
                await Command(foundationCommands, JavaRuntimeInventoryContract.Manage, new JavaRuntimeManageCommand(path,
                    managed is null ? JavaRuntimeManagementAction.Remove : JavaRuntimeManagementAction.DeleteManaged, inventory.RegistryRevision) { ExpectedManagedIdentity = managed?.Identity }, token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        })
    ];
}
