using System.IO.Compression;
using System.Text.Json;

namespace Nexa.Services.Setup;

internal static partial class LauncherMigrationFormats
{
    private static async Task DiscoverTechnicAsync(string source, List<LauncherMigrationSourceInstance> result, CancellationToken token)
    {
        List<string> roots = [source]; List<string> settings = [];
        if (Exists(source, "settings.json"))
        {
            string path = Path.Combine(source, "settings.json");
            using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
            if (Text(document.RootElement, "directory") is { Length: > 0 } directory)
            {
                settings.Add(path);
                if (!directory.Equals("portable", StringComparison.OrdinalIgnoreCase))
                {
                    string resolved = Path.GetFullPath(directory, source); CheckPath(resolved);
                    if (Directory.Exists(resolved)) roots.Add(resolved);
                }
            }
        }
        HashSet<string> seen = new(Nexa.Core.PathIdentity.Comparer);
        foreach (string root in roots.Distinct(Nexa.Core.PathIdentity.Comparer))
        {
            if (Exists(root, "installedPacks"))
            {
                string path = Path.Combine(root, "installedPacks");
                using JsonDocument document = await ReadJsonAsync(path, token).ConfigureAwait(false);
                if (!TryObject(document.RootElement, "installedPacks", out JsonElement installed)) throw new InvalidDataException("Technic installedPacks 结构无效。");
                int count = 0;
                foreach (JsonProperty pack in installed.EnumerateObject())
                {
                    token.ThrowIfCancellationRequested(); if (++count > MaxInstances) throw new IOException("迁移实例超过 256 个，请分批迁移。");
                    if (pack.Name == "addpack") continue;
                    string name = Text(pack.Value, "name") ?? pack.Name;
                    string? directory = Text(pack.Value, "directory");
                    if (directory is null) continue; // Technic also stores bookmarks that have never been installed.
                    string resolved = ResolveTechnicDirectory(root, directory);
                    if (!seen.Add(resolved)) continue;
                    Add(result, await ReadTechnicAsync(resolved, name, [path, .. settings], token).ConfigureAwait(false));
                }
            }
            foreach (string container in new[] { root, Path.Combine(root, "modpacks") }.Distinct(Nexa.Core.PathIdentity.Comparer))
            {
                CheckPath(container); if (!Directory.Exists(container)) continue;
                if (Exists(container, "bin/version") && seen.Add(container))
                    Add(result, await ReadTechnicAsync(container, Path.GetFileName(container), settings, token).ConfigureAwait(false));
                else foreach (string directory in CheckedDirectories(container))
                    if (Exists(directory, "bin/version") && seen.Add(directory))
                        Add(result, await ReadTechnicAsync(directory, Path.GetFileName(directory), settings, token).ConfigureAwait(false));
            }
        }
    }

    private static string ResolveTechnicDirectory(string root, string value)
    {
        string normalized = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string modpacksPrefix = "%MODPACKS%" + Path.DirectorySeparatorChar, launcherPrefix = "launcher" + Path.DirectorySeparatorChar;
        string baseDirectory = root, relative = normalized;
        if (normalized.StartsWith(modpacksPrefix, StringComparison.Ordinal)) { baseDirectory = Path.Combine(root, "modpacks"); relative = normalized[modpacksPrefix.Length..]; }
        else if (normalized.StartsWith(launcherPrefix, StringComparison.Ordinal)) relative = normalized[launcherPrefix.Length..];
        if (relative.Length == 0 || relative.Any(char.IsControl)) throw new InvalidDataException("Technic 实例路径无效。");
        string resolved = Path.GetFullPath(relative, baseDirectory); CheckPath(resolved); return resolved;
    }

    private static async Task<LauncherMigrationSourceInstance> ReadTechnicAsync(string directory, string name,
        IReadOnlyList<string> settings, CancellationToken token)
    {
        List<string> manifests = [.. settings]; string installedPath = Path.Combine(directory, "bin", "version");
        if (!Exists(directory, "bin/version")) return Unsupported(directory, name, "Technic 实例尚未安装完成。", manifests.ToArray());
        manifests.Add(installedPath);
        try
        {
            using JsonDocument installed = await ReadJsonAsync(installedPath, token).ConfigureAwait(false);
            if (Text(installed.RootElement, "version") is not { Length: > 0 } || IsTrue(installed.RootElement, "legacy"))
                return Unsupported(directory, name, "Technic 旧版 JAR 注入实例需要先导出兼容整合包。", manifests.ToArray());
            string modpack = Path.Combine(directory, "bin", "modpack.jar");
            string versionPath = Path.Combine(directory, "bin", "version.json");
            JsonDocument? packaged = null;
            try
            {
                if (Exists(directory, "bin/modpack.jar"))
                {
                    manifests.Add(modpack);
                    packaged = await ReadTechnicArchiveVersionAsync(modpack, requireInstaller: true, token).ConfigureAwait(false);
                    if (packaged is null) return Unsupported(directory, name, "Technic 的 modpack.jar 含自定义 JAR 注入，不能自动转换。", manifests.ToArray());
                }
                if (Exists(directory, "bin/version.json"))
                {
                    manifests.Add(versionPath);
                    using JsonDocument document = await ReadJsonAsync(versionPath, token).ConfigureAwait(false);
                    var item = ReadTechnicVersion(directory, name, document.RootElement, manifests);
                    if (packaged is not null)
                    {
                        var archiveItem = ReadTechnicVersion(directory, name, packaged.RootElement, manifests);
                        if (archiveItem.UnsupportedReason is not null || archiveItem.GameVersion != item.GameVersion
                            || archiveItem.Loader != item.Loader || archiveItem.LoaderBuild != item.LoaderBuild)
                            return item with { UnsupportedReason = "Technic 已安装清单与安装器的版本不一致。" };
                    }
                    return item;
                }
                if (packaged is not null) return ReadTechnicVersion(directory, name, packaged.RootElement, manifests);
                string client = Path.Combine(directory, "bin", "minecraft.jar");
                if (Exists(directory, "bin/minecraft.jar"))
                {
                    manifests.Add(client); packaged = await ReadTechnicArchiveVersionAsync(client, requireInstaller: false, token).ConfigureAwait(false);
                }
                if (packaged is not null)
                {
                    string version = Text(packaged.RootElement, "id") ?? "";
                    return Converted(directory, name, directory, version, null, null,
                        IsExplicitMainstreamVersion(version) ? null : "Technic 客户端缺少明确的已安装 Minecraft 版本。", manifests);
                }
                return Unsupported(directory, name, "Technic 缺少明确的已安装 Minecraft 版本，不能将整合包版本当作游戏版本。", manifests.ToArray());
            }
            finally { packaged?.Dispose(); }
        }
        catch (JsonException) { return Unsupported(directory, name, "Technic 已安装版本清单不是有效 JSON。", manifests.ToArray()); }
        catch (InvalidDataException) { return Unsupported(directory, name, "Technic 安装器或客户端归档无效。", manifests.ToArray()); }
    }

    private static LauncherMigrationSourceInstance ReadTechnicVersion(string directory, string name, JsonElement root, IReadOnlyList<string> manifests)
    {
        if (root.ValueKind != JsonValueKind.Object) return Unsupported(directory, name, "Technic 游戏版本清单不是有效对象。", manifests.ToArray());
        string version = Text(root, "inheritsFrom") ?? Text(root, "id") ?? "", mainClass = Text(root, "mainClass") ?? "";
        string? loader = null, build = null, unsupported = null;
        if (TryArray(root, "libraries", out JsonElement libraries))
        {
            if (libraries.GetArrayLength() > 1024) unsupported = "Technic 版本的依赖数量超过读取预算。";
            else foreach (JsonElement library in libraries.EnumerateArray())
            {
                string coordinate = Text(library, "name") ?? ""; string[] parts = coordinate.Split(':');
                if (parts.Length < 3) { unsupported ??= "Technic 依赖坐标无效。"; continue; }
                if (!IsKnownTechnicLibraryGroup(parts[0])) unsupported ??= "Technic 版本含未支持的自定义运行依赖。";
                string? type = (parts[0], parts[1]) switch
                {
                    ("net.minecraftforge", "forge" or "minecraftforge") => "Forge",
                    ("net.neoforged", "neoforge") => "NeoForge",
                    ("net.fabricmc", "fabric-loader") => "Fabric",
                    ("org.quiltmc", "quilt-loader") => "Quilt",
                    _ => null
                };
                if (type is null) continue;
                string actual = parts[2];
                if (type is "Forge" or "NeoForge" && actual.StartsWith(version + "-", StringComparison.Ordinal)) actual = actual[(version.Length + 1)..];
                if (loader is not null && (loader != type || build != actual)) unsupported ??= "Technic 版本含多个加载器。";
                loader = type; build = actual;
            }
        }
        bool knownMain = loader switch
        {
            null => mainClass == "net.minecraft.client.main.Main",
            "Fabric" => mainClass is "net.fabricmc.loader.impl.launch.knot.KnotClient" or "net.fabricmc.loader.launch.knot.KnotClient",
            "Quilt" => mainClass == "org.quiltmc.loader.impl.launch.knot.KnotClient",
            "Forge" or "NeoForge" => mainClass is "cpw.mods.bootstraplauncher.BootstrapLauncher" or "cpw.mods.modlauncher.Launcher" or "net.neoforged.bootstraplauncher.BootstrapLauncher",
            _ => false
        };
        if (!knownMain) unsupported ??= "Technic 使用未支持的启动入口或旧版 JAR 注入。";
        if (root.TryGetProperty("jar", out _) || root.TryGetProperty("tweakers", out _)) unsupported ??= "Technic 版本含自定义客户端或启动补丁。";
        if (!IsExplicitMainstreamVersion(version) || loader is not null && !IsExplicitMainstreamVersion(build)) unsupported ??= "Technic 缺少明确的游戏或加载器版本。";
        return Converted(directory, name, directory, version, loader, build, unsupported, manifests);
    }

    private static bool IsKnownTechnicLibraryGroup(string group) => group is "com.mojang" or "net.minecraft" or "net.minecraftforge" or "net.neoforged"
        or "net.fabricmc" or "org.quiltmc" or "cpw.mods" or "org.ow2.asm" or "net.sf.jopt-simple" or "com.electronwill.night-config"
        or "org.apache.logging.log4j" or "org.apache.commons" or "commons-io" or "commons-codec" or "commons-logging"
        or "org.lwjgl" or "org.lwjgl.lwjgl" or "net.java.jinput" or "net.java.jutils" or "net.java.dev.jna" or "org.joml"
        or "com.google.guava" or "com.google.code.gson" or "com.google.code.findbugs" or "com.google.errorprone" or "com.google.j2objc"
        or "com.ibm.icu" or "com.github.oshi" or "oshi-project" or "io.netty" or "org.slf4j"
        or "org.jetbrains" or "org.checkerframework" or "net.jodah" or "it.unimi.dsi" or "com.github.stephenc.jcip"
        or "org.apache.httpcomponents" or "org.lz4" or "com.github.jponge" or "org.tukaani" or "de.oceanlabs.mcp";

    private static async Task<JsonDocument?> ReadTechnicArchiveVersionAsync(string path, bool requireInstaller, CancellationToken token)
    {
        CheckPath(path);
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        if (input.Length > 256L * 1024 * 1024) throw new IOException("Technic 归档超过 256 MiB 读取预算。");
        using ZipArchive archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 32_768) throw new IOException("Technic 归档条目超过读取预算。");
        if (requireInstaller)
        {
            using JsonDocument? profile = await ReadMemberAsync("install_profile.json").ConfigureAwait(false);
            if (profile is null) return null;
            JsonElement root = profile.RootElement;
            bool validSpec = TryInt(root, "spec", out int spec) && spec is 0 or 1 || Text(root, "spec") == "1";
            if (!validSpec || Text(root, "profile")?.ToLowerInvariant() is not ("forge" or "neoforge")) return null;
            string member = (Text(root, "json") ?? "").TrimStart('/');
            if (member.Length == 0 || member.Length > 180 || member.Contains('\\') || member.Split('/').Any(part => part is "" or "." or "..")) return null;
            JsonDocument? document = await ReadMemberAsync(member).ConfigureAwait(false);
            if (document is null) return null;
            if (Text(document.RootElement, "inheritsFrom") != Text(root, "minecraft") || Text(document.RootElement, "id") != Text(root, "version"))
            { document.Dispose(); throw new InvalidDataException("Technic 安装器版本与嵌入清单不一致。"); }
            return document;
        }
        return await ReadMemberAsync("version.json").ConfigureAwait(false);

        async Task<JsonDocument?> ReadMemberAsync(string name)
        {
            ZipArchiveEntry? entry = archive.GetEntry(name);
            if (entry is null) return null;
            if (archive.Entries.Count(item => item.FullName == name) != 1 || entry.Length > MaxManifestBytes)
                throw new IOException("Technic 归档版本清单重复或超过读取预算。");
            await using Stream stream = entry.Open(); byte[] bytes = new byte[(int)entry.Length];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (stream.ReadByte() != -1) throw new IOException("Technic 归档版本清单长度不一致。");
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        }
    }
}
