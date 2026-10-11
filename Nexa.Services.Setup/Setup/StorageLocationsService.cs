using System.Collections.ObjectModel;
using Nexa.Core;
using Nexa.Services.Files;
using Nexa.Xsr;

namespace Nexa.Services.Setup;

/// <summary>Projects configured paths without scanning the filesystem or creating directories.</summary>
public sealed class StorageLocationsService
{
    private readonly AppFolders _folders;
    private readonly string? _cache;
    private readonly ReadOnlyCollection<string> _javaRoots;
    private readonly Func<StorageMinecraftSelection> _selection;

    public StorageLocationsService(AppFolders folders, string? cacheDirectory,
        IReadOnlyList<string> javaRuntimeDirectories, Func<StorageMinecraftSelection> selection)
    {
        _folders = folders;
        _cache = NormalizeOptional(cacheDirectory);
        _javaRoots = Freeze(javaRuntimeDirectories);
        _selection = selection;
    }

    public XsrResult<StorageLocationsSnapshot> Read(CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            StorageMinecraftSelection selected = _selection();
            string? root = NormalizeOptional(selected.ActiveRootDirectory);
            string? instance = NormalizeOptional(selected.SelectedInstanceDirectory);
            string? instances = root is null ? null : Path.Combine(root, "versions");
            // A selected instance must be qualified by the same active library root.
            if (instance is not null && (instances is null || !instance.StartsWith(instances + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new InvalidDataException("所选版本目录不属于当前游戏目录。");
            token.ThrowIfCancellationRequested();
            return XsrResult.Success(new StorageLocationsSnapshot(selected.Revision, _folders.Root,
                _folders.ResolveSafePath(FolderNames.Settings, ""), _folders.ResolveSafePath(FolderNames.Logs, ""),
                _cache, _javaRoots, Freeze(selected.LibraryDirectories), root, instances, instance));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return XsrResult.Failure<StorageLocationsSnapshot>(XsrRuntimeErrors.Cancelled());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return XsrResult.Failure<StorageLocationsSnapshot>(new(XsrErrorKind.Rejected,
                XsrSemanticId.Parse("setup.storage.locations.unavailable"), "暂时无法读取目录信息。"));
        }
    }

    private static string? NormalizeOptional(string? path) => string.IsNullOrWhiteSpace(path) ? null : PathIdentity.Normalize(path);
    private static ReadOnlyCollection<string> Freeze(IEnumerable<string> paths) => Array.AsReadOnly(paths
        .Where(path => !string.IsNullOrWhiteSpace(path)).Select(PathIdentity.Normalize).Distinct(PathIdentity.Comparer).ToArray());
}
