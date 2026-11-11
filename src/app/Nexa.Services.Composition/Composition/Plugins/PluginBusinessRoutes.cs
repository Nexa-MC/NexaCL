using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Accounts;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Services.Composition.Plugins;

/// <summary>Explicit projections of public launcher state. Opaque IDs are scoped to one admitted load.</summary>
public sealed class PluginBusinessRoutes(XsrStateStore store, XsrCommandRouter libraryCommands, XsrCommandRouter accountCommands)
{
    private readonly byte[] _identity = RandomNumberGenerator.GetBytes(32);
    public IEnumerable<HostCapabilityRoute> Routes => [
        HostRoutes.Typed<InstanceListRequest, Page<InstanceSummary>>("Minecraft.Instances", "List", (c, v) =>
        {
            var snapshot = Library(); var root = Root(c, snapshot.RootDirectory);
            if (v.Root is not null && v.Root != root) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return ValueTask.FromResult(Page(c, v.Page, snapshot.Instances.Select(i => Instance(c, snapshot, i)).ToImmutableArray(), "instances", snapshot.Revision));
        }),
        HostRoutes.Typed<PageRequest, Page<RootRegistration>>("Minecraft.Instances", "ListRoots", (c, v) =>
        {
            var snapshot = Library(); return ValueTask.FromResult(Page(c, v,
                snapshot.Directories.Select(d => new RootRegistration(Root(c, d.Path), d.DisplayName)).ToImmutableArray(), "roots", snapshot.Revision));
        }),
        HostRoutes.Typed<EntityRequest, InstanceSummary>("Minecraft.Instances", "ReadIdentity", (c, v) => ValueTask.FromResult(FindInstance(c, v.Id).Summary)),
        HostRoutes.Typed<EntityRequest, InstanceSummary>("Minecraft.Instances", "Select", async (c, v) =>
        {
            var item = FindInstance(c, v.Id);
            var result = await libraryCommands.Dispatch(Resolve(libraryCommands, MinecraftLibraryRoutes.Select),
                new MinecraftLibrarySelectCommand(item.Snapshot.RootDirectory, item.Instance.Id), cancellationToken: c.Options.Cancellation).Completion.ConfigureAwait(false);
            Require(result); return FindInstance(c, v.Id).Summary;
        }),
        HostRoutes.Typed<PageRequest, Page<AccountPublic>>("Accounts", "List", (c, v) =>
        {
            var roster = Accounts(); return ValueTask.FromResult(Page(c, v, roster.Items.Select(p => Account(c, p, roster.Revision)).ToImmutableArray(), "accounts", roster.Revision));
        }),
        HostRoutes.Typed<EntityRequest, AccountPublic>("Accounts", "ReadPublic", (c, v) => ValueTask.FromResult(FindAccount(c, v.Id).Public)),
        HostRoutes.Typed<EntityRequest, AccountPublic>("Accounts", "Select", async (c, v) =>
        {
            var account = FindAccount(c, v.Id);
            var result = await accountCommands.Dispatch(Resolve(accountCommands, FoundationRouteIds.AccountSelectProfile),
                new AccountSelectProfileCommand(account.Profile.Index, account.Revision), cancellationToken: c.Options.Cancellation).Completion.ConfigureAwait(false);
            Require(result); return account.Public;
        }),
        HostRoutes.Typed<Empty, ArchitectureInfo>("Platform", "QueryArchitecture", (_, _) => ValueTask.FromResult(new ArchitectureInfo(
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant())))
    ];
    private MinecraftLibrarySnapshot Library()
    {
        var state = store.Read<MinecraftLibrarySnapshot>(store.Resolve(MinecraftLibraryContract.StateKey));
        if (!state.IsAvailable || state.Value is null || state.Value.IsLoading || state.Value.IsProvisional) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return state.Value;
    }
    private XsrCollectionSnapshot<LaunchProfileView> Accounts()
    {
        var state = store.ReadCollection<LaunchProfileView>(store.Resolve(AccountStateContract.ProfilesKey));
        if (state.Availability != XsrStateAvailability.Available) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return state;
    }
    private string Token(HostInvocation c, string value) => Convert.ToHexString(HMACSHA256.HashData(_identity,
        Encoding.UTF8.GetBytes(c.Identity.LoadInstanceId.ToString("N") + ":" + value))).ToLowerInvariant();
    private RootRef Root(HostInvocation c, string path) => new(new Guid(Convert.FromHexString(Token(c, "root:" + path)[..32])), c.Identity.LoadInstanceId);
    private InstanceSummary Instance(HostInvocation c, MinecraftLibrarySnapshot snapshot, MinecraftInstanceDescriptor item) => new(
        new(Token(c, "instance:" + snapshot.RootDirectory + ":" + item.Id)),
        string.IsNullOrWhiteSpace(item.Metadata.DisplayName) ? item.Id : item.Metadata.DisplayName,
        item.VersionId, null, Root(c, snapshot.RootDirectory), new(Token(c, "library:" + snapshot.Revision.ToString(CultureInfo.InvariantCulture))));
    private (MinecraftLibrarySnapshot Snapshot, MinecraftInstanceDescriptor Instance, InstanceSummary Summary) FindInstance(HostInvocation c, EntityId id)
    {
        var snapshot = Library();
        foreach (var item in snapshot.Instances) { var summary = Instance(c, snapshot, item); if (summary.Id == id) return (snapshot, item, summary); }
        throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private AccountPublic Account(HostInvocation c, LaunchProfileView p, long revision) => new(
        new(Token(c, "account:" + revision.ToString(CultureInfo.InvariantCulture) + ":" + p.Index.ToString(CultureInfo.InvariantCulture))),
        new("nexa:accounts/" + p.Kind.ToString().ToLowerInvariant()), p.Username, null,
        new(Token(c, "roster:" + revision.ToString(CultureInfo.InvariantCulture))));
    private (LaunchProfileView Profile, AccountPublic Public, long Revision) FindAccount(HostInvocation c, EntityId id)
    {
        var roster = Accounts();
        foreach (var item in roster.Items) { var account = Account(c, item, roster.Revision); if (account.Id == id) return (item, account, roster.Revision); }
        throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private Page<T> Page<T>(HostInvocation c, PageRequest request, ImmutableArray<T> items, string kind, long revision) where T : IAbiData<T>
    {
        if (request.Limit is < 1 or > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string stamp = Token(c, kind + ":" + revision.ToString(CultureInfo.InvariantCulture)); int start = 0;
        if (request.Cursor is { } cursor)
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || parts[0] != stamp || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out start) || start < 0 || start > items.Length)
                throw new AbiHostException(AbiErrors.CONFLICT);
        }
        var page = items.Skip(start).Take((int)request.Limit).ToImmutableArray(); int end = start + page.Length;
        return new(page, end < items.Length ? stamp + ":" + end.ToString(CultureInfo.InvariantCulture) : null, new(stamp));
    }
    private static XsrCommandId Resolve(XsrCommandRouter router, XsrSemanticId name) => router.TryResolve(name, out var id) ? id : throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    private static void Require(XsrResult result) { if (!result.IsSuccess) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE); }
}
