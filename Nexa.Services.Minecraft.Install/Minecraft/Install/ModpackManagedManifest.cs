using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Minecraft.Install;

internal sealed record ModpackManagedManifest(int Schema, string Name, string Version, string ArchiveSha256, ModpackPublishedFile[] Files);

internal static class ModpackManagedManifestStore
{
    internal const string RelativePath = "Nexa/ModpackManaged.json";
    internal static bool IsUserOwned(string path)
    {
        string first = path.Split('/')[0];
        return new[] { "Nexa", "PCL", "saves", "screenshots", "backups", "logs", "crash-reports", "options.txt", "servers.dat", "servers.dat_old" }
            .Contains(first, StringComparer.OrdinalIgnoreCase) || first.StartsWith(".nexa", StringComparison.OrdinalIgnoreCase);
    }
    internal static bool IsPackFile(string path, string instance) => !IsUserOwned(path)
        && path != instance + ".json" && path != instance + ".jar";

    internal static IEnumerable<string> Enumerate(string directory) => EnumerateCore(directory, directory);

    private static IEnumerable<string> EnumerateCore(string directory, string root)
    {
        RecoveryBlobStore.CheckLinks(directory);
        if (!Directory.Exists(directory)) yield break;
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            RecoveryBlobStore.CheckLinks(path);
            if (Directory.Exists(path))
            {
                if (IsUserOwned(Path.GetRelativePath(root, path).Replace('\\', '/'))) continue;
                foreach (string file in EnumerateCore(path, root)) yield return file;
            }
            else yield return path;
        }
    }

    internal static async Task<ModpackManagedManifest> CaptureAsync(string instance, MinecraftModpackPreview pack, CancellationToken token)
    {
        List<ModpackPublishedFile> files = []; long total = 0;
        foreach (string path in Enumerate(instance))
        {
            string relative = Path.GetRelativePath(instance, path).Replace('\\', '/');
            if (!IsPackFile(relative, Path.GetFileName(instance))) continue;
            await using var input = File.OpenRead(path);
            if (files.Count >= 100000 || input.Length > MinecraftModpackArchive.MaxFile || (total += input.Length) > MinecraftModpackArchive.MaxExpanded)
                throw new InvalidDataException("整合包文件清单超过限制。");
            files.Add(new(relative, input.Length, Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false))));
        }
        return new(1, pack.Name, pack.Version, pack.Sha256, files.ToArray());
    }
    internal static async Task WriteAsync(string instance, ModpackManagedManifest manifest, CancellationToken token)
    {
        string path = Path.Combine(instance, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        RecoveryBlobStore.CheckLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ModpackManagedJson.Default.ModpackManagedManifest);
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("整合包文件清单过大。");
        await File.WriteAllBytesAsync(path, bytes, token).ConfigureAwait(false);
    }
    internal static async Task<ModpackManagedManifest?> ReadAsync(string instance, CancellationToken token)
    {
        string path = Path.Combine(instance, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        RecoveryBlobStore.CheckLinks(path);
        if (!File.Exists(path)) return null;
        await using var input = File.OpenRead(path);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("整合包文件清单过大。");
        var value = await JsonSerializer.DeserializeAsync(input, ModpackManagedJson.Default.ModpackManagedManifest, token).ConfigureAwait(false);
        if (value is not { Schema: 1, Files: not null, ArchiveSha256.Length: 64 } || value.Files.Length > 100000
            || !value.ArchiveSha256.All(char.IsAsciiHexDigit)) throw new InvalidDataException("整合包文件清单无效。");
        HashSet<string> paths = new(Nexa.Core.PathIdentity.Comparer);
        long total = 0;
        foreach (var file in value.Files)
        {
            MinecraftModpackArchive.SafeRelative(file.Path);
            if (!IsPackFile(file.Path, Path.GetFileName(instance)) || !paths.Add(file.Path)
                || file.Length < 0 || file.Length > MinecraftModpackArchive.MaxFile || (total += file.Length) > MinecraftModpackArchive.MaxExpanded
                || file.Sha256 is not { Length: 64 } || !file.Sha256.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("整合包文件清单条目无效。");
        }
        return value;
    }
}

[JsonSerializable(typeof(ModpackManagedManifest))]
internal sealed partial class ModpackManagedJson : JsonSerializerContext;
