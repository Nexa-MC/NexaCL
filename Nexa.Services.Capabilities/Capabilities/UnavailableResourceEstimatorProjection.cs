namespace Nexa.Services.Capabilities;

/// <summary>Product suspension: publishes absence without executing the model or history calibration.</summary>
internal sealed class UnavailableResourceEstimatorProjection : ICapabilityProjection
{
    public IReadOnlyList<ICapability> Project(IReadOnlyDictionary<string, ICapability> values, DateTimeOffset timestamp) =>
        ResourceEstimateCatalog.Definitions().Select(definition =>
            definition.Unavailable(CapabilityAvailability.TemporarilyUnavailable, timestamp, "暂不可用")).ToArray();
}
