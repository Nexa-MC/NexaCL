namespace Nexa.Services;

/// <summary>One override policy for resource browsing, addons and modpack operations.</summary>
internal static class CurseForgeCredentials
{
    internal static string? ResolveApiKey(string? packagedFallback, Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        foreach (string name in new[] { "NEXA_CURSEFORGE_API_KEY", "Nexa_CURSEFORGE_API_KEY", "CURSEFORGE_API_KEY" })
            if (readEnvironment(name) is { } value && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        return string.IsNullOrWhiteSpace(packagedFallback) ? null : packagedFallback.Trim();
    }
}
