using System.IO.Compression;
using System.Text.Json.Nodes;
using Nexa.Services.Composition;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationMainstreamRecognizesInstalledSchemas()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string carbon = MigrationFormatDirectory(root, "carbon", "instances", "Carbon"), game = MigrationFormatDirectory(carbon, "instance");
            File.WriteAllText(Path.Combine(carbon, "instance.json"), """{"_version":"1","name":"Carbon world","game_configuration":{"version":{"release":"1.21.1","modloaders":[{"type":"Neoforge","version":"21.1.72"}]}}}""");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.GdLauncher, Path.Combine(root, "carbon"), default)).Single();
            AssertEqual(game, item.GameDirectory); AssertEqual("NeoForge", item.Loader); AssertEqual("21.1.72", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.GdLauncher, game, default)).Single();
            AssertEqual("Carbon world", item.Name);

            string legacy = MigrationFormatDirectory(root, "legacy"), leaf = MigrationFormatDirectory(legacy, "instances", "Old world");
            File.WriteAllText(Path.Combine(legacy, "config.json"), """{"language":"en-US"}""");
            File.WriteAllText(Path.Combine(leaf, "config.json"), """{"modloader":["forge","1.20.1","47.2.0",123,456,"curseforge"]}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.GdLauncher, legacy, default)).Single();
            AssertEqual(leaf, item.GameDirectory); AssertEqual("Old world", item.Name); AssertEqual("Forge", item.Loader); AssertEqual("47.2.0", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);

            string ftb = MigrationFormatDirectory(root, "ftb"), meta = MigrationFormatDirectory(ftb, ".ftbapp");
            File.WriteAllText(Path.Combine(ftb, "instance.json"), """{"uuid":"047c3eb3-2b7d-46bc-9236-c0a125f0ec1f","name":"FTB world","mcVersion":"1.20.1","installComplete":true,"version":"pack-build-42","modLoader":"forge-47.2.0"}""");
            File.WriteAllText(Path.Combine(meta, "version.json"), """{"targets":[{"type":"game","name":"minecraft","version":"1.20.1"},{"type":"runtime","name":"java","version":"17"},{"type":"modloader","name":"forge","version":"47.2.0"}]}""");
            File.WriteAllText(Path.Combine(meta, "modifications.json"), """{"modLoaderOverride":{"type":"modloader","name":"fabric","version":"0.16.10"},"overrides":[]}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Ftb, ftb, default)).Single();
            AssertEqual("1.20.1", item.GameVersion); AssertEqual("Fabric", item.Loader); AssertEqual("0.16.10", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null); AssertEqual(3, item.ManifestPaths.Count);

            string technic = MigrationFormatDirectory(root, "technic"), pack = MigrationFormatDirectory(technic, "modpacks", "fabric-pack"), bin = MigrationFormatDirectory(pack, "bin");
            File.WriteAllText(Path.Combine(technic, "installedPacks"), """{"installedPacks":{"fabric-pack":{"name":"fabric-pack","build":"recommended","directory":"%MODPACKS%\\fabric-pack"}}}""");
            File.WriteAllText(Path.Combine(bin, "version"), """{"version":"modpack-build-42","legacy":false}""");
            File.WriteAllText(Path.Combine(bin, "version.json"), """{"id":"fabric-loader-0.16.10-1.20.1","inheritsFrom":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.10"},{"name":"net.fabricmc:intermediary:1.20.1"}]}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single();
            AssertEqual(pack, item.GameDirectory); AssertEqual("1.20.1", item.GameVersion); AssertEqual("Fabric", item.Loader); AssertEqual("0.16.10", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);

            File.Delete(Path.Combine(bin, "version.json"));
            using (FileStream archiveFile = File.Create(Path.Combine(bin, "modpack.jar")))
            using (ZipArchive archive = new(archiveFile, ZipArchiveMode.Create))
            {
                using (StreamWriter writer = new(archive.CreateEntry("install_profile.json").Open()))
                    writer.Write("""{"spec":1,"profile":"forge","version":"1.20.1-forge-47.2.0","minecraft":"1.20.1","json":"/version.json"}""");
                using (StreamWriter writer = new(archive.CreateEntry("version.json").Open()))
                    writer.Write("""{"id":"1.20.1-forge-47.2.0","inheritsFrom":"1.20.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.0"}]}""");
            }
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single();
            AssertEqual("1.20.1", item.GameVersion); AssertEqual("Forge", item.Loader); AssertEqual("47.2.0", item.LoaderBuild); AssertTrue(item.UnsupportedReason is null);
            AssertTrue(item.ManifestPaths.Contains(Path.Combine(bin, "modpack.jar")));
            File.Delete(Path.Combine(bin, "modpack.jar"));
            using (FileStream archiveFile = File.Create(Path.Combine(bin, "minecraft.jar")))
            using (ZipArchive archive = new(archiveFile, ZipArchiveMode.Create))
            using (StreamWriter writer = new(archive.CreateEntry("version.json").Open())) writer.Write("""{"id":"1.21.1","name":"1.21.1","release_target":"1.21.1"}""");
            item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single();
            AssertEqual("1.21.1", item.GameVersion); AssertTrue(item.Loader is null); AssertTrue(item.UnsupportedReason is null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationMainstreamRefusesUnknownRuntime()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string carbon = MigrationFormatDirectory(root, "carbon"); MigrationFormatDirectory(carbon, "instance");
            foreach (string body in new[]
            {
                """{"_version":"2","name":"Future","game_configuration":{"version":{"release":"1.20.1"}}}""",
                """{"_version":"1","name":"Custom","game_configuration":{"version":"private-runtime"}}""",
                """{"_version":"1","name":"Hybrid","game_configuration":{"version":{"release":"1.20.1","modloaders":[{"type":"Forge","version":"47.2.0"},{"type":"Fabric","version":"0.16.10"}]}}}""",
                """{"_version":"1","name":"Dynamic","game_configuration":{"version":{"release":"1.20.1","modloaders":[{"type":"Fabric","version":"latest"}]}}}"""
            })
            {
                File.WriteAllText(Path.Combine(carbon, "instance.json"), body);
                AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.GdLauncher, carbon, default)).Single().UnsupportedReason is not null);
            }
            string ftb = MigrationFormatDirectory(root, "ftb"), meta = MigrationFormatDirectory(ftb, ".ftbapp");
            File.WriteAllText(Path.Combine(ftb, "instance.json"), """{"name":"Partial","mcVersion":"1.20.1","installComplete":false}""");
            File.WriteAllText(Path.Combine(meta, "version.json"), """{"targets":[{"type":"game","version":"1.20.1"}]}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Ftb, ftb, default)).Single().UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(ftb, "instance.json"), """{"name":"Invalid","mcVersion":"1.20.1","installComplete":true}""");
            File.WriteAllText(Path.Combine(meta, "version.json"), """{"targets":[{"type":"game","version":"1.20.1"},{"type":"modloader","name":"private-loader","version":"1.0"}]}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Ftb, ftb, default)).Single().UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(meta, "version.json"), """{"targets":[{"type":"game","version":"1.21.1"}]}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Ftb, ftb, default)).Single().UnsupportedReason is not null);

            string technic = MigrationFormatDirectory(root, "technic"), bin = MigrationFormatDirectory(technic, "bin");
            File.WriteAllText(Path.Combine(bin, "version"), """{"version":"1.20.1","legacy":false}""");
            var item = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single();
            AssertTrue(item.UnsupportedReason is not null); // Pack builds are never used as Minecraft versions.
            using (FileStream archiveFile = File.Create(Path.Combine(bin, "minecraft.jar")))
            using (ZipArchive archive = new(archiveFile, ZipArchiveMode.Create))
            using (StreamWriter writer = new(archive.CreateEntry("version.json").Open())) writer.Write("""{"id":"latest-release"}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single().UnsupportedReason is not null);
            File.Delete(Path.Combine(bin, "minecraft.jar"));
            File.WriteAllText(Path.Combine(bin, "version.json"), "[]");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single().UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(bin, "version.json"), """{"id":"private-runtime","inheritsFrom":"1.20.1","mainClass":"private.bootstrap.Main"}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single().UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(bin, "version.json"), """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"private.runtime:required-patch:1.0"}]}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single().UnsupportedReason is not null);
            File.WriteAllText(Path.Combine(bin, "version.json"), """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main"}""");
            using (FileStream archiveFile = File.Create(Path.Combine(bin, "modpack.jar")))
            using (ZipArchive archive = new(archiveFile, ZipArchiveMode.Create))
            using (StreamWriter writer = new(archive.CreateEntry("custom/Injected.class").Open())) writer.Write("legacy-injection");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single().UnsupportedReason is not null);
            File.Delete(Path.Combine(bin, "modpack.jar")); File.WriteAllText(Path.Combine(bin, "version"), """{"version":"legacy","legacy":true}""");
            AssertTrue((await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Technic, technic, default)).Single().UnsupportedReason is not null);

            string unrelated = MigrationFormatDirectory(root, "unrelated");
            File.WriteAllText(Path.Combine(unrelated, "instance.json"), """{"name":"XMCL","runtime":{"minecraft":"1.20.1"}}""");
            AssertEqual(0, (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.GdLauncher, unrelated, default)).Count);
            AssertEqual(0, (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Ftb, unrelated, default)).Count);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationMainstreamCompletesTransactionalWorkflow()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            for (int family = 0; family < 4; family++)
            {
                string source = MigrationFormatDirectory(root, "source-" + family), destination = Path.Combine(root, "destination-" + family);
                string game, revisionPath; LauncherMigrationSourceKind kind;
                if (family == 0)
                {
                    kind = LauncherMigrationSourceKind.GdLauncher;
                    string runtime = MigrationFormatDirectory(root, "carbon-runtime"), instance = MigrationFormatDirectory(runtime, "instances", "world"); game = MigrationFormatDirectory(instance, "instance");
                    revisionPath = Path.Combine(source, "runtime_path_override"); File.WriteAllText(revisionPath, runtime);
                    File.WriteAllText(Path.Combine(instance, "instance.json"), """{"_version":"1","name":"Carbon world","game_configuration":{"version":{"release":"1.20.1","modloaders":[{"type":"Fabric","version":"0.16.10"}]},"extra_java_args":"-javaagent:private.jar"},"pre_launch_hook":"do-not-run"}""");
                }
                else if (family == 1)
                {
                    kind = LauncherMigrationSourceKind.GdLauncher;
                    string runtime = MigrationFormatDirectory(root, "legacy-runtime"); game = MigrationFormatDirectory(runtime, "instances", "Legacy world");
                    revisionPath = Path.Combine(source, "override.data"); File.WriteAllText(revisionPath, runtime);
                    File.WriteAllText(Path.Combine(runtime, "config.json"), """{"language":"zh-CN"}""");
                    File.WriteAllText(Path.Combine(game, "config.json"), """{"loader":{"loaderType":"fabric","mcVersion":"1.20.1","loaderVersion":"0.16.10"},"javaArgs":"-javaagent:private.jar"}""");
                }
                else if (family == 2)
                {
                    kind = LauncherMigrationSourceKind.Ftb;
                    string instances = MigrationFormatDirectory(root, "ftb-custom-instances"); game = MigrationFormatDirectory(instances, "world");
                    string storage = MigrationFormatDirectory(source, "storage"), meta = MigrationFormatDirectory(game, ".ftbapp");
                    revisionPath = Path.Combine(storage, "settings.json"); File.WriteAllText(revisionPath, new JsonObject { ["instanceLocation"] = instances }.ToJsonString());
                    File.WriteAllText(Path.Combine(game, "instance.json"), """{"name":"FTB world","mcVersion":"1.20.1","installComplete":true,"shellArgs":"do-not-run","jvmArgs":"-javaagent:private.jar"}""");
                    File.WriteAllText(Path.Combine(meta, "version.json"), """{"targets":[{"type":"game","version":"1.20.1"},{"type":"modloader","name":"forge","version":"47.2.0"}]}""");
                    File.WriteAllText(Path.Combine(meta, "modifications.json"), """{"modLoaderOverride":{"type":"modloader","name":"fabric","version":"0.16.10"}}""");
                }
                else
                {
                    kind = LauncherMigrationSourceKind.Technic;
                    string runtime = MigrationFormatDirectory(root, "technic-runtime"); game = MigrationFormatDirectory(runtime, "modpacks", "world");
                    string bin = MigrationFormatDirectory(game, "bin");
                    revisionPath = Path.Combine(source, "settings.json"); File.WriteAllText(revisionPath, new JsonObject { ["directory"] = runtime, ["javaArgs"] = "-javaagent:private.jar" }.ToJsonString());
                    File.WriteAllText(Path.Combine(runtime, "installedPacks"), """{"installedPacks":{"world":{"name":"Technic world","build":"recommended","directory":"%MODPACKS%\\world"}}}""");
                    File.WriteAllText(Path.Combine(bin, "version"), """{"version":"pack-release-42","legacy":false}""");
                    File.WriteAllText(Path.Combine(bin, "version.json"), """{"id":"fabric-loader-0.16.10-1.20.1","inheritsFrom":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.10"}]}""");
                }
                await MigrationWriteAsync(game, "saves/world/level.dat", "world-" + family);
                await MigrationWriteAsync(game, "mods/user.jar", "user-mod-" + family);
                await MigrationWriteAsync(game, "config/settings.toml", "user-setting=true");
                await MigrationWriteAsync(source, "launcher_accounts.json", "private-token");
                await MigrationWriteAsync(game, "launcher_accounts.json", "private-instance-token");
                List<LauncherMigrationInstallRequest> requests = [];
                using var service = LauncherMigrationRuntime.CreateService(async (request, token) =>
                {
                    requests.Add(request); AssertEqual("1.20.1", request.GameVersion); AssertEqual("Fabric", request.Loader); AssertEqual("0.16.10", request.LoaderBuild);
                    await MigrationWriteAsync(request.DestinationRoot, "versions/" + request.InstanceId + "/" + request.InstanceId + ".json",
                        "{\"id\":\"" + request.InstanceId + "\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\"}", token);
                });
                var preview = await service.PreviewAsync(new(kind, source, destination));
                AssertEqual(1, preview.Instances.Count); AssertTrue(preview.Instances.Single().CanImport, preview.Instances.Single().UnsupportedReason ?? "mainstream fixture");
                File.AppendAllText(revisionPath, "\n");
                await MigrationFailsAsync(() => service.ApplyAsync(new(preview, [preview.Instances.Single().Id])));
                AssertFalse(Directory.Exists(destination)); AssertEqual(0, requests.Count);
                preview = await service.PreviewAsync(preview.Query);
                var receipt = await service.ApplyAsync(new(preview, [preview.Instances.Single().Id]));
                AssertEqual(1, requests.Count);
                var imported = receipt.Instances.Single(); string importedGame = Path.Combine(imported.MinecraftRootDirectory, "versions", imported.InstanceId);
                AssertEqual("world-" + family, File.ReadAllText(Path.Combine(importedGame, "saves", "world", "level.dat")));
                AssertEqual("user-mod-" + family, File.ReadAllText(Path.Combine(importedGame, "mods", "user.jar")));
                AssertEqual("user-setting=true", File.ReadAllText(Path.Combine(importedGame, "config", "settings.toml")));
                AssertFalse(File.Exists(Path.Combine(importedGame, "launcher_accounts.json"))); AssertFalse(Directory.Exists(Path.Combine(importedGame, "bin")));
                using var restarted = LauncherMigrationRuntime.CreateService();
                var reopened = await restarted.ReadReceiptAsync(destination); AssertEqual(receipt.Identity, reopened.Identity);
                await restarted.UndoAsync(reopened); AssertFalse(Directory.Exists(destination));
                AssertEqual("private-token", File.ReadAllText(Path.Combine(source, "launcher_accounts.json")));
                AssertEqual("private-instance-token", File.ReadAllText(Path.Combine(game, "launcher_accounts.json")));
                AssertEqual("world-" + family, File.ReadAllText(Path.Combine(game, "saves", "world", "level.dat")));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
