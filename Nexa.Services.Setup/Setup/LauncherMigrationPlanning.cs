using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexa.Services.Setup;

public sealed partial class LauncherMigrationService
{
    private static readonly string[] GameDirectories = ["saves", "mods", "config", "defaultconfigs", "resourcepacks",
        "shaderpacks", "screenshots", "schematics", "journeymap", "kubejs", "scripts", "patchouli_books", "backups"];
    private static readonly string[] GameFiles = ["options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat",
        "servers.dat_old", "hotbar.nbt", "icon.png"];

    private static async Task<MigrationPlan> PlanAsync(LauncherMigrationQuery input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.SourceKind)) throw new InvalidDataException("未知的启动器类型。");
        string source = LauncherMigrationFiles.Absolute(input.SourceDirectory), destination = LauncherMigrationFiles.Absolute(input.DestinationDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("迁移来源目录不存在。");
        if (Path.Exists(destination)) throw new IOException("请指定一个尚不存在的新目标目录。");
        if (LauncherMigrationFiles.Within(destination, source) || LauncherMigrationFiles.Within(source, destination))
            throw new IOException("来源目录与目标目录不能相互包含。");
        LauncherMigrationFiles.CheckLinks(source); LauncherMigrationFiles.CheckLinks(destination);
        var query = input with { SourceDirectory = source, DestinationDirectory = destination };
        var discovered = await LauncherMigrationFormats.DiscoverAsync(query.SourceKind, source, token).ConfigureAwait(false);
        if (discovered.Count > 256) throw new InvalidDataException("一次最多迁移 256 个实例。");
        List<PlannedInstance> instances = [];
        using var revision = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddRevision(revision, ((int)query.SourceKind).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddRevision(revision, source); AddRevision(revision, destination);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int totalFiles = 0, totalDirectories = 0;
        long totalBytes = 0;
        foreach (var candidate in discovered.OrderBy(item => item.Identity, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            foreach (string dataRoot in candidate.NativeRoot is { } nativeRoot
                ? new[] { candidate.GameDirectory, nativeRoot } : new[] { candidate.GameDirectory })
                if (LauncherMigrationFiles.Within(destination, dataRoot) || LauncherMigrationFiles.Within(dataRoot, destination))
                    throw new IOException("目标目录与来源实例的游戏数据不能相互包含。");
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Identity))).ToLowerInvariant();
            if (!ids.Add(id)) throw new InvalidDataException("来源包含重复实例标识。");
            string instanceId = candidate.NativeVersion ?? "import-" + id[..16];
            string destinationName = "instance-" + id[..16];
            var files = new Dictionary<string, LauncherMigrationFile>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> warnings = [.. candidate.Warnings];
            string? unsupported = candidate.UnsupportedReason;
            if (candidate.Name.Length is < 1 or > 4096 || candidate.Name.Any(char.IsControl))
                unsupported ??= "实例名称无效或超过 4096 字符，请在原启动器中修改名称。";
            AddRevision(revision, candidate.Identity); AddRevision(revision, candidate.Name);
            AddRevision(revision, candidate.GameVersion); AddRevision(revision, candidate.Loader ?? "");
            AddRevision(revision, candidate.LoaderBuild ?? "");
            foreach (string manifest in candidate.ManifestPaths.Order(StringComparer.Ordinal))
            {
                var inspected = await LauncherMigrationFiles.InspectFileAsync(manifest, Path.GetFileName(manifest), token).ConfigureAwait(false);
                AddRevision(revision, manifest); AddRevision(revision, inspected.Sha256);
            }
            if (unsupported is null)
            {
                try
                {
                    if (!SafeVersion(instanceId) || string.IsNullOrWhiteSpace(candidate.GameVersion))
                        throw new InvalidDataException("实例没有有效的 Minecraft 版本。");
                    if (candidate.NativeVersion is not null)
                        await AddNativeAsync(candidate, files, directories, token).ConfigureAwait(false);
                    await AddGameDataAsync(candidate, instanceId, files, directories, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is InvalidDataException or JsonException)
                { unsupported = error.Message; files.Clear(); directories.Clear(); }
            }
            foreach (var file in files.Values.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
            { AddRevision(revision, file.Source); AddRevision(revision, file.RelativePath); AddRevision(revision, file.Sha256); }
            foreach (string directory in directories.Order(StringComparer.Ordinal)) AddRevision(revision, directory);
            AddRevision(revision, unsupported ?? "");
            long bytes = files.Values.Sum(item => item.Bytes);
            totalFiles = checked(totalFiles + files.Count); totalDirectories = checked(totalDirectories + directories.Count);
            totalBytes = checked(totalBytes + bytes);
            if (totalFiles > 200_000 || totalDirectories > 50_000 || totalBytes > 200L * 1024 * 1024 * 1024)
                throw new InvalidDataException("来源超过 200000 文件、50000 目录或 200 GiB 迁移预算，请分批迁移。");
            var preview = new LauncherMigrationInstance(id, candidate.Name, candidate.GameDirectory, candidate.GameVersion,
                candidate.Loader, candidate.LoaderBuild, bytes, files.Count, unsupported is null, unsupported,
                Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).Take(32).ToArray()));
            instances.Add(new(preview, candidate, files.Values.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray(),
                directories.Order(StringComparer.Ordinal).ToArray(), destinationName, instanceId));
        }
        var previewResult = new LauncherMigrationPreview(query, Convert.ToHexString(revision.GetHashAndReset()),
            Array.AsReadOnly(instances.Select(item => item.Preview).ToArray()),
            ["仅复制游戏数据；账户、令牌、启动命令和旧 Java 配置不会导入。",
                "每个实例迁入独立游戏目录；来源文件保持不变。",
                "显示大小为本地复制量，不含转换实例所需的游戏和加载器下载。"]);
        return new(previewResult, instances);
    }

    private static void AddRevision(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
    }

    private static async Task AddNativeAsync(LauncherMigrationSourceInstance instance,
        Dictionary<string, LauncherMigrationFile> files, HashSet<string> directories, CancellationToken token)
    {
        string current = instance.NativeVersion!;
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            if (!SafeVersion(current) || !visited.Add(current) || visited.Count > 100)
                throw new InvalidDataException("原生版本的继承链无效或存在循环。");
            string? manifest = instance.NativeRoot is { } root
                ? Path.Combine(root, "versions", current, current + ".json")
                : instance.ManifestPaths.FirstOrDefault(path => Path.GetFileName(path) == current + ".json");
            if (manifest is null || !File.Exists(manifest)) throw new InvalidDataException("缺少原生父版本：" + current);
            using var document = await ReadJsonAsync(manifest, token).ConfigureAwait(false);
            if (Text(document.RootElement, "id") != current) throw new InvalidDataException("原生版本标识与目录不一致。");
            RejectAgents(document.RootElement);
            string relative = "versions/" + current + "/" + current;
            files.Add(relative + ".json", await LauncherMigrationFiles.InspectFileAsync(manifest, relative + ".json", token).ConfigureAwait(false));
            string jar = Path.ChangeExtension(manifest, ".jar");
            if (File.Exists(jar)) files.Add(relative + ".jar", await LauncherMigrationFiles.InspectFileAsync(jar, relative + ".jar", token).ConfigureAwait(false));
            if (Text(document.RootElement, "inheritsFrom") is { Length: > 0 } parent) { current = parent; continue; }
            if (Text(document.RootElement, "mainClass") is not { Length: > 0 }) throw new InvalidDataException("原生版本缺少启动入口。");
            if (Text(document.RootElement, "jar") is { Length: > 0 } alias && !alias.Equals(current, StringComparison.OrdinalIgnoreCase))
                await AddNativeJarAliasAsync(alias, Path.GetDirectoryName(manifest)!, instance.NativeRoot, files,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current }, token).ConfigureAwait(false);
            break;
        }
        if (instance.NativeRoot is not { } nativeRoot) return;
        foreach (string name in new[] { "libraries", "assets" })
            await AddDirectoryAsync(Path.Combine(nativeRoot, name), name, files, directories, token).ConfigureAwait(false);
    }

    private static async Task AddNativeJarAliasAsync(string alias, string localDirectory, string? nativeRoot,
        Dictionary<string, LauncherMigrationFile> files, HashSet<string> visited, CancellationToken token)
    {
        if (!SafeVersion(alias) || !visited.Add(alias) || visited.Count > 100)
            throw new InvalidDataException("原生客户端 JAR 别名无效或存在循环。");
        string? aliasDirectory = nativeRoot is null ? null : Path.Combine(nativeRoot, "versions", alias);
        string jar = Path.Combine(localDirectory, alias + ".jar");
        if (!File.Exists(jar) && aliasDirectory is not null) jar = Path.Combine(aliasDirectory, alias + ".jar");
        string relative = "versions/" + alias + "/" + alias;
        bool copiedJar = File.Exists(jar);
        if (copiedJar)
            await AddAliasFileAsync(jar, relative + ".jar").ConfigureAwait(false);
        string manifest = Path.Combine(aliasDirectory ?? localDirectory, alias + ".json");
        if (!File.Exists(manifest))
        {
            if (!copiedJar) throw new InvalidDataException("缺少别名客户端 JAR：" + alias);
            return;
        }
        using var document = await ReadJsonAsync(manifest, token).ConfigureAwait(false);
        if (Text(document.RootElement, "id") != alias) throw new InvalidDataException("客户端别名版本描述无效。");
        RejectAgents(document.RootElement);
        await AddAliasFileAsync(manifest, relative + ".json").ConfigureAwait(false);
        string? next = Text(document.RootElement, "inheritsFrom");
        if (string.IsNullOrWhiteSpace(next)) next = Text(document.RootElement, "jar");
        if (!string.IsNullOrWhiteSpace(next) && !next.Equals(alias, StringComparison.OrdinalIgnoreCase))
            await AddNativeJarAliasAsync(next, Path.GetDirectoryName(manifest)!, nativeRoot, files, visited, token).ConfigureAwait(false);
        else if (!copiedJar) throw new InvalidDataException("缺少别名客户端 JAR：" + alias);

        async Task AddAliasFileAsync(string source, string target)
        {
            var inspected = await LauncherMigrationFiles.InspectFileAsync(source, target, token).ConfigureAwait(false);
            if (files.TryGetValue(target, out var existing))
            {
                if (existing.Sha256 != inspected.Sha256) throw new InvalidDataException("原生客户端别名文件冲突。");
            }
            else files.Add(target, inspected);
        }
    }

    private static void RejectAgents(JsonElement root)
    {
        if (!root.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("jvm", out var jvm)) return;
        void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString()!;
                if (text.Contains("-javaagent", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("-agentpath", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("-agentlib", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("版本描述包含额外 Java agent，请在原启动器中移除后重新迁移。");
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Check(item);
            else if (value.ValueKind == JsonValueKind.Object) foreach (var item in value.EnumerateObject()) Check(item.Value);
        }
        Check(jvm);
    }

    private static async Task AddGameDataAsync(LauncherMigrationSourceInstance instance, string id,
        Dictionary<string, LauncherMigrationFile> files, HashSet<string> directories, CancellationToken token)
    {
        string prefix = "versions/" + id + "/";
        foreach (string name in GameDirectories)
        {
            string source = Path.Combine(instance.GameDirectory, name);
            await AddDirectoryAsync(source, prefix + name, files, directories, token).ConfigureAwait(false);
        }
        foreach (string name in GameFiles)
        {
            string source = Path.Combine(instance.GameDirectory, name);
            if (File.Exists(source)) files.Add(prefix + name, await LauncherMigrationFiles.InspectFileAsync(source, prefix + name, token).ConfigureAwait(false));
        }
    }

    private static async Task AddDirectoryAsync(string source, string relative, Dictionary<string, LauncherMigrationFile> files,
        HashSet<string> directories, CancellationToken token)
    {
        if (!Directory.Exists(source)) return;
        var snapshot = await LauncherMigrationFiles.CaptureAsync(source, token).ConfigureAwait(false);
        if (files.Count + snapshot.Files.Count > 200_000 || directories.Count + snapshot.Directories.Count + 1 > 50_000)
            throw new InvalidDataException("实例文件或目录数量超过迁移预算。");
        directories.Add(relative);
        foreach (string directory in snapshot.Directories) directories.Add(relative + "/" + directory);
        foreach (var file in snapshot.Files)
        {
            string target = relative + "/" + file.RelativePath;
            if (!files.TryAdd(target, file with { RelativePath = target }))
                throw new InvalidDataException("游戏数据存在跨平台文件名冲突。");
        }
    }
}
