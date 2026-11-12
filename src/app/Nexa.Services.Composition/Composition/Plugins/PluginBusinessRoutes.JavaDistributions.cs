using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Java;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static readonly QualifiedId DistributionProviderKind = new("nexa:java/distributions");
    private static readonly BusinessProviderMethod DistributionProviderMethod = new("ListDistributions", Abi100Schemas.PageRequest, Abi100Schemas.PageOfJavaDistribution);
    private static int CheckDistribution(JavaDistribution distribution)
    {
        var version = distribution.Version.Split('.', '-', '+');
        if (!int.TryParse(version[0], out int major) || major == 1 && (version.Length < 2 || !int.TryParse(version[1], out major))
            || major is < 8 or > 99 || distribution.Download.Size is not (> 0 and <= 536870912) || distribution.Download.Sha256 is not { Length: 64 } hash
            || !hash.All(char.IsAsciiHexDigit) || distribution.Download.Sources.Length is < 1 or > 8) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        if (!distribution.Architecture.Equals(RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase) || !distribution.OperatingSystem.Equals(os, StringComparison.OrdinalIgnoreCase)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        foreach (var source in distribution.Download.Sources)
            if (source.Credential is not null || !Uri.TryCreate(source.Uri.Value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return major;
    }
    private IEnumerable<HostCapabilityRoute> JavaDistributionRoutes => [
        HostRoutes.Typed<ProviderDescriptor, RegistrationRef>("Minecraft.Java", "RegisterDistributionProvider", (c, v) => ValueTask.FromResult(Services.RegisterHostProvider(c, DistributionProviderKind, v, [DistributionProviderMethod]))),
        HostRoutes.Typed<PageRequest, Page<JavaDistribution>>("Minecraft.Java", "ListDistributions", async (c, v) =>
        {
            var providers = Services.HostProviders(DistributionProviderKind);
            if (providers.Length > 8) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var result = ImmutableArray.CreateBuilder<JavaDistribution>();
            foreach (var provider in providers)
            {
                var page = Nexa.Plugin.Sdk.Abi100.Data.Page<JavaDistribution>.Read(await Services.QueryHostProvider(c, DistributionProviderKind, provider.Id, DistributionProviderMethod, PageRequest.Write(new(null, 128))).ConfigureAwait(false));
                if (page.NextCursor is not null || page.Items.Length > 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                foreach (var item in page.Items)
                {
                    if (item.Provider != provider.Id) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                    // The provider may publish distributions for several host platforms.
                    if (!item.Architecture.Equals(RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase)
                        || !item.OperatingSystem.Equals(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux", StringComparison.OrdinalIgnoreCase)) continue;
                    CheckDistribution(item); result.Add(item);
                }
            }
            return ContentPage(result.ToImmutable(), v);
        }),
        HostRoutes.Typed<JavaInstallRequest, MutationPlan>("Minecraft.Java", "PrepareInstall", async (c, v) =>
        {
            int major = CheckDistribution(v.Distribution);
            if (!Services.HostProviders(DistributionProviderKind).Any(p => p.Id == v.Distribution.Provider)) throw new AbiHostException(AbiErrors.PROVIDER_UNAVAILABLE);
            string root = Library().Directories.FirstOrDefault(d => Root(c, d.Path) == v.Target)?.Path ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            string destination = Path.Combine(root, "runtime", "plugin-" + v.Distribution.Download.Sha256!.ToLowerInvariant());
            if (Path.Exists(destination)) throw new AbiHostException(AbiErrors.CONFLICT);
            var inventory = await JavaInventory(c.Options.Cancellation).ConfigureAwait(false);
            return Plan(c, "java/install", JavaInstallRequest.Write(v), "Download, verify and execute Java " + v.Distribution.Version + " from " + v.Distribution.Provider.Value
                + " for runtime probing; install into " + destination + "; SHA-256 " + v.Distribution.Download.Sha256, async token =>
            {
                if (!Library().Directories.Any(d => Root(c, d.Path) == v.Target) || Path.Exists(destination)
                    || (await JavaInventory(token).ConfigureAwait(false)).RegistryRevision != inventory.RegistryRevision) throw new AbiHostException(AbiErrors.PLAN_STALE);
            }, async token =>
            {
                string temporary = Path.Combine(Path.GetTempPath(), "nexa-plugin-java-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
                try
                {
                    string archive = Path.Combine(temporary, "runtime.zip");
                    var file = Application.Files.GrantPath(c.Identity, archive, true);
                    var caller = c with { Options = c.Options with { Cancellation = token } };
                    var download = await ResolveDownload(caller, v.Distribution.Download with { Target = file }).ConfigureAwait(false);
                    await RunDownload(caller, new(c.Identity, download, archive), token, false).ConfigureAwait(false);
                    await Command(foundationCommands, JavaRuntimeInventoryContract.InstallArchive, new JavaArchiveInstallCommand(archive, destination,
                        download.Size!.Value, download.Sha256!, major, v.Distribution.Architecture, inventory.RegistryRevision), token).ConfigureAwait(false);
                    var updated = await JavaInventory(token).ConfigureAwait(false);
                    var installed = updated.Runtimes.FirstOrDefault(j => j.IsAvailable && j.Installation.JavaExecutablePath.StartsWith(destination + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) ?? throw new AbiHostException(AbiErrors.CONFLICT);
                    return JavaRuntime.Write(ProjectJava(c, installed, updated));
                }
                finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            });
        })
    ];
}
