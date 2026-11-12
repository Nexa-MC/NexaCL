using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> InstallTemplateRoutes => [
        HostRoutes.Typed<InstallTemplate, RegistrationRef>("Minecraft.Install", "RegisterTemplate", (c, v) =>
        {
            if (!MinecraftInstanceReference.IsSafe(v.GameVersion) || v.Loaders.Length > 16 || v.Content.Length > 256
                || v.Loaders.Distinct().Count() != v.Loaders.Length || v.Content.Distinct().Count() != v.Content.Length
                || !v.Workflow.Value.StartsWith(c.Identity.PluginId + ":", StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            foreach (var loader in v.Loaders) _ = Loader(loader);
            foreach (var content in v.Content) CheckCatalogReference(content.Resource, content.Resource.Provider);
            return ValueTask.FromResult(Application.Runtime.Definitions.Stage(c, [new(v.Id, new("nexa:install/templates"), Abi100Schemas.InstallTemplate, InstallTemplate.Write(v), [])]).Single());
        })
    ];
    private ValueTask<MutationPlan> PrepareTemplate(HostInvocation c, InstanceCreateRequest request)
    {
        var entry = Application.Runtime.Definitions.Entries.FirstOrDefault(e => e.Definition.Id == request.Template && e.Definition.Registry.Value == "nexa:install/templates")
            ?? throw new AbiHostException(AbiErrors.NOT_FOUND);
        if (entry.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_OWNER_MISMATCH);
        var template = InstallTemplate.Read(entry.Definition.Body);
        if (request.GameVersion != template.GameVersion || !MinecraftInstanceReference.IsSafe(request.Name)
            || request.Loader is not null && !template.Loaders.Contains(request.Loader)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string root = Library().Directories.FirstOrDefault(d => Root(c, d.Path) == request.Root)?.Path ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        string target = Path.Combine(root, "versions", request.Name);
        if (Path.Exists(target)) throw new AbiHostException(AbiErrors.CONFLICT);
        var definition = Services.HostWorkflow(c, template.Workflow, Abi100Schemas.InstallTemplateInvocation, Abi100Schemas.Empty);
        var invocation = new InstallTemplateInvocation(template, request);
        return ValueTask.FromResult(Plan(c, "instances/create", InstallTemplateInvocation.Write(invocation), "Create " + request.Name + " using template " + template.Id.Value, token =>
        {
            token.ThrowIfCancellationRequested();
            if (Path.Exists(target) || !Library().Directories.Any(d => Root(c, d.Path) == request.Root)
                || !Application.Runtime.Definitions.Entries.Any(e => e.Owner == entry.Owner && e.Registration == entry.Registration && e.Revision == entry.Revision)
                || Services.HostWorkflow(c, template.Workflow, Abi100Schemas.InstallTemplateInvocation, Abi100Schemas.Empty).DefinitionDigest != definition.DefinitionDigest) throw new AbiHostException(AbiErrors.PLAN_STALE);
            return ValueTask.CompletedTask;
        }, async token =>
        {
            await Services.ExecuteHostWorkflow(c with { Options = c.Options with { Cancellation = token } }, "nexa.minecraft.instances.write",
                new(template.Workflow, InstallTemplateInvocation.Write(invocation), definition.DefinitionDigest)).ConfigureAwait(false);
            var installed = await Query<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(_installQueries, MinecraftInstallEditContract.Query, new(root, request.Name), token).ConfigureAwait(false);
            if (installed.GameVersion != template.GameVersion || template.Loaders.Any(loader => !installed.Selection.Any(item => LoaderId(item.Loader) == loader))) throw new AbiHostException(AbiErrors.CONFLICT);
            await Command(libraryCommands, MinecraftLibraryRoutes.Refresh, new MinecraftLibraryRefreshCommand(), token).ConfigureAwait(false);
            return Empty.Write(new());
        }));
    }
}
