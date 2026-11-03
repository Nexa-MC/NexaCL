namespace Nexa.Services.Accounts;

public static class Nide8Account
{
    public static string ServerFromId(string serverId)
    {
        if (serverId.Length != 32 || !serverId.All(char.IsAsciiHexDigit)) throw new ArgumentException("统一通行证服务器 ID 必须是 32 位十六进制字符。", nameof(serverId));
        return "https://auth.mc-user.com:233/" + serverId.ToLowerInvariant();
    }
    public static string? ServerId(string server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "auth.mc-user.com" || uri.Port != 233 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return null;
        string path = uri.AbsolutePath.Trim('/');
        return path.Length == 32 && path.All(char.IsAsciiHexDigit) ? path.ToLowerInvariant() : null;
    }
}
