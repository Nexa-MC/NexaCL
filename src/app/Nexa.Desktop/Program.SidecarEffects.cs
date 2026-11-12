using Nexa.Services.Accounts;
using Nexa.Services.Composition.Sidecars;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static SidecarHostEffects CreateSidecarHostEffects(FoundationHost host, IAccountLaunchIdentityResolver resolver,
        XsrQueryRouter queries, IXsrDispatchObserver observer)
    {
#if NEXA_NATIVE_SERVICES
        return new(host.Accounts, resolver, host.NativeMinecraft.InspectJavaAsync, host.NativeMinecraft.SelectPluginJavaAsync,
            host.NativeMinecraft.ReadPluginJavaRequirementsAsync, queries, observer);
#else
        async ValueTask<JavaRuntimeCandidate?> Select(string? instance, bool versionLocal, CancellationToken token)
        {
            var requirement = instance is null ? JavaRequirementResolution.Valid(JavaVersionRange.Any)
                : await Nexa.Services.Minecraft.Launch.MinecraftPluginJavaPolicy.ReadAsync(instance, token).ConfigureAwait(false);
            var result = await new JavaSelectionService(host.JavaLocator).SelectAsync(requirement,
                versionLocal ? new InstanceJavaPreference(instance!) : new AutoSelectJavaPreference(), token).ConfigureAwait(false);
            return result.SelectedJava;
        }
        return new(host.Accounts, resolver, host.JavaLocator.InspectAsync, Select,
            Nexa.Services.Minecraft.Launch.MinecraftPluginJavaPolicy.ReadAsync, queries, observer);
#endif
    }
}
