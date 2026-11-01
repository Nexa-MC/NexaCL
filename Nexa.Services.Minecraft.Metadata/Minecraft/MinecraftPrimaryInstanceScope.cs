using Nexa.Services.Caching;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;

namespace Nexa.Services.Capabilities;

/// <summary>
/// Explicit instance scope shared by environment projections. A caller must identify the
/// selected instance; an unscoped machine query never guesses from release time.
/// </summary>
public static class MinecraftPrimaryInstanceScope
{
    public sealed record PrimaryInstance(
        MinecraftInstanceDescriptor Instance,
        MinecraftResolvedVersionManifests Manifests,
        string GameDirectory,
        MinecraftOptionsSnapshot Options);

    public static ValueTask<PrimaryInstance?> ResolveAsync(string? minecraftRootDirectory,
        MachineCapabilityQuery query, CancellationToken cancellationToken) =>
        ResolveAsync(minecraftRootDirectory, query, null, cancellationToken);

    public static async ValueTask<PrimaryInstance?> ResolveAsync(string? minecraftRootDirectory,
        MachineCapabilityQuery query, ISharedStateCache? sharedCache, CancellationToken cancellationToken)
    {
        if (!query.HasInstanceScope) return null;
        string? requestedRoot = string.IsNullOrWhiteSpace(query.MinecraftRootDirectory)
            ? minecraftRootDirectory
            : query.MinecraftRootDirectory;
        if (string.IsNullOrWhiteSpace(requestedRoot)) return null;
        string root = Path.GetFullPath(requestedRoot);
        PrimaryInstance? resolved = null;
        try
        {
            MinecraftInstanceDiscovery discovery = sharedCache is null ? new() : new(sharedCache);
            IReadOnlyList<MinecraftInstanceDescriptor> instances = query.RefreshInstance
                ? await discovery.RefreshAsync(root, cancellationToken).ConfigureAwait(false)
                : await discovery.DiscoverAsync(root, cancellationToken).ConfigureAwait(false);
            string? selectedDirectory = string.IsNullOrWhiteSpace(query.InstanceDirectory)
                ? null : Path.GetFullPath(query.InstanceDirectory);
            MinecraftInstanceDescriptor? selected = instances.FirstOrDefault(instance =>
                selectedDirectory is not null
                    ? Nexa.Core.PathIdentity.Comparer.Equals(Path.GetFullPath(instance.DirectoryPath), selectedDirectory)
                    : string.Equals(instance.Id, query.InstanceId, StringComparison.Ordinal));
            if (selected is { } instance)
            {
                MinecraftResolvedVersionManifests manifests = await MinecraftVersionJsonReader
                    .ResolveAsync(instance, root, cancellationToken).ConfigureAwait(false);
                string gameDirectory = instance.Metadata.InstanceIsolation ? instance.DirectoryPath : root;
                MinecraftOptionsSnapshot options = await MinecraftOptionsReader
                    .ReadAsync(gameDirectory, cancellationToken).ConfigureAwait(false);
                resolved = new PrimaryInstance(instance, manifests, gameDirectory, options);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not AccessViolationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Discovery races a running install or a broken version dir; projections answer
            // unavailable for this query instead of retaining an authful or failed snapshot.
            resolved = null;
        }

        return resolved;
    }
}

internal static class InstalledLoaderProjection
{
    public static async ValueTask<MinecraftModLoaderDescriptor> ReadAsync(
        MinecraftPrimaryInstanceScope.PrimaryInstance primary,
        string? configuredRoot,
        MachineCapabilityQuery query,
        CancellationToken cancellationToken)
    {
        string? root = string.IsNullOrWhiteSpace(query.MinecraftRootDirectory)
            ? configuredRoot
            : query.MinecraftRootDirectory;
        if (!string.IsNullOrWhiteSpace(root))
        {
            try
            {
                MinecraftInstallEditSnapshot edit = await MinecraftInstallEditService.ReadAsync(
                    new(Path.GetFullPath(root), primary.Instance.Id), cancellationToken).ConfigureAwait(false);
                InstallBuildSelection? selected = edit.Selection.FirstOrDefault(item => !InstallLoaderFacts.IsAddon(item.Loader));
                if (selected is not null)
                {
                    MinecraftModLoaderKind kind = selected.Loader switch
                    {
                        InstallLoader.Forge => MinecraftModLoaderKind.Forge,
                        InstallLoader.NeoForge => MinecraftModLoaderKind.NeoForge,
                        InstallLoader.Fabric or InstallLoader.LegacyFabric => MinecraftModLoaderKind.Fabric,
                        InstallLoader.Quilt => MinecraftModLoaderKind.Quilt,
                        InstallLoader.Cleanroom => MinecraftModLoaderKind.Cleanroom,
                        InstallLoader.OptiFine => MinecraftModLoaderKind.OptiFine,
                        InstallLoader.LiteLoader => MinecraftModLoaderKind.LiteLoader,
                        InstallLoader.LabyMod => MinecraftModLoaderKind.LabyMod,
                        _ => MinecraftModLoaderKind.Unknown,
                    };
                    return new(kind, selected.Version, primary.Manifests.Current["mainClass"]?.ToString(),
                        ["MinecraftInstallEditService"]);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
            {
                // A manually assembled or partially installed version can lack the edit receipt.
                // Fall through to the manifest detector instead of losing all loader facts.
            }
        }

        return MinecraftModLoaderDetector.Detect(primary.Manifests.Current);
    }
}

