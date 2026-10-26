using Nexa.Services.Capabilities;

namespace Nexa.Platform;

public sealed class PlatformPowerPressureProbe : IPlatformPowerPressure
{
    public async ValueTask<PlatformPowerPressure> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var facts = await new HardwarePowerCapabilityProvider().CollectAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        string? thermal = OperatingSystem.IsMacOS()
            ? await PlatformNetworkCostProbe.RunAsync(Path.Combine(AppContext.BaseDirectory, "Nexa.Platform.Helper"), ["thermal-state"], cancellationToken).ConfigureAwait(false) : null;
        return Project(facts, thermal?.Trim());
    }

    internal static PlatformPowerPressure Project(IReadOnlyList<ICapability> facts, string? thermal)
    {
        if (thermal is "serious" or "critical") return PlatformPowerPressure.Constrained;
        string? source = facts.OfType<Capability<string>>().FirstOrDefault(f => f.Id == MachineEnvironmentCatalog.PowerSource.Id
            && f.Availability == CapabilityAvailability.Available)?.Value;
        int? level = facts.OfType<Capability<int>>().FirstOrDefault(f => f.Id == MachineEnvironmentCatalog.PowerBatteryLevelPercent.Id
            && f.Availability == CapabilityAvailability.Available)?.Value;
        if (source == "电池" && level is >= 0 and <= 20) return PlatformPowerPressure.Constrained;
        if (source == "外接电源" || source == "电池" && level is > 20 and <= 100 || thermal is "normal" or "fair")
            return PlatformPowerPressure.Normal;
        return PlatformPowerPressure.Unknown;
    }
}
