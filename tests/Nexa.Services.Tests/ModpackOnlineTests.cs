using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using Nexa.Services.Downloads;
using Nexa.Services.Files;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Resources;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private sealed class OnlinePackCatalog(ResourceProvider provider, ContentFingerprint old, ContentFingerprint next) : IResourceCatalogSource, IResourceFileSource
    {
        public bool Offline, Changed, Withdrawn;
        public int Reads;
        public string Project => provider == ResourceProvider.Modrinth ? "Pack" : "123";
        public string Old => provider == ResourceProvider.Modrinth ? "Old" : "11";
        public string Next => provider == ResourceProvider.Modrinth ? "Next" : "12";
        public ResourceVersion Version(bool newer) => new(newer ? Next : Old, newer ? "New pack" : "Installed pack", newer ? "aaa" : "zzz", "release",
            ["1.21.1"], ["fabric"], newer ? "2026-02-01T00:00:00Z" : "2026-01-01T00:00:00Z", "https://modrinth.com/modpack/Pack")
        {
            Provider = provider,
            ProjectId = Project,
            File = new((newer ? "new" : "old") + (provider == ResourceProvider.Modrinth ? ".mrpack" : ".zip"),
                provider == ResourceProvider.Modrinth ? "https://cdn.modrinth.com/data/Pack/" + (newer ? "new.mrpack" : "old.mrpack")
                    : "https://edge.forgecdn.net/files/123/456/" + (newer ? "new.zip" : "old.zip"),
                newer ? next.Length : old.Length, newer ? next.Sha1 : old.Sha1, Changed && newer ? new string('0', 128)
                    : provider == ResourceProvider.CurseForge ? null : newer ? next.Sha512 : old.Sha512)
        };
        public Task<ResourceSearchResult> SearchAsync(ResourceSearchQuery query, CancellationToken token) => throw new NotSupportedException();
        public Task<ResourceDetail> DetailAsync(ResourceDetailQuery query, CancellationToken token)
        {
            Reads++; token.ThrowIfCancellationRequested(); if (Offline) throw new IOException("offline fixture");
            return Task.FromResult(new ResourceDetail(new(Project, "Example <pack>", "Pack description", "Author", 42, "https://modrinth.com/modpack/Pack")
            { Kind = ResourceKind.Modpack, Sources = [new(provider, Project)] }, "MIT", Withdrawn ? [Version(false)] : [Version(false), Version(true)]));
        }
        public Task<ResourceVersion?> ReadVersionAsync(ResourceDownloadCommand command, CancellationToken token)
        { Reads++; token.ThrowIfCancellationRequested(); if (Offline) throw new IOException("offline fixture"); return Task.FromResult<ResourceVersion?>(Version(command.VersionId == Next)); }
    }

    private static async ValueTask OnlineModpackUpdatesIdentifyDownloadPreviewAndPublish()
    {
        foreach (var provider in new[] { ResourceProvider.Modrinth, ResourceProvider.CurseForge })
            foreach (string scenario in new[] { "success", "legacy-receipt", "legacy", "legacy-source-missing", "source-deleted", "offline", "metadata-change", "withdrawn", "cache-change", "instance-change", "bad-download", "cancel" })
            {
                string temporary = CreateTempDirectory();
                try
                {
                    string root = Path.Combine(temporary, "game"); Directory.CreateDirectory(root);
                    string oldPath = Path.Combine(temporary, "old.mrpack"), newPath = Path.Combine(temporary, "new.mrpack");
                    WriteUpdatePack(oldPath, "1.0", false); WriteUpdatePack(newPath, "2.0", true);
                    if (provider == ResourceProvider.CurseForge)
                        foreach (string path in new[] { oldPath, newPath })
                        {
                            using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
                            var entry = zip.GetEntry("modrinth.index.json")!; JsonNode index;
                            using (var input = entry.Open()) index = JsonNode.Parse(input)!;
                            entry.Delete();
                            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
                            writer.Write(new JsonObject
                            {
                                ["manifestType"] = "minecraftModpack",
                                ["manifestVersion"] = 1,
                                ["name"] = index["name"]!.GetValue<string>(),
                                ["version"] = index["versionId"]!.GetValue<string>(),
                                ["minecraft"] = new JsonObject
                                {
                                    ["version"] = index["dependencies"]!["minecraft"]!.GetValue<string>(),
                                    ["modLoaders"] = new JsonArray()
                                },
                                ["files"] = new JsonArray(),
                                ["overrides"] = "overrides"
                            }.ToJsonString());
                        }
                    byte[] oldBytes = await File.ReadAllBytesAsync(oldPath);
                    byte[] nextBytes = await File.ReadAllBytesAsync(newPath);
                    async Task<ContentFingerprint> Fingerprint(string path) { await using var stream = File.OpenRead(path); return await ContentFingerprintReader.ReadAsync(stream, 1024 * 1024); }
                    var old = await Fingerprint(oldPath); var next = await Fingerprint(newPath);
                    using var fixture = new InstallFixture(PackMetadata());
                    var installed = await fixture.Install.InstallModpackAsync(new(await MinecraftModpackArchive.InspectAsync(oldPath), root)); AssertTrue(installed.IsSuccess);
                    string instance = installed.Value!.InstanceDirectory;
                    string receiptPath = Path.Combine(instance, "Nexa", "ModpackManaged.json");
                    AssertEqual(old.Sha512, (await ModpackManagedManifestStore.ReadAsync(instance, default))!.ArchiveIdentity!.Sha512);
                    if (scenario == "legacy") File.Delete(receiptPath);
                    if (scenario is "legacy-receipt" or "legacy-source-missing")
                    { var document = JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!.AsObject(); document.Remove("ArchiveIdentity"); await File.WriteAllTextAsync(receiptPath, document.ToJsonString()); }
                    if (scenario is "source-deleted" or "legacy-source-missing") File.Delete(oldPath);
                    await File.WriteAllTextAsync(Path.Combine(instance, "config", "local.txt"), "user config");
                    Directory.CreateDirectory(Path.Combine(instance, "saves")); await File.WriteAllTextAsync(Path.Combine(instance, "saves", "user.dat"), "user world");
                    var source = new OnlinePackCatalog(provider, old, next);
                    int transfers = 0;
                    using var http = new HttpClient(new ResourceHttp(request =>
                    {
                        if (source.Offline) return new(HttpStatusCode.ServiceUnavailable);
                        if (request.Method == HttpMethod.Get)
                        {
                            transfers++; byte[] bytes = nextBytes;
                            if (scenario == "bad-download") { bytes = nextBytes.ToArray(); bytes[0] ^= 1; }
                            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                        }
                        var modrinth = new JsonObject(); var curse = new JsonArray();
                        foreach (var (fingerprint, id) in new[] { (old, source.Old), (next, source.Next) })
                        {
                            if (provider == ResourceProvider.Modrinth) modrinth[fingerprint.Sha512] = new JsonObject { ["project_id"] = source.Project, ["id"] = id };
                            else curse.Add((JsonNode)new JsonObject
                            {
                                ["file"] = new JsonObject
                                {
                                    ["modId"] = 123,
                                    ["id"] = long.Parse(id, System.Globalization.CultureInfo.InvariantCulture),
                                    ["fileFingerprint"] = fingerprint.CurseForge,
                                    ["hashes"] = new JsonArray(new JsonObject { ["algo"] = 1, ["value"] = fingerprint.Sha1 })
                                }
                            });
                        }
                        return new(HttpStatusCode.OK)
                        {
                            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("version_files", StringComparison.Ordinal)
                            ? modrinth.ToJsonString() : new JsonObject { ["data"] = new JsonObject { ["exactMatches"] = curse } }.ToJsonString())
                        };
                    }));
                    string metadataCache = Path.Combine(temporary, "metadata"), archives = Path.Combine(temporary, "archives");
                    using var information = new ResourceOnlineInformationCache(directory: metadataCache);
                    var catalog = new MergedResourceCatalog(source, source, information);
                    var transport = new ResourceProviderHttp(http, "fixture-key") { CountryPolicy = new("US"), RequestTimeout = TimeSpan.FromSeconds(1) };
                    using var identities = new ResourceInstanceService(transport, information);
                    ResourceModpackOnlineService Create(ResourceOnlineInformationCache cache, ResourceInstanceService ids, IResourceCatalogSource sourceCatalog)
                        => new(fixture.Install, ids, sourceCatalog, new(sourceCatalog, new DownloadService(fixture.Store), fixture.Tasks, http)
                        { CountryPolicy = new("US") }, cache, archives);
                    var service = Create(information, identities, catalog);
                    var query = new ResourceModpackOnlineQuery(instance, MirrorFirst: false);
                    if (scenario == "legacy-source-missing")
                    {
                        AssertTrue((await service.ReadAsync(query)).Project is null);
                        string replacement = Path.Combine(temporary, "replacement.pack"); await File.WriteAllBytesAsync(replacement, oldBytes);
                        query = query with { OriginalArchivePath = replacement };
                    }
                    var online = await service.ReadAsync(query);
                    AssertTrue(online.Project is not null, provider + "/" + scenario + ": " + online.Notice);
                    AssertEqual(source.Project, online.Project!.Id); AssertEqual(source.Old, online.InstalledVersion!.Id);
                    AssertEqual(source.Next, online.UpdateVersion!.Id); AssertEqual(old.Sha256, online.InstalledArchiveSha256);
                    AssertEqual(1, online.Versions.Count); AssertEqual(0, transfers);
                    var selection = new ResourceModpackPreviewQuery(query, new(provider, source.Project), source.Next, old.Sha256);
                    using var canceled = new CancellationTokenSource(); if (scenario == "cancel") canceled.Cancel();
                    ResourceModpackPreview? preview = null;
                    try { preview = await service.PreviewAsync(selection, canceled.Token); }
                    catch (OperationCanceledException) when (scenario == "cancel") { }
                    catch (IOException) when (scenario == "bad-download") { }
                    if (scenario is "cancel" or "bad-download")
                    { AssertTrue(preview is null); AssertTrue(File.Exists(Path.Combine(instance, "mods", "main-1.jar"))); Console.WriteLine("PASS online pack scenario: " + provider + "/" + scenario); continue; }
                    AssertTrue(preview is not null); AssertEqual(1, transfers);
                    AssertTrue(File.Exists(Path.Combine(instance, "mods", "main-1.jar")));
                    AssertTrue(preview!.Installation.Preserved.Contains("config/local.txt"));
                    var repeated = await service.PreviewAsync(selection); AssertEqual(preview.Installation.Pack.Sha256, repeated.Installation.Pack.Sha256); AssertEqual(1, transfers);
                    if (scenario == "offline")
                    {
                        source.Offline = true;
                        using var restoredCache = new ResourceOnlineInformationCache(directory: metadataCache);
                        using var restoredIds = new ResourceInstanceService(transport, restoredCache);
                        var restoredCatalog = new MergedResourceCatalog(source, source, restoredCache);
                        var restored = Create(restoredCache, restoredIds, restoredCatalog);
                        int reads = source.Reads;
                        AssertEqual(source.Project, (await restored.ReadAsync(query)).Project!.Id); AssertEqual(reads, source.Reads);
                        var stale = await restored.ReadAsync(query with { Refresh = true });
                        AssertEqual(source.Project, stale.Project!.Id); AssertTrue(stale.IsStale); AssertTrue(stale.UpdateVersion is null);
                    }
                    if (scenario == "metadata-change") source.Changed = true;
                    if (scenario == "withdrawn") source.Withdrawn = true;
                    if (scenario == "cache-change") await File.AppendAllTextAsync(preview.Installation.Request.ArchivePath, "tampered");
                    if (scenario == "instance-change") await File.WriteAllTextAsync(Path.Combine(instance, "config", "default.txt"), "changed after preview");
                    bool success = scenario is "success" or "legacy" or "legacy-receipt" or "legacy-source-missing" or "source-deleted";
                    bool applied = false;
                    try { applied = (await service.UpdateAsync(new(preview))).IsSuccess; }
                    catch (Exception error) when (error is IOException or InvalidDataException) { }
                    AssertEqual(success, applied);
                    AssertEqual(success, File.Exists(Path.Combine(instance, "mods", "main-2.jar")));
                    AssertEqual(!success, File.Exists(Path.Combine(instance, "mods", "main-1.jar")));
                    AssertEqual("user config", await File.ReadAllTextAsync(Path.Combine(instance, "config", "local.txt")));
                    AssertEqual("user world", await File.ReadAllTextAsync(Path.Combine(instance, "saves", "user.dat")));
                    if (success)
                    {
                        var updated = await service.ReadAsync(query with { Refresh = true });
                        AssertEqual(next.Sha256, updated.InstalledArchiveSha256); AssertEqual(source.Next, updated.InstalledVersion!.Id); AssertTrue(updated.UpdateVersion is null);
                    }
                    Console.WriteLine("PASS online pack scenario: " + provider + "/" + scenario);
                }
                finally { Directory.Delete(temporary, true); }
            }
    }
}
