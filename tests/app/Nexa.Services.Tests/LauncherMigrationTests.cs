using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Composition;
using Nexa.Services.Minecraft;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationCopiesNativeInheritanceAndData()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "migrated");
            await MigrationWriteAsync(source, "versions/base/base.json", """{"id":"base","mainClass":"net.minecraft.client.main.Main","jar":"client-alias"}""");
            await MigrationWriteAsync(source, "versions/base/base.jar", "client");
            await MigrationWriteAsync(source, "versions/client-alias/client-alias.jar", "aliased-client");
            await MigrationWriteAsync(source, "versions/custom/custom.json", """{"id":"custom","inheritsFrom":"base"}""");
            await MigrationWriteAsync(source, "versions/custom/Nexa/InstanceMetadata.json", """{"instanceIsolation":true,"preLaunchCommand":"do-not-run","jvmArguments":"-javaagent:private.jar","displayName":"Unsafe old settings"}""");
            await MigrationWriteAsync(source, "versions/custom/config/settings.json", "leaf-config");
            await MigrationWriteAsync(source, "config/settings.json", "shared-config");
            await MigrationWriteAsync(source, "saves/world/level.dat", "shared-world");
            await MigrationWriteAsync(source, "versions/custom/saves/world/level.dat", "world");
            await MigrationWriteAsync(source, "libraries/group/example.jar", "library");
            await MigrationWriteAsync(source, "assets/objects/ab/abcdef", "asset");
            await MigrationWriteAsync(source, "options.txt", "music:0.5");
            await MigrationWriteAsync(source, "launcher_accounts.json", "private-token");
            await MigrationWriteAsync(source, "old-launcher.exe", "do-not-copy");
            Directory.CreateDirectory(Path.Combine(source, "versions", "custom", "screenshots", "empty"));
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Nexa, source, destination));
            var selected = preview.Instances.Single(item => item.Name == "custom");
            AssertTrue(selected.CanImport, selected.UnsupportedReason ?? "native");
            var receipt = await service.ApplyAsync(new(preview, [selected.Id]));
            var imported = receipt.Instances.Single();
            string root = imported.MinecraftRootDirectory, game = Path.Combine(root, "versions", "custom");
            AssertEqual("leaf-config", await File.ReadAllTextAsync(Path.Combine(game, "config", "settings.json")));
            AssertEqual("world", await File.ReadAllTextAsync(Path.Combine(game, "saves", "world", "level.dat")));
            AssertEqual("library", await File.ReadAllTextAsync(Path.Combine(root, "libraries", "group", "example.jar")));
            AssertEqual("client", await File.ReadAllTextAsync(Path.Combine(root, "versions", "base", "base.jar")));
            AssertEqual("aliased-client", await File.ReadAllTextAsync(Path.Combine(root, "versions", "client-alias", "client-alias.jar")));
            AssertEqual("asset", await File.ReadAllTextAsync(Path.Combine(root, "assets", "objects", "ab", "abcdef")));
            AssertTrue(Directory.Exists(Path.Combine(game, "screenshots", "empty")));
            AssertFalse(File.Exists(Path.Combine(root, "launcher_accounts.json")));
            AssertFalse(File.Exists(Path.Combine(root, "old-launcher.exe")));
            var metadata = await new MinecraftInstanceMetadataStore().LoadAsync(game);
            AssertEqual("custom", metadata.DisplayName); AssertTrue(metadata.InstanceIsolation);
            AssertEqual("", metadata.PreLaunchCommand); AssertEqual("", metadata.JvmArguments);
            var discovered = await new MinecraftInstanceDiscovery().DiscoverAsync(root);
            AssertTrue(discovered.Any(item => item.Id == "custom"));
            using var restarted = LauncherMigrationRuntime.CreateService();
            var reopened = await restarted.ReadReceiptAsync(destination);
            AssertEqual(receipt.Identity, reopened.Identity);
            await service.UndoAsync(reopened);
            AssertFalse(Directory.Exists(destination));
            AssertEqual("private-token", await File.ReadAllTextAsync(Path.Combine(source, "launcher_accounts.json")));
            AssertTrue(File.Exists(Path.Combine(source, "saves", "world", "level.dat")));

            string sourceMetadata = Path.Combine(source, "versions", "custom", "Nexa", "InstanceMetadata.json");
            File.Delete(sourceMetadata);
            preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Nexa, source, destination));
            AssertFalse(preview.Instances.Single(item => item.Name == "custom").CanImport);
            await File.WriteAllTextAsync(sourceMetadata, """{"instanceIsolation":false}""");
            preview = await service.PreviewAsync(preview.Query);
            selected = preview.Instances.Single(item => item.Name == "custom");
            AssertTrue(selected.CanImport, selected.UnsupportedReason ?? "shared game data");
            receipt = await service.ApplyAsync(new(preview, [selected.Id]));
            game = Path.Combine(receipt.Instances.Single().MinecraftRootDirectory, "versions", "custom");
            AssertEqual("shared-world", await File.ReadAllTextAsync(Path.Combine(game, "saves", "world", "level.dat")));
            AssertEqual("shared-config", await File.ReadAllTextAsync(Path.Combine(game, "config", "settings.json")));
            await service.UndoAsync(receipt);
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationConvertsThroughRealInstallationPort()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "prism"), destination = Path.Combine(temporary, "migrated");
            await MigrationWriteAsync(source, "instance.cfg", "[General]\nname=Fabric World\nPreLaunchCommand=do-not-run\n");
            await MigrationWriteAsync(source, "mmc-pack.json", """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.16.10"}]}""");
            await MigrationWriteAsync(source, "minecraft/mods/user.jar", "mod-data");
            await MigrationWriteAsync(source, "minecraft/config/config.toml", "setting=true");
            List<LauncherMigrationInstallRequest> requests = [];
            using var service = LauncherMigrationRuntime.CreateService(async (request, token) =>
            {
                requests.Add(request); AssertEqual("1.20.1", request.GameVersion); AssertEqual("Fabric", request.Loader);
                AssertEqual("0.16.10", request.LoaderBuild);
                await MigrationWriteAsync(request.DestinationRoot, "versions/" + request.InstanceId + "/" + request.InstanceId + ".json",
                    "{\"id\":\"" + request.InstanceId + "\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\"}", token);
            });
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Auto, source, destination));
            var receipt = await service.ApplyAsync(new(preview, [preview.Instances.Single().Id]));
            AssertEqual(1, requests.Count);
            string game = Path.Combine(receipt.Instances.Single().MinecraftRootDirectory, "versions", receipt.Instances.Single().InstanceId);
            AssertEqual("mod-data", await File.ReadAllTextAsync(Path.Combine(game, "mods", "user.jar")));
            AssertEqual("setting=true", await File.ReadAllTextAsync(Path.Combine(game, "config", "config.toml")));
            AssertFalse(File.Exists(Path.Combine(game, "instance.cfg")));
            await service.UndoAsync(receipt);

            // Current database profiles can have names longer than the JSON launchers' name bounds.
            string modrinth = Path.Combine(temporary, "modrinth"), longName = new('n', 300);
            Directory.CreateDirectory(Path.Combine(modrinth, "profiles", "long-name"));
            using (var database = new MigrationSqliteFixture(Path.Combine(modrinth, "app.db")))
                database.Execute("CREATE TABLE profiles(path TEXT,name TEXT,game_version TEXT,mod_loader TEXT,mod_loader_version TEXT,install_stage TEXT); "
                    + "INSERT INTO profiles VALUES('long-name','" + longName + "','1.20.1','fabric','0.16.10','installed');");
            preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Modrinth, modrinth, destination));
            AssertEqual(longName, preview.Instances.Single().Name);
            receipt = await service.ApplyAsync(new(preview, [preview.Instances.Single().Id]));
            AssertEqual(longName, (await service.ReadReceiptAsync(destination)).Instances.Single().Name);
            await service.UndoAsync(receipt);
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationRejectsSourceChangesAndExistingTargets()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "migrated");
            await MigrationWriteAsync(source, "versions/test/test.json", """{"id":"test","mainClass":"main"}""");
            await MigrationWriteAsync(source, "options.txt", "old");
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Minecraft, source, destination));
            DateTime stamp = File.GetLastWriteTimeUtc(Path.Combine(source, "options.txt"));
            await File.WriteAllTextAsync(Path.Combine(source, "options.txt"), "new"); File.SetLastWriteTimeUtc(Path.Combine(source, "options.txt"), stamp);
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, [preview.Instances.Single().Id])));
            AssertFalse(Directory.Exists(destination));
            preview = await service.PreviewAsync(preview.Query);
            Directory.CreateDirectory(destination); await File.WriteAllTextAsync(Path.Combine(destination, "keep"), "original");
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, [preview.Instances.Single().Id])));
            AssertEqual("original", await File.ReadAllTextAsync(Path.Combine(destination, "keep")));
            AssertEqual(0, Directory.GetDirectories(temporary, ".nexa-migration-stage-*").Length);
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationCancellationCleansOnlyOwnedStaging()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "migrated");
            await MigrationWriteAsync(source, "versions/test/test.json", """{"id":"test","mainClass":"main"}""");
            await MigrationWriteAsync(source, "saves/world/level.dat", "keep-source");
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Auto, source, destination));
            using var canceled = new CancellationTokenSource();
            var progress = new MigrationProgress(value => { if (value.Stage == "复制游戏数据") canceled.Cancel(); });
            try
            {
                await service.ApplyAsync(new(preview, [preview.Instances.Single().Id]), progress, canceled.Token);
                throw new InvalidOperationException("Cancellation was ignored.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
            AssertFalse(Directory.Exists(destination));
            AssertEqual(0, Directory.GetDirectories(temporary, ".nexa-migration-stage-*").Length);
            AssertEqual("keep-source", await File.ReadAllTextAsync(Path.Combine(source, "saves", "world", "level.dat")));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationUndoRefusesModifiedOrAddedContent()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "migrated");
            await MigrationWriteAsync(source, "versions/test/test.json", """{"id":"test","mainClass":"main"}""");
            await MigrationWriteAsync(source, "options.txt", "old");
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Auto, source, destination));
            var receipt = await service.ApplyAsync(new(preview, [preview.Instances.Single().Id]));
            await MigrationFailsAsync(() => service.UndoAsync(receipt with { DestinationDirectory = "relative" }));
            await MigrationFailsAsync(() => service.UndoAsync(receipt with { Instances = [] }));
            string options = Path.Combine(receipt.Instances.Single().MinecraftRootDirectory, "versions", "test", "options.txt");
            await File.WriteAllTextAsync(options, "new");
            await MigrationFailsAsync(() => service.UndoAsync(receipt));
            AssertEqual("new", await File.ReadAllTextAsync(options));
            await File.WriteAllTextAsync(options, "old");
            string added = Path.Combine(destination, "user-empty-directory"); Directory.CreateDirectory(added);
            await MigrationFailsAsync(() => service.UndoAsync(receipt)); AssertTrue(Directory.Exists(added));
            Directory.Delete(added); await service.UndoAsync(receipt).WaitAsync(TimeSpan.FromSeconds(5)); AssertFalse(Directory.Exists(destination));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationRefusesInvalidSelectionAndUnsafeManifests()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "migrated");
            await MigrationWriteAsync(source, "versions/test/test.json", """{"id":"test","mainClass":"main","arguments":{"jvm":["-javaagent:private.jar"]}}""");
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Auto, source, destination));
            AssertFalse(preview.Instances.Single().CanImport);
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, [preview.Instances.Single().Id])));
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, [])));
            await MigrationFailsAsync(() => service.ApplyAsync(new(preview, ["foreign-id"])));
            AssertFalse(Directory.Exists(destination));
            await MigrationFailsAsync(() => service.PreviewAsync(new(LauncherMigrationSourceKind.Auto, source, Path.Combine(source, "nested"))));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationReceiptRequiresExternalOwnership()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "real-import");
            await MigrationWriteAsync(source, "versions/test/test.json", """{"id":"test","mainClass":"main"}""");
            await MigrationWriteAsync(source, "saves/world/level.dat", "keep-world");
            using var service = LauncherMigrationRuntime.CreateService();
            var preview = await service.PreviewAsync(new(LauncherMigrationSourceKind.Auto, source, destination));
            var receipt = await service.ApplyAsync(new(preview, [preview.Instances.Single().Id]));
            using var restarted = LauncherMigrationRuntime.CreateService();
            AssertEqual(receipt.Identity, (await restarted.ReadReceiptAsync(destination)).Identity);

            // Self-consistent content and a fresh GUID cannot claim ownership of another directory.
            string forged = Path.Combine(temporary, "foreign-directory");
            foreach (string file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(forged, Path.GetRelativePath(destination, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            string forgedPath = Path.Combine(forged, ".nexa-launcher-migration.json");
            var document = JsonNode.Parse(await File.ReadAllTextAsync(forgedPath))!.AsObject();
            document["destination"] = forged; string nonce = Guid.NewGuid().ToString("N"); document["identity"] = nonce;
            byte[] forgedBytes = Encoding.UTF8.GetBytes(document.ToJsonString()); await File.WriteAllBytesAsync(forgedPath, forgedBytes);
            var forgedReceipt = receipt with
            {
                DestinationDirectory = forged,
                Identity = nonce + ":" + Convert.ToHexString(SHA256.HashData(forgedBytes)),
                Instances = receipt.Instances.Select(item => item with
                { MinecraftRootDirectory = Path.Combine(forged, Path.GetRelativePath(destination, item.MinecraftRootDirectory)) }).ToArray()
            };
            await MigrationFailsAsync(() => restarted.ReadReceiptAsync(forged));
            await MigrationFailsAsync(() => restarted.UndoAsync(forgedReceipt));
            AssertTrue(File.Exists(forgedPath)); AssertTrue(Directory.Exists(destination));

            using var missingAuthority = new LauncherMigrationService();
            string unowned = Path.Combine(temporary, "no-authority");
            var unownedPreview = await missingAuthority.PreviewAsync(preview.Query with { DestinationDirectory = unowned });
            await MigrationFailsAsync(() => missingAuthority.ApplyAsync(new(unownedPreview, [unownedPreview.Instances.Single().Id])));
            await MigrationFailsAsync(() => missingAuthority.ReadReceiptAsync(destination));
            await MigrationFailsAsync(() => missingAuthority.UndoAsync(receipt));
            AssertFalse(Directory.Exists(unowned));

            string denied = Path.Combine(temporary, "denied-publication"); bool attempted = false;
            using var deniedService = new LauncherMigrationService(receiptAuthority: new MigrationTestAuthority((path, bytes, _) =>
            {
                attempted = true; AssertEqual(Path.Combine(denied, ".nexa-launcher-migration.json"), path);
                AssertFalse(Directory.Exists(denied)); AssertTrue(bytes.Length > 0);
                return Task.FromException(new IOException("Fixture authorization failure."));
            }));
            var deniedPreview = await deniedService.PreviewAsync(preview.Query with { DestinationDirectory = denied });
            await MigrationFailsAsync(() => deniedService.ApplyAsync(new(deniedPreview, [deniedPreview.Instances.Single().Id])));
            AssertTrue(attempted); AssertFalse(Directory.Exists(denied));

            string canceledDestination = Path.Combine(temporary, "canceled-publication");
            using var canceled = new CancellationTokenSource();
            using var canceledService = new LauncherMigrationService(receiptAuthority: new MigrationTestAuthority((_, _, token) =>
            { canceled.Cancel(); return Task.FromCanceled(token); }));
            var canceledPreview = await canceledService.PreviewAsync(preview.Query with { DestinationDirectory = canceledDestination });
            try
            {
                await canceledService.ApplyAsync(new(canceledPreview, [canceledPreview.Instances.Single().Id]), token: canceled.Token);
                throw new InvalidOperationException("Canceled authorization was published.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
            AssertFalse(Directory.Exists(canceledDestination));
            AssertEqual(0, Directory.GetDirectories(temporary, ".nexa-migration-stage-*").Length);
            AssertEqual("keep-world", await File.ReadAllTextAsync(Path.Combine(source, "saves", "world", "level.dat")));
            await restarted.UndoAsync(receipt); AssertFalse(Directory.Exists(destination)); AssertTrue(Directory.Exists(forged));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private sealed class MigrationTestAuthority(Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> authorize)
        : ILauncherMigrationReceiptAuthority
    {
        public Task AuthorizeAsync(string receiptPath, ReadOnlyMemory<byte> bytes, CancellationToken token) => authorize(receiptPath, bytes, token);
        public void Verify(string receiptPath, ReadOnlyMemory<byte> bytes) => throw new IOException("Fixture receipt was never authorized.");
    }

    private static string CreateLauncherMigrationTempDirectory()
    {
        string path = Path.Combine(LauncherMigrationFiles.TemporaryRoot(), "nexa-migration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static async Task MigrationWriteAsync(string root, string relative, string content, CancellationToken token = default)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, content, token);
    }

    private static async Task MigrationFailsAsync(Func<Task> operation)
    {
        try { await operation(); throw new InvalidOperationException("Expected migration rejection."); }
        catch (Exception error) when (error is IOException or InvalidDataException) { }
    }

    private sealed class MigrationProgress(Action<LauncherMigrationProgress> report) : IProgress<LauncherMigrationProgress>
    { public void Report(LauncherMigrationProgress value) => report(value); }
}
