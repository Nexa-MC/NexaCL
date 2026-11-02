using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationFormatsRecognizeDocumentedManifests()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string prism = MigrationFormatDirectory(root, "prism");
            Directory.CreateDirectory(Path.Combine(prism, ".minecraft"));
            File.WriteAllText(Path.Combine(prism, "instance.cfg"), "[General]\nname=Prism=示例\nOverrideJavaArgs=true\nJvmArgs=-Dprivate=retained-only-at-source\n");
            File.WriteAllText(Path.Combine(prism, "mmc-pack.json"), """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.16.10"},{"uid":"org.lwjgl3","version":"3.3.3"}]}""");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Prism, prism, default)).Single();
            AssertEqual("Prism=示例", item.Name); AssertEqual("1.20.1", item.GameVersion); AssertEqual("Fabric", item.Loader); AssertEqual("0.16.10", item.LoaderBuild);
            AssertEqual(Path.Combine(prism, ".minecraft"), item.GameDirectory); AssertTrue(item.UnsupportedReason is null); AssertEqual(2, item.ManifestPaths.Count);
            var fromContainer = await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Auto, root, default);
            AssertEqual("Prism=示例", fromContainer.Single().Name);
            Directory.CreateDirectory(Path.Combine(prism, "minecraft"));
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Prism, prism, default)).Single();
            AssertEqual(Path.Combine(prism, "minecraft"), item.GameDirectory);

            string curse = MigrationFormatDirectory(root, "curse");
            File.WriteAllText(Path.Combine(curse, "minecraftinstance.json"), """{"name":"CurseForge 示例","gameVersion":"1.20.1","baseModLoader":{"name":"fabric-0.16.10-1.20.1"},"instancePath":"/untrusted/metadata/path"}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.CurseForge, curse, default)).Single();
            AssertEqual(curse, item.GameDirectory); AssertEqual("Fabric", item.Loader); AssertEqual("0.16.10", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);

            string at = MigrationFormatDirectory(root, "at");
            File.WriteAllText(Path.Combine(at, "instance.json"), """{"id":"1.20.1","launcher":{"name":"AT 示例","loaderVersion":{"type":"Forge","version":"47.2.0"}}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Auto, at, default)).Single();
            AssertEqual("AT 示例", item.Name); AssertEqual("Forge", item.Loader); AssertEqual("47.2.0", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);

            string xmcl = MigrationFormatDirectory(root, "xmcl");
            File.WriteAllText(Path.Combine(xmcl, "instance.json"), """{"name":"XMCL 示例","edition":"java","runtime":{"minecraft":"1.21.1","neoForged":"21.1.72"}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Xmcl, xmcl, default)).Single();
            AssertEqual("NeoForge", item.Loader); AssertEqual("21.1.72", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);

            string modrinth = MigrationFormatDirectory(root, "modrinth");
            File.WriteAllText(Path.Combine(modrinth, "profile.json"), """{"install_stage":"installed","metadata":{"name":"Modrinth 示例","game_version":"1.20.1","loader":"quilt","loader_version":{"id":"0.27.1"}}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Modrinth, modrinth, default)).Single();
            AssertEqual("Quilt", item.Loader); AssertEqual("0.27.1", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);
            File.WriteAllText(Path.Combine(modrinth, "profile.json"), """{"install_stage":"installed","metadata":{"name":"Forge catalog ID","game_version":"1.20.1","loader":"forge","loader_version":{"id":"1.20.1-47.2.0"}}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Modrinth, modrinth, default)).Single();
            AssertEqual("Forge", item.Loader); AssertEqual("47.2.0", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);
            File.WriteAllText(Path.Combine(modrinth, "profile.json"), """{"install_stage":"installed","metadata":{"name":"NeoForge catalog ID","game_version":"1.20.1","loader":"neoforge","loader_version":{"id":"1.20.1-47.1.106"}}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Modrinth, modrinth, default)).Single();
            AssertEqual("NeoForge", item.Loader); AssertEqual("47.1.106", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationFormatsRefuseUnsupportedComponents()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            MigrationFormatDirectory(root, "minecraft");
            File.WriteAllText(Path.Combine(root, "instance.cfg"), "[General]\nname=Custom\n");
            File.WriteAllText(Path.Combine(root, "mmc-pack.json"), """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"custom.loader","version":"1.0"}]}""");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Prism, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(root, "instance.json"), """{"name":"Hybrid","runtime":{"minecraft":"1.20.1","forge":"47.2.0","fabricLoader":"0.16.10"}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Xmcl, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(root, "instance.json"), """{"name":"Bedrock","edition":"bedrock","runtime":{"minecraft":"1.21.1"}}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Xmcl, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationFormatsFindHmclNativeDirectories()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string game = MigrationFormatDirectory(root, "games"), leaf = MigrationFormatDirectory(game, "versions", "custom");
            Directory.CreateDirectory(Path.Combine(leaf, ".minecraft"));
            File.WriteAllText(Path.Combine(leaf, "custom.json"), """{"id":"custom","inheritsFrom":"1.20.1"}""");
            File.WriteAllText(Path.Combine(root, "hmcl.json"), """{"configurations":{"Custom":{"gameDir":"games"}}}""");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, root, default)).Single();
            AssertEqual("custom", item.NativeVersion); AssertEqual(game, item.NativeRoot);
            AssertEqual(Path.Combine(leaf, ".minecraft"), item.GameDirectory);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(root, "hmcl.json")));
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(leaf, "custom.json")));
            Directory.CreateDirectory(Path.Combine(game, "saves")); Directory.CreateDirectory(Path.Combine(leaf, "config"));
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
            string metadata = MigrationFormatDirectory(leaf, "Nexa");
            File.WriteAllText(Path.Combine(metadata, "InstanceMetadata.json"), """{"instanceIsolation":true}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, root, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(metadata, "InstanceMetadata.json")));
            File.WriteAllText(Path.Combine(metadata, "InstanceMetadata.json"), """{"instanceIsolation":false}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, root, default)).Single();
            AssertEqual(game, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            File.Delete(Path.Combine(metadata, "InstanceMetadata.json"));
            string pcl = MigrationFormatDirectory(leaf, "PCL");
            File.WriteAllText(Path.Combine(pcl, "InstanceMetadata.json"), """{"InstanceIsolation":false}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, root, default)).Single();
            AssertEqual(game, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            string broken = MigrationFormatDirectory(game, "versions", "broken");
            File.WriteAllText(Path.Combine(broken, "broken.json"), "{");
            var partial = await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, root, default);
            AssertEqual(2, partial.Count); AssertTrue(partial.Single(entry => entry.Name == "broken").UnsupportedReason is not null);
            AssertTrue(partial.Single(entry => entry.Name == "custom").UnsupportedReason is null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationFormatsBoundManifestReads()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "minecraftinstance.json"), new string(' ', 4 * 1024 * 1024 + 1));
            bool rejected = false;
            try { await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.CurseForge, root, default); }
            catch (IOException) { rejected = true; }
            AssertTrue(rejected);
            File.WriteAllText(Path.Combine(root, "minecraftinstance.json"), "{}{}");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.CurseForge, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string MigrationFormatDirectory(string root, params string[] parts)
    {
        string path = parts.Aggregate(root, Path.Combine); Directory.CreateDirectory(path); return path;
    }
}
