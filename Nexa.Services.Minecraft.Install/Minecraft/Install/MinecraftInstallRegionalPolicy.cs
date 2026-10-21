using Nexa.Services.Minecraft.Downloads;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    private async Task<byte[]> ReadLocalBaseIndexAsync(MinecraftAssetIndexDownloadPlan plan,
        string root, string? reuseRoot, CancellationToken token)
    {
        if (plan.LocalPath is { } localPath)
        {
            string relative = Path.GetRelativePath(root, localPath);
            foreach (string baseRoot in reuseRoot is null ? [root] : new[] { root, reuseRoot })
            {
                string path = Nexa.Core.PathIdentity.Contained(baseRoot, relative);
                if (File.Exists(path) && await _verification.VerifyAsync(new(path, plan.Size >= 0 ? plan.Size : null, plan.Sha1),
                    token, forceHash: true).ConfigureAwait(false))
                    return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            }
        }
        CountryPolicy.EnsureMinecraftDownloadsAllowed();
        throw new InvalidDataException("版本缺少本地资源索引。");
    }
}
