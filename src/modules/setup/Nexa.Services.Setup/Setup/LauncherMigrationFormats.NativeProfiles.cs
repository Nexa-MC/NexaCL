using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nexa.Services.Setup;

internal static partial class LauncherMigrationFormats
{
    private const string AmbiguousNativeGameDirectory = "游戏数据同时存在于多个目录，且没有明确隔离设置；请先在原启动器确认当前游戏目录。";

    private static async Task DiscoverNativeProfilesAsync(LauncherMigrationSourceKind kind, string source,
        List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        if (kind is LauncherMigrationSourceKind.Auto or LauncherMigrationSourceKind.Hmcl)
            await DiscoverHmclAsync(source, result, token).ConfigureAwait(false);
        else await DiscoverNativeAsync(source, result, [], token).ConfigureAwait(false);

        foreach (string root in new[] { source, Path.Combine(source, ".minecraft") })
            foreach (string name in new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" })
            {
                if (!Exists(root, name)) continue;
                string manifest = Path.Combine(root, name);
                try
                {
                    using JsonDocument document = await ReadJsonAsync(manifest, token).ConfigureAwait(false);
                    if (!TryObject(document.RootElement, "profiles", out JsonElement profiles))
                    { Add(result, Unsupported(manifest, "Minecraft Launcher", "官方启动器 profiles 结构无效。", manifest)); continue; }
                    if (profiles.EnumerateObject().Take(MaxInstances + 1).Count() > MaxInstances)
                        throw new IOException("迁移实例超过 256 个，请分批迁移。");
                    foreach (JsonProperty profile in profiles.EnumerateObject())
                    {
                        token.ThrowIfCancellationRequested();
                        await AddOfficialProfileAsync(root, manifest, profile, result, token).ConfigureAwait(false);
                    }
                }
                catch (JsonException)
                { Add(result, Unsupported(manifest, "Minecraft Launcher", "官方启动器配置不是有效 JSON。", manifest)); }
            }
    }

    private static async Task AddOfficialProfileAsync(string root, string manifest, JsonProperty profile,
        List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        JsonElement row = profile.Value;
        string identity = manifest + "#profile=" + profile.Name;
        string name = Text(row, "name") is { Length: > 0 } title ? title : profile.Name;
        string version = Text(row, "lastVersionId") ?? "";
        if (!SafeReference(version) || version is "latest-release" or "latest-snapshot")
        {
            Add(result, Unsupported(identity, name, "官方配置未指定已安装的固定 Minecraft 版本；请先在原启动器选择并运行该版本。", manifest));
            return;
        }
        string directory = Path.Combine(root, "versions", version);
        if (!Exists(directory, version + ".json"))
        {
            Add(result, Unsupported(identity, name, "官方配置对应的 Minecraft 版本尚未安装。", manifest));
            return;
        }
        var native = await ReadNativeVersionAsync(root, directory, [manifest], token).ConfigureAwait(false);
        string game = root;
        string? unsupported = native.UnsupportedReason == AmbiguousNativeGameDirectory ? null : native.UnsupportedReason;
        if (row.ValueKind != JsonValueKind.Object) unsupported ??= "官方启动器配置不是有效对象。";
        else if (row.TryGetProperty("gameDir", out JsonElement custom))
        {
            if (custom.ValueKind != JsonValueKind.String) unsupported ??= "官方配置的游戏目录不是字符串。";
            else if (custom.GetString() is { Length: > 0 } path)
                game = ResolveNativeGameDirectory(path, root);
        }
        CheckPath(game);
        if (!Directory.Exists(game)) unsupported ??= "官方配置的游戏目录不存在。";
        // A profile is an instance, even when several profiles share one native version.
        // Keep the version id for copied manifests but give each profile its own identity.
        result.RemoveAll(item => Nexa.Core.PathIdentity.Comparer.Equals(item.Identity, directory));
        Add(result, native with
        {
            Identity = identity,
            Name = name,
            GameDirectory = game,
            UnsupportedReason = unsupported,
        });
    }

    private static async Task<LauncherMigrationSourceInstance> ReadNativeConfiguredVersionAsync(string? root, string directory,
        IReadOnlyList<string> manifests, CancellationToken token)
    {
        var instance = await ReadNativeVersionAsync(root, directory, manifests, token).ConfigureAwait(false);
        // The explicit Nexa metadata contract keeps its established precedence.
        if (Exists(directory, Path.Combine("Nexa", "InstanceMetadata.json"))
            || Exists(directory, Path.Combine("PCL", "InstanceMetadata.json"))) return instance;

        List<string> observed = [.. instance.ManifestPaths];
        try
        {
            string pcl = Path.Combine(directory, "PCL", "Setup.ini");
            if (Exists(directory, Path.Combine("PCL", "Setup.ini")))
            {
                observed.Add(pcl);
                Dictionary<string, string> settings = ReadNativeIni(await ReadBytesAsync(pcl, token).ConfigureAwait(false));
                if (settings.TryGetValue("VersionArgumentIndieV2", out string? v2))
                {
                    if (!bool.TryParse(v2, out bool isolated))
                        return NativeConfigurationResult(instance, observed, null, "PCL 版本隔离设置不是布尔值。");
                    return NativeConfigurationResult(instance, observed, isolated ? directory : root,
                        !isolated && root is null ? "非隔离实例缺少原 Minecraft 根目录。" : null);
                }
                if (settings.TryGetValue("VersionArgumentIndie", out string? v1))
                {
                    if (!int.TryParse(v1, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode) || mode is < -1 or > 2)
                        return NativeConfigurationResult(instance, observed, null, "PCL 旧版隔离设置无效。");
                    if (mode is 1 or 2)
                        return NativeConfigurationResult(instance, observed, mode == 1 ? directory : root,
                            mode == 2 && root is null ? "非隔离实例缺少原 Minecraft 根目录。" : null);
                }
            }
            bool hasLocalPcl = observed.Contains(pcl, Nexa.Core.PathIdentity.Comparer);
            bool hasHmcl = Exists(directory, "hmclversion.cfg") || Exists(directory, "modpack.cfg")
                || Exists(directory, Path.Combine(".hmcl", "config", "instance-game-settings.json"));
            var pclInherited = !hasLocalPcl && hasHmcl ? null : await ReadPclInheritedIsolationAsync(instance, observed, root, directory,
                hasLocalPcl, token).ConfigureAwait(false);
            if (pclInherited is not null) return pclInherited;

            if (Exists(directory, "modpack.cfg"))
            {
                observed.Add(Path.Combine(directory, "modpack.cfg"));
                return NativeConfigurationResult(instance, observed, directory, null);
            }
            string currentRelative = Path.Combine(".hmcl", "config", "instance-game-settings.json");
            if (Exists(directory, currentRelative))
            {
                string current = Path.Combine(directory, currentRelative); observed.Add(current);
                using JsonDocument document = await ReadJsonAsync(current, token).ConfigureAwait(false);
                JsonElement settings = document.RootElement;
                if (!TryArray(settings, "overrideProperties", out JsonElement overrides))
                    return NativeConfigurationResult(instance, observed, null, "HMCL 实例配置缺少 overrideProperties。");
                if (overrides.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "runningDirectory"))
                {
                    if (Text(settings, "runningDirectory") is not { } running)
                        return NativeConfigurationResult(instance, observed, null, "HMCL 实例的运行目录不是字符串。");
                    return NativeConfigurationResult(instance, observed,
                        running.Length == 0 ? directory : ResolveNativeGameDirectory(running, root ?? directory), null);
                }
                return await ApplyHmclCurrentParentAsync(instance, observed, settings, root, manifests, token).ConfigureAwait(false);
            }

            JsonElement? inherited = await FindHmclGlobalSettingsAsync(root, manifests, token).ConfigureAwait(false);
            if (Exists(directory, "hmclversion.cfg"))
            {
                string local = Path.Combine(directory, "hmclversion.cfg"); observed.Add(local);
                using JsonDocument document = await ReadJsonAsync(local, token).ConfigureAwait(false);
                JsonElement settings = document.RootElement;
                if (settings.ValueKind != JsonValueKind.Object)
                    return NativeConfigurationResult(instance, observed, null, "HMCL 版本配置不是有效对象。");
                if (settings.TryGetProperty("usesGlobal", out JsonElement global) && global.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return NativeConfigurationResult(instance, observed, null, "HMCL 全局配置继承标记不是布尔值。");
                if (!IsTrue(settings, "usesGlobal"))
                    return ApplyHmclGameDirectory(instance, observed, settings, root, directory);
                if (inherited is { } parent) return ApplyHmclGameDirectory(instance, observed, parent, root, directory);
                return NativeConfigurationResult(instance, observed, null,
                    "HMCL 实例使用全局配置；请选择包含原 hmcl.json 的启动器目录后重试。");
            }
            if (inherited is { } globalSettings) return ApplyHmclGameDirectory(instance, observed, globalSettings, root, directory);
            if (manifests.Any(path => Path.GetFileName(path) is "game-directories.json" or "user-game-directories.json"))
                return await ApplyHmclCurrentParentAsync(instance, observed, default, root, manifests, token).ConfigureAwait(false);
            return NativeConfigurationResult(instance, observed, null, null);
        }
        catch (JsonException)
        { return NativeConfigurationResult(instance, observed, null, "PCL/HMCL 实例配置不是有效 JSON。"); }
    }

    private static async Task<LauncherMigrationSourceInstance?> ReadPclInheritedIsolationAsync(LauncherMigrationSourceInstance instance,
        List<string> observed, string? root, string directory, bool hasLocalSettings, CancellationToken token)
    {
        string[] roots = root is null ? [] : Path.GetFileName(root) == ".minecraft" && Path.GetDirectoryName(root) is { } parent
            ? [parent, root] : [root];
        Dictionary<string, string>? global = null;
        foreach (string launcher in roots)
        {
            if (!Exists(launcher, Path.Combine("PCL", "Setup.ini"))) continue;
            string path = Path.Combine(launcher, "PCL", "Setup.ini"); observed.Add(path);
            global = ReadNativeIni(await ReadBytesAsync(path, token).ConfigureAwait(false));
            break;
        }
        if (!hasLocalSettings && global is null) return null;
        // This rule precedes global defaults only while PCL initializes an unset local V2 flag.
        // Files are inspected through the same no-links boundary as the copied game data.
        bool occupiedMods = DirectoryExists(directory, "mods") && CheckedFiles(Path.Combine(directory, "mods"), "*").Any();
        bool occupiedSaves = DirectoryExists(directory, "saves") && CheckedDirectories(Path.Combine(directory, "saves")).Any();
        if (occupiedMods || occupiedSaves) return NativeConfigurationResult(instance, observed, directory, null);
        int mode = 4; // Current PCL II and PCL CE initialize all unconfigured instances as isolated.
        if (global is not null && global.TryGetValue("LaunchArgumentIndieV2", out string? value)
            && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out mode) || mode is < 0 or > 4))
            return NativeConfigurationResult(instance, observed, null, "PCL 全局隔离方案无效。");
        if (mode is 0 or 4) return NativeConfigurationResult(instance, observed, mode == 4 ? directory : root,
            mode == 0 && root is null ? "非隔离实例缺少原 Minecraft 根目录。" : null);
        // Conditional schemes also depend on PCL's version classification and user-assigned
        // card category. Keep the existing visible ambiguity when that context is unavailable.
        return NativeConfigurationResult(instance, observed, null,
            "PCL 条件隔离方案依赖原启动器的版本分类；请先在原启动器为该实例明确开启或关闭版本隔离。");
    }

    private static Dictionary<string, string> ReadNativeIni(byte[] bytes)
    {
        string text = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })
            ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string line in text.TrimStart('\uFEFF').Split('\n'))
        {
            string entry = line.TrimEnd('\r');
            if (entry.Length == 0 || entry.TrimStart().StartsWith('#') || entry.TrimStart().StartsWith('[')) continue;
            int separator = entry.IndexOf(':'); // PCL's Setup.ini uses Key:Value, not standard INI syntax.
            if (separator < 1) separator = entry.IndexOf('=');
            if (separator > 0) result[entry[..separator].Trim()] = entry[(separator + 1)..].Trim();
        }
        return result;
    }

    private static async Task<JsonElement?> FindHmclGlobalSettingsAsync(string? root, IReadOnlyList<string> manifests, CancellationToken token)
    {
        if (root is null) return null;
        foreach (string path in manifests)
        {
            if (Path.GetFileName(path) is not ("hmcl.json" or ".hmcl.json")) continue;
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            if (!TryObject(document.RootElement, "configurations", out JsonElement configurations)) continue;
            foreach (JsonProperty configuration in configurations.EnumerateObject())
                if (Text(configuration.Value, "gameDir") is { Length: > 0 } game
                    && Nexa.Core.PathIdentity.Comparer.Equals(ResolveNativeGameDirectory(game, Path.GetDirectoryName(path)!), root)
                    && TryObject(configuration.Value, "global", out JsonElement settings)) return settings.Clone();
        }
        return null;
    }

    private static LauncherMigrationSourceInstance ApplyHmclGameDirectory(LauncherMigrationSourceInstance instance,
        List<string> observed, JsonElement settings, string? root, string directory)
    {
        int mode = 0;
        if (settings.TryGetProperty("gameDirType", out _) && !TryInt(settings, "gameDirType", out mode) || mode is < 0 or > 2)
            return NativeConfigurationResult(instance, observed, null, "HMCL 游戏目录类型无效。");
        if (mode == 1) return NativeConfigurationResult(instance, observed, directory, null);
        if (mode == 0) return NativeConfigurationResult(instance, observed, root,
            root is null ? "非隔离实例缺少原 Minecraft 根目录。" : null);
        if (Text(settings, "gameDir") is not { Length: > 0 } custom)
            return NativeConfigurationResult(instance, observed, null, "HMCL 自定义游戏目录未指定。");
        return NativeConfigurationResult(instance, observed, ResolveNativeGameDirectory(custom, root ?? directory), null);
    }

    private static async Task<LauncherMigrationSourceInstance> ApplyHmclCurrentParentAsync(LauncherMigrationSourceInstance instance,
        List<string> observed, JsonElement settings, string? root, IReadOnlyList<string> manifests, CancellationToken token)
    {
        foreach (string manifest in manifests)
        {
            if (Path.GetFileName(manifest) is not ("game-directories.json" or "user-game-directories.json")) continue;
            string config = Path.GetDirectoryName(manifest)!;
            string presetsPath = Path.Combine(config, "game-settings.json");
            if (!Exists(config, "game-settings.json")) continue;
            observed.Add(presetsPath);
            using JsonDocument presetsDocument = await ReadJsonAsync(presetsPath, token).ConfigureAwait(false);
            if (!TryArray(presetsDocument.RootElement, "presets", out JsonElement presets))
                return NativeConfigurationResult(instance, observed, null, "HMCL 全局游戏配置结构无效。");
            if (presets.GetArrayLength() > MaxInstances) throw new IOException("HMCL 游戏配置超过读取预算。");
            string? parent = Text(settings, "parent"), defaultParent = null;
            if (Exists(config, "launcher-settings.json"))
            {
                string launcher = Path.Combine(config, "launcher-settings.json"); observed.Add(launcher);
                using JsonDocument document = await ReadJsonAsync(launcher, token).ConfigureAwait(false);
                defaultParent = Text(document.RootElement, "defaultGameSettingsPreset");
            }
            JsonElement? selected = null;
            foreach (string? target in new[] { parent, defaultParent })
            {
                if (target is null) continue;
                foreach (JsonElement preset in presets.EnumerateArray())
                    if (Text(preset, "id") == target) { selected = preset; break; }
                if (selected is not null) break;
            }
            if (selected is null && presets.GetArrayLength() > 0) selected = presets[0];
            if (selected is not { } effective)
                return NativeConfigurationResult(instance, observed, root,
                    root is null ? "非隔离实例缺少原 Minecraft 根目录。" : null);
            if (effective.ValueKind != JsonValueKind.Object)
                return NativeConfigurationResult(instance, observed, null, "HMCL 游戏配置预设不是有效对象。");
            if (effective.TryGetProperty("runningDirectory", out JsonElement value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                return NativeConfigurationResult(instance, observed, null, "HMCL 全局运行目录不是字符串。");
            string running = Text(effective, "runningDirectory") ?? "";
            return NativeConfigurationResult(instance, observed,
                running.Length == 0 ? root : ResolveNativeGameDirectory(running, Path.GetDirectoryName(config)!),
                running.Length == 0 && root is null ? "非隔离实例缺少原 Minecraft 根目录。" : null);
        }
        return NativeConfigurationResult(instance, observed, null,
            "HMCL 实例继承全局运行目录；请选择包含原 config 目录的启动器目录后重试。");
    }

    private static string ResolveNativeGameDirectory(string value, string relativeRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new InvalidDataException("实例游戏目录无效。");
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value, relativeRoot));
        CheckPath(path);
        return path;
    }

    private static LauncherMigrationSourceInstance NativeConfigurationResult(LauncherMigrationSourceInstance instance,
        IReadOnlyList<string> observed, string? gameDirectory, string? error)
    {
        string? unsupported = instance.UnsupportedReason;
        if (gameDirectory is not null)
        {
            CheckPath(gameDirectory);
            if (unsupported == AmbiguousNativeGameDirectory) unsupported = null;
            if (!Directory.Exists(gameDirectory)) error ??= "实例配置的游戏目录不存在。";
        }
        return instance with
        {
            GameDirectory = gameDirectory ?? instance.GameDirectory,
            UnsupportedReason = unsupported ?? error,
            ManifestPaths = Array.AsReadOnly(observed.Distinct(Nexa.Core.PathIdentity.Comparer).ToArray()),
        };
    }
}
