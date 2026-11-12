using System.Collections.Immutable;
using System.Security.Cryptography;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record ContentIdentity(PluginIdentity Owner, EntityId Instance, string Page, string Name);
    private sealed record ContentRecord(ContentIdentity Identity, InstanceManagementSnapshot Snapshot, InstanceContentEntry Entry, ContentItem Public);
    private readonly Dictionary<EntityId, ContentIdentity> _contentIdentities = [];
    private ValueTask<InstanceManagementSnapshot> Management(HostInvocation c, EntityId instance, CancellationToken token, bool graph = false, bool trash = false) =>
        Query<InstanceManagementQuery, InstanceManagementSnapshot>(foundationQueries, InstanceManagementContract.Query,
            new(FindInstance(c, instance).Instance.DirectoryPath) { IncludeContentGraph = graph, IncludeTrash = trash }, token);
    private ContentItem Content(HostInvocation c, EntityId instance, string page, InstanceContentEntry entry)
    {
        var id = new EntityId(Token(c, "content:" + instance.Value + ":" + page + ":" + entry.Name));
        lock (_planGate)
        {
            if (!_contentIdentities.ContainsKey(id) && _contentIdentities.Count >= 8192) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            _contentIdentities[id] = new(c.Identity, instance, page, entry.Name);
        }
        var fingerprint = string.Join('|', entry.Size, entry.ModifiedUtcTicks, entry.Enabled, entry.World?.Revision);
        return new(id, instance, new("nexa:content/" + page), string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Name : entry.DisplayName,
            null, entry.Enabled ?? true, new(Token(c, "content-revision:" + id.Value + ":" + fingerprint)));
    }
    private static Revision ProjectionRevision<T>(T value) where T : IAbiData<T> => new(Convert.ToHexString(SHA256.HashData(AbiValueBinary.Encode(T.Write(value)))));
    private async ValueTask<ContentRecord> FindContent(HostInvocation c, EntityId id, CancellationToken token)
    {
        ContentIdentity identity;
        lock (_planGate)
            if (!_contentIdentities.TryGetValue(id, out identity!) || identity.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        var snapshot = await Management(c, identity.Instance, token).ConfigureAwait(false);
        var content = snapshot.Contents.FirstOrDefault(p => p.PageId == identity.Page);
        if (content is null || !content.Complete) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var entry = content.Entries.FirstOrDefault(e => e.Name == identity.Name) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        return new(identity, snapshot, entry, Content(c, identity.Instance, identity.Page, entry));
    }
    private static ValidationIssue Issue(string code, string text, string target, bool fatal = true) => new(code, new(text, []), target, fatal);
    private static ImmutableArray<ContentItem> CompleteContent(InstanceManagementSnapshot snapshot, Func<string, InstanceContentEntry, ContentItem> project, string? page = null)
    {
        var contents = snapshot.Contents.Where(p => page is null || p.PageId == page).ToArray();
        if (contents.Any(p => !p.Complete)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return contents.SelectMany(p => p.Entries.Select(e => project(p.PageId, e))).ToImmutableArray();
    }
}
