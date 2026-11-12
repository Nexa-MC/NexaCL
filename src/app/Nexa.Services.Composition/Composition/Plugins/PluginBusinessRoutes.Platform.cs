using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Capabilities;
using Availability = Nexa.Services.Capabilities.CapabilityAvailability;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> PlatformRoutes => [
        HostRoutes.Typed<Empty, PowerState>("Platform", "QueryPower", async (c, _) =>
        {
            var facts = await Query<MachineCapabilityQuery, MachineCapabilitySnapshot>(foundationQueries, MachineCapabilityStateContract.SnapshotQuery, new(), c.Options.Cancellation).ConfigureAwait(false);
            var source = facts.Get<string>(MachineEnvironmentCatalog.PowerSource.Id);
            var level = facts.Get<int>(MachineEnvironmentCatalog.PowerBatteryLevelPercent.Id);
            var profile = facts.Get<string>(MachineEnvironmentCatalog.PowerProfileCurrent.Id);
            if (source?.Availability != Availability.Available || source.Value is not ("电池" or "外接电源")) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            if (profile?.Availability != Availability.Available) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            bool saving = profile.Value is "节能" or "power-saver" or "low-power";
            return new(source.Value == "电池", level is { Availability: Availability.Available, Value: >= 0 and <= 100 } ? level.Value : null, saving);
        }),
        HostRoutes.Typed<Empty, Page<DisplayInfo>>("Platform", "QueryDisplay", async (c, _) =>
        {
            var displays = await Platform.Displays(c.Options.Cancellation).ConfigureAwait(false);
            if (displays.Any(d => d.Width <= 0 || d.Height <= 0 || !double.IsFinite(d.Scale) || d.Scale <= 0)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            return ContentPage(displays.Select(d => new DisplayInfo(new(Token(c, "display:" + d.Id)), d.Width, d.Height, d.Scale)).ToImmutableArray(), new(null, 32));
        }),
        HostRoutes.Typed<Empty, PlatformDescription>("Platform", "Describe", (_, _) => ValueTask.FromResult(new PlatformDescription([
            new("nexa.platform/100", 100, Nexa.Plugin.Sdk.Abi100.Data.CapabilityAvailability.Supported, ["application"],
                [new("clipboardTextCharacters", new AbiValue.Int64(65536)), new("clipboardPngBytes", new AbiValue.Int64(16 * 1024 * 1024)),
                 new("clipboardPixels", new AbiValue.Int64(4 * 1024 * 1024)), new("displayLimit", new AbiValue.Int64(32))])
        ])))
    ];
}
