using System.Runtime.InteropServices;
using Nexa.Services.Capabilities;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void MacMemoryProbeMatchesMachWordCountAndLayout()
    {
        // Apple HOST_VM_INFO64_REV0_COUNT is the offset of decompressions / sizeof(integer_t).
        // Guard the actual native output envelope, not a separate test-only definition.
        AssertEqual(96, Marshal.SizeOf<MemoryCapabilityProvider.MacVmStatisticsRev0>());
        AssertEqual(24u, MemoryCapabilityProvider.MacVmStatisticsWordCount);
        AssertEqual(0, Marshal.OffsetOf<MemoryCapabilityProvider.MacVmStatisticsRev0>("FreeCount").ToInt32());
        AssertEqual(8, Marshal.OffsetOf<MemoryCapabilityProvider.MacVmStatisticsRev0>("InactiveCount").ToInt32());
        AssertEqual(16, Marshal.OffsetOf<MemoryCapabilityProvider.MacVmStatisticsRev0>("ZeroFillCount").ToInt32());
        AssertEqual(92, Marshal.OffsetOf<MemoryCapabilityProvider.MacVmStatisticsRev0>("SpeculativeCount").ToInt32());
    }

    private static async ValueTask NativeMacMemoryProbeSurvivesRepeatedBackgroundCollection()
    {
        if (!OperatingSystem.IsMacOS()) return;
        // Settings' capability broker collects off the UI thread. Exercise that exact native
        // path repeatedly, including NativeAOT: a bad count corrupts the worker's stack.
        await Task.Run(() =>
        {
            for (int attempt = 0; attempt < 128; attempt++)
            {
                var snapshot = new MachineCapabilitySnapshot(attempt, DateTimeOffset.UtcNow,
                    MemoryCapabilityProvider.CollectMacOs(DateTimeOffset.UtcNow));
                var total = snapshot.Get<long>(MachineCapabilityCatalog.PhysicalUsable.Id)!;
                var available = snapshot.Get<long>(MachineCapabilityCatalog.PhysicalAvailable.Id)!;
                AssertEqual(CapabilityAvailability.Available, total.Availability);
                AssertEqual(CapabilityAvailability.Available, available.Availability);
                AssertTrue(total.Value > 0);
                AssertTrue(available.Value >= 0 && available.Value <= total.Value);
                AssertEqual(CapabilityAvailability.PlatformUnsupported,
                    snapshot.Get<long>(MachineCapabilityCatalog.CommitLimit.Id)!.Availability);
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }
}
