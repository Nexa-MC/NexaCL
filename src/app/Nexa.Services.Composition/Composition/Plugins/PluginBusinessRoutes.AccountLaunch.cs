using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Accounts;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IAccountLaunchAccess? _accountLaunchAccess;
    private IAccountLaunchIdentityResolver? _accountResolver;
    public void BindAccountLaunch(IAccountLaunchAccess access, IAccountLaunchIdentityResolver? resolver)
    { _accountLaunchAccess = access; _accountResolver = resolver ?? access; }
    private sealed record GrantedLaunch(PluginIdentity Owner, LaunchGrant Public, EntityId Instance, long RosterRevision, int AccountIndex, DateTimeOffset Expires);
    private readonly Dictionary<Guid, GrantedLaunch> _launchGrants = [];
    private async ValueTask<LaunchEligibility> LaunchEligibility(HostInvocation c, EntityId id, CancellationToken token)
    {
        var account = FindAccount(c, id);
        var access = _accountLaunchAccess ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var result = await access.GetProfileAsync(account.Profile.Index, account.Revision, token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new AbiHostException(ErrorCode(result.Error));
        var p = result.Value!;
        bool allowed = p.Kind switch
        {
            LaunchProfileKind.Offline => !string.IsNullOrWhiteSpace(p.Username),
            LaunchProfileKind.Microsoft => !string.IsNullOrWhiteSpace(p.Uuid) && (!string.IsNullOrWhiteSpace(p.AccessToken) || !string.IsNullOrWhiteSpace(p.RefreshToken)),
            LaunchProfileKind.LittleSkin => !string.IsNullOrWhiteSpace(p.Uuid) && (!string.IsNullOrWhiteSpace(p.AccessToken) || !string.IsNullOrWhiteSpace(p.ProviderAccessToken) || !string.IsNullOrWhiteSpace(p.RefreshToken)),
            LaunchProfileKind.ThirdParty or LaunchProfileKind.Nide8 => !string.IsNullOrWhiteSpace(p.Uuid) && !string.IsNullOrWhiteSpace(p.AuthServer) && !string.IsNullOrWhiteSpace(p.AccessToken),
            _ => false
        };
        ImmutableArray<ValidationIssue> reasons = !allowed ? [Issue("SIGN_IN_REQUIRED", "This account needs a supported sign-in before launch.", id.Value)]
            : p.Kind == LaunchProfileKind.Offline ? [] : [Issue("SESSION_CHECK_AT_LAUNCH", "The account provider will validate or refresh this session during launch.", id.Value, false)];
        return new(id, allowed, reasons);
    }
    private GrantedLaunch FindGrant(HostInvocation c, LaunchGrant reference, EntityId instance)
    {
        lock (_planGate)
        {
            if (!_launchGrants.TryGetValue(reference.Token, out var grant) || grant.Owner != c.Identity || grant.Public != reference || grant.Instance != instance)
                throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (grant.Expires <= DateTimeOffset.UtcNow || FindAccount(c, reference.Account).Revision != grant.RosterRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            FindInstance(c, instance); return grant;
        }
    }
    private IEnumerable<HostCapabilityRoute> AccountLaunchRoutes => [
        HostRoutes.Typed<EntityRequest, LaunchEligibility>("Accounts", "ReadLaunchEligibility", (c, v) => LaunchEligibility(c, v.Id, c.Options.Cancellation)),
        HostRoutes.Typed<GrantRequest, LaunchGrant>("Accounts", "RequestLaunchGrant", async (c, v) =>
        {
            FindInstance(c, v.Instance);
            if (!(await LaunchEligibility(c, v.Account, c.Options.Cancellation).ConfigureAwait(false)).Allowed) throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
            var account = FindAccount(c, v.Account);
            lock (_planGate)
            {
                foreach (var key in _launchGrants.Where(p => p.Value.Expires <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _launchGrants.Remove(key);
                if (_launchGrants.Count >= 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var reference = new LaunchGrant(Guid.NewGuid(), c.Identity.LoadInstanceId, v.Account);
                _launchGrants.Add(reference.Token, new(c.Identity, reference, v.Instance, account.Revision, account.Profile.Index, DateTimeOffset.UtcNow.AddMinutes(10)));
                return reference;
            }
        }),
        HostRoutes.Typed<EntityRequest, OperationRef>("Accounts", "Refresh", async (c, v) =>
        {
            var account = FindAccount(c, v.Id);
            var access = _accountLaunchAccess ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var resolver = _accountResolver ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var captured = await access.GetProfileAsync(account.Profile.Index, account.Revision, c.Options.Cancellation).ConfigureAwait(false);
            if (!captured.IsSuccess) throw new AbiHostException(ErrorCode(captured.Error));
            return Services.StartHostOperation(c, "nexa.accounts.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "accounts/refresh", "Refresh sign-in for " + account.Profile.Username, EntityRequest.Write(v), token).ConfigureAwait(false);
                FindAccount(c, v.Id);
                var result = await resolver.ResolveAsync(account.Profile.Index, captured.Value!, token).ConfigureAwait(false);
                if (!result.IsSuccess) throw new AbiHostException(ErrorCode(result.Error));
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        })
    ];
}
