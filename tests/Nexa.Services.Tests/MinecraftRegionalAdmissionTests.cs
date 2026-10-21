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

    private static async Task SeedLocalPackBaseAsync(string root)
    {
        string hash = Sha1Hex("ASSET!");
        foreach (var (relative, content) in new[]
        {
            ("versions/1.20.1/1.20.1.json", VanillaJson().ToJsonString()),
            ("versions/1.20.1/1.20.1.jar", "JARCONTENT"),
            ("libraries/com/example/library/1.0.0/library-1.0.0.jar", "JARCONTENT"),
            ("assets/indexes/5.json", AssetIndexJson().ToJsonString()),
            ($"assets/objects/{hash[..2]}/{hash}", "ASSET!")
        })
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }
    }

    private static async ValueTask RegionalModpacksReuseMainlandBaseAndDownloadInternationally()
    {
        string temporary = CreateTempDirectory();
        try
        {
            foreach (string country in new[] { "CN", "US" })
                foreach (string extension in new[] { "mrpack", "nexapack" })
                {
                    string archive = Path.Combine(temporary, "pack." + extension), root = Path.Combine(temporary, country + extension);
                    Directory.CreateDirectory(root);
                    if (country == "CN") await SeedLocalPackBaseAsync(root);
                    if (File.Exists(archive)) File.Delete(archive);
                    WriteLocalJar(archive, ("modrinth.index.json", MrpackIndex().ToJsonString()), ("overrides/config/test.txt", "pack configuration"));
                    var pack = await MinecraftModpackArchive.InspectAsync(archive);
                    var metadata = PackMetadata();
                    var transfers = new System.Collections.Concurrent.ConcurrentQueue<string>();
                    using var fixture = new InstallFixture(metadata, countryPolicy: new(country), connectionFactory: url =>
                    { transfers.Enqueue(url); return new ServingConnection(PayloadFor(url)); });
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
                    AssertEqual(1, metadata.VanillaReads);
                    AssertFalse(transfers.IsEmpty);
                    AssertEqual(country == "CN", transfers.All(url => url.StartsWith("https://cdn.modrinth.com/", StringComparison.Ordinal)));
                    AssertEqual(Nexa.Services.Tasks.TaskCenterEntryState.Finished, fixture.Entry().State);
                    if (country == "CN")
                    {
                        int reads = metadata.VanillaReads;
                        var standalone = await fixture.Install.InstallAsync(new(root, "1.20.1"));
                        AssertFalse(standalone.IsSuccess);
                        AssertEqual("minecraft.download_region_unavailable", standalone.Error!.Code.Value);
                        AssertEqual(reads, metadata.VanillaReads);
                        AssertEqual(1, fixture.Tasks.ReadEntries().Count);
                    }
                }
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask MainlandModpacksPromptForMissingOrCorruptBaseBeforeTransfers()
    {
        string temporary = CreateTempDirectory();
        try
        {
            foreach (string scenario in new[] { "missing", "wrong-version", "client-missing", "client-corrupt", "library-missing", "library-corrupt", "index-missing", "index-corrupt", "asset-missing", "asset-corrupt" })
            {
                string root = Path.Combine(temporary, scenario), archive = Path.Combine(root, "pack.mrpack");
                Directory.CreateDirectory(root);
                WriteLocalJar(archive, ("modrinth.index.json", MrpackIndex().ToJsonString()));
                if (scenario != "missing") await SeedLocalPackBaseAsync(root);
                if (scenario == "wrong-version") Directory.Move(Path.Combine(root, "versions/1.20.1"), Path.Combine(root, "versions/1.20.2"));
                string hash = Sha1Hex("ASSET!");
                string? damaged = scenario.Split('-')[0] switch
                {
                    "client" => "versions/1.20.1/1.20.1.jar",
                    "library" => "libraries/com/example/library/1.0.0/library-1.0.0.jar",
                    "index" => "assets/indexes/5.json",
                    "asset" => $"assets/objects/{hash[..2]}/{hash}",
                    _ => null
                };
                if (damaged is not null)
                {
                    string path = Path.Combine(root, damaged);
                    if (scenario.EndsWith("-missing", StringComparison.Ordinal)) File.Delete(path);
                    else await File.WriteAllTextAsync(path, scenario == "asset-corrupt" ? "WRONG!" : scenario == "index-corrupt"
                        ? AssetIndexJson().ToJsonString().Replace("click.ogg", "clack.ogg", StringComparison.Ordinal) : "BADCONTENT");
                }
                var metadata = PackMetadata();
                var transfers = new System.Collections.Concurrent.ConcurrentQueue<string>();
                using var fixture = new InstallFixture(metadata, countryPolicy: new("CN"), connectionFactory: url =>
                { transfers.Enqueue(url); return new ServingConnection(PayloadFor(url)); });
                var pack = await MinecraftModpackArchive.InspectAsync(archive);
                var result = await fixture.Install.InstallModpackAsync(new(pack, root));
                AssertFalse(result.IsSuccess);
                AssertEqual(XsrErrorKind.Rejected, result.Error!.Kind);
                AssertEqual("minecraft.modpack_base_required", result.Error.Code.Value);
                AssertTrue(result.Error.Message.Contains(pack.Game, StringComparison.Ordinal));
                AssertTrue(result.Error.Message.Contains("Minecraft Launcher", StringComparison.Ordinal));
                AssertEqual(0, transfers.Count);
                AssertEqual(0, metadata.LoaderReads);
                AssertFalse(Directory.Exists(Path.Combine(root, "versions", pack.InstanceId)));
                AssertEqual(0, fixture.InstalledRoots.Count);
                AssertTrue(fixture.Entry().CanRetry);
                if (scenario is "missing" or "wrong-version" or "client-missing") AssertEqual(0, metadata.VanillaReads);
                if (scenario == "missing")
                {
                    await SeedLocalPackBaseAsync(root);
                    File.Delete(archive);
                    string id = fixture.Entry().TaskId;
                    AssertTrue(fixture.Tasks.RequestRetry(id));
                    await WaitForRetryEntryAsync(fixture.Tasks, id, entry => entry.State == Nexa.Services.Tasks.TaskCenterEntryState.Finished);
                    AssertTrue(transfers.All(url => url.StartsWith("https://cdn.modrinth.com/", StringComparison.Ordinal)));
                    AssertTrue(File.Exists(Path.Combine(root, "versions", pack.InstanceId, "mods/required.jar")));
                }
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
