using System.IO.Compression;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Tasks;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class InstanceModpackExportService(TaskCenterService tasks, XsrStateStore store)
{
    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
    {
        ["mods"] = "模组",
        ["config"] = "模组配置",
        ["defaultconfigs"] = "默认配置",
        ["resourcepacks"] = "资源包",
        ["shaderpacks"] = "光影包",
        ["saves"] = "存档",
        ["schematics"] = "蓝图",
        ["kubejs"] = "KubeJS 脚本",
        ["scripts"] = "脚本",
        ["datapacks"] = "数据包",
        ["options.txt"] = "游戏选项",
        ["optionsof.txt"] = "OptiFine 设置",
        ["configureddefaults"] = "游戏默认设置",
        ["screenshots"] = "截图",
        ["servers.dat"] = "服务器列表",
        ["texturepacks"] = "旧版资源包",
        ["replay_recordings"] = "录像",
        ["replay_videos"] = "录像视频",
        ["journeymap"] = "JourneyMap 数据",
        ["xaero"] = "Xaero 数据",
        ["XaeroWaypoints"] = "小地图路径点",
        ["XaeroWorldMap"] = "世界地图",
        ["patchouli_books"] = "帕秋莉手册",
        ["structures"] = "结构",
        ["openloader"] = "OpenLoader 内容",
        ["resources"] = "附加资源"
    };
    private sealed record ExportFile(string Path, string Relative, long Size, long Modified);
    private const long FileLimit = 512L * 1024 * 1024, TotalLimit = 8L * 1024 * 1024 * 1024, ArchiveLimit = 2L * 1024 * 1024 * 1024;
    public static Task<InstanceModpackExportPreview> PreviewAsync(InstanceModpackExportQuery query, CancellationToken token = default) => Task.Run(async () =>
    {
        var snapshot = await InstanceManagementService.ReadAsync(new(query.InstanceDirectory), token).ConfigureAwait(false);
        var available = new List<InstanceExportCategory>();
        var entries = new List<InstanceExportFile>();
        var categories = AvailableCategories(snapshot.GameDirectory);
        foreach (var (id, label) in categories)
        {
            var files = Collect(snapshot.GameDirectory, [id], token, categories);
            if (files.Count > 0)
            {
                available.Add(new(id, label, files.Count, files.Sum(f => f.Size)));
                entries.AddRange(files.Select(file => new InstanceExportFile(file.Relative, id, file.Size)));
                if (entries.Count > 50000) throw new InvalidDataException("导出文件数量超过限制。");
            }
        }
        return new InstanceModpackExportPreview(snapshot.InstanceDirectory, available.AsReadOnly()) { Files = entries.AsReadOnly() };
    }, token);

    public async Task<XsrResult> ExportAsync(InstanceModpackExportCommand command, CancellationToken token = default)
    {
        using var task = tasks.Begin(new("modpack.export." + Guid.NewGuid().ToString("N"), "导出整合包", ["准备", "打包", "验证"]));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, task.CancellationToken); token = linked.Token;
        string? stage = null;
        try
        {
            bool zip = command.Format == InstanceExportFormat.Zip;
            if (!Enum.IsDefined(command.Format) || (!zip && (command.IncludeGameCore || command.IncludeLibraries || command.IncludeAssets)))
                throw new InvalidDataException("只有 ZIP 格式可以包含游戏核心、运行库和资源文件。");
            if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 256 || string.IsNullOrWhiteSpace(command.Version) || command.Version.Length > 128
                || !Path.IsPathFullyQualified(command.DestinationPath) || !Path.GetExtension(command.DestinationPath).Equals(zip ? ".zip" : ".mrpack", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请填写整合包名称、版本，并选择 .mrpack 或 .zip 文件。");
            var snapshot = await InstanceManagementService.ReadAsync(new(command.InstanceDirectory), token).ConfigureAwait(false);
            using var lease = await InstanceRecoveryOperationGate.EnterRestoreAsync(Directory.GetParent(snapshot.InstanceDirectory)!.Parent!.FullName, token).ConfigureAwait(false);
            snapshot = await InstanceManagementService.ReadAsync(new(command.InstanceDirectory), token).ConfigureAwait(false);
            InstanceContentTrash.RejectRunning(snapshot, store, token);
            var dependencies = zip ? null : Dependencies(snapshot);
            var files = Collect(snapshot.GameDirectory, command.Categories, token);
            if (command.Summary.Length > 4096 || command.ExcludedPaths.Count > 50000) throw new InvalidDataException("导出选项超过限制。");
            var excluded = command.ExcludedPaths.Select(MinecraftModpackArchive.SafeRelative).ToHashSet(StringComparer.Ordinal);
            files.RemoveAll(file => excluded.Contains(file.Relative));
            if (zip) files = await CollectZipAsync(snapshot, command, files, token).ConfigureAwait(false);
            string destination = Path.GetFullPath(command.DestinationPath), directory = Path.GetDirectoryName(destination)!;
            RecoveryBlobStore.CheckLinks(directory); RecoveryBlobStore.CheckLinks(destination);
            if (!Directory.Exists(directory) || Path.Exists(destination)) throw new IOException("导出目录不可用或同名文件已存在。");
            if (files.Any(f => Nexa.Core.PathIdentity.Comparer.Equals(f.Path, destination))) throw new IOException("导出目标不能是包内文件。");
            stage = Path.Combine(directory, ".nexa-export-" + Guid.NewGuid().ToString("N"));
            await using var output = new FileStream(stage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                var manifest = zip ? new JsonObject
                {
                    ["format"] = "nexa-game-directory",
                    ["schema"] = 1,
                    ["name"] = command.Name,
                    ["version"] = command.Version,
                    ["summary"] = command.Summary,
                    ["instance"] = Path.GetFileName(snapshot.InstanceDirectory),
                    ["minecraft"] = snapshot.GameVersion,
                    ["gameCore"] = command.IncludeGameCore,
                    ["libraries"] = command.IncludeLibraries,
                    ["assets"] = command.IncludeAssets
                } : new JsonObject
                {
                    ["formatVersion"] = 1,
                    ["game"] = "minecraft",
                    ["name"] = command.Name,
                    ["versionId"] = command.Version,
                    ["dependencies"] = dependencies,
                    ["files"] = new JsonArray(),
                    ["summary"] = command.Summary
                };
                await using (var index = archive.CreateEntry(zip ? "nexa-export.json" : "modrinth.index.json").Open())
                    await index.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()), token).ConfigureAwait(false);
                byte[] buffer = new byte[81920]; long written = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i]; RecoveryBlobStore.CheckLinks(file.Path);
                    await using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, true);
                    if (input.Length != file.Size || File.GetLastWriteTimeUtc(file.Path).Ticks != file.Modified) throw new IOException("导出期间文件已变化，请重新导出。");
                    var entry = archive.CreateEntry((zip ? ".minecraft/" : "overrides/") + MinecraftModpackArchive.SafeRelative(file.Relative), CompressionLevel.Fastest);
                    await using (var target = entry.Open())
                    {
                        long actual = 0; int read;
                        while ((read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, file.Size - actual + 1)), token).ConfigureAwait(false)) > 0)
                        {
                            actual += read; written += read;
                            if (actual > file.Size || written > TotalLimit) throw new InvalidDataException("导出实际大小超过预算。");
                            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                            if (output.Length > ArchiveLimit) throw new InvalidDataException("整合包超过 2 GiB。");
                        }
                        if (actual != file.Size || File.GetLastWriteTimeUtc(file.Path).Ticks != file.Modified) throw new IOException("导出期间文件已变化。");
                    }
                    task.Report("打包", file.Relative, (double)(i + 1) / Math.Max(1, files.Count), i + 1, files.Count, 0);
                }
            }
            output.Flush(true); if (output.Length > ArchiveLimit) throw new InvalidDataException("整合包超过 2 GiB。");
            await output.DisposeAsync().ConfigureAwait(false);
            var current = await InstanceManagementService.ReadAsync(new(command.InstanceDirectory), token).ConfigureAwait(false);
            if (!Nexa.Core.PathIdentity.Comparer.Equals(current.GameDirectory, snapshot.GameDirectory)
                || (!zip && !JsonNode.DeepEquals(Dependencies(current), dependencies)))
                throw new IOException("导出期间版本信息已变化，请重新导出。");
            // MRPack uses the production import parser; a directory ZIP uses archive safety validation.
            if (zip)
            {
                using var validation = ZipFile.OpenRead(stage);
                MinecraftModpackArchive.ValidateArchive(validation);
            }
            else await MinecraftModpackArchive.InspectAsync(stage, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(destination);
            File.Move(stage, destination, false); task.Complete("整合包已导出。"); return XsrResult.Success();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { task.Canceled(); return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException)
        { task.Fail(error.Message); return XsrResult.Failure(MinecraftErrors.InvalidRequest(error.Message)); }
        finally { if (stage is not null) try { File.Delete(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private static JsonObject Dependencies(InstanceManagementSnapshot snapshot)
    {
        var result = new JsonObject { ["minecraft"] = snapshot.GameVersion };
        foreach (var component in snapshot.Components)
        {
            string? name = component.Loader switch
            {
                InstallLoader.Forge => "forge",
                InstallLoader.NeoForge => "neoforge",
                InstallLoader.Fabric => "fabric-loader",
                InstallLoader.Quilt => "quilt-loader",
                InstallLoader.FabricApi or InstallLoader.Qsl or InstallLoader.OptiFabric => null,
                _ => throw new InvalidDataException("当前加载器不支持标准 MRPack 导出，请先使用支持的加载器。")
            };
            if (name is not null) result[name] = component.Version;
        }
        return result;
    }
    private static List<ExportFile> Collect(string game, IReadOnlyList<string> selected, CancellationToken token, Dictionary<string, string>? allowed = null)
    {
        allowed ??= AvailableCategories(game);
        if (selected.Count > allowed.Count || selected.Any(id => !allowed.ContainsKey(id)) || selected.Distinct(Nexa.Core.PathIdentity.Comparer).Count() != selected.Count)
            throw new InvalidDataException("导出范围无效。");
        List<ExportFile> files = []; long total = 0; int directories = 0;
        foreach (string id in selected)
        {
            string root = Path.Combine(game, id); RecoveryBlobStore.CheckLinks(root);
            if (File.Exists(root)) Add(root);
            else if (Directory.Exists(root))
            {
                Stack<string> pending = new(); pending.Push(root);
                while (pending.TryPop(out var directory))
                {
                    if (++directories > 100000 || Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar).Length > 32)
                        throw new InvalidDataException("导出目录过多或层级过深。");
                    token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(directory);
                    foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("导出范围包含链接，请先移除链接。");
                        if (item is DirectoryInfo) pending.Push(item.FullName); else Add(item.FullName);
                    }
                }
            }
        }
        return files;
        void Add(string path)
        {
            token.ThrowIfCancellationRequested(); var file = new FileInfo(path);
            string relative = Path.GetRelativePath(game, path).Replace('\\', '/');
            if (ExcludedExportFile(relative)) return;
            total += file.Length;
            if (file.Length > FileLimit || total > TotalLimit || files.Count >= 50000) throw new InvalidDataException("导出范围超过大小或文件数限制。");
            files.Add(new(path, relative, file.Length, file.LastWriteTimeUtc.Ticks));
        }
    }

    private static bool ExcludedExportFile(string relative) =>
        relative.Split('/').Any(part => part.StartsWith('.') || part.Equals("logs", StringComparison.OrdinalIgnoreCase) || part.Equals("accountsx", StringComparison.OrdinalIgnoreCase))
        || new[] { ".log", ".pdb", ".tmp", ".dat_old", ".key", ".pem", ".pfx" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)
        || new[] { "accounts.json", "launcher_accounts.json", "microsoft_accounts.json", "usercache.json", "launcher_profiles.json" }.Contains(Path.GetFileName(relative), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> AvailableCategories(string game)
    {
        var result = new Dictionary<string, string>(Categories, Nexa.Core.PathIdentity.Comparer);
        RecoveryBlobStore.CheckLinks(game);
        foreach (var directory in new DirectoryInfo(game).EnumerateDirectories())
        {
            string name = directory.Name;
            if (name.StartsWith('.') || (directory.Attributes & FileAttributes.ReparsePoint) != 0
                || new[] { "assets", "libraries", "versions", "natives", "logs", "crash-reports", "PCL", "Nexa", "accounts" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            result.TryAdd(name, name);
            if (result.Count > 512) throw new InvalidDataException("导出目录数量超过限制。");
        }
        return result;
    }
}
