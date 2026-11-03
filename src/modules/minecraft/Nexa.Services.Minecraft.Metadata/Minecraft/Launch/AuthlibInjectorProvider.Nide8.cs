using System.Security.Cryptography;
using System.Text.Json;
namespace Nexa.Services.Minecraft.Launch;

public sealed partial class AuthlibInjectorProvider
{
    public async Task<string> EnsureNide8Async(string minecraftRoot, string serverId, CancellationToken cancellationToken = default)
    {
        if (serverId.Length != 32 || !serverId.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Nide8 服务器 ID 无效。");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] metadata = await ReadNide8BytesAsync("https://auth.mc-user.com:233/" + serverId, 65536, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(metadata);
            string hash = document.RootElement.GetProperty("jarHash").GetString() ?? "";
            if (hash.Length != 40 || !hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Nide8 官方代理校验信息无效。");
            string directory = Path.Combine(minecraftRoot, "libraries", "nide8");
            CheckNide8Links(directory); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, hash.ToLowerInvariant() + ".jar");
            CheckNide8Links(path);
            if (File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 * 1024)
            {
                byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (existing.Length <= 16 * 1024 * 1024 && MatchesNide8(existing, hash)) return path;
            }
            byte[] jar = await ReadNide8BytesAsync("https://login.mc-user.com:233/index/jar", 16 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            if (!jar.AsSpan().StartsWith("PK"u8) || !MatchesNide8(jar, hash)) throw new InvalidDataException("Nide8 代理下载校验失败。");
            string stage = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try { await File.WriteAllBytesAsync(stage, jar, cancellationToken).ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); File.Move(stage, path, true); }
            finally { File.Delete(stage); }
            return path;
        }
        finally { _gate.Release(); }
    }
    private static void CheckNide8Links(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0 && !Nexa.Core.PathIdentity.IsMacOSSystemDirectoryAlias(current))
                throw new IOException("Nide8 缓存目录不能包含链接。");
    }
    private static bool MatchesNide8(byte[] bytes, string expected)
    {
#pragma warning disable CA5350 // Nide8's HTTPS metadata specifies its published agent by SHA-1.
        return Convert.ToHexString(SHA1.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase);
#pragma warning restore CA5350
    }
    private async Task<byte[]> ReadNide8BytesAsync(string url, int limit, CancellationToken token)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var final = response.RequestMessage?.RequestUri;
        if (final is null || final.Scheme != "https" || final.Port != 233 || final.Host is not ("auth.mc-user.com" or "login.mc-user.com")) throw new InvalidDataException("Nide8 下载重定向至非官方地址。");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); byte[] buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        { if (output.Length + count > limit) throw new InvalidDataException("Nide8 响应超过大小限制。"); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
        return output.ToArray();
    }
}
