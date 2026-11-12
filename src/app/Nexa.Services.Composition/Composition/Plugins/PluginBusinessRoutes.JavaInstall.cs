using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Java;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record JavaOffer(PluginIdentity Owner, ManagedJavaOffer Public, JavaManualPreview Preview, long RegistryRevision);
    private readonly Dictionary<EntityId, JavaOffer> _javaOffers = [];
    private readonly SemaphoreSlim _javaOfferGate = new(1, 1);
    private async ValueTask<Page<ManagedJavaOffer>> ManagedJavaOffers(HostInvocation c, PageRequest request)
    {
        await _javaOfferGate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
        try
        {
            var inventory = await JavaInventory(c.Options.Cancellation).ConfigureAwait(false);
            JavaOffer[] offers;
            lock (_planGate)
            {
                foreach (var key in _javaOffers.Where(p => p.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow || p.Value.RegistryRevision != inventory.RegistryRevision).Select(p => p.Key).ToArray()) _javaOffers.Remove(key);
                offers = _javaOffers.Values.Where(o => o.Owner == c.Identity && o.RegistryRevision == inventory.RegistryRevision).ToArray();
            }
            if (offers.Length == 0)
            {
                if (request.Cursor is not null) throw new AbiHostException(AbiErrors.STALE_REVISION);
                if (foundationQueries is null || !foundationQueries.TryResolve(JavaManualDownloadContract.Preview, out var route)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                var found = new List<JavaOffer>();
                foreach (int major in new[] { 8, 17, 21, 25 })
                {
                    var result = await foundationQueries.QueryAsync<JavaManualPreviewQuery, JavaManualPreview>(route, new(major, inventory.RegistryRevision), cancellationToken: c.Options.Cancellation).ConfigureAwait(false);
                    if (!result.IsSuccess) { c.Options.Cancellation.ThrowIfCancellationRequested(); continue; }
                    var preview = result.Value!;
                    var value = new ManagedJavaOffer(new(Token(c, "java-offer:" + preview.Id)), new("nexa:mojang"), preview.Version, preview.Platform, preview.Files, preview.Bytes,
                        preview.LicenseUrls.Select(url => new UriRef(url)).ToImmutableArray(), preview.ExpiresAt, new(Token(c, "java-offer:" + preview.PlanFingerprint + ":" + inventory.RegistryRevision)));
                    found.Add(new(c.Identity, value, preview, inventory.RegistryRevision));
                }
                if (found.Count == 0) throw new AbiHostException(AbiErrors.PROVIDER_UNAVAILABLE);
                lock (_planGate)
                {
                    if (_javaOffers.Count + found.Count > 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                    foreach (var offer in found) _javaOffers.Add(offer.Public.Id, offer);
                }
                offers = found.ToArray();
            }
            return Page(c, request, offers.OrderBy(o => o.Preview.Major).Select(o => o.Public).ToImmutableArray(), "java-offers", inventory.RegistryRevision);
        }
        finally { _javaOfferGate.Release(); }
    }
    private IEnumerable<HostCapabilityRoute> JavaInstallRoutes => [
        HostRoutes.Typed<PageRequest, Page<ManagedJavaOffer>>("Minecraft.Java", "ListManagedDistributions", ManagedJavaOffers),
        HostRoutes.Typed<ManagedJavaInstallRequest, MutationPlan>("Minecraft.Java", "PrepareManagedInstall", (c, v) =>
        {
            JavaOffer offer;
            lock (_planGate)
                if (!_javaOffers.TryGetValue(v.Offer, out offer!) || offer.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (offer.Public.Revision != v.ExpectedRevision || offer.Preview.ExpiresAt <= DateTimeOffset.UtcNow) throw new AbiHostException(AbiErrors.STALE_REVISION);
            return ValueTask.FromResult(Plan(c, "java/install", ManagedJavaInstallRequest.Write(v),
                "Install Java " + offer.Public.Version + " (" + offer.Public.Platform + ", " + offer.Public.Bytes + " bytes). Accept licenses: " + string.Join(", ", offer.Preview.LicenseUrls),
                async token =>
                {
                    if (offer.Preview.ExpiresAt <= DateTimeOffset.UtcNow || (await JavaInventory(token).ConfigureAwait(false)).RegistryRevision != offer.RegistryRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
                },
                async token =>
                {
                    await Command(foundationCommands, JavaManualDownloadContract.Install, new JavaManualInstallCommand(offer.Preview.Id, true), token).ConfigureAwait(false);
                    var receipt = await Query<JavaManualStatusQuery, JavaManualReceipt>(foundationQueries, JavaManualDownloadContract.Status, new(offer.Preview.Id), token).ConfigureAwait(false);
                    if (receipt.Status != JavaManualInstallStatus.Installed) throw new AbiHostException(AbiErrors.CONFLICT);
                    var inventory = await JavaInventory(token).ConfigureAwait(false);
                    var runtime = inventory.Runtimes.FirstOrDefault(r => r.IsAvailable && Nexa.Core.PathIdentity.Comparer.Equals(r.Installation.JavaExecutablePath, receipt.Executable)) ?? throw new AbiHostException(AbiErrors.CONFLICT);
                    return JavaRuntime.Write(ProjectJava(c, runtime, inventory));
                }));
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Java", "Install", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "java/install", "nexa.minecraft.java.write", Abi100Schemas.JavaRuntime)))
    ];
}
