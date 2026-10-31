using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class InstanceModpackExportService
{
    // A ZIP is an extractable game directory, not an MRPack with a different suffix.
    // Resolve only this instance's references; never sweep shared libraries/assets/versions.
    private static async Task<List<ExportFile>> CollectZipAsync(InstanceManagementSnapshot snapshot,
        InstanceModpackExportCommand command, List<ExportFile> content, CancellationToken token)
    {
        string root = Directory.GetParent(snapshot.InstanceDirectory)!.Parent!.FullName;
        var files = new Dictionary<string, ExportFile>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in content) Add(file.Path);
        List<JsonObject> manifests = [];
        HashSet<string> visited = new(Nexa.Core.PathIdentity.Comparer);
        string? path = Path.Combine(snapshot.InstanceDirectory, Path.GetFileName(snapshot.InstanceDirectory) + ".json");
        while (path is not null)
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(path) || visited.Count > 100) throw new InvalidDataException("版本继承链存在循环或过深。");
            Add(path);
            JsonObject manifest = await ReadExportJsonAsync(path, 4 * 1024 * 1024, token).ConfigureAwait(false);
            manifests.Add(manifest);
            string? parent = manifest["inheritsFrom"]?.GetValue<string>();
            if (parent is null) break;
            if (!MinecraftVersionPaths.IsSafeReference(parent)) throw new InvalidDataException("父版本名称无效。");
            path = MinecraftVersionPaths.ResolveJsonPath(root, Path.GetDirectoryName(path), parent)
                ?? throw new InvalidDataException($"缺少父版本 {parent}，请先准备离线依赖。");
        }
        if (command.IncludeGameCore)
        {
            var client = MinecraftClientJarResolver.Resolve(new()
            {
                VersionJson = manifests[0],
                InheritedVersionJsons = manifests.Skip(1).ToArray(),
                VersionId = Path.GetFileName(snapshot.InstanceDirectory),
                InstanceDirectory = snapshot.InstanceDirectory,
                MinecraftRootDirectory = root
            });
            Add(client.Path);
            // Legacy loaders may keep a patched client alongside the inherited vanilla JAR.
            foreach (string manifestPath in visited)
            {
                string jar = Path.ChangeExtension(manifestPath, ".jar");
                if (File.Exists(jar)) Add(jar);
            }
        }
        if (command.IncludeLibraries)
        {
            foreach (var manifest in manifests)
            {
                var libraries = MinecraftLibraryResolver.Resolve(new()
                {
                    VersionJson = manifest,
                    MinecraftRootDirectory = root,
                    TargetInstanceDirectory = snapshot.InstanceDirectory,
                    OperatingSystem = OperatingSystem.IsWindows() ? MinecraftLibraryOperatingSystem.Win32
                        : OperatingSystem.IsMacOS() ? MinecraftLibraryOperatingSystem.MacOs : MinecraftLibraryOperatingSystem.Linux,
                    OperatingSystemVersion = Environment.OSVersion.Version.ToString(),
                    Is64BitArchitecture = Environment.Is64BitProcess,
                    IsArm64Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                });
                foreach (var library in libraries) Add(library.LocalPath);
            }
        }
        if (command.IncludeAssets)
        {
            string? indexId = manifests.Select(manifest => manifest["assetIndex"]?["id"]?.GetValue<string>()
                ?? manifest["assets"]?.GetValue<string>()).FirstOrDefault(id => !string.IsNullOrEmpty(id));
            indexId ??= "legacy";
            if (!MinecraftVersionPaths.IsSafeReference(indexId)) throw new InvalidDataException("资源索引名称无效。");
            string indexPath = Path.Combine(root, "assets", "indexes", indexId + ".json");
            Add(indexPath);
            var index = await ReadExportJsonAsync(indexPath, 16 * 1024 * 1024, token).ConfigureAwait(false);
            if (index["objects"] is not JsonObject objects || objects.Count > 100000) throw new InvalidDataException("资源索引无效。");
            foreach (var entry in objects)
            {
                token.ThrowIfCancellationRequested();
                string hash = MinecraftModpackArchive.RequireHash(entry.Value?["hash"]?.GetValue<string>(), 40).ToLowerInvariant();
                Add(Path.Combine(root, "assets", "objects", hash[..2], hash));
            }
        }
        return files.Values.ToList();

        void Add(string filePath)
        {
            token.ThrowIfCancellationRequested();
            filePath = Path.GetFullPath(filePath);
            string relative = MinecraftModpackArchive.SafeRelative(Path.GetRelativePath(root, filePath).Replace('\\', '/'));
            if (ExcludedExportFile(relative)) throw new InvalidDataException("版本引用了不允许导出的私有或临时文件。");
            if (files.TryGetValue(relative, out var existing))
            {
                if (!Nexa.Core.PathIdentity.Comparer.Equals(existing.Path, filePath)) throw new InvalidDataException("导出路径大小写冲突。");
                return;
            }
            RecoveryBlobStore.CheckLinks(filePath);
            var info = new FileInfo(filePath);
            if (!info.Exists) throw new InvalidDataException($"缺少 {relative}，请先准备离线依赖。");
            total += info.Length;
            if (info.Length > FileLimit || total > TotalLimit || files.Count >= 50000) throw new InvalidDataException("导出范围超过大小或文件数限制。");
            files.Add(relative, new(filePath, relative, info.Length, info.LastWriteTimeUtc.Ticks));
        }
    }

    private static async Task<JsonObject> ReadExportJsonAsync(string path, int limit, CancellationToken token)
    {
        RecoveryBlobStore.CheckLinks(path);
        await using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new InvalidDataException("导出元数据超过大小限制。");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.ReadByte() != -1) throw new IOException("导出期间元数据已变化。");
        return JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("导出元数据无效。");
    }
}
