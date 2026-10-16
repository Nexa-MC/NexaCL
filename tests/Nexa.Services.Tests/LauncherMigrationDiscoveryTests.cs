using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationDiscoveryFindsOnlyRecognizedSources()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string prism = MigrationFormatDirectory(root, "PrismLauncher", "instances", "fabric");
            await MigrationWriteAsync(prism, "instance.cfg", "[General]\nname=Detected Prism\n");
            await MigrationWriteAsync(prism, "mmc-pack.json", """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"}]}""");
            Directory.CreateDirectory(Path.Combine(prism, "minecraft"));
            string native = MigrationFormatDirectory(root, "native");
            await MigrationWriteAsync(native, "versions/test/test.json", """{"id":"test","mainClass":"main"}""");
            string empty = MigrationFormatDirectory(root, "empty"), corrupt = MigrationFormatDirectory(root, "corrupt");
            await MigrationWriteAsync(corrupt, "hmcl.json", "{");
            string prismRoot = Path.Combine(root, "PrismLauncher");
            var candidates = new LauncherMigrationSource[]
            {
                new(LauncherMigrationSourceKind.Prism, "PrismLauncher", prismRoot),
                new(LauncherMigrationSourceKind.Prism, "Duplicate", prismRoot + Path.DirectorySeparatorChar),
                new(LauncherMigrationSourceKind.Minecraft, "Native", native),
                new(LauncherMigrationSourceKind.Hmcl, "Empty", empty),
                new(LauncherMigrationSourceKind.Hmcl, "Corrupt", corrupt),
                new(LauncherMigrationSourceKind.Prism, "Missing", Path.Combine(root, "missing"))
            };
            var found = await LauncherMigrationLocations.DiscoverAsync(candidates, LauncherMigrationSourceKind.Auto, default);
            AssertEqual(2, found.Sources.Count); AssertEqual(1, found.Warnings.Count);
            AssertEqual(prismRoot, found.Sources.Single(item => item.SourceKind == LauncherMigrationSourceKind.Prism).Directory);
            AssertEqual("{", await File.ReadAllTextAsync(Path.Combine(corrupt, "hmcl.json")));
            AssertEqual(0, Directory.GetDirectories(root, ".nexa-migration-stage-*").Length);
            found = await LauncherMigrationLocations.DiscoverAsync(candidates, LauncherMigrationSourceKind.Prism, default);
            AssertEqual(1, found.Sources.Count); AssertEqual(0, found.Warnings.Count);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try
            {
                await LauncherMigrationLocations.DiscoverAsync(candidates, LauncherMigrationSourceKind.Auto, cancelled.Token);
                throw new InvalidOperationException("Cancelled discovery ran to completion.");
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        }
        finally { Directory.Delete(root, true); }
    }

    private static ValueTask LauncherMigrationKnownLocationsStayBoundedAndPreserveFamilies()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            var locations = LauncherMigrationLocations.KnownLocations(root, Path.Combine(root, "roaming"),
                Path.Combine(root, "local"), Path.Combine(root, "documents"));
            AssertTrue(locations.Count <= 128);
            foreach (var family in new[] { LauncherMigrationSourceKind.Minecraft, LauncherMigrationSourceKind.Prism,
                LauncherMigrationSourceKind.Modrinth, LauncherMigrationSourceKind.CurseForge, LauncherMigrationSourceKind.Hmcl,
                LauncherMigrationSourceKind.Xmcl, LauncherMigrationSourceKind.AtLauncher, LauncherMigrationSourceKind.GdLauncher,
                LauncherMigrationSourceKind.Ftb, LauncherMigrationSourceKind.Technic })
                AssertTrue(locations.Any(item => item.SourceKind == family), "Missing known locations: " + family);
            AssertTrue(locations.Any(item => item.Directory == Path.Combine(root, "roaming", ".minecraft")));
            AssertTrue(locations.Any(item => item.Directory == Path.Combine(root, "Library", "Application Support", "minecraft")));
            AssertTrue(locations.Any(item => item.Directory == Path.Combine(root, ".local", "share", "PrismLauncher")));
            AssertEqual(8, (int)LauncherMigrationSourceKind.Minecraft);
            AssertEqual(2, (int)LauncherMigrationSourceKind.Prism);
            return ValueTask.CompletedTask;
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationAutoDiscoversMixedFamilies()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string prism = MigrationFormatDirectory(root, "instances", "prism", "minecraft");
            await MigrationWriteAsync(Path.GetDirectoryName(prism)!, "instance.cfg", "name=Prism Mixed\n");
            await MigrationWriteAsync(Path.GetDirectoryName(prism)!, "mmc-pack.json", """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"}]}""");
            await MigrationWriteAsync(root, "instances/at/instance.json", """{"id":"1.20.1","launcher":{"name":"AT Mixed"}}""");
            await MigrationWriteAsync(root, "instances/xmcl/instance.json", """{"name":"XMCL Mixed","runtime":{"minecraft":"1.21.1"}}""");
            await MigrationWriteAsync(root, "instances.json", """{"instances":["xmcl"]}""");
            await MigrationWriteAsync(root, "instances/curse/minecraftinstance.json", """{"name":"Curse Mixed","gameVersion":"1.20.1"}""");
            await MigrationWriteAsync(root, "instances/gd/instance.json", """{"_version":"1","name":"GD Mixed","game_configuration":{"version":{"release":"1.21.1","modloaders":[]}}}""");
            Directory.CreateDirectory(Path.Combine(root, "instances", "gd", "instance"));
            await MigrationWriteAsync(root, "instances/ftb/instance.json", """{"name":"FTB Mixed","mcVersion":"1.20.1","installComplete":true}""");
            await MigrationWriteAsync(root, "instances/ftb/.ftbapp/version.json", """{"targets":[{"type":"game","version":"1.20.1"}]}""");
            await MigrationWriteAsync(root, "versions/native/native.json", """{"id":"native","mainClass":"main"}""");
            var items = await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Auto, root, default);
            AssertEqual(7, items.Count);
            AssertTrue(items.All(item => item.UnsupportedReason is null));
            foreach (string name in new[] { "Prism Mixed", "AT Mixed", "XMCL Mixed", "Curse Mixed", "GD Mixed", "FTB Mixed", "native" })
                AssertTrue(items.Any(item => item.Name == name), "Auto omitted " + name);
        }
        finally { Directory.Delete(root, true); }
    }
}
