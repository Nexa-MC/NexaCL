using Nexa.Services.Accounts;
using Nexa.Xsr;

namespace Nexa.Testing;

internal static class LicensedAccountFixture
{
    internal static int AddVerifiedFixtureOwner(AccountService accounts, string username = "Fixture owner")
    {
        string uuid = Guid.NewGuid().ToString("N");
        var created = accounts.AddProfile(new() { Username = username, Kind = LaunchProfileKind.Microsoft, Uuid = uuid, AccessToken = "fixture-owner-access" });
        if (!created.IsSuccess) throw new InvalidOperationException("Could not create the fixture's licensed owner.");
        accounts.RecordVerifiedOwnership(uuid);
        return created.Value;
    }
    // Set up an already-admitted account through the real roster boundary. Entitlement
    // tests deliberately do not use this helper: their absence of proof must be observable.
    internal static XsrResult<int> AddFixtureProfile(AccountService accounts, LaunchProfile profile)
    {
        if (profile.Kind == LaunchProfileKind.Microsoft || accounts.HasVerifiedMinecraftOwnership)
            return accounts.AddProfile(profile);
        int owner = AddVerifiedFixtureOwner(accounts);
        XsrResult<int> result;
        bool removed;
        try
        {
            result = accounts.AddProfile(profile);
        }
        finally { removed = accounts.RemoveProfile(owner).IsSuccess; }
        if (!removed) throw new InvalidOperationException("Could not retire the fixture's licensed owner.");
        return result.IsSuccess ? XsrResult.Success(result.Value > owner ? result.Value - 1 : result.Value) : result;
    }
}
