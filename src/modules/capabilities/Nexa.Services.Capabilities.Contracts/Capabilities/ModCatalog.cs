namespace Nexa.Services.Capabilities;

/// <summary>mod.* capability definitions (§22 subset: enumeration facts the estimator eats).</summary>
public static class ModCatalog
{
    public const string ProviderId = "nexa.mod";

    public static readonly CapabilityDefinition<int> ModCount = new(
        "mod.count", "模组总数", "模组", ProviderId, CapabilityKind.Metric, CapabilityStability.Dynamic);
    public static readonly CapabilityDefinition<int> ModEnabled = new(
        "mod.enabled", "启用模组数", "模组", ProviderId, CapabilityKind.Metric, CapabilityStability.Dynamic);
    public static readonly CapabilityDefinition<int> ModDisabled = new(
        "mod.disabled", "禁用模组数", "模组", ProviderId, CapabilityKind.Metric, CapabilityStability.Dynamic);
    public static readonly CapabilityDefinition<long> ModJsonSize = new(
        "mod.json.size", "模组文件总量", "模组", ProviderId, CapabilityKind.Metric, CapabilityStability.Dynamic, unit: "bytes");
    public static readonly CapabilityDefinition<bool> ModRuntimeProfileKnown = new(
        "mod.runtime_profile.known", "模组画像已知", "模组", ProviderId, CapabilityKind.Fact, CapabilityStability.Dynamic);
    public static readonly CapabilityDefinition<string> ModMetadataFingerprint = new(
        "mod.metadata.fingerprint", "模组元数据指纹", "模组", ProviderId, CapabilityKind.Fact, CapabilityStability.Dynamic);

    public static IReadOnlyList<ICapabilityDefinition> Definitions() => (ICapabilityDefinition[])
    [
        ModCount, ModEnabled, ModDisabled, ModJsonSize, ModRuntimeProfileKnown, ModMetadataFingerprint,
    ];
}

