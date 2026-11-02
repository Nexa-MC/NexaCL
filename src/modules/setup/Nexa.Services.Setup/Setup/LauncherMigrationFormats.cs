using System.Text;
using System.Text.Json;

namespace Nexa.Services.Setup;

internal sealed record LauncherMigrationSourceInstance(string Identity, string Name, string GameDirectory,
    string? NativeRoot, string? NativeVersion, string GameVersion, string? Loader, string? LoaderBuild,
    string? UnsupportedReason, IReadOnlyList<string> ManifestPaths, IReadOnlyList<string> Warnings)
{
    internal bool SharedDatabaseManifests { get; init; }
}

/// <summary>Reads documented launcher manifests without executing launcher commands or importing credentials.</summary>
internal static partial class LauncherMigrationFormats
{
    private const int MaxInstances = 256;
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private static readonly string[] OmittedSettings = ["账户、登录凭据、启动钩子和启动器专属设置不会导入；Java 与 JVM 参数请在迁移后确认。"];

    public static async Task<IReadOnlyList<LauncherMigrationSourceInstance>> DiscoverAsync(
        LauncherMigrationSourceKind kind, string source, CancellationToken token)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        CheckPath(source);
        if (!Directory.Exists(source)) throw new IOException("迁移来源目录不存在。");
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("未知的启动器类型。");
        token.ThrowIfCancellationRequested();
        if (kind == LauncherMigrationSourceKind.Auto) return await DiscoverAllAsync(source, token).ConfigureAwait(false);
        if (kind == LauncherMigrationSourceKind.Prism && (Path.GetFileName(source) is "minecraft" or ".minecraft")
            && Path.GetDirectoryName(source) is { } parent && Exists(parent, "instance.cfg")) source = parent;
        List<LauncherMigrationSourceInstance> result = [];
        switch (kind)
        {
            case LauncherMigrationSourceKind.Prism:
                foreach (string candidate in CandidateInstances(source, "instance.cfg", "instances"))
                    Add(result, await ReadPrismAsync(candidate, token).ConfigureAwait(false));
                break;
            case LauncherMigrationSourceKind.CurseForge:
                foreach (string candidate in CandidateInstances(source, "minecraftinstance.json", "Instances", "instances"))
                    Add(result, await ReadCurseForgeAsync(candidate, token).ConfigureAwait(false));
                break;
            case LauncherMigrationSourceKind.AtLauncher:
                foreach (string candidate in CandidateInstances(source, "instance.json", "instances", "Instances"))
                    Add(result, await ReadAtLauncherAsync(candidate, token).ConfigureAwait(false));
                break;
            case LauncherMigrationSourceKind.Xmcl:
                await DiscoverXmclAsync(source, result, token).ConfigureAwait(false);
                break;
            case LauncherMigrationSourceKind.Modrinth:
                foreach (string candidate in CandidateInstances(source, "profile.json", "profiles"))
                    Add(result, await ReadModrinthAsync(candidate, token).ConfigureAwait(false));
                if (result.Count == 0 && Exists(source, "app.db"))
                    foreach (var instance in await LauncherMigrationModrinthDatabase.ReadAsync(source, token).ConfigureAwait(false)) Add(result, instance);
                break;
            case LauncherMigrationSourceKind.Hmcl:
                await DiscoverNativeProfilesAsync(kind, source, result, token).ConfigureAwait(false);
                break;
            case LauncherMigrationSourceKind.GdLauncher:
                await DiscoverGdLauncherAsync(source, result, token).ConfigureAwait(false);
                break;
            case LauncherMigrationSourceKind.Ftb:
                await DiscoverFtbAsync(source, result, token).ConfigureAwait(false);
                break;
            case LauncherMigrationSourceKind.Technic:
                await DiscoverTechnicAsync(source, result, token).ConfigureAwait(false);
                break;
            default:
                await DiscoverNativeProfilesAsync(kind, source, result, token).ConfigureAwait(false);
                break;
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static async Task<IReadOnlyList<LauncherMigrationSourceInstance>> DiscoverAllAsync(string source, CancellationToken token)
    {
        List<LauncherMigrationSourceInstance> result = [];
        foreach (var family in new[] { LauncherMigrationSourceKind.Prism, LauncherMigrationSourceKind.CurseForge,
            LauncherMigrationSourceKind.Modrinth })
            foreach (var item in await DiscoverAsync(family, source, token).ConfigureAwait(false)) Add(result, item);
        // instance.json is shared by several unrelated formats. Classify its actual
        // schema before routing so a mixed directory never hides another family.
        foreach (string candidate in CandidateInstances(source, "instance.json", "instances", "Instances", "data/instances"))
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(candidate, "instance.json");
            try
            {
                using var document = await ReadJsonAsync(path, token).ConfigureAwait(false);
                var root = document.RootElement;
                if (TryObject(root, "launcher", out _)) Add(result, await ReadAtLauncherAsync(candidate, token).ConfigureAwait(false));
                else if (TryObject(root, "runtime", out _)) Add(result, await ReadXmclAsync(candidate, token).ConfigureAwait(false));
                else if (!IsGdLauncherManifest(root) && !IsFtbManifest(root))
                    Add(result, Unsupported(candidate, Text(root, "name") ?? Path.GetFileName(candidate), "无法识别 instance.json 的启动器格式，请明确选择来源类型。", path));
            }
            catch (JsonException) { Add(result, Unsupported(candidate, Path.GetFileName(candidate), "实例清单不是有效 JSON。", path)); }
        }
        await DiscoverGdLauncherAsync(source, result, token).ConfigureAwait(false);
        await DiscoverFtbAsync(source, result, token).ConfigureAwait(false);
        await DiscoverTechnicAsync(source, result, token).ConfigureAwait(false);
        if (Exists(source, "instances.json"))
            await DiscoverXmclAsync(source, result, token).ConfigureAwait(false);
        await DiscoverNativeProfilesAsync(LauncherMigrationSourceKind.Auto, source, result, token).ConfigureAwait(false);
        return Array.AsReadOnly(result.ToArray());
    }

    private static async Task<LauncherMigrationSourceInstance> ReadPrismAsync(string directory, CancellationToken token)
    {
        string cfgPath = Path.Combine(directory, "instance.cfg"), packPath = Path.Combine(directory, "mmc-pack.json");
        string game = DirectoryExists(directory, "minecraft") ? Path.Combine(directory, "minecraft") : Path.Combine(directory, ".minecraft");
        string name = Path.GetFileName(directory);
        try
        {
            string text = Encoding.UTF8.GetString(await ReadBytesAsync(cfgPath, token).ConfigureAwait(false));
            Dictionary<string, string> cfg = new(StringComparer.Ordinal);
            foreach (string line in text.Split('\n'))
            {
                string value = line.TrimEnd('\r'); int separator = value.IndexOf('=');
                if (separator > 0 && !value.TrimStart().StartsWith('#') && !value.TrimStart().StartsWith('['))
                    cfg[value[..separator].Trim()] = value[(separator + 1)..];
            }
            if (cfg.TryGetValue("name", out string? title) && ValidName(title)) name = title;
            using JsonDocument document = await ReadJsonAsync(packPath, token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (!TryInt(root, "formatVersion", out int format) || format != 1 || !TryArray(root, "components", out JsonElement components))
                return Unsupported(directory, name, "Prism 组件清单版本或结构不受支持。", cfgPath, packPath);
            string gameVersion = ""; string? loader = null, build = null, unsupported = null;
            foreach (JsonElement component in components.EnumerateArray())
            {
                if (IsTrue(component, "disabled")) continue;
                string uid = Text(component, "uid") ?? "", version = Text(component, "version") ?? "";
                string? mapped = uid switch
                {
                    "net.minecraftforge" => "Forge",
                    "net.neoforged" => "NeoForge",
                    "net.fabricmc.fabric-loader" => "Fabric",
                    "org.quiltmc.quilt-loader" => "Quilt",
                    _ => null
                };
                if (uid == "net.minecraft") gameVersion = version;
                else if (mapped is not null)
                {
                    if (loader is not null) unsupported = "实例含多个模组加载器，不能自动转换。";
                    loader = mapped; build = version;
                }
                else if (uid is not ("org.lwjgl" or "org.lwjgl3" or "net.fabricmc.intermediary"))
                    unsupported = "实例包含未支持的自定义 Prism 组件：" + SafeLabel(uid);
            }
            string patches = Path.Combine(directory, "patches");
            if (DirectoryExists(directory, "patches") && CheckedFiles(patches, "*.json").Any())
                unsupported = "实例包含自定义 Prism 补丁，请先导出兼容整合包。";
            return Converted(directory, name, game, gameVersion, loader, build, unsupported, [cfgPath, packPath]);
        }
        catch (JsonException) { return Unsupported(directory, name, "Prism 组件清单不是有效 JSON。", cfgPath, packPath); }
    }

    private static async Task<LauncherMigrationSourceInstance> ReadCurseForgeAsync(string directory, CancellationToken token)
    {
        string path = Path.Combine(directory, "minecraftinstance.json");
        try
        {
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            string name = Text(root, "name") ?? Path.GetFileName(directory), version = Text(root, "gameVersion") ?? "";
            string? loader = null, build = null, unsupported = null;
            if (TryObject(root, "baseModLoader", out JsonElement modLoader))
            {
                string id = Text(modLoader, "name") ?? "";
                (loader, build) = LoaderId(id, version);
                if (loader is null || id.Length == 0) unsupported = "未支持的 CurseForge 加载器：" + SafeLabel(id);
            }
            else if (root.TryGetProperty("baseModLoader", out JsonElement malformedLoader) && malformedLoader.ValueKind != JsonValueKind.Null)
                unsupported = "CurseForge 加载器配置结构无效。";
            return Converted(directory, name, directory, version, loader, build, unsupported, [path]);
        }
        catch (JsonException) { return Unsupported(directory, Path.GetFileName(directory), "CurseForge 实例清单不是有效 JSON。", path); }
    }

    private static async Task<LauncherMigrationSourceInstance> ReadAtLauncherAsync(string directory, CancellationToken token)
    {
        string path = Path.Combine(directory, "instance.json");
        try
        {
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (!TryObject(root, "launcher", out JsonElement launcher)) return Unsupported(directory, Path.GetFileName(directory), "未找到 ATLauncher launcher 实例配置。", path);
            string name = Text(launcher, "name") ?? Path.GetFileName(directory), version = Text(root, "id") ?? "";
            string? loader = null, build = null, unsupported = null;
            if (TryObject(launcher, "loaderVersion", out JsonElement modLoader))
            {
                string type = Text(modLoader, "type") ?? "";
                loader = KnownLoader(type); build = Text(modLoader, "version");
                if (loader is null && !IsVanilla(type)) unsupported = "未支持的 ATLauncher 加载器：" + SafeLabel(type);
            }
            else if (launcher.TryGetProperty("loaderVersion", out JsonElement malformedLoader) && malformedLoader.ValueKind != JsonValueKind.Null)
                unsupported = "ATLauncher 加载器配置结构无效。";
            if (launcher.TryGetProperty("multiMCManifest", out JsonElement multiMc) && multiMc.ValueKind == JsonValueKind.Object)
                unsupported = "ATLauncher 实例含 MultiMC 自定义清单，请从原始 Prism/MultiMC 实例迁移。";
            return Converted(directory, name, directory, version, loader, build, unsupported, [path]);
        }
        catch (JsonException) { return Unsupported(directory, Path.GetFileName(directory), "ATLauncher 实例清单不是有效 JSON。", path); }
    }

    private static async Task<LauncherMigrationSourceInstance> ReadXmclAsync(string directory, CancellationToken token)
    {
        string path = Path.Combine(directory, "instance.json");
        try
        {
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (TryObject(root, "launcher", out _)) return await ReadAtLauncherAsync(directory, token).ConfigureAwait(false);
            string name = Text(root, "name") ?? Path.GetFileName(directory);
            if (Text(root, "edition") is { } edition && edition != "java") return Unsupported(directory, name, "仅支持迁移 Minecraft Java 版实例。", path);
            if (!TryObject(root, "runtime", out JsonElement runtime)) return Unsupported(directory, name, "XMCL 实例缺少 runtime 版本要求。", path);
            string version = Text(runtime, "minecraft") ?? "", native = Text(root, "version") ?? "";
            List<(string Loader, string Build)> loaders = [];
            foreach ((string key, string loader) in new[] { ("forge", "Forge"), ("neoForged", "NeoForge"), ("fabricLoader", "Fabric"), ("quiltLoader", "Quilt") })
                if (Text(runtime, key) is { Length: > 0 } build) loaders.Add((loader, build));
            string? unsupported = null;
            if (loaders.Count > 1) unsupported = "XMCL 实例含多个加载器，不能自动转换。";
            if (Text(runtime, "optifine") is { Length: > 0 } || Text(runtime, "labyMod") is { Length: > 0 }) unsupported = "XMCL 的 OptiFine/LabyMod 组合需手动安装。";
            if (SafeReference(native) && Exists(directory, Path.Combine("versions", native, native + ".json")))
            {
                var installed = await ReadNativeVersionAsync(directory, Path.Combine(directory, "versions", native), [path], token).ConfigureAwait(false);
                return installed with { Name = name, GameDirectory = directory };
            }
            return Converted(directory, name, directory, version, loaders.Count == 1 ? loaders[0].Loader : null,
                loaders.Count == 1 ? loaders[0].Build : null, unsupported, [path]);
        }
        catch (JsonException) { return Unsupported(directory, Path.GetFileName(directory), "XMCL 实例清单不是有效 JSON。", path); }
    }

    private static async Task<LauncherMigrationSourceInstance> ReadModrinthAsync(string directory, CancellationToken token)
    {
        string path = Path.Combine(directory, "profile.json");
        try
        {
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (!TryObject(root, "metadata", out JsonElement metadata)) return Unsupported(directory, Path.GetFileName(directory), "Modrinth profile.json 缺少受支持的 metadata。", path);
            string name = Text(metadata, "name") ?? Path.GetFileName(directory), version = Text(metadata, "game_version") ?? "";
            string type = Text(metadata, "loader") ?? "vanilla", loaderBuild = "";
            if (TryObject(metadata, "loader_version", out JsonElement loaderVersion)) loaderBuild = Text(loaderVersion, "id") ?? "";
            string? loader = KnownLoader(type), unsupported = null;
            if (loader is null && !IsVanilla(type)) unsupported = "未支持的 Modrinth 加载器：" + SafeLabel(type);
            if (loader is "Forge" or "NeoForge" && loaderBuild.StartsWith(version + "-", StringComparison.Ordinal))
                loaderBuild = loaderBuild[(version.Length + 1)..];
            if (loaderBuild.ToLowerInvariant() is "latest" or "stable" or "recommended" or "nightly")
                unsupported = "Modrinth 加载器需要明确版本，不能迁移动态版本别名。";
            if (Text(root, "install_stage") is { } stage && stage != "installed") unsupported = "Modrinth 实例尚未安装完成。";
            return Converted(directory, name, directory, version, loader, loaderBuild, unsupported, [path]);
        }
        catch (JsonException) { return Unsupported(directory, Path.GetFileName(directory), "Modrinth 实例清单不是有效 JSON。", path); }
    }

    private static async Task DiscoverXmclAsync(string source, List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        HashSet<string> candidates = new(Nexa.Core.PathIdentity.Comparer);
        foreach (string candidate in CandidateInstances(source, "instance.json", "instances")) candidates.Add(candidate);
        string index = Path.Combine(source, "instances.json");
        if (Exists(source, "instances.json"))
        {
            using JsonDocument document = await ReadJsonAsync(index, token).ConfigureAwait(false);
            if (TryArray(document.RootElement, "instances", out JsonElement instances))
            {
                if (instances.GetArrayLength() > MaxInstances) throw new IOException("迁移实例超过 256 个，请分批迁移。");
                foreach (JsonElement row in instances.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.String || row.GetString() is not { Length: > 0 } value) continue;
                    string path = Path.GetFullPath(value, Path.Combine(source, "instances")); CheckPath(path);
                    if (Exists(path, "instance.json")) candidates.Add(path);
                }
            }
        }
        foreach (string candidate in candidates.Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var item = await ReadXmclAsync(candidate, token).ConfigureAwait(false);
            if (File.Exists(index)) item = item with { ManifestPaths = Array.AsReadOnly(item.ManifestPaths.Append(index).ToArray()) };
            Add(result, item);
        }
    }

    private static async Task DiscoverHmclAsync(string source, List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        foreach (string relative in new[] { "hmcl.json", ".hmcl.json", "config/game-directories.json", "config/user-game-directories.json" })
        {
            if (!Exists(source, relative)) continue;
            string path = Path.Combine(source, relative);
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            List<string> roots = [];
            if (TryObject(document.RootElement, "configurations", out JsonElement configurations))
                foreach (JsonProperty entry in configurations.EnumerateObject())
                    if (Text(entry.Value, "gameDir") is { Length: > 0 } game) roots.Add(game);
            if (TryArray(document.RootElement, "directories", out JsonElement directories))
                foreach (JsonElement entry in directories.EnumerateArray())
                    if (Text(entry, "path") is { Length: > 0 } game) roots.Add(game);
            if (roots.Count > MaxInstances) throw new IOException("迁移目录超过读取预算。");
            foreach (string root in roots)
            {
                string resolved = Path.GetFullPath(root, source); CheckPath(resolved);
                if (Directory.Exists(resolved)) await DiscoverNativeAsync(resolved, result, [path], token).ConfigureAwait(false);
            }
        }
        await DiscoverNativeAsync(source, result, [], token).ConfigureAwait(false);
    }

    private static async Task DiscoverNativeAsync(string source, List<LauncherMigrationSourceInstance> result,
        IReadOnlyList<string> manifests, CancellationToken token)
    {
        foreach (string root in new[] { source, Path.Combine(source, ".minecraft") })
        {
            if (!DirectoryExists(root, "versions")) continue;
            foreach (string version in CheckedDirectories(Path.Combine(root, "versions")))
            {
                token.ThrowIfCancellationRequested();
                if (!SafeReference(Path.GetFileName(version)) || !Exists(version, Path.GetFileName(version) + ".json")) continue;
                Add(result, await ReadNativeConfiguredVersionAsync(root, version, manifests, token).ConfigureAwait(false));
            }
        }
        string? parent = Path.GetDirectoryName(source);
        if (Exists(source, Path.GetFileName(source) + ".json"))
        {
            string? nativeRoot = parent is not null && Path.GetFileName(parent) == "versions" ? Path.GetDirectoryName(parent) : null;
            if (nativeRoot is null)
            {
                // A launcher directory named "hmcl" also contains hmcl.json.
                // Outside versions/, require an actual matching native identity.
                try
                {
                    using var document = await ReadJsonAsync(Path.Combine(source, Path.GetFileName(source) + ".json"), token).ConfigureAwait(false);
                    if (Text(document.RootElement, "id") != Path.GetFileName(source)) return;
                }
                catch (JsonException) { return; }
            }
            Add(result, await ReadNativeConfiguredVersionAsync(nativeRoot, source, manifests, token).ConfigureAwait(false));
        }
    }

    private static async Task<LauncherMigrationSourceInstance> ReadNativeVersionAsync(string? root, string directory,
        IReadOnlyList<string> manifests, CancellationToken token)
    {
        string id = Path.GetFileName(directory), path = Path.Combine(directory, id + ".json");
        List<string> observed = [.. manifests, path];
        try
        {
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            JsonElement version = document.RootElement;
            string? unsupported = SafeReference(Text(version, "id")) ? null : "Minecraft 版本清单缺少合法 id。";
            string gameVersion = Text(version, "inheritsFrom") ?? Text(version, "id") ?? id;
            string gameDirectory = directory;
            bool? isolated = null;
            foreach (string metadataDirectory in new[] { "Nexa", "PCL" })
            {
                string relative = Path.Combine(metadataDirectory, "InstanceMetadata.json");
                if (!Exists(directory, relative)) continue;
                string metadataPath = Path.Combine(directory, relative); observed.Add(metadataPath);
                using JsonDocument metadataDocument = await ReadJsonAsync(metadataPath, token).ConfigureAwait(false);
                JsonElement metadata = metadataDocument.RootElement;
                if (metadata.ValueKind != JsonValueKind.Object) unsupported ??= "实例隔离配置不是有效对象。";
                else if (metadata.TryGetProperty("instanceIsolation", out JsonElement value)
                    || metadata.TryGetProperty("InstanceIsolation", out value))
                {
                    if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) isolated = value.GetBoolean();
                    else unsupported ??= "实例隔离配置不是布尔值。";
                }
                break; // Match the native metadata store's Nexa-before-PCL precedence.
            }
            if (isolated == false)
            {
                if (root is null) unsupported ??= "非隔离实例缺少原 Minecraft 根目录。";
                else gameDirectory = root;
            }
            else if (isolated is null)
            {
                string nested = Path.Combine(directory, ".minecraft");
                bool leafData = HasGameData(directory), nestedData = DirectoryExists(directory, ".minecraft") && HasGameData(nested);
                bool rootData = root is not null && HasGameData(root);
                if (leafData && nestedData || rootData && (leafData || nestedData))
                    unsupported ??= "游戏数据同时存在于多个目录，且没有明确隔离设置；请先在原启动器确认当前游戏目录。";
                else if (rootData) gameDirectory = root!;
                else if (nestedData || !leafData && DirectoryExists(directory, ".minecraft")) gameDirectory = nested;
            }
            return new(directory, id, gameDirectory, root, id, gameVersion, null, null, unsupported,
                Array.AsReadOnly(observed.Distinct(Nexa.Core.PathIdentity.Comparer).ToArray()), OmittedSettings);
        }
        catch (JsonException)
        {
            return Unsupported(directory, id, "Minecraft 版本清单或实例隔离配置不是有效 JSON。", observed.ToArray());
        }
    }

    private static bool HasGameData(string directory)
    {
        foreach (string name in new[] { "saves", "mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks",
            "screenshots", "schematics", "journeymap", "kubejs", "scripts", "patchouli_books", "backups",
            "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat", "servers.dat_old", "hotbar.nbt", "icon.png" })
        {
            string path = Path.Combine(directory, name); CheckPath(path);
            if (File.Exists(path) || Directory.Exists(path)) return true;
        }
        return false;
    }

    private static LauncherMigrationSourceInstance Converted(string identity, string name, string game, string version,
        string? loader, string? build, string? unsupported, IReadOnlyList<string> manifests)
    {
        CheckPath(game);
        if (!Directory.Exists(game)) unsupported ??= "实例游戏目录不存在。";
        if (!SafeReference(version)) unsupported ??= "实例缺少合法的 Minecraft 版本。";
        if (loader is not null && !SafeReference(build)) unsupported ??= "实例缺少明确的加载器版本。";
        if (!ValidName(name)) name = Path.GetFileName(identity);
        return new(identity, name, game, null, null, version, loader, loader is null ? null : build, unsupported,
            Array.AsReadOnly(manifests.ToArray()), OmittedSettings);
    }

    private static LauncherMigrationSourceInstance Unsupported(string source, string name, string reason, params string[] manifests) =>
        new(source, name, source, null, null, "", null, null, reason, Array.AsReadOnly(manifests), OmittedSettings);

    private static void Add(List<LauncherMigrationSourceInstance> result, LauncherMigrationSourceInstance item)
    {
        if (result.Any(existing => Nexa.Core.PathIdentity.Comparer.Equals(existing.Identity, item.Identity))) return;
        if (result.Count >= MaxInstances) throw new IOException("迁移实例超过 256 个，请分批迁移。");
        result.Add(item);
    }

    private static IEnumerable<string> CandidateInstances(string source, string marker, params string[] containers)
    {
        if (Exists(source, marker)) { yield return source; yield break; }
        HashSet<string> seen = new(Nexa.Core.PathIdentity.Comparer);
        foreach (string container in containers.Select(name => ExistingContainer(source, name)).OfType<string>().Prepend(source).Distinct(Nexa.Core.PathIdentity.Comparer))
        {
            CheckPath(container); if (!Directory.Exists(container)) continue;
            foreach (string directory in CheckedDirectories(container))
                if (Exists(directory, marker) && seen.Add(directory)) yield return directory;
        }
    }

    private static string? ExistingContainer(string source, string relative)
    {
        string current = source;
        foreach (string part in relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string requested = Path.Combine(current, part);
            CheckPath(requested);
            if (!Directory.Exists(requested)) return null;
            string[] directories = CheckedDirectories(current).ToArray();
            string? actual = directories.FirstOrDefault(path => Path.GetFileName(path).Equals(part, StringComparison.Ordinal))
                ?? directories.FirstOrDefault(path => Path.GetFileName(path).Equals(part, StringComparison.OrdinalIgnoreCase));
            if (actual is null) return null;
            current = actual;
        }
        return current;
    }

    private static IEnumerable<string> CheckedDirectories(string root)
    {
        CheckPath(root);
        string[] directories = Directory.EnumerateDirectories(root).Take(4097).ToArray();
        if (directories.Length > 4096) throw new IOException("来源目录条目超过读取预算。");
        foreach (string directory in directories.Order(StringComparer.Ordinal)) { CheckPath(directory); yield return directory; }
    }

    private static IEnumerable<string> CheckedFiles(string root, string pattern)
    {
        CheckPath(root);
        foreach (string path in Directory.EnumerateFiles(root, pattern)) { CheckPath(path); yield return path; }
    }

    private static bool Exists(string source, string relative)
    {
        string path = Path.Combine(source, relative); CheckPath(path); return File.Exists(path);
    }

    private static bool DirectoryExists(string source, string relative)
    {
        string path = Path.Combine(source, relative); CheckPath(path); return Directory.Exists(path);
    }

    private static void CheckPath(string path) => ContentBackupService.CheckLinks(path);

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken token) =>
        JsonDocument.Parse(await ReadBytesAsync(path, token).ConfigureAwait(false), new JsonDocumentOptions { MaxDepth = 32 });

    private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken token)
    {
        CheckPath(path);
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        if (input.Length > MaxManifestBytes) throw new IOException("迁移清单超过 4 MiB 读取预算。");
        byte[] bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (input.ReadByte() != -1) throw new IOException("来源清单在读取期间发生变化。");
        return bytes;
    }

    private static bool TryObject(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryArray(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out value);
    }

    private static bool IsTrue(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? KnownLoader(string value) => value.ToLowerInvariant() switch
    {
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "cleanroom" => "Cleanroom",
        _ => null
    };

    private static bool IsVanilla(string value) => value.Equals("vanilla", StringComparison.OrdinalIgnoreCase) || value.Length == 0;

    private static (string? Loader, string? Build) LoaderId(string id, string gameVersion)
    {
        int separator = id.IndexOf('-'); if (separator < 1) return (null, null);
        string? loader = KnownLoader(id[..separator]); string build = id[(separator + 1)..];
        if (loader is "Fabric" or "Quilt" && build.EndsWith("-" + gameVersion, StringComparison.Ordinal)) build = build[..^(gameVersion.Length + 1)];
        return (loader, build);
    }

    private static bool SafeReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 180
        && value is not "." and not ".." && !Path.IsPathRooted(value) && value.IndexOfAny(['/', '\\', ':']) < 0
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Any(char.IsControl);

    private static bool ValidName(string value) => value.Length is > 0 and <= 180 && !value.Any(char.IsControl);
    private static string SafeLabel(string value) => new(value.Where(c => !char.IsControl(c)).Take(80).ToArray());
}
