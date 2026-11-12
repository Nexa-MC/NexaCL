using System.IO.Compression;
using System.Security.Cryptography;
using Nexa.Native.Minecraft.Client;
using Nexa.Native.Network.Client;
using Nexa.Native.Network.Services;
using Nexa.Services.Downloads;
using Nexa.Services.Resources;

internal static class ResourceCases
{
    internal sealed class Downloads(NativeNetworkClient network, string address) : INativeDownloadPort
    {
        public Task<DownloadTransferResult> DownloadAsync(NativeDownloadRequest request, Action<DownloadProgress>? progress = null, CancellationToken token = default)
        {
            var sources = request.Sources.Select(source =>
            {
                if (!source.ResourceHostsOnly) return source;
                var uri = new Uri(source.Address);
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.Host != "cdn.modrinth.com" || uri.UserInfo.Length != 0
                    || !uri.AbsolutePath.StartsWith("/data/Native01/", StringComparison.Ordinal)
                    || source.ExactLength is not > 0 || source.MaximumRedirects != 4)
                    throw new InvalidDataException("Native resource download lost production host/length/redirect admission.");
                return source with { Address = address + (uri.AbsolutePath.Contains("NativeV2", StringComparison.Ordinal) ? "/resource2" : "/resource1"), ResourceHostsOnly = false, AllowAutoRedirect = false };
            }).ToArray();
            return network.DownloadAsync(request with { Sources = sources }, progress, token);
        }
    }
    internal static readonly byte[] FirstJar = Jar("1.0");
    internal static readonly byte[] SecondJar = Jar("2.0");
    private const string ProjectId = "Native01";
    internal static string ProjectJson => "{\"id\":\"Native01\",\"project_id\":\"Native01\",\"slug\":\"native-fixture\",\"title\":\"Native Fixture\",\"description\":\"Native resource fixture\",\"author\":\"fixture\",\"downloads\":1,\"project_type\":\"mod\",\"license\":{\"name\":\"MIT\"}}";
    internal static string VersionJson(bool second) => "{\"id\":\"" + (second ? "NativeV2" : "NativeV1") + "\",\"project_id\":\"Native01\",\"name\":\"Native fixture\",\"version_number\":\"" + (second ? "2.0" : "1.0") + "\",\"version_type\":\"release\",\"game_versions\":[\"1.20.1\"],\"loaders\":[\"fabric\"],\"date_published\":\"2026-01-0" + (second ? "2" : "1") + "T00:00:00Z\",\"changelog\":\"native changelog\",\"dependencies\":[],\"files\":[{\"primary\":true,\"filename\":\"native-" + (second ? "2" : "1") + ".jar\",\"url\":\"https://cdn.modrinth.com/data/Native01/versions/" + (second ? "NativeV2/native-2" : "NativeV1/native-1") + ".jar\",\"size\":" + (second ? SecondJar : FirstJar).Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"hashes\":{\"sha512\":\"" + Hash(second ? SecondJar : FirstJar) + "\"}}]}";
    internal static string IdentitiesJson => "{\"" + Hash(FirstJar) + "\":" + VersionJson(false) + ",\"" + Hash(SecondJar) + "\":" + VersionJson(true) + "}";
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA512.HashData(bytes));
    private static byte[] Jar(string version)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
            writer.Write("{\"schemaVersion\":1,\"id\":\"native_fixture\",\"version\":\"" + version + "\",\"name\":\"Native Fixture\",\"environment\":\"client\"}");
        }
        return stream.ToArray();
    }
    internal static async Task RunAsync(NativeMinecraftClient client, string directory, CancellationToken token)
    {
        string favorites = Path.Combine(directory, "native-resource-favorites.json");
        await client.ConfigureResourcesAsync(favorites, token: token);
        var search = await client.QueryResourceSearchQueryAsync(new(Text: "native") { Provider = ResourceProvider.Modrinth }, token);
        var project = search.Value!.Projects.Single();
        if (project.Title != "Native Fixture" || project.Id != ProjectId) throw new InvalidDataException("Native provider search lost parsed metadata.");
        var detail = await client.QueryResourceDetailQueryAsync(new(ProjectId, "1.20.1", "fabric") { Sources = [new(ResourceProvider.Modrinth, ProjectId)] }, token);
        if (detail.Value!.Versions.Count != 2 || (await client.QueryResourceChangelogQueryAsync(new(new(ResourceProvider.Modrinth, ProjectId), "NativeV2"), token)).Value!.Text != "native changelog")
            throw new InvalidDataException("Native provider detail/changelog did not use typed metadata.");
        var favoriteBefore = (await client.QueryResourceFavoritesQueryAsync(new(), token)).Value!;
        if (!(await client.DispatchResourceFavoriteCommandAsync(new(project, true) { ExpectedRevision = favoriteBefore.Revision }, token)).IsSuccess
            || (await client.QueryResourceFavoritesQueryAsync(new(), token)).Value!.Projects.Single().Id != ProjectId || !File.Exists(favorites))
            throw new InvalidDataException("Native favorites lost durable state.");
        string downloads = Path.Combine(directory, "native-resource-downloads"); Directory.CreateDirectory(downloads);
        var command = new ResourceDownloadCommand(ResourceProvider.Modrinth, ProjectId, "NativeV1", downloads);
        if (!(await client.DispatchResourceDownloadCommandAsync(command, token)).IsSuccess
            || Hash(await File.ReadAllBytesAsync(Path.Combine(downloads, "native-1.jar"), token)) != Hash(FirstJar)
            || (await client.DispatchResourceDownloadCommandAsync(command, token)).IsSuccess)
            throw new InvalidDataException("Native resource download lost verified bytes or overwrote an existing file.");
        string root = Path.Combine(directory, "native-resource-game");
        var installed = await client.InstallMinecraftAsync(new(root, "1.20.1"), token);
        if (!installed.IsSuccess) throw new InvalidDataException("Native resource fixture base install failed.");
        string instance = Path.Combine(root, "versions", "native-fabric"); Directory.CreateDirectory(instance);
        await File.WriteAllTextAsync(Path.Combine(instance, "native-fabric.json"), "{\"id\":\"native-fabric\",\"inheritsFrom\":\"1.20.1\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.16.9\"}]}", token);
        var context = await client.QueryResourceInstanceQueryAsync(new(root, "native-fabric"), token);
        if (context.Value!.Loader != "fabric" || context.Value.Game != "1.20.1") throw new InvalidDataException("Native resource context lost inherited game/loader identity.");
        var mod = new ResourceModInstallCommand(new(ResourceProvider.Modrinth, ProjectId), "NativeV1", new(root, "native-fabric"));
        var plan = await client.QueryResourceModPlanQueryAsync(new(mod), token);
        if ((await client.DispatchResourceModInstallCommandAsync(mod with { ExpectedPlanFingerprint = "stale" }, token)).IsSuccess) throw new InvalidDataException("Native install accepted a stale dependency plan.");
        mod = mod with { ExpectedPlanFingerprint = ResourceInstallPlanIdentity.Fingerprint(plan.Value!.Versions) };
        if (!plan.IsSuccess || plan.Value!.Versions.Single().Id != "NativeV1" || !(await client.DispatchResourceModInstallCommandAsync(mod, token)).IsSuccess)
            throw new InvalidDataException("Native dependency plan/install lost its admitted version.");
        string mods = Path.Combine(context.Value.GameDirectory, "mods"); var file = new FileInfo(Path.Combine(mods, "native-1.jar"));
        if (!file.Exists || Hash(await File.ReadAllBytesAsync(file.FullName, token)) != Hash(FirstJar)) throw new InvalidDataException("Native mod installation lost verified bytes.");
        var query = new ResourceContentOnlineQuery(instance, "mods", file.Name, file.Length, file.LastWriteTimeUtc.Ticks) { Refresh = true, WaitForRefresh = true };
        var online = await client.QueryResourceContentOnlineQueryAsync(query, token);
        if (!online.IsSuccess || online.Value!.InstalledVersion != "1.0" || online.Value.UpdateVersion?.Id != "NativeV2")
            throw new InvalidDataException("Native content identification/update offer lost its archive fingerprint.");
        var update = new ResourceContentUpdateCommand(query, new(ResourceProvider.Modrinth, ProjectId), "NativeV2");
        if (!(await client.DispatchResourceContentUpdateCommandAsync(update, token)).IsSuccess
            || Hash(await File.ReadAllBytesAsync(Path.Combine(mods, "native-2.jar"), token)) != Hash(SecondJar)
            || File.Exists(file.FullName) || (await client.DispatchResourceContentUpdateCommandAsync(update, token)).IsSuccess)
            throw new InvalidDataException("Native content update lost its reviewed replacement or accepted a stale file.");
        if ((await client.DispatchResourceFavoriteCommandAsync(new(project, false) { ExpectedRevision = favoriteBefore.Revision }, token)).IsSuccess)
            throw new InvalidDataException("Native favorites accepted a stale revision.");
        Console.WriteLine("Native resources parsed provider HTTP, persisted favorites and verified downloads, dependency plans and content updates.");
    }
}
