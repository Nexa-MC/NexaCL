






namespace Nexa.Services.Minecraft.Process;

public sealed record LaunchModIdentity(string Id, string Version, string Format, bool Enabled,
    IReadOnlyDictionary<string, string> Dependencies, bool DependenciesComplete)
{
    public string? ContentSha256 { get; init; }
    public bool NestedCandidate { get; init; }
    public IReadOnlyDictionary<string, string> ProvidedIds { get; init; } = new Dictionary<string, string>();
}
public sealed record LaunchModInventory(IReadOnlyList<LaunchModIdentity> Mods, int UnknownFiles, bool Complete);
public sealed record JvmRunContext(Guid SessionId, string Loader, string LoaderVersion, LaunchModInventory Inventory)
{
    public JvmRunLaunchProfile? LaunchProfile { get; init; }
    public string GameVersion { get; init; } = "unknown";
    public IReadOnlyDictionary<string, string> Components { get; init; } = new Dictionary<string, string>();
}

/// <summary>Captured configuration, not proof of achieved runtime resource usage.</summary>
public sealed record JvmRunLaunchProfile(int JavaMajor, int ClasspathCount, int LibraryCount,
    int NativeLibraryCount, int HeapLimitMiB, int CpuSetCount, int CpuPolicy, int ProcessQuality,
    int CpuQuotaPercent, long ProcessMemoryLimitMiB, long IoReadLimitMiB, long IoWriteLimitMiB,
    bool GameMode, bool Wrapped, bool InheritedClientJar);
