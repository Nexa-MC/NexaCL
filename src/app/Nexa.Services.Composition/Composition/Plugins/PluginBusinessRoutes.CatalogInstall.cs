using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Resources;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private ContentPlan ContentPreview(MutationPlan plan, ImmutableArray<ContentItem> changes, ImmutableArray<ValidationIssue> issues)
    {
        var result = new ContentPlan(plan, changes, issues);
        lock (_planGate) _plans[plan.Id] = _plans[plan.Id] with { PreviewBody = ContentPlan.Write(result) };
        return result;
    }
    private async ValueTask<ContentPlan> PrepareCatalogInstall(HostInvocation c, CatalogInstallRequest request, string kind)
    {
        if (request.Versions.Length != 1) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var instance = FindInstance(c, request.Instance);
        if (request.ExpectedRevision != instance.Summary.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
        var selected = request.Versions[0];
        var detail = await CatalogDetail(selected.Resource, c.Options.Cancellation).ConfigureAwait(false);
        if (detail.Project.Kind != ResourceKind.Mod) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        await CatalogVersion(selected, c.Options.Cancellation).ConfigureAwait(false);
        var command = new ResourceModInstallCommand(CatalogReference(selected.Resource), selected.VersionId, new(instance.Snapshot.RootDirectory, instance.Instance.Id));
        var preview = await Query<ResourceModPlanQuery, ResourceInstallPlan>(_catalogQueries, ResourceCatalogContract.PlanMod, new(command), c.Options.Cancellation).ConfigureAwait(false);
        if (preview.Versions.Count > 64) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        string fingerprint = ResourceInstallPlanIdentity.Fingerprint(preview.Versions);
        command = command with { ExpectedPlanFingerprint = fingerprint };
        async ValueTask Validate(CancellationToken token)
        {
            if (FindInstance(c, request.Instance).Summary.Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            var current = await Query<ResourceModPlanQuery, ResourceInstallPlan>(_catalogQueries, ResourceCatalogContract.PlanMod, new(command), token).ConfigureAwait(false);
            if (ResourceInstallPlanIdentity.Fingerprint(current.Versions) != fingerprint) throw new AbiHostException(AbiErrors.CONFLICT);
        }
        var plan = Plan(c, kind, CatalogInstallRequest.Write(request), "Install " + detail.Project.DisplayName + " and " + Math.Max(0, preview.Versions.Count - 1) + " dependencies", Validate, async token =>
        {
            await Command(_catalogCommands, ResourceCatalogContract.InstallMod, command, token).ConfigureAwait(false);
            return Empty.Write(new());
        });
        return ContentPreview(plan, preview.Versions.Select(version => new ContentItem(new(Token(c, "planned-content:" + plan.Id.Value + ":" + version.ProjectId)), request.Instance,
            new("nexa:content/mods"), version.File?.Name ?? version.Name, new(new(CatalogProviderId(version.Provider), version.ProjectId), version.Id), true, plan.Revision)).ToImmutableArray(),
            preview.HasCycles ? [Issue("DEPENDENCY_CYCLE", "The dependency graph contains a cycle resolved by the resource owner.", request.Instance.Value, false)] : []);
    }
    private async ValueTask<ContentPlan> PrepareContentUpdate(HostInvocation c, ContentChangeRequest request, string kind)
    {
        if (request.Items.Length != 1 || request.Versions.Length != 1) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var record = await FindContent(c, request.Items[0], c.Options.Cancellation).ConfigureAwait(false);
        if (record.Identity.Instance != request.Instance || record.Entry.IsDirectory || record.Entry.Size is null) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        if (record.Public.Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
        var version = request.Versions[0]; var source = CatalogReference(version.Resource);
        var file = new ResourceContentOnlineQuery(record.Snapshot.InstanceDirectory, record.Identity.Page, record.Identity.Name, record.Entry.Size.Value, record.Entry.ModifiedUtcTicks) { WaitForRefresh = true };
        var online = await Query<ResourceContentOnlineQuery, ResourceContentOnline>(_catalogQueries, ResourceCatalogContract.ContentOnline, file, c.Options.Cancellation).ConfigureAwait(false);
        if (online.IsStale || online.Project?.Sources.Contains(source) != true || !online.Versions.Any(v => v.Id == version.VersionId && v.Provider == source.Provider)) throw new AbiHostException(AbiErrors.CONFLICT);
        var plan = Plan(c, kind, ContentChangeRequest.Write(request), "Update " + record.Public.Name + " to " + version.VersionId, async token =>
        {
            if ((await FindContent(c, record.Public.Id, token).ConfigureAwait(false)).Public.Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
        }, async token =>
        {
            await Command(_catalogCommands, ResourceCatalogContract.UpdateContent, new ResourceContentUpdateCommand(file, source, version.VersionId), token).ConfigureAwait(false);
            return Empty.Write(new());
        });
        return ContentPreview(plan, [record.Public with { Version = version }], []);
    }
    private async ValueTask<ContentPlan> PrepareCatalogUpdate(HostInvocation c, CatalogInstallRequest request)
    {
        if (request.Versions.Length != 1) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        if (FindInstance(c, request.Instance).Summary.Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
        var snapshot = await Management(c, request.Instance, c.Options.Cancellation).ConfigureAwait(false);
        if (snapshot.Contents.Any(p => !p.Complete)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var candidates = snapshot.Contents.SelectMany(p => p.Entries.Select(e => (p.PageId, e)));
        // Search matching installed files through the canonical hash/catalog owner, never filenames alone.
        var files = candidates.Where(p => p.Item1 is "mods" or "resourcepacks" or "shaderpacks" && !p.Item2.IsDirectory && p.Item2.Size is not null).ToArray();
        if (files.Length > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        var matches = await Query<ResourceContentOnlineBatchQuery, ResourceContentOnlineBatch>(_catalogQueries, ResourceCatalogContract.ContentOnlineBatch,
            new(files.Select(p => new ResourceContentOnlineQuery(snapshot.InstanceDirectory, p.Item1, p.Item2.Name, p.Item2.Size!.Value, p.Item2.ModifiedUtcTicks)).ToArray()) { WaitForRefresh = true }, c.Options.Cancellation).ConfigureAwait(false);
        var source = CatalogReference(request.Versions[0].Resource);
        var found = matches.Matches.Where(m => !m.Content.IsStale && m.Content.Project?.Sources.Contains(source) == true).ToArray();
        if (found.Length != 1) throw new AbiHostException(AbiErrors.CONFLICT);
        var match = found[0].File;
        var entry = files.Single(p => p.Item1 == match.PageId && p.Item2.Name == match.Name);
        var item = Content(c, request.Instance, entry.Item1, entry.Item2);
        return await PrepareContentUpdate(c, new(request.Instance, [item.Id], request.Versions, item.Revision), "catalog/execute").ConfigureAwait(false);
    }
    private IEnumerable<HostCapabilityRoute> CatalogInstallRoutes => [
        HostRoutes.Typed<CatalogInstallRequest, ContentPlan>("Catalog", "PlanInstall", (c, v) => PrepareCatalogInstall(c, v, "catalog/execute")),
        HostRoutes.Typed<CatalogInstallRequest, ContentPlan>("Catalog", "PlanUpdate", PrepareCatalogUpdate),
        HostRoutes.Typed<PlanRequest, OperationRef>("Catalog", "ExecutePlan", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "catalog/execute", "nexa.catalog.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<ContentChangeRequest, ContentPlan>("Minecraft.Content", "PlanInstall", (c, v) =>
        {
            if (v.Items.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return PrepareCatalogInstall(c, new(v.Instance, v.Versions, v.ExpectedRevision), "content/execute");
        }),
        HostRoutes.Typed<ContentChangeRequest, ContentPlan>("Minecraft.Content", "PlanUpdate", (c, v) => PrepareContentUpdate(c, v, "content/execute"))
    ];
}
