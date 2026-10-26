namespace Nexa.Services.Caching;

public enum StateCacheMemoryPressure { Unknown, Normal, Elevated, Critical }

/// <summary>Payload estimates, not observed process memory or guaranteed reclaimed bytes.</summary>
public sealed record StateCachePressureSnapshot(StateCacheMemoryPressure Pressure, int Entries,
    long EstimatedBytes, int EntryBudget, long EstimatedByteBudget, int PendingProducers);

public interface IStateCachePressureControl
{
    StateCachePressureSnapshot ApplyMemoryPressure(StateCacheMemoryPressure pressure);
}
