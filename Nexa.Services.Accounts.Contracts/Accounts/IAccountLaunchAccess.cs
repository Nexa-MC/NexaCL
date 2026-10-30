using Nexa.Xsr;

namespace Nexa.Services.Accounts;

/// <summary>
/// Privileged account access for game launch. Implementations own credential reads
/// and default identity resolution; consumers must never publish profiles to UI state.
/// Explicitly composed provider-refresh resolvers may override the default resolver.
/// </summary>
public interface IAccountLaunchAccess : IAccountLaunchIdentityResolver
{
    XsrResult<LaunchProfile> GetProfile(int index);
}
