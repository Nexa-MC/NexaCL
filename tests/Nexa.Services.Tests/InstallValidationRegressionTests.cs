using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Nexa.Services.Downloads;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Resources;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask NestedJavaLibrariesDoNotRejectValidModInstalls()
    {
        string root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            using var library = new MemoryStream();
            using (var archive = new ZipArchive(library, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("example/Library.class").Open()))
                writer.Write("non-executable metadata fixture");
            string path = Path.Combine(root, "mods", "bundled.jar");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("META-INF/mods.toml").Open()))
                    writer.Write("[[mods]]\nmodId=\"parent\"\nversion=\"1.0.0\"\n");
                using (var writer = new StreamWriter(archive.CreateEntry("META-INF/jarjar/metadata.json").Open()))
                    writer.Write("""{"jars":[{"path":"META-INF/jarjar/library.jar","identifier":{"group":"example","artifact":"library"},"version":{"range":"[1,2)","artifactVersion":"1.0"}}]}""");
                using var output = archive.CreateEntry("META-INF/jarjar/library.jar").Open();
                output.Write(library.ToArray());
            }
            var inventory = await LaunchModInventoryReader.ReadAsync(root);
            AssertTrue(inventory.Complete);
            AssertEqual(0, inventory.UnknownFiles);
            AssertEqual(1, inventory.Mods.Count);
            ResourceDependencyVerifier.Verify(inventory, new([], 0, true));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void ContentUpdatesDoNotRequireUnrelatedLegacyMetadata()
    {
        LaunchModIdentity Mod(string id, string version, bool complete = true, string? required = null, string? range = null)
            => new(id, version, "fixture", true, required is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [required] = range ?? "*" }, complete);
        var legacy = Mod("legacy", "1.0", false);
        var unrelatedBroken = Mod("unrelated", "1.0", required: "already-missing");
        LaunchModIdentity old = Mod("main", "1.0"), updated = Mod("main", "2.0");
        var retained = new LaunchModInventory([legacy, unrelatedBroken], 1, false);
        AssertTrue(ResourceDependencyVerifier.VerifyUpdate(new([updated], 0, true), retained, new([legacy, unrelatedBroken, old], 1, false)) is not null);
        var consumer = Mod("consumer", "1.0", required: "main", range: "[1,2)");
        AssertThrows<InvalidDataException>(() => ResourceDependencyVerifier.VerifyUpdate(new([updated], 0, true), new([consumer], 0, true), new([consumer, old], 0, true)));
        AssertThrows<InvalidDataException>(() => ResourceDependencyVerifier.VerifyForInstall(new([Mod("new", "1", required: "missing")], 0, true), new([], 0, true)));
        AssertTrue(ResourceDependencyVerifier.VerifyForInstall(new([legacy], 0, true), new([], 0, true)) is not null);
    }

    private sealed class ValidationModCatalog(byte[] oldBytes, byte[] newBytes) : IResourceCatalogSource, IResourceFileSource
    {
        public Task<ResourceSearchResult> SearchAsync(ResourceSearchQuery query, CancellationToken token) => throw new NotSupportedException();
        private ResourceVersion Version(string id) => new(id, id, id == "Old" ? "1.0.0" : "2.0.0", "release", ["1.21.1"], ["fabric"],
            id == "Old" ? "2026-01-01T00:00:00Z" : "2026-02-01T00:00:00Z", "https://modrinth.com/mod/A")
        {
            Provider = ResourceProvider.Modrinth,
            ProjectId = "A",
            File = new(id == "Old" ? "old.jar" : "new.jar", "https://cdn.modrinth.com/data/A/new.jar",
                id == "Old" ? oldBytes.Length : newBytes.Length, null, Convert.ToHexString(SHA512.HashData(id == "Old" ? oldBytes : newBytes)))
        };
        public Task<ResourceDetail> DetailAsync(ResourceDetailQuery query, CancellationToken token) => Task.FromResult(new ResourceDetail(
            new("A", "Target", "", "", 0, "https://modrinth.com/mod/A") { Sources = query.Sources }, "MIT", [Version("New")]));
        public Task<ResourceVersion?> ReadVersionAsync(ResourceDownloadCommand command, CancellationToken token) => Task.FromResult<ResourceVersion?>(Version(command.VersionId));
    }

    private static async ValueTask ModUpdatesCommitWithUnrelatedIncompleteMetadata()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root), directory = Path.Combine(instance, "mods"); Directory.CreateDirectory(directory);
            byte[] oldBytes = ResourceJar("target"), newBytes = ResourceJar("target", version: "2.0.0");
            string path = Path.Combine(directory, "old.jar"); await File.WriteAllBytesAsync(path, oldBytes);
            WriteLocalJar(Path.Combine(directory, "legacy.jar"), ("mcmod.info", """[{"modid":"legacy","version":"1.0"}]"""));
            string hash = Convert.ToHexString(SHA512.HashData(oldBytes)).ToLowerInvariant();
            using var http = new HttpClient(new ResourceHttp(request => request.RequestUri!.Host == "cdn.modrinth.com"
                ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(newBytes) }
                : new(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("version_files", StringComparison.Ordinal)
                    ? new JsonObject { [hash] = new JsonObject { ["project_id"] = "A", ["id"] = "Old" } }.ToJsonString() : "{\"data\":{\"exactMatches\":[]}}")
                }));
            var builder = new XsrStateStoreBuilder(); DownloadService.DeclareState(builder); TaskCenterStateContract.DeclareState(builder);
            MinecraftProcessStateComposition.DeclareState(builder); var store = builder.Build(); var tasks = new TaskCenterService(store);
            var catalog = new ValidationModCatalog(oldBytes, newBytes);
            var service = new ResourceContentUpdateService(new(new(new(http, "")), catalog), new(catalog, new DownloadService(store), tasks, http), store);
            var file = new FileInfo(path);
            await service.UpdateAsync(new(new(instance, "mods", file.Name, file.Length, file.LastWriteTimeUtc.Ticks), new(ResourceProvider.Modrinth, "A"), "New"), default);
            AssertFalse(File.Exists(path)); AssertTrue(File.Exists(Path.Combine(directory, "legacy.jar")));
            AssertTrue((await File.ReadAllBytesAsync(Path.Combine(directory, "new.jar"))).SequenceEqual(newBytes));
            var entry = store.ReadCollection<TaskCenterEntry>(store.Resolve(TaskCenterStateContract.EntriesKey)).Items.Single();
            AssertEqual(TaskCenterEntryState.Finished, entry.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
