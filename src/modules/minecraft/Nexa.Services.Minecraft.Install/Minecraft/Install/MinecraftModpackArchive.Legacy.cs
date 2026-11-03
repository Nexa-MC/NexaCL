using System.IO.Compression;
using System.Text.Json.Nodes;
using Nexa.Services.Files;

namespace Nexa.Services.Minecraft.Install;

public static partial class MinecraftModpackArchive
{
    internal static MinecraftModpackOptionalFile[] OptionalEntries(IEnumerable<ModpackFile> files, IEnumerable<CurseForgePackFile> curse) =>
        files.Where(f => f.Optional).Select(f => new MinecraftModpackOptionalFile("path:" + f.Path, f.Path))
            .Concat(curse.Where(f => f.Optional).Select(f => new MinecraftModpackOptionalFile("cf:" + f.ProjectId, "CurseForge " + f.ProjectId + " / " + f.FileId))).ToArray();

    internal static HashSet<string> ValidateSelection(ModpackPlan plan, IReadOnlyList<string> excluded)
    {
        var known = plan.Preview.OptionalEntries.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        if (excluded.Count > 10000 || excluded.Any(id => !known.Contains(id)) || excluded.Distinct(StringComparer.Ordinal).Count() != excluded.Count)
            throw new InvalidDataException("可选文件选择已失效，请重新检查整合包。");
        return excluded.ToHashSet(StringComparer.Ordinal);
    }

    // Optional descriptors are derived display data. Older pinned journals predate this field;
    // the archive digest and scalar facts still bind their authoritative install plan.
    internal static bool SamePreview(MinecraftModpackPreview left, MinecraftModpackPreview right) =>
        left == right with { OptionalEntries = left.OptionalEntries };

    private static async Task<JsonObject> ReadObjectAsync(ZipArchiveEntry entry, CancellationToken token)
    {
        await using var input = entry.Open(); using var output = new MemoryStream();
        await ArchiveReadBudget.CopyAsync(input, output, entry.Length, 16 * 1024 * 1024, new(16 * 1024 * 1024), token).ConfigureAwait(false);
        output.Position = 0;
        return await JsonNode.ParseAsync(output, cancellationToken: token).ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException("整合包清单无效。");
    }

    private static async Task VerifyLocalChecksumAsync(ZipArchiveEntry entry, string expected, CancellationToken token)
    {
#pragma warning disable CA5350 // MCBBS publishes SHA-1 content checksums, not authentication signatures.
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
#pragma warning restore CA5350
        await using var input = entry.Open(); byte[] buffer = new byte[81920]; long size = 0; int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            size += read;
            if (size > entry.Length || size > MaxFile) throw new InvalidDataException("MCBBS 本地文件大小无效。");
            hash.AppendData(buffer, 0, read);
        }
        if (size != entry.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCBBS 文件校验失败：" + entry.FullName);
    }

    private static async Task<ModpackPlan> ReadLegacyAsync(string path, string hash, ZipArchive archive, string prefix, CancellationToken token)
    {
        bool mcbbs = archive.GetEntry(prefix + "mcbbs.packmeta") is not null;
        bool mmc = archive.GetEntry(prefix + "mmc-pack.json") is not null;
        bool hmcl = archive.GetEntry(prefix + "modpack.json") is not null;
        if ((mcbbs ? 1 : 0) + (mmc ? 1 : 0) + (hmcl ? 1 : 0) != 1 || archive.GetEntry(prefix + "modrinth.index.json") is not null)
            throw new InvalidDataException("整合包包含互相冲突的清单。");
        var manifest = await ReadObjectAsync(archive.GetEntry(prefix + (mcbbs ? "mcbbs.packmeta" : mmc ? "mmc-pack.json" : "modpack.json"))!, token).ConfigureAwait(false);
        string name = manifest["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
        string version = manifest["version"]?.GetValue<string>() ?? "1.0";
        string game = ""; InstallLoader? loader = null; string? build = null;
        List<CurseForgePackFile> curse = []; List<MinecraftModpackOptionalFile> optional = [];
        string contentRoot = mcbbs ? "overrides/" : mmc ? ".minecraft/" : "minecraft/";
        void Component(string id, string value)
        {
            if (id is "game" or "net.minecraft") { if (game.Length != 0) throw new InvalidDataException("Minecraft 依赖重复。"); game = value; return; }
            // LWJGL is supplied by the selected Minecraft version, never imported as executable patches.
            if (id is "org.lwjgl" or "org.lwjgl3") return;
            var parsed = ParseLoader(id switch { "net.minecraftforge" => "forge", "net.neoforged" => "neoforge", "net.fabricmc.fabric-loader" => "fabric", "org.quiltmc.quilt-loader" => "quilt", _ => id });
            if (loader is not null) throw new InvalidDataException("不支持多个基础加载器或自定义组件。");
            loader = parsed; build = value;
        }
        if (mmc)
        {
            if (manifest["formatVersion"]?.GetValue<int>() != 1) throw new InvalidDataException("不支持此 MultiMC 格式。");
            var cfg = archive.GetEntry(prefix + "instance.cfg") ?? throw new InvalidDataException("MultiMC 整合包缺少 instance.cfg。");
            if (cfg.Length > 65536) throw new InvalidDataException("MultiMC 配置过大。");
            await using var cfgInput = cfg.Open(); using var cfgOutput = new MemoryStream();
            await ArchiveReadBudget.CopyAsync(cfgInput, cfgOutput, cfg.Length, 65536, new(65536), token).ConfigureAwait(false);
            foreach (string line in System.Text.Encoding.UTF8.GetString(cfgOutput.ToArray()).Split('\n'))
                if (line.StartsWith("name=", StringComparison.Ordinal)) name = line[5..].Trim();
            foreach (var c in manifest["components"] as JsonArray ?? throw new InvalidDataException("MultiMC 缺少组件列表。"))
                Component(Text(c as JsonObject ?? throw new InvalidDataException("组件无效。"), "uid"), c?["version"]?.GetValue<string>() ?? "");
            if (archive.Entries.Any(e => e.FullName.StartsWith(prefix + "patches/", StringComparison.Ordinal) || e.FullName.StartsWith(prefix + "jarmods/", StringComparison.Ordinal)))
                throw new InvalidDataException("此 MultiMC 包包含自定义补丁或 JarMod，无法安全地转换为标准安装。");
            if (!archive.Entries.Any(e => e.FullName.StartsWith(prefix + contentRoot, StringComparison.Ordinal))) contentRoot = "minecraft/";
        }
        else if (mcbbs)
        {
            if (manifest["manifestType"]?.GetValue<string>() != "minecraftModpack" || manifest["manifestVersion"]?.GetValue<int>() is not (1 or 2)) throw new InvalidDataException("MCBBS 清单版本无效。");
            if (manifest["libraries"] is JsonArray { Count: > 0 }) throw new InvalidDataException("暂不支持 MCBBS 自定义运行库。");
            foreach (var c in manifest["addons"] as JsonArray ?? throw new InvalidDataException("MCBBS 缺少组件列表。"))
                Component(Text(c as JsonObject ?? throw new InvalidDataException("组件无效。"), "id"), c?["version"]?.GetValue<string>() ?? "");
            foreach (var f in manifest["files"] as JsonArray ?? [])
            {
                token.ThrowIfCancellationRequested();
                // MCBBS force controls update replacement policy; it does not make a file optional.
                switch (f?["type"]?.GetValue<string>())
                {
                    case "curse":
                        long project = f["projectID"]?.GetValue<long>() ?? 0, file = f["fileID"]?.GetValue<long>() ?? 0;
                        if (project <= 0 || file <= 0) throw new InvalidDataException("CurseForge 文件标识无效。");
                        curse.Add(new(project, file, false)); break;
                    case "addon":
                        string relative = SafeRelative(f["path"]?.GetValue<string>() ?? "");
                        string expected = RequireHash(f["hash"]?.GetValue<string>(), 40);
                        var entry = archive.GetEntry(prefix + contentRoot + relative) ?? throw new InvalidDataException("MCBBS 本地文件缺失：" + relative);
                        await VerifyLocalChecksumAsync(entry, expected, token).ConfigureAwait(false);
                        break;
                    default: throw new InvalidDataException("未知的 MCBBS 文件类型。");
                }
            }
        }
        else
        {
            game = Text(manifest, "gameVersion");
            foreach (string id in new[] { "forge", "neoforge", "fabric", "quilt" })
                if (manifest[id] is JsonValue value && value.TryGetValue<string>(out var v) && v.Length > 0) Component(id, v);
        }
        if (!MinecraftVersionPaths.IsSafeReference(game) || (loader is not null && (build is null || !MinecraftVersionPaths.IsSafeReference(build)))) throw new InvalidDataException("Minecraft 或加载器版本无效。");
        if (name.Length is 0 or > 1024 || version.Length is 0 or > 1024 || curse.Count > 10000 || curse.Select(f => f.ProjectId).Distinct().Count() != curse.Count) throw new InvalidDataException("整合包元数据或文件列表无效。");
        optional.AddRange(OptionalEntries([], curse));
        if (optional.Count > 10000 || optional.Select(f => f.Id).Distinct().Count() != optional.Count) throw new InvalidDataException("可选文件列表无效。");
        string safeName = string.Concat(name.Take(60).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var preview = new MinecraftModpackPreview(path, hash, name, version, mcbbs ? "MCBBS" : mmc ? "MultiMC" : "HMCL", game, loader, build,
            safeName + "-" + hash[..8].ToLowerInvariant(), curse.Count(f => !f.Optional), optional.Count)
        { OptionalEntries = optional.ToArray() };
        return new(preview, [], curse, [prefix + contentRoot]);
    }
}
