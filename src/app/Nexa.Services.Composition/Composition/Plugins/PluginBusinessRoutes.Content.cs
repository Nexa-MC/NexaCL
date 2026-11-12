using System.Collections.Immutable;
using System.Globalization;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static Page<T> ContentPage<T>(ImmutableArray<T> items, PageRequest request) where T : IAbiData<T>
    {
        if (request.Limit is < 1 or > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var revision = ProjectionRevision(new Page<T>(items, null, new("content"))); int start = 0;
        if (request.Cursor is { } cursor)
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || parts[0] != revision.Value || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out start) || start < 0 || start > items.Length)
                throw new AbiHostException(AbiErrors.CONFLICT);
        }
        var page = items.Skip(start).Take((int)request.Limit).ToImmutableArray(); int end = start + page.Length;
        return new(page, end < items.Length ? revision.Value + ":" + end.ToString(CultureInfo.InvariantCulture) : null, revision);
    }
    private async ValueTask<Page<ContentItem>> ReadContent(HostInvocation c, ContentQuery request, CancellationToken token)
    {
        var snapshot = await Management(c, request.Instance, token).ConfigureAwait(false);
        string? kind = null;
        if (request.Kind is { } selected)
        {
            const string prefix = "nexa:content/";
            if (!selected.Value.StartsWith(prefix, StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            kind = selected.Value[prefix.Length..];
            if (!snapshot.Contents.Any(p => p.PageId == kind)) throw new AbiHostException(AbiErrors.NOT_FOUND);
        }
        return ContentPage(CompleteContent(snapshot, (p, e) => Content(c, request.Instance, p, e), kind), request.Page);
    }
    private async ValueTask<IntegrityReport> ContentIntegrity(HostInvocation c, EntityId id, CancellationToken token)
    {
        var record = await FindContent(c, id, token).ConfigureAwait(false);
        if (record.Entry.IsDirectory || record.Entry.Size is null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var integrity = await Query<InstanceContentIntegrityQuery, InstanceContentIntegrity>(foundationQueries, InstanceContentIntegrityContract.Read,
            new(record.Snapshot.InstanceDirectory, record.Identity.Page, record.Identity.Name, record.Entry.Size.Value, record.Entry.ModifiedUtcTicks), token).ConfigureAwait(false);
        ImmutableArray<ValidationIssue> issues = integrity.Modified switch
        {
            true => [Issue("CONTENT_MODIFIED", "The file differs from its recorded baseline.", id.Value)],
            null => [Issue("BASELINE_UNAVAILABLE", "No complete verified baseline is available.", id.Value)],
            false => []
        };
        return new(id, integrity.Modified == false, issues, new(Token(c, "integrity:" + record.Public.Revision.Value + ":" + integrity.Sha256)));
    }
    private async ValueTask<ContentPlan> PrepareContentChange(HostInvocation c, ContentChangeRequest request, string action)
    {
        if (request.Items.Length != 1 || request.Versions.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var record = await FindContent(c, request.Items[0], c.Options.Cancellation).ConfigureAwait(false);
        if (request.Instance != record.Identity.Instance) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        if (request.ExpectedRevision != record.Public.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
        if (action is "enable" or "disable" && record.Identity.Page is not ("mods" or "resourcepacks")) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        if (action != "remove" && record.Entry.IsDirectory && record.Identity.Page == "mods") throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var plan = Plan(c, "content/execute", ContentChangeRequest.Write(request), action + " " + record.Public.Name, async token =>
        {
            if ((await FindContent(c, request.Items[0], token).ConfigureAwait(false)).Public.Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
        }, async token =>
        {
            if (action == "remove")
                await Command(foundationCommands, InstanceManagementContract.RemoveContent,
                    new InstanceContentRemoveCommand(record.Snapshot.InstanceDirectory, record.Identity.Page, record.Identity.Name, record.Entry.IsDirectory, record.Entry.Size, record.Entry.ModifiedUtcTicks), token).ConfigureAwait(false);
            else if (record.Identity.Page == "mods")
                await Command(foundationCommands, InstanceManagementContract.SetModEnabled,
                    new InstanceModEnabledCommand(record.Snapshot.InstanceDirectory, record.Identity.Name, action == "enable", record.Entry.Size ?? 0, record.Entry.ModifiedUtcTicks), token).ConfigureAwait(false);
            else
            {
                var config = record.Snapshot.ResourcePacks ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                var pack = config.Packs.FirstOrDefault(p => p.Name == record.Identity.Name) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                await Command(foundationCommands, InstanceResourcePackContract.Set, new InstanceResourcePackCommand(record.Snapshot.InstanceDirectory, config.Revision, pack.Name, action == "enable", pack.Order), token).ConfigureAwait(false);
            }
            return Empty.Write(new());
        });
        var result = new ContentPlan(plan, [record.Public with { Enabled = action == "enable" || action == "remove" && record.Public.Enabled }], []);
        lock (_planGate) _plans[plan.Id] = _plans[plan.Id] with { PreviewBody = ContentPlan.Write(result) };
        return result;
    }
    private IEnumerable<HostCapabilityRoute> ContentRoutes => [
        HostRoutes.Typed<ContentQuery, Page<ContentItem>>("Minecraft.Content", "List", (c, v) => ReadContent(c, v, c.Options.Cancellation)),
        HostRoutes.Typed<EntityRequest, ContentItem>("Minecraft.Content", "Inspect", async (c, v) => (await FindContent(c, v.Id, c.Options.Cancellation).ConfigureAwait(false)).Public),
        HostRoutes.Typed<ContentChangeRequest, ContentPlan>("Minecraft.Content", "PlanEnable", (c, v) => PrepareContentChange(c, v, "enable")),
        HostRoutes.Typed<ContentChangeRequest, ContentPlan>("Minecraft.Content", "PlanDisable", (c, v) => PrepareContentChange(c, v, "disable")),
        HostRoutes.Typed<ContentChangeRequest, ContentPlan>("Minecraft.Content", "PlanRemove", (c, v) => PrepareContentChange(c, v, "remove")),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Content", "Execute", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "content/execute", "nexa.minecraft.content.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<EntityRequest, ContentPlan>("Minecraft.Content", "Preview", (c, v) =>
        {
            lock (_planGate)
            {
                if (!_plans.TryGetValue(v.Id, out var plan) || plan.Owner != c.Identity || plan.Kind is not ("content/execute" or "content/rollback")) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (plan.Expires <= DateTimeOffset.UtcNow) throw new AbiHostException(AbiErrors.CONFLICT);
                return ValueTask.FromResult(plan.PreviewBody is { } body ? ContentPlan.Read(body) : new ContentPlan(plan.Public, [], []));
            }
        }),
        HostRoutes.Typed<EntityRequest, IntegrityReport>("Minecraft.Content", "ReadIntegrity", (c, v) => ContentIntegrity(c, v.Id, c.Options.Cancellation)),
        HostRoutes.Typed<EntityRequest, OperationRef>("Minecraft.Content", "Verify", (c, v) => ValueTask.FromResult(Services.StartHostOperation(c, "nexa.minecraft.content.write", Abi100Schemas.IntegrityReport,
            async token => AbiReply<AbiValue>.Ok(IntegrityReport.Write(await ContentIntegrity(c, v.Id, token).ConfigureAwait(false)))))),
        HostRoutes.Typed<EntityRequest, Page<ContentItem>>("Minecraft.Content", "ListTrash", async (c, v) =>
        {
            var snapshot = await Management(c, v.Id, c.Options.Cancellation, trash: true).ConfigureAwait(false);
            if (snapshot.Trash.Count > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var items = snapshot.Trash.Select(t => new ContentItem(new(Token(c, "trash:" + v.Id.Value + ":" + t.Id)), v.Id,
                new("nexa:content/" + t.PageId), t.Name, null, false, new(Token(c, "trash-revision:" + t.Id + ":" + t.RemovedAt.ToString("O"))))).ToImmutableArray();
            return ContentPage(items, new(null, 256));
        }),
        HostRoutes.Typed<ContentChangeRequest, OperationRef>("Minecraft.Content", "RestoreTrash", async (c, v) =>
        {
            if (v.Items.Length != 1 || v.Versions.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var snapshot = await Management(c, v.Instance, c.Options.Cancellation, trash: true).ConfigureAwait(false);
            var entry = snapshot.Trash.FirstOrDefault(t => new EntityId(Token(c, "trash:" + v.Instance.Value + ":" + t.Id)) == v.Items[0]) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (v.ExpectedRevision != new Revision(Token(c, "trash-revision:" + entry.Id + ":" + entry.RemovedAt.ToString("O")))) throw new AbiHostException(AbiErrors.CONFLICT);
            return Services.StartHostOperation(c, "nexa.minecraft.content.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "content/restore", "Restore " + entry.Name, ContentChangeRequest.Write(v), token).ConfigureAwait(false);
                await Command(foundationCommands, InstanceManagementContract.RestoreContent, new InstanceContentRestoreCommand(snapshot.InstanceDirectory, entry.Id), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        })
    ];
}
