using Nexa.Xsr;

namespace Nexa.Services.Setup;

/// <summary>Configured locations, not disk inventory or a writable storage policy.</summary>
public sealed record StorageLocationsQuery;
public sealed record StorageMinecraftSelection(long Revision, IReadOnlyList<string> LibraryDirectories,
    string? ActiveRootDirectory, string? SelectedInstanceDirectory);
public sealed record StorageLocationsSnapshot(long LibraryRevision, string DataDirectory, string SettingsDirectory,
    string LogDirectory, string? CacheDirectory, IReadOnlyList<string> JavaRuntimeDirectories,
    IReadOnlyList<string> MinecraftLibraryDirectories, string? ActiveMinecraftDirectory,
    string? InstancesDirectory, string? SelectedInstanceDirectory);

public static class StorageLocationsContract
{
    public static readonly XsrSemanticId Query = XsrSemanticId.Parse("setup.storage.locations");
}
