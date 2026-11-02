namespace Nexa.Services.Setup;

internal static class LauncherMigrationLocations
{
    internal static IReadOnlyList<LauncherMigrationSource> KnownLocations(string home, string roaming, string local, string documents)
    {
        List<LauncherMigrationSource> locations = [];
        void Add(LauncherMigrationSourceKind kind, string name, string parent, params string[] parts)
        {
            if (string.IsNullOrWhiteSpace(parent)) return;
            string path = parts.Aggregate(parent, Path.Combine);
            if (!locations.Any(item => item.SourceKind == kind && Nexa.Core.PathIdentity.Comparer.Equals(item.Directory, path)))
                locations.Add(new(kind, name, path));
        }

        Add(LauncherMigrationSourceKind.Minecraft, "Minecraft 官方 / 原生游戏目录", roaming, ".minecraft");
        Add(LauncherMigrationSourceKind.Minecraft, "Minecraft 官方 / 原生游戏目录", home, ".minecraft");
        Add(LauncherMigrationSourceKind.Minecraft, "Minecraft 官方 / 原生游戏目录", home, "Library", "Application Support", "minecraft");
        foreach (string parent in new[] { roaming, local, Path.Combine(home, ".local", "share"), Path.Combine(home, "Library", "Application Support") })
        {
            foreach (string brand in new[] { "PrismLauncher", "MultiMC", "PolyMC" })
                Add(LauncherMigrationSourceKind.Prism, brand, parent, brand);
            foreach (string brand in new[] { "ModrinthApp", "com.modrinth.theseus" })
                Add(LauncherMigrationSourceKind.Modrinth, "Modrinth App", parent, brand);
            Add(LauncherMigrationSourceKind.AtLauncher, "ATLauncher", parent, "ATLauncher");
            Add(LauncherMigrationSourceKind.GdLauncher, "GDLauncher Carbon", parent, "gdlauncher_carbon");
            Add(LauncherMigrationSourceKind.GdLauncher, "GDLauncher Legacy", parent, "gdlauncher_next");
            Add(LauncherMigrationSourceKind.Ftb, "FTB App", parent, ".ftba");
            Add(LauncherMigrationSourceKind.Technic, "Technic Launcher", parent, ".technic");
            Add(LauncherMigrationSourceKind.Hmcl, "HMCL", parent, "hmcl");
            Add(LauncherMigrationSourceKind.Xmcl, "XMCL", parent, "xmcl");
        }
        Add(LauncherMigrationSourceKind.Prism, "PrismLauncher (Flatpak)", home, ".var", "app", "org.prismlauncher.PrismLauncher", "data", "PrismLauncher");
        Add(LauncherMigrationSourceKind.Prism, "PolyMC (Flatpak)", home, ".var", "app", "org.polymc.PolyMC", "data", "PolyMC");
        Add(LauncherMigrationSourceKind.AtLauncher, "ATLauncher (Flatpak)", home, ".var", "app", "com.atlauncher.ATLauncher", "data", "ATLauncher");
        Add(LauncherMigrationSourceKind.Modrinth, "Modrinth App (Flatpak)", home, ".var", "app", "com.modrinth.ModrinthApp", "data", "com.modrinth.theseus");
        Add(LauncherMigrationSourceKind.CurseForge, "CurseForge", home, "curseforge", "minecraft");
        Add(LauncherMigrationSourceKind.CurseForge, "CurseForge", documents, "CurseForge", "Minecraft");
        Add(LauncherMigrationSourceKind.CurseForge, "Curse / Twitch", documents, "Curse", "Minecraft");
        Add(LauncherMigrationSourceKind.CurseForge, "Twitch", documents, "Twitch", "Minecraft");
        Add(LauncherMigrationSourceKind.Ftb, "FTB App", home, ".ftba");
        Add(LauncherMigrationSourceKind.Technic, "Technic Launcher", home, ".technic");
        Add(LauncherMigrationSourceKind.Hmcl, "HMCL", home, ".hmcl");
        return locations.AsReadOnly();
    }

    internal static async Task<LauncherMigrationDiscoveryResult> DiscoverAsync(IEnumerable<LauncherMigrationSource> candidates,
        LauncherMigrationSourceKind kind, CancellationToken token)
    {
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("未知的启动器类型。");
        List<LauncherMigrationSource> sources = [];
        List<string> warnings = [];
        var bounded = candidates.Take(129).ToArray();
        if (bounded.Length > 128) throw new InvalidDataException("启动器检测位置超过读取预算。");
        foreach (var candidate in bounded)
        {
            token.ThrowIfCancellationRequested();
            if (kind != LauncherMigrationSourceKind.Auto && candidate.SourceKind != kind) continue;
            try
            {
                string path = LauncherMigrationFiles.Absolute(candidate.Directory);
                if (!Directory.Exists(path)) continue;
                LauncherMigrationFiles.CheckLinks(path);
                if (sources.Any(item => item.SourceKind == candidate.SourceKind && Nexa.Core.PathIdentity.Comparer.Equals(item.Directory, path))) continue;
                if ((await LauncherMigrationFormats.DiscoverAsync(candidate.SourceKind, path, token).ConfigureAwait(false)).Count > 0)
                    sources.Add(candidate with { Directory = path });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or System.Text.Json.JsonException or System.Security.SecurityException)
            {
                warnings.Add(candidate.Name + "：无法检查此位置，请手动选择来源目录。" + error.Message);
            }
        }
        return new(sources.AsReadOnly(), warnings.AsReadOnly());
    }
}

public sealed partial class LauncherMigrationService
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Discovery uses the same owned Setup service route and lifetime as preview.")]
    public Task<LauncherMigrationDiscoveryResult> DiscoverSourcesAsync(LauncherMigrationDiscoveryQuery query, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return LauncherMigrationLocations.DiscoverAsync(LauncherMigrationLocations.KnownLocations(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), query.SourceKind, token);
    }
}
