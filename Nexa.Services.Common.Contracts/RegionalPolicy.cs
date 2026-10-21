using System.Globalization;

namespace Nexa.Services;

/// <summary>Product country policy, independent of the chosen display language.</summary>
public sealed record RegionalPolicy(string CountryCode)
{
    public bool IsMainlandChina => string.Equals(CountryCode, "CN", StringComparison.OrdinalIgnoreCase);
    public bool RequireMinecraftOwnership { get; } = true;
    public bool AllowMinecraftDownloads => !IsMainlandChina;
    public const string MinecraftDownloadUnavailable = "不向中国大陆提供 Minecraft 本体下载服务。请使用已完整安装的本地版本，或通过本地导入迁移已有版本。";

    public void EnsureMinecraftDownloadsAllowed()
    {
        if (!AllowMinecraftDownloads) throw new InvalidDataException(MinecraftDownloadUnavailable);
    }
    public const string PurchaseReminder = "请支持并购买正版 Minecraft；其他档案仅用于你有权访问的游戏与服务器。";
    public static RegionalPolicy Current { get; } = Resolve();

    public static RegionalPolicy Resolve(string? country = null)
    {
        country ??= Environment.GetEnvironmentVariable("NEXA_COUNTRY");
        if (string.IsNullOrWhiteSpace(country))
        {
            try { country = RegionInfo.CurrentRegion.TwoLetterISORegionName; }
            catch (ArgumentException) { country = ""; }
        }
        string normalized = country.Trim().ToUpperInvariant();
        return new(normalized.Length == 2 && normalized.All(char.IsAsciiLetter) ? normalized : "");
    }
}
