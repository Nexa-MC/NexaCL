using System.Text.Json.Nodes;
using Nexa.Services.Composition;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationOfficialProfilesPreserveIndependentGameDirectories()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = MigrationFormatDirectory(temporary, "official"), destination = Path.Combine(temporary, "imported");
            await MigrationWriteAsync(source, "versions/1.20.1/1.20.1.json", """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main"}""");
            await MigrationWriteAsync(source, "versions/1.20.1/1.20.1.jar", "client");
            await MigrationWriteAsync(source, "saves/World/level.dat", "default-world");
            await MigrationWriteAsync(source, "custom game/saves/World/level.dat", "custom-world");
            await MigrationWriteAsync(source, "custom game/options.txt", "music:0.25");
            await MigrationWriteAsync(source, "custom game/launcher_accounts.json", "do-not-copy");
            const string profiles = """{"profiles":{"shared":{"name":"Shared World","lastVersionId":"1.20.1"},"custom":{"name":"Custom World","lastVersionId":"1.20.1","gameDir":"custom game"},"latest":{"name":"Unresolved latest","lastVersionId":"latest-release"}}}""";
            await MigrationWriteAsync(source, "launcher_profiles.json", profiles);
            var discovered = await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Minecraft, source, default);
            AssertEqual(3, discovered.Count);
            AssertEqual(2, discovered.Count(item => item.UnsupportedReason is null));
            AssertEqual(2, discovered.Count(item => item.NativeVersion == "1.20.1"));
            AssertEqual(3, discovered.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count());
            var custom = discovered.Single(item => item.Name == "Custom World");
            AssertEqual(Path.Combine(source, "custom game"), custom.GameDirectory);
            AssertTrue(custom.ManifestPaths.Contains(Path.Combine(source, "launcher_profiles.json")));
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Minecraft, source, destination));
            await File.WriteAllTextAsync(Path.Combine(source, "launcher_profiles.json"), profiles.Replace("Custom World", "Changed World", StringComparison.Ordinal));
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, preview.Instances.Where(item => item.CanImport).Select(item => item.Id).ToArray())));
            AssertFalse(Directory.Exists(destination));
            await File.WriteAllTextAsync(Path.Combine(source, "launcher_profiles.json"), profiles);
            preview = await service.PreviewAsync(preview.Query);
            var receipt = await service.ApplyAsync(new(preview, preview.Instances.Where(item => item.CanImport).Select(item => item.Id).ToArray()));
            AssertEqual(2, receipt.Instances.Count);
            foreach (var item in receipt.Instances)
            {
                string game = Path.Combine(item.MinecraftRootDirectory, "versions", item.InstanceId);
                AssertEqual("1.20.1", item.InstanceId);
                AssertEqual(item.Name == "Custom World" ? "custom-world" : "default-world",
                    await File.ReadAllTextAsync(Path.Combine(game, "saves", "World", "level.dat")));
                AssertFalse(File.Exists(Path.Combine(game, "launcher_accounts.json")));
                AssertFalse(File.Exists(Path.Combine(item.MinecraftRootDirectory, "launcher_profiles.json")));
            }
            await service.UndoAsync(receipt);
            AssertFalse(Directory.Exists(destination));
            AssertEqual(profiles, await File.ReadAllTextAsync(Path.Combine(source, "launcher_profiles.json")));
            AssertEqual("custom-world", await File.ReadAllTextAsync(Path.Combine(source, "custom game", "saves", "World", "level.dat")));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationNativePclIsolationMatchesSourceContract()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string root = MigrationFormatDirectory(temporary, "pcl"), leaf = MigrationFormatDirectory(root, "versions", "1.20.1");
            await MigrationWriteAsync(root, "versions/1.20.1/1.20.1.json", """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main"}""");
            await MigrationWriteAsync(root, "config/settings.json", "shared");
            await MigrationWriteAsync(leaf, "config/settings.json", "isolated");
            await MigrationWriteAsync(leaf, "PCL/Setup.ini", "VersionArgumentIndie:2\r\nPreLaunchCommand:do-not-run\r\n");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(root, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(leaf, "PCL", "Setup.ini")));
            await File.WriteAllTextAsync(Path.Combine(leaf, "PCL", "Setup.ini"),
                "VersionArgumentIndie:2\r\nVersionArgumentIndieV2:False\r\nVersionArgumentIndieV2:True\r\n", System.Text.Encoding.Unicode);
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await File.WriteAllTextAsync(Path.Combine(leaf, "PCL", "Setup.ini"), "VersionArgumentIndieV2:False\n");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(root, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await File.WriteAllTextAsync(Path.Combine(leaf, "PCL", "Setup.ini"), "VersionArgumentIndie:0\n");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await MigrationWriteAsync(root, "PCL/Setup.ini", "LaunchArgumentIndieV2:0\n");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(root, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(root, "PCL", "Setup.ini")));
            await File.WriteAllTextAsync(Path.Combine(root, "PCL", "Setup.ini"), "LaunchArgumentIndieV2:4\n");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await File.WriteAllTextAsync(Path.Combine(root, "PCL", "Setup.ini"), "LaunchArgumentIndieV2:1\n");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
            await MigrationWriteAsync(leaf, "mods/example.jar", "mod-data");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await File.WriteAllTextAsync(Path.Combine(leaf, "PCL", "Setup.ini"), "VersionArgumentIndieV2:not-a-bool\n");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
            await File.WriteAllTextAsync(Path.Combine(leaf, "PCL", "Setup.ini"), "VersionArgumentIndieV2:True\n");
            await MigrationWriteAsync(leaf, "Nexa/InstanceMetadata.json", """{"instanceIsolation":false}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Nexa, root, default)).Single();
            AssertEqual(root, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationNativeHmclIsolationMatchesSourceContract()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = MigrationFormatDirectory(temporary, "hmcl"), root = MigrationFormatDirectory(source, "games");
            string leaf = MigrationFormatDirectory(root, "versions", "1.20.1"), custom = MigrationFormatDirectory(source, "custom");
            await MigrationWriteAsync(root, "versions/1.20.1/1.20.1.json", """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main"}""");
            await MigrationWriteAsync(root, "config/settings.json", "shared");
            await MigrationWriteAsync(leaf, "config/settings.json", "isolated");
            await MigrationWriteAsync(source, "hmcl.json", """{"configurations":{"Default":{"gameDir":"games","global":{"gameDirType":0}}}}""");
            await MigrationWriteAsync(leaf, "hmclversion.cfg", """{"usesGlobal":false,"gameDirType":1,"javaArgs":"-javaagent:private.jar"}""");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await File.WriteAllTextAsync(Path.Combine(leaf, "hmclversion.cfg"), """{"usesGlobal":true,"gameDirType":1}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(root, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await File.WriteAllTextAsync(Path.Combine(leaf, "hmclversion.cfg"), new JsonObject
            { ["usesGlobal"] = false, ["gameDirType"] = 2, ["gameDir"] = custom }.ToJsonString());
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(custom, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            await MigrationWriteAsync(leaf, "modpack.cfg", "{}");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(leaf, "modpack.cfg")));
            File.Delete(Path.Combine(leaf, "modpack.cfg"));
            File.Delete(Path.Combine(leaf, "hmclversion.cfg"));
            File.Delete(Path.Combine(source, "hmcl.json"));
            await MigrationWriteAsync(source, "config/game-directories.json", """{"directories":[{"path":"games"}]}""");
            await MigrationWriteAsync(source, "config/game-settings.json", new JsonObject
            { ["presets"] = new JsonArray(new JsonObject { ["id"] = "custom-preset", ["runningDirectory"] = custom }) }.ToJsonString());
            await MigrationWriteAsync(source, "config/launcher-settings.json", """{"defaultGameSettingsPreset":"custom-preset"}""");
            await MigrationWriteAsync(leaf, ".hmcl/config/instance-game-settings.json", """{"overrideProperties":[],"parent":"custom-preset"}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(custom, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(source, "config", "game-settings.json")));
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(source, "config", "launcher-settings.json")));
            File.Delete(Path.Combine(leaf, ".hmcl", "config", "instance-game-settings.json"));
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(custom, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
            using var service = LauncherMigrationRuntime.CreateService();
            string destination = Path.Combine(temporary, "imported");
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Hmcl, source, destination));
            await MigrationWriteAsync(source, "config/game-settings.json", """{"presets":[17]}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertTrue(item.UnsupportedReason is not null);
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, [preview.Instances.Single().Id])));
            AssertFalse(Directory.Exists(destination));
            await MigrationWriteAsync(source, "config/game-settings.json", new JsonObject
            { ["presets"] = new JsonArray(new JsonObject { ["id"] = "custom-preset", ["runningDirectory"] = custom }) }.ToJsonString());
            await MigrationWriteAsync(leaf, ".hmcl/config/instance-game-settings.json", "{}");
            await File.WriteAllTextAsync(Path.Combine(leaf, ".hmcl", "config", "instance-game-settings.json"),
                """{"overrideProperties":["runningDirectory"],"runningDirectory":""}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Hmcl, source, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertTrue(item.UnsupportedReason is null);
        }
        finally { Directory.Delete(temporary, true); }
    }
}
