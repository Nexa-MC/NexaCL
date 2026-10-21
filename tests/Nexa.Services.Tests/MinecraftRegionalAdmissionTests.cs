using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Xsr;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask MainlandPackUpdateReusesVerifiedLocalBaseWithoutTransfers()
    {
        string temporary = CreateTempDirectory();
        try
        {
            string root = Path.Combine(temporary, "game"), first = Path.Combine(temporary, "old.mrpack"), next = Path.Combine(temporary, "new.mrpack");
            Directory.CreateDirectory(root);
            WriteUpdatePack(first, "1.0", false); WriteUpdatePack(next, "2.0", true);
            var original = await MinecraftModpackArchive.InspectAsync(first);
            string instance;
            var metadata = PackMetadata();
            metadata.HasVerifiedIndexBytes = true;
            using (var international = new InstallFixture(metadata))
            {
                var installed = await international.Install.InstallModpackAsync(new(original, root));
                if (!installed.IsSuccess) throw new InvalidOperationException(installed.Error?.Message);
                instance = installed.Value!.InstanceDirectory;
            }
            int transfers = 0;
            using var mainland = new InstallFixture(metadata, countryPolicy: new("CN"), connectionFactory: url =>
            { transfers++; return new ServingConnection(PayloadFor(url)); });
            var preview = await mainland.Install.PreviewModpackUpdateAsync(new(instance, next));
            var result = await mainland.Install.UpdateModpackAsync(new(preview));
            if (!result.IsSuccess) throw new InvalidOperationException(result.Error?.Message);
            AssertEqual(0, transfers);
            AssertTrue(File.Exists(Path.Combine(instance, "mods", "main-2.jar")));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask MainlandRejectsStandaloneGameBeforeNetwork()
    {
        string root = CreateTempDirectory();
        try
        {
            var metadata = new FakeMetadata();
            using var fixture = new InstallFixture(metadata, countryPolicy: new("CN"));
            var result = await fixture.Install.InstallAsync(new(root, "1.20.1"));
            AssertFalse(result.IsSuccess);
            AssertEqual(XsrErrorKind.Rejected, result.Error!.Kind);
            AssertEqual("minecraft.download_region_unavailable", result.Error.Code.Value);
            AssertEqual(0, metadata.VanillaReads);
            AssertFalse(Directory.Exists(Path.Combine(root, "versions")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask MainlandInstallsModpacksWithRequiredGameFiles()
    {
        string temporary = CreateTempDirectory();
        try
        {
            foreach (string extension in new[] { "mrpack", "nexapack" })
            {
                string archive = Path.Combine(temporary, "pack." + extension), root = Path.Combine(temporary, extension);
                Directory.CreateDirectory(root);
                WriteLocalJar(archive, ("modrinth.index.json", MrpackIndex().ToJsonString()), ("overrides/config/test.txt", "pack configuration"));
                var pack = await MinecraftModpackArchive.InspectAsync(archive);
                var metadata = PackMetadata();
                using var fixture = new InstallFixture(metadata, countryPolicy: new("CN"));
                var result = await fixture.Install.InstallModpackAsync(new(pack, root, IncludeOptional: true));
                AssertTrue(result.IsSuccess, result.Error?.Message ?? "Mainland pack installation failed.");
                string instance = result.Value!.InstanceDirectory;
                AssertEqual("JARCONTENT", await File.ReadAllTextAsync(Path.Combine(root, "versions", pack.Game, pack.Game + ".jar")));
                AssertEqual("JARCONTENT", await File.ReadAllTextAsync(Path.Combine(root, "libraries/com/example/library/1.0.0/library-1.0.0.jar")));
                AssertEqual("ASSET!", await File.ReadAllTextAsync(Path.Combine(root, "assets/objects", Sha1Hex("ASSET!")[..2], Sha1Hex("ASSET!"))));
                AssertTrue(File.Exists(Path.Combine(root, "assets/indexes/5.json")));
                AssertTrue(File.Exists(Path.Combine(instance, "mods/required.jar")));
                AssertTrue(File.Exists(Path.Combine(instance, "mods/optional.jar")));
                AssertEqual("pack configuration", await File.ReadAllTextAsync(Path.Combine(instance, "config/test.txt")));
                AssertEqual(1, fixture.InstalledRoots.Count);
                AssertEqual(Nexa.Services.Tasks.TaskCenterEntryState.Finished, fixture.Entry().State);
                int reads = metadata.VanillaReads;
                var standalone = await fixture.Install.InstallAsync(new(root, "1.20.1"));
                AssertFalse(standalone.IsSuccess);
                AssertEqual("minecraft.download_region_unavailable", standalone.Error!.Code.Value);
                AssertEqual(reads, metadata.VanillaReads);
                AssertEqual(1, fixture.Tasks.ReadEntries().Count);
            }
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask MainlandCompletionUsesLocalGameAndRejectsEveryMissingBaseKind()
    {
        using var fixture = new CompletionFixture(countryPolicy: new("CN"));
        const string payload = "REPAIRED";
        string hash = Sha1Hex(payload);
        string game = Path.Combine(fixture.Root, "versions", "1.20.1");
        var index = new JsonObject
        {
            ["objects"] = new JsonObject
            {
                ["minecraft/sounds/test.ogg"] = new JsonObject { ["hash"] = hash, ["size"] = payload.Length },
            }
        }.ToJsonString();
        string indexPath = Path.Combine(fixture.Root, "assets", "indexes", "5.json");
        string clientPath = Path.Combine(game, "1.20.1.jar");
        string libraryPath = Path.Combine(fixture.Root, "libraries", "example", "base", "1", "base-1.jar");
        string objectPath = Path.Combine(fixture.Root, "assets", "objects", hash[..2], hash);
        var version = JsonNode.Parse("""
            {"id":"1.20.1","assetIndex":{"id":"5","url":"https://piston-meta.mojang.com/index/5.json"},
             "downloads":{"client":{"url":"https://piston-data.mojang.com/client.jar"}},
             "libraries":[{"name":"example:base:1","downloads":{"artifact":{"path":"example/base/1/base-1.jar",
               "url":"https://libraries.minecraft.net/example/base.jar"}}}]}
            """)!.AsObject();
        version["assetIndex"]!["sha1"] = Sha1Hex(index); version["assetIndex"]!["size"] = Encoding.UTF8.GetByteCount(index);
        version["downloads"]!["client"]!["sha1"] = hash; version["downloads"]!["client"]!["size"] = payload.Length;
        version["libraries"]![0]!["downloads"]!["artifact"]!["sha1"] = hash;
        version["libraries"]![0]!["downloads"]!["artifact"]!["size"] = payload.Length;
        foreach (var path in new[] { indexPath, clientPath, libraryPath, objectPath }) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(Path.Combine(game, "1.20.1.json"), version.ToJsonString());
        await File.WriteAllTextAsync(indexPath, index);
        foreach (var path in new[] { clientPath, libraryPath, objectPath }) await File.WriteAllTextAsync(path, payload);
        var instance = new MinecraftInstanceDescriptor("1.20.1", game, "1.20.1",
            new MinecraftVersionDescriptor("1.20.1", game, Path.Combine(game, "1.20.1.json"), null, null, null, null,
                new MinecraftVersionClassification("1.20.1", "release", MinecraftVersionCategory.Release, null)), new MinecraftInstanceMetadata());
        var manifests = await MinecraftVersionJsonReader.ResolveAsync(instance, fixture.Root);
        foreach (string missing in new[] { clientPath, indexPath, libraryPath, objectPath })
        {
            File.Delete(missing);
            bool rejected = false;
            try { await Complete(); }
            catch (InvalidDataException error) { rejected = error.Message == RegionalPolicy.MinecraftDownloadUnavailable; }
            AssertTrue(rejected);
            AssertEqual(0, fixture.RequestedUrls.Count);
            AssertFalse(File.Exists(missing));
            await File.WriteAllTextAsync(missing, missing == indexPath ? index : payload);
            await Complete();
            AssertEqual(0, fixture.RequestedUrls.Count);
        }

        // Loader-only repair remains available once every base-game file is present.
        string loaderPath = Path.Combine(fixture.Root, "libraries", "loader", "extra", "1", "extra-1.jar");
        var loader = JsonNode.Parse("""
            {"id":"fabric","inheritsFrom":"1.20.1","libraries":[{"name":"loader:extra:1",
              "downloads":{"artifact":{"path":"loader/extra/1/extra-1.jar","url":"https://maven.fabricmc.net/extra.jar"}}}]}
            """)!.AsObject();
        loader["libraries"]![0]!["downloads"]!["artifact"]!["sha1"] = hash;
        loader["libraries"]![0]!["downloads"]!["artifact"]!["size"] = payload.Length;
        manifests = new(loader, [version]);
        await Complete();
        AssertEqual(payload, await File.ReadAllTextAsync(loaderPath));
        AssertEqual(1, fixture.RequestedUrls.Count);

        Task Complete() => fixture.Completion.CompleteAsync(fixture.Root, instance, manifests,
            new MinecraftLaunchPlatform(MinecraftLibraryOperatingSystem.Win32, "10.0", true, false), "offline", fixture.Progress, default).AsTask();
    }
}
