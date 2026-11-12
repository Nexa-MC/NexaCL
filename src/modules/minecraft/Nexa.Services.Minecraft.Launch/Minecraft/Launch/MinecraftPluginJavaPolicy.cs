using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Java;

namespace Nexa.Services.Minecraft.Launch;

/// <summary>Read-only requirement projection shared with launch preparation and remediation.</summary>
public static class MinecraftPluginJavaPolicy
{
    public static async ValueTask<JavaRequirementResolution> ReadAsync(string instanceDirectory, CancellationToken token = default)
    {
        string instance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instanceDirectory));
        var versions = Directory.GetParent(instance);
        if (versions?.Name != "versions" || versions.Parent is null)
            return JavaRequirementResolution.Invalid(JavaRequirementFailureReason.InvalidVersionMetadata, "Invalid instance directory.");
        string root = versions.Parent.FullName;
        var query = new MachineCapabilityQuery(instance, Path.GetFileName(instance), root) { RefreshInstance = true };
        var primary = await MinecraftPrimaryInstanceScope.ResolveAsync(root, query, token).ConfigureAwait(false);
        if (primary is null) return JavaRequirementResolution.Invalid(JavaRequirementFailureReason.InvalidVersionMetadata, "Instance manifests are unavailable.");
        var loader = await InstalledLoaderProjection.ReadAsync(primary, root, query, token).ConfigureAwait(false);
        return MinecraftJavaRequirementResolver.Resolve(MinecraftLaunchCoordinator.CreateJavaRequirement(primary.Instance, primary.Manifests, loader));
    }
}
