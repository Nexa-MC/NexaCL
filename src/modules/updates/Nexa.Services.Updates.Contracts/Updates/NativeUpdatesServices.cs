using Nexa.Platform.Updates;
using Nexa.Services.Rollouts;

namespace Nexa.Services.Updates;

public sealed record NativeUpdatesInitialization(string BaseDirectory, string PublicKey, string Fingerprint);
public sealed record NativeSidecarUpdateConfiguration(string Root, string Bundled, string RuntimeId, string HostVersion);
public sealed record NativeUpdateDiscoveryConfiguration(string SeedPath, string Channel, string RuntimeId, bool UseHostWork);
public sealed record NativeAutomaticUpdateConfiguration(string? PortableRoot, UpdateBuildIdentity Running);
public sealed record NativeUpdateInstallRequest(string Version, string Channel);
public sealed record NativeSidecarPackages(IReadOnlyDictionary<string, string> Packages);
public sealed record NativeRolloutProjection(RolloutSnapshot Snapshot, DateTimeOffset EffectiveUntil);
public interface IUpdateTelemetrySource { Action<string, string>? Record { get; set; } }
