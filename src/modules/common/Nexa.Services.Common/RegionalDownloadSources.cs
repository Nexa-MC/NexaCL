namespace Nexa.Services;

/// <summary>Recovers only known official origins from persisted product mirror URLs.</summary>
internal static class RegionalDownloadSources
{
    internal static string ForRegion(string source, RegionalPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string normalized = source.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net", StringComparison.Ordinal);
        if (policy.IsMainlandChina || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)) return normalized;
        string path = uri.AbsolutePath;
        bool bmcl = uri.Host is "bmclapi2.bangbang93.com" or "bmclapi.bangbang93.com" or "download.mcbbs.net";
        bool mirror = bmcl || uri.Host is "mod.mcimirror.top" or "mcim-files.pysio.online";
        if (uri.Host is "mirrors.cernet.edu.cn" or "cmcc.mirrors.ustc.edu.cn"
            && (path == "/bmclapi" || path.StartsWith("/bmclapi/", StringComparison.Ordinal)))
        {
            mirror = true;
            path = path == "/bmclapi" ? "/maven/" : "/maven/" + path["/bmclapi/".Length..];
            bmcl = true;
        }
        const string unlisted = "/d/mirror/unlisted-versions-of-minecraft/Auto/";
        bool unlistedMirror = uri.Host == "alist.8mi.tech" && (path == unlisted[..^1] || path.StartsWith(unlisted, StringComparison.Ordinal));
        if (!mirror && !unlistedMirror) return normalized;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw Unavailable();
        if (unlistedMirror)
        {
            if (path.Length <= unlisted.Length) throw Unavailable();
            return "https://zkitefly.github.io/unlisted-versions-of-minecraft/" + path[unlisted.Length..];
        }
        if (bmcl)
        {
            string? maven = path.StartsWith("/maven/", StringComparison.Ordinal) ? path["/maven/".Length..]
                : path.StartsWith("/libraries/", StringComparison.Ordinal) ? path["/libraries/".Length..] : null;
            if (maven is not null)
            {
                string? origin = maven.StartsWith("com/mumfrey/liteloader/", StringComparison.Ordinal) ? "https://repo.liteloader.com/"
                    : maven.StartsWith("net/minecraftforge/", StringComparison.Ordinal) ? "https://maven.minecraftforge.net/"
                    : maven.StartsWith("net/fabricmc/", StringComparison.Ordinal) ? "https://maven.fabricmc.net/"
                    : maven.StartsWith("net/neoforged/", StringComparison.Ordinal) ? "https://maven.neoforged.net/releases/" : null;
                if (origin is not null) return origin + maven;
            }
            if (HasHashSegment(path, "/v1/objects/")) return "https://piston-data.mojang.com" + path;
            if (HasHashSegment(path, "/v1/packages/")) return "https://piston-meta.mojang.com" + path;
            if (path is "/mc/game/version_manifest.json" or "/mc/game/version_manifest_v2.json")
                return "https://piston-meta.mojang.com" + path;
            const string assets = "/assets/";
            if (path.StartsWith(assets, StringComparison.Ordinal))
            {
                string[] segments = path[assets.Length..].Split('/');
                if (segments is [var prefix, var hash] && prefix.Length == 2 && hash.Length == 40
                    && hash.All(char.IsAsciiHexDigit) && hash.StartsWith(prefix, StringComparison.Ordinal))
                    return "https://resources.download.minecraft.net/" + prefix + "/" + hash;
            }
        }
        throw Unavailable();
    }

    private static bool HasHashSegment(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string tail = path[prefix.Length..];
        int slash = tail.IndexOf('/');
        return slash == 40 && tail[..slash].All(char.IsAsciiHexDigit) && slash < tail.Length - 1;
    }

    private static InvalidDataException Unavailable() => new("此地区不使用已保存的镜像地址，且无法确定其官方来源。请刷新版本资料或重新选择官方来源。");
}
