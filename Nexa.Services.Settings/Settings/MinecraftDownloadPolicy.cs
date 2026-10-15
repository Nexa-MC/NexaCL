using System.Globalization;

namespace Nexa.Services.Settings;

/// <summary>One immutable game-file batch policy; shared resource admission remains authoritative.</summary>
internal sealed record MinecraftDownloadPolicy(int Concurrency = 8, bool Retry = true, string Source = "official-first")
{
    internal string[] SelectSources(string original, IEnumerable<string> sources, RegionalPolicy? region = null)
    {
        RegionalPolicy policy = region ?? RegionalPolicy.Current;
        string canonical = RegionalDownloadSources.ForRegion(original, policy);
        string[] allowed = sources.Distinct(StringComparer.Ordinal).ToArray();
        if (!policy.IsMainlandChina)
            return allowed.Where(source => source == original || source == canonical).Select(_ => canonical).Distinct(StringComparer.Ordinal).ToArray();
        if (Source == "official-only")
            return allowed.Where(source => source == canonical).ToArray();
        return allowed.OrderBy(source => Source == "mirrors-first" ? source == canonical ? 1 : 0 : source == canonical ? 0 : 1).ToArray();
    }

    internal static MinecraftDownloadPolicy Read(SettingsPolicyService? settings)
    {
        var result = settings?.Read(new());
        if (result is null || !result.IsSuccess) return new();
        var values = result.Value!.Values;
        string? count = values.Single(item => item.Key == "network.file-concurrency").Value.Value;
        bool retry = values.Single(item => item.Key == "network.file-retry").Value.Value != "false";
        return new(int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out int concurrency)
            && concurrency is >= 1 and <= 64 ? concurrency : 8, retry,
            values.Single(item => item.Key == "network.game-source").Value.Value ?? "official-first");
    }
}
