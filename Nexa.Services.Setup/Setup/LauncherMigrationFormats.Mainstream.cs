using System.Text;
using System.Text.Json;

namespace Nexa.Services.Setup;

internal static partial class LauncherMigrationFormats
{
    private static bool IsGdLauncherManifest(JsonElement root) => root.ValueKind == JsonValueKind.Object
        && (root.TryGetProperty("game_configuration", out _) || root.TryGetProperty("_version", out _)
            || TryObject(root, "loader", out _) || TryArray(root, "modloader", out _));

    private static bool IsFtbManifest(JsonElement root) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("mcVersion", out _) && (root.TryGetProperty("installComplete", out _) || root.TryGetProperty("uuid", out _));

    private static async Task DiscoverGdLauncherAsync(string source, List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        if (Path.GetFileName(source) == "instance" && Path.GetDirectoryName(source) is { } parent && Exists(parent, "instance.json")) source = parent;
        List<string> roots = [source]; List<string> redirects = [];
        foreach (string marker in new[] { "runtime_path_override", "override.data" })
        {
            if (!Exists(source, marker)) continue;
            string path = Path.Combine(source, marker); redirects.Add(path);
            string target = Encoding.UTF8.GetString(await ReadBytesAsync(path, token).ConfigureAwait(false)).Trim();
            if (target.Length == 0 || target.Any(char.IsControl)) throw new InvalidDataException("GDLauncher 自定义目录标记无效。");
            string resolved = Path.GetFullPath(target, source); CheckPath(resolved);
            if (Directory.Exists(resolved)) roots.Add(resolved);
        }
        if (DirectoryExists(source, "data")) roots.Add(Path.Combine(source, "data"));
        HashSet<string> seen = new(Nexa.Core.PathIdentity.Comparer);
        foreach (string root in roots.Distinct(Nexa.Core.PathIdentity.Comparer))
            foreach (string marker in new[] { "instance.json", "config.json" })
                foreach (string candidate in MainstreamCandidates(root, marker, "instances"))
                {
                    token.ThrowIfCancellationRequested();
                    string path = Path.Combine(candidate, marker);
                    try
                    {
                        using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
                        if (!IsGdLauncherManifest(document.RootElement) || !seen.Add(candidate)) continue;
                        var item = ReadGdLauncher(candidate, path, document.RootElement);
                        Add(result, item with { ManifestPaths = Array.AsReadOnly(item.ManifestPaths.Concat(redirects).Distinct(Nexa.Core.PathIdentity.Comparer).ToArray()) });
                    }
                    catch (JsonException)
                    {
                        if (marker == "instance.json" && DirectoryExists(candidate, "instance") && seen.Add(candidate))
                            Add(result, Unsupported(candidate, Path.GetFileName(candidate), "GDLauncher 实例清单不是有效 JSON。", [path, .. redirects]));
                    }
                }
    }

    private static LauncherMigrationSourceInstance ReadGdLauncher(string directory, string path, JsonElement root)
    {
        bool carbon = root.TryGetProperty("game_configuration", out _) || root.TryGetProperty("_version", out _);
        string name = carbon ? Text(root, "name") ?? Path.GetFileName(directory) : Path.GetFileName(directory);
        string game = carbon ? Path.Combine(directory, "instance") : directory;
        string version = ""; string? loader = null, build = null, unsupported = null;
        if (carbon)
        {
            if (Text(root, "_version") != "1" || !TryObject(root, "game_configuration", out JsonElement gameConfig)
                || !TryObject(gameConfig, "version", out JsonElement gameVersion))
                return Unsupported(directory, name, "GDLauncher Carbon 的清单版本或自定义游戏版本不受支持。", path);
            version = Text(gameVersion, "release") ?? "";
            if (gameVersion.TryGetProperty("modloaders", out JsonElement rawLoaders))
            {
                if (rawLoaders.ValueKind != JsonValueKind.Array || rawLoaders.GetArrayLength() > 1)
                    unsupported = "GDLauncher 加载器配置无效或含多个加载器。";
                else foreach (JsonElement entry in rawLoaders.EnumerateArray())
                {
                    string type = Text(entry, "type") ?? ""; loader = KnownLoader(type); build = Text(entry, "version");
                    if (loader is null) unsupported = "未支持的 GDLauncher 加载器：" + SafeLabel(type);
                }
            }
        }
        else
        {
            string type;
            if (TryObject(root, "loader", out JsonElement config))
            {
                type = Text(config, "loaderType") ?? ""; version = Text(config, "mcVersion") ?? ""; build = Text(config, "loaderVersion");
            }
            else if (TryArray(root, "modloader", out JsonElement legacy) && legacy.GetArrayLength() >= 2)
            {
                type = ArrayText(legacy, 0) ?? ""; version = ArrayText(legacy, 1) ?? ""; build = legacy.GetArrayLength() > 2 ? ArrayText(legacy, 2) : null;
            }
            else return Unsupported(directory, name, "GDLauncher 旧版实例缺少 loader 配置。", path);
            loader = KnownLoader(type);
            if (loader is null && !IsVanilla(type)) unsupported = "未支持的 GDLauncher 加载器：" + SafeLabel(type);
        }
        if (loader is "Forge" or "NeoForge" && build is not null && build.StartsWith(version + "-", StringComparison.Ordinal))
            build = build[(version.Length + 1)..];
        if (!IsExplicitMainstreamVersion(version) || loader is not null && !IsExplicitMainstreamVersion(build))
            unsupported ??= "GDLauncher 需要明确的游戏和加载器版本，不能使用动态版本别名。";
        return Converted(directory, name, game, version, loader, build, unsupported, [path]);
    }

    private static async Task DiscoverFtbAsync(string source, List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        List<string> roots = [source]; List<string> settings = [];
        foreach (string relative in new[] { "storage/settings.json", "bin/settings.json" })
        {
            if (!Exists(source, relative)) continue;
            string path = Path.Combine(source, relative); settings.Add(path);
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            if (Text(document.RootElement, "instanceLocation") is not { Length: > 0 } location) continue;
            string resolved = Path.GetFullPath(location, source); CheckPath(resolved);
            if (Nexa.Core.PathIdentity.Comparer.Equals(resolved, source)) resolved = Path.Combine(source, "instances");
            if (Directory.Exists(resolved)) roots.Add(resolved);
            break; // The current storage settings supersede the legacy settings file.
        }
        HashSet<string> seen = new(Nexa.Core.PathIdentity.Comparer);
        foreach (string root in roots.Distinct(Nexa.Core.PathIdentity.Comparer))
            foreach (string candidate in CandidateInstances(root, "instance.json", "instances"))
            {
                token.ThrowIfCancellationRequested();
                if (!seen.Add(candidate)) continue;
                string path = Path.Combine(candidate, "instance.json");
                try
                {
                    using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
                    if (!IsFtbManifest(document.RootElement)) continue;
                    Add(result, await ReadFtbAsync(candidate, path, document.RootElement, settings, token).ConfigureAwait(false));
                }
                catch (JsonException)
                {
                    if (DirectoryExists(candidate, ".ftbapp")) Add(result, Unsupported(candidate, Path.GetFileName(candidate), "FTB 实例清单不是有效 JSON。", [path, .. settings]));
                }
            }
    }

    private static async Task<LauncherMigrationSourceInstance> ReadFtbAsync(string directory, string path, JsonElement root,
        IReadOnlyList<string> settings, CancellationToken token)
    {
        List<string> manifests = [path, .. settings];
        string name = Text(root, "name") ?? Path.GetFileName(directory), version = Text(root, "mcVersion") ?? "";
        string? loader = null, build = null, unsupported = IsTrue(root, "installComplete") ? null : "FTB 实例尚未安装完成。";
        string metadata = DirectoryExists(directory, ".ftbapp") ? Path.Combine(directory, ".ftbapp") : directory;
        string versionPath = Path.Combine(metadata, "version.json");
        if (!Exists(metadata, "version.json")) return Unsupported(directory, name, "FTB 缺少已安装的版本清单，不能推测加载器版本。", manifests.ToArray());
        manifests.Add(versionPath);
        try
        {
            using JsonDocument document = await ReadJsonAsync(versionPath, token).ConfigureAwait(false);
            if (!TryArray(document.RootElement, "targets", out JsonElement targets) || targets.GetArrayLength() > 64)
                return Unsupported(directory, name, "FTB 版本清单缺少受支持的 targets。", manifests.ToArray());
            JsonElement? effectiveLoader = null; int games = 0, loaders = 0;
            foreach (JsonElement target in targets.EnumerateArray())
            {
                switch (Text(target, "type"))
                {
                    case "game":
                        games++; string actual = Text(target, "version") ?? "";
                        if (version.Length > 0 && version != actual) unsupported ??= "FTB 实例与版本清单的 Minecraft 版本不一致。";
                        version = actual; break;
                    case "modloader": loaders++; effectiveLoader = target; break;
                    case "runtime": break;
                    default: unsupported ??= "FTB 版本含未支持的自定义运行组件。"; break;
                }
            }
            if (games != 1 || loaders > 1) unsupported ??= "FTB 版本的游戏或加载器组件数量无效。";
            if (Exists(metadata, "modifications.json"))
            {
                string modifications = Path.Combine(metadata, "modifications.json"); manifests.Add(modifications);
                using JsonDocument changes = await ReadJsonAsync(modifications, token).ConfigureAwait(false);
                if (changes.RootElement.ValueKind != JsonValueKind.Object) unsupported ??= "FTB 修改清单结构无效。";
                else if (changes.RootElement.TryGetProperty("modLoaderOverride", out JsonElement overridden) && overridden.ValueKind != JsonValueKind.Null)
                {
                    if (overridden.ValueKind != JsonValueKind.Object || Text(overridden, "type") != "modloader") unsupported ??= "FTB 自定义加载器配置结构无效。";
                    else effectiveLoader = overridden.Clone();
                }
            }
            if (effectiveLoader is { } selected)
            {
                string type = Text(selected, "name") ?? ""; loader = KnownLoader(type); build = Text(selected, "version");
                if (loader is null) unsupported ??= "未支持的 FTB 加载器：" + SafeLabel(type);
            }
            if (loader is "Forge" or "NeoForge" && build is not null && build.StartsWith(version + "-", StringComparison.Ordinal))
                build = build[(version.Length + 1)..];
            if (!IsExplicitMainstreamVersion(version) || loader is not null && !IsExplicitMainstreamVersion(build)) unsupported ??= "FTB 缺少明确的已安装版本。";
            return Converted(directory, name, directory, version, loader, build, unsupported, manifests);
        }
        catch (JsonException) { return Unsupported(directory, name, "FTB 版本或修改清单不是有效 JSON。", manifests.ToArray()); }
    }

    private static string? ArrayText(JsonElement array, int index) => array[index].ValueKind == JsonValueKind.String ? array[index].GetString() : null;

    private static IEnumerable<string> MainstreamCandidates(string source, string marker, params string[] containers)
    {
        if (Exists(source, marker)) yield return source;
        HashSet<string> seen = new(Nexa.Core.PathIdentity.Comparer) { source };
        foreach (string container in containers.Select(name => Path.Combine(source, name)).Prepend(source))
        {
            CheckPath(container); if (!Directory.Exists(container)) continue;
            foreach (string directory in CheckedDirectories(container))
                if (Exists(directory, marker) && seen.Add(directory)) yield return directory;
        }
    }

    private static bool IsExplicitMainstreamVersion(string? value) => SafeReference(value) && value!.ToLowerInvariant()
        is not ("latest" or "stable" or "recommended" or "nightly" or "release" or "snapshot" or "latest-release" or "latest-snapshot");
}
