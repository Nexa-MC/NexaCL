using System.Net;
using System.Text.Json;

namespace Nexa.Services.Updates;

/// <summary>Public discovery is routing data; the helper still independently admits signed bytes.</summary>
internal static class GitHubUpdateDiscovery
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const string Releases = "https://api.github.com/repos/" + LauncherRepository.UpdatesSlug + "/releases?per_page=30";

    internal static async Task<NexaUpdateStatus?> ReadAsync(HttpClient http, NexaUpdateQuery query,
        UpdateVersion current, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Releases);
        request.Headers.UserAgent.ParseAdd("NexaCL-Update/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("更新信息超过大小限制。");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (read > MaximumBytes - buffer.Length) throw new InvalidDataException("更新信息超过大小限制。");
            buffer.Write(chunk, 0, read);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 30)
            throw new InvalidDataException("更新版本列表无效。");
        NexaUpdateOffer? result = null;
        UpdateVersion best = current;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            string version = tag.StartsWith('v') ? tag[1..] : tag;
            if (!UpdateVersion.TryParse(version, out var candidate) || candidate.ToString() != version || candidate.Major != 2
                || candidate.Stage == UpdateVersionStage.Ci
                || query.Channel == "stable" && candidate.Stage != UpdateVersionStage.Stable
                || query.Channel == "beta" && candidate.Stage == UpdateVersionStage.Alpha) continue;
            if (release.GetProperty("prerelease").GetBoolean() != (candidate.Stage != UpdateVersionStage.Stable))
                throw new InvalidDataException("更新通道与版本不匹配。");
            string releaseUrl = LauncherRepository.UpdatesUrl + "/releases/tag/" + tag;
            if (release.GetProperty("html_url").GetString() != releaseUrl)
                throw new InvalidDataException("更新说明地址无效。");
            if (candidate.CompareTo(best) <= 0) continue;
            string installerFormat = query.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "setup.exe"
                : query.RuntimeIdentifier.StartsWith("osx-", StringComparison.Ordinal) ? "dmg" : "AppImage";
            string portableFormat = query.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "portable.zip" : "portable.tar.gz";
            string prefix = "Nexa-" + version + "-" + query.RuntimeIdentifier + ".";
            var assets = release.GetProperty("assets");
            if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 1024)
                throw new InvalidDataException("更新安装包列表无效。");
            result = new(version, Asset(assets, tag, prefix + installerFormat), Asset(assets, tag, prefix + portableFormat), releaseUrl);
            best = candidate;
        }
        return new NexaUpdateStatus(result);
    }

    private static string Asset(JsonElement assets, string tag, string name)
    {
        string expected = LauncherRepository.UpdatesUrl + "/releases/download/" + tag + "/" + name;
        bool found = false;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            if (found || asset.GetProperty("size").GetInt64() <= 0 || asset.GetProperty("browser_download_url").GetString() != expected)
                throw new InvalidDataException("更新安装包地址无效。");
            found = true;
        }
        return found ? expected : throw new InvalidDataException("发布缺少当前平台的安装包。");
    }
}
