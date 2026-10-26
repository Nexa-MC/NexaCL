using Nexa.Platform;
using Nexa.Services.Capabilities;
using Nexa.Services.Foundation;
using Nexa.Services.Scheduling;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask PowerPressurePausesOnlyBackgroundAdmission()
    {
        var stamp = DateTimeOffset.UtcNow;
        ICapability[] battery = [MachineEnvironmentCatalog.PowerSource.Observe("电池", stamp, "fixture"),
            MachineEnvironmentCatalog.PowerBatteryLevelPercent.Observe(15, stamp, "fixture")];
        AssertEqual(PlatformPowerPressure.Constrained, PlatformPowerPressureProbe.Project(battery, "normal"));
        AssertEqual(PlatformPowerPressure.Constrained, PlatformPowerPressureProbe.Project([], "serious"));
        AssertEqual(PlatformPowerPressure.Unknown, PlatformPowerPressureProbe.Project([], null));
        AssertEqual(PlatformPowerPressure.Normal, PlatformPowerPressureProbe.Project(
            [MachineEnvironmentCatalog.PowerSource.Observe("外接电源", stamp, "fixture")], null));
        using var work = new WorkScheduler();
        await using var pressure = new FoundationPowerPressure(new UnknownPowerPressure(), work);
        pressure.Observe(PlatformPowerPressure.Constrained);
        using (var foreground = await work.AcquireAsync(WorkPriority.Interactive, WorkResource.Disk))
            AssertTrue(foreground is not null);
        AssertTrue(work.TryAcquire(WorkPriority.Background, WorkResource.Disk) is null);
        pressure.Observe(PlatformPowerPressure.Normal); pressure.Observe(PlatformPowerPressure.Normal);
        AssertTrue(work.TryAcquire(WorkPriority.Background, WorkResource.Disk) is null);
        pressure.Observe(PlatformPowerPressure.Constrained); // Interrupt recovery.
        pressure.Observe(PlatformPowerPressure.Unknown); pressure.Observe(PlatformPowerPressure.Unknown);
        AssertTrue(work.TryAcquire(WorkPriority.Background, WorkResource.Disk) is null);
        pressure.Observe(PlatformPowerPressure.Unknown);
        using (var resumed = work.TryAcquire(WorkPriority.Background, WorkResource.Disk)) AssertTrue(resumed is not null);
        pressure.Observe(PlatformPowerPressure.Constrained);
        await pressure.DisposeAsync();
        using var released = work.TryAcquire(WorkPriority.Background, WorkResource.Disk);
        AssertTrue(released is not null);
    }

    private sealed class UnknownPowerPressure : IPlatformPowerPressure
    {
        public ValueTask<PlatformPowerPressure> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(PlatformPowerPressure.Unknown);
    }
}
