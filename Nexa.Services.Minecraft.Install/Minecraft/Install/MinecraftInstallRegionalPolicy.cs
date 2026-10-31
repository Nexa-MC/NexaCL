using Nexa.Services.Minecraft.Assets;
using Nexa.Services.Minecraft.Downloads;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    private sealed class ModpackLocalBaseRequiredException(string game) : InvalidOperationException(
        $"未找到完整可用的 Minecraft {game} 原版。请先通过 Minecraft Launcher 安装该版本，再将完整原版导入当前游戏目录后重试整合包安装。");

    private async Task EnsureLocalModpackBaseAsync(MinecraftModpackPreview pack, string root, string? reuseClientPath,
        PersistentInstallMetadataSource metadata, CancellationToken token)
    {
        if (CountryPolicy.AllowMinecraftDownloads) return;
        string game = SafeName(pack.Game), gameDirectory = Nexa.Core.PathIdentity.Contained(root, "versions/" + game);
        string clientPath = Path.Combine(gameDirectory, game + ".jar");
        if (!File.Exists(clientPath) && (reuseClientPath is null || !File.Exists(reuseClientPath)))
            throw new ModpackLocalBaseRequiredException(game);

        var vanilla = await metadata.FetchVanillaVersionJsonAsync(game, token).ConfigureAwait(false);
        var client = MinecraftClientDownloadPlanner.CreateClientJarPlan(new()
        { VersionJson = vanilla, InstanceDirectory = gameDirectory, VersionName = game }).File;
        if (client is null || string.IsNullOrEmpty(client.Sha1)) throw new ModpackLocalBaseRequiredException(game);
        long? size = client.ActualSize >= 0 ? client.ActualSize : null;
        bool reusable = await _verification.VerifyAsync(new(client.LocalPath, size, client.Sha1), token, forceHash: true).ConfigureAwait(false);
        if (!reusable && reuseClientPath is not null)
            reusable = await _verification.VerifyAsync(new(reuseClientPath, size, client.Sha1), token, forceHash: true).ConfigureAwait(false);
        if (!reusable)
            throw new ModpackLocalBaseRequiredException(game);

        var platform = MinecraftLaunchPlatform.Detect();
        var libraries = MinecraftLibraryResolver.Resolve(new()
        {
            VersionJson = vanilla,
            MinecraftRootDirectory = root,
            OperatingSystem = platform.OperatingSystem,
            Is64BitArchitecture = platform.Is64BitArchitecture,
            IsArm64Architecture = platform.IsArm64Architecture,
            OperatingSystemVersion = platform.OperatingSystemVersion
        });
        foreach (var library in libraries.Where(library => !library.IsLocal && !string.IsNullOrWhiteSpace(library.Url)))
            if (string.IsNullOrEmpty(library.Sha1)
                || !await _verification.VerifyAsync(new(library.LocalPath, library.Size > 0 ? library.Size : null, library.Sha1), token, forceHash: true).ConfigureAwait(false))
                throw new ModpackLocalBaseRequiredException(game);

        var indexPlan = MinecraftClientDownloadPlanner.CreateAssetIndexPlan(new() { VersionJson = vanilla, MinecraftRootDirectory = root });
        byte[] bytes = await ReadLocalBaseIndexAsync(indexPlan, root, null, token).ConfigureAwait(false);
        var index = AuthoritativeMetadata.ParseVerified(bytes, indexPlan.Sha1, indexPlan.Size);
        var assets = MinecraftAssetListResolver.GetAssetList(new()
        { IndexJson = index, MinecraftRootDirectory = root, InstanceDirectory = Nexa.Core.PathIdentity.Contained(root, "versions/" + pack.InstanceId) });
        foreach (var asset in assets)
            if (!await _verification.VerifyAsync(new(asset.LocalPath, asset.Size, asset.Hash), token, forceHash: true).ConfigureAwait(false))
                throw new ModpackLocalBaseRequiredException(game);
    }

    private async Task<byte[]> ReadLocalBaseIndexAsync(MinecraftAssetIndexDownloadPlan plan,
        string root, string? reuseRoot, CancellationToken token)
    {
        if (plan.LocalPath is { } localPath)
        {
            string relative = Path.GetRelativePath(root, localPath);
            foreach (string baseRoot in reuseRoot is null ? [root] : new[] { root, reuseRoot })
            {
                var candidates = new List<string> { relative };
                if (plan.Sha1 is { Length: 40 } digest && digest.All(char.IsAsciiHexDigit))
                {
                    string suffix = "-" + digest.ToLowerInvariant();
                    string name = Path.GetFileNameWithoutExtension(relative);
                    string alternate = name.EndsWith(suffix, StringComparison.Ordinal)
                        ? name[..^suffix.Length] : name + suffix;
                    candidates.Add(Path.Combine(Path.GetDirectoryName(relative)!, alternate + ".json"));
                }
                foreach (string candidate in candidates)
                {
                    string path = Nexa.Core.PathIdentity.Contained(baseRoot, candidate);
                    if (File.Exists(path) && await _verification.VerifyAsync(new(path, plan.Size >= 0 ? plan.Size : null, plan.Sha1),
                        token, forceHash: true).ConfigureAwait(false))
                        return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
                }
            }
        }
        CountryPolicy.EnsureMinecraftDownloadsAllowed();
        throw new InvalidDataException("版本缺少本地资源索引。");
    }
}
