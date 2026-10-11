using Nexa.Services.Capabilities;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void UnavailableMemoryAnalysisRetainsOtherPreflightChecks()
    {
        var now = DateTimeOffset.UtcNow;
        var projection = new UnavailableResourceEstimatorProjection();
        var absent = projection.Project(new Dictionary<string, ICapability>(), now);
        AssertEqual(ResourceEstimateCatalog.Definitions().Count, absent.Count);
        AssertTrue(absent.All(value => value.Availability == CapabilityAvailability.TemporarilyUnavailable));
        AssertEqual(PreflightSeverity.None, CapabilityPreflightEngine.Evaluate(new(1, now, absent)).OverallSeverity);
        // Even an old prediction carried alongside the suspension marker must not cause a prompt.
        var stale = absent.Where(value => value.Id is not ("estimate.heap.launch" or "estimate.heap.runtime" or "estimate.physical.launch"))
            .Concat(new ICapability[]
            {
                ResourceEstimateCatalog.HeapLaunch.Observe(8192, now, "old"),
                ResourceEstimateCatalog.HeapRuntime.Observe(8192, now, "old"),
                ResourceEstimateCatalog.PhysicalLaunch.Observe(8192, now, "old"),
                LaunchPolicyCatalog.MinecraftMemory.Observe(1024, now, "fixture"),
                MachineCapabilityCatalog.PhysicalAvailable.Observe(512L * 1024 * 1024, now, "fixture"),
                MachineDerivedRules.JavaDerivedMissing.Observe(true, now, "fixture")
            });
        var report = CapabilityPreflightEngine.Evaluate(new(2, now, stale));
        AssertTrue(report.Issues.Any(issue => issue.Code == "JAVA_MISSING" && issue.Severity == PreflightSeverity.Blocked));
        AssertFalse(report.Issues.Any(issue => issue.Category is "memory" or "estimator"));
        var preflightFacts = new PreflightProjection().Project(stale.ToDictionary(value => value.Id), now);
        AssertTrue(preflightFacts.Where(value => value.Id.StartsWith("preflight.memory.", StringComparison.Ordinal)
            || value.Id.StartsWith("preflight.estimator.", StringComparison.Ordinal))
            .All(value => value.Availability == CapabilityAvailability.TemporarilyUnavailable));
        AssertTrue(preflightFacts.Single(value => value.Id == "preflight.java.missing") is Capability<bool> { Value: true, Availability: CapabilityAvailability.Available });
        var row = SettingsCatalog.Read(new()).Entries.Single(entry => entry.Id == "global.game.preflight-memory-analysis");
        AssertEqual(SettingsCapabilityAvailability.TemporarilyUnavailable, row.Availability);
        AssertTrue(row.SettingKey is null);
    }
}
