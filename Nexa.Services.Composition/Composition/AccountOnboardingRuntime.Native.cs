#if NEXA_NATIVE_SERVICES
using Nexa.Services.Accounts;
using Nexa.Services.Foundation;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class AccountOnboardingRuntime : IDisposable
{
    internal AccountOnboardingRuntime(FoundationHost host, AccountOnboardingOptions options, string? dataDirectory, IXsrDispatchObserver? observer)
    {
        Initialization = host.Accounts.ConfigureRuntimeAsync(options, dataDirectory);
        (Commands, Queries) = NativeAccountRouters.Create(host.Accounts, observer);
        LaunchIdentityResolver = host.Accounts;
    }
    public Task Initialization { get; }
    public XsrCommandRouter Commands { get; }
    public XsrQueryRouter Queries { get; }
    public IAccountLaunchIdentityResolver LaunchIdentityResolver { get; }
    // The enclosing account-service lifetime drains all onboarding, skin and HTTP work.
    public void Dispose() { }
}

public static class AccountOnboardingRuntimeComposer
{
    public static AccountOnboardingRuntime ComposeWithAppearance(FoundationHost host, string? dataDirectory,
        AccountOnboardingOptions? options = null, IXsrDispatchObserver? observer = null) =>
        new(host, options ?? AccountOnboardingOptions.FromEnvironment(), dataDirectory, observer);
}
#endif
