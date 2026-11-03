using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Downloads;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;
namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static readonly string[] ParityPortableModuleNames = ["host", "ui", "pac", "crypto", "network", "updates", "media", "minecraft", "markup", "artwork", "accounts"];

    private static async ValueTask ParityLegacyArchivesAndOptionalFiles()
    {
        string root = CreateTempDirectory();
        try
        {
            var cases = new[]
            {
                ("HMCL", "modpack.json", "{\"name\":\"Pack\",\"version\":\"1\",\"gameVersion\":\"1.20.1\",\"fabric\":\"0.16.0\"}", "minecraft/"),
                ("MCBBS", "mcbbs.packmeta", "{\"manifestType\":\"minecraftModpack\",\"manifestVersion\":2,\"name\":\"Pack\",\"version\":\"1\",\"addons\":[{\"id\":\"game\",\"version\":\"1.20.1\"},{\"id\":\"fabric\",\"version\":\"0.16.0\"}],\"files\":[]}", "overrides/"),
                ("MultiMC", "mmc-pack.json", "{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.20.1\"},{\"uid\":\"net.fabricmc.fabric-loader\",\"version\":\"0.16.0\"}]}", ".minecraft/")
            };
            foreach (var (format, manifest, json, prefix) in cases)
            {
                string source = Path.Combine(root, format + ".zip");
                WriteLocalJar(source, ("wrapped/" + manifest, json), ("wrapped/instance.cfg", "InstanceType=OneSix\nname=Pack\n"), ("wrapped/" + prefix + "config/example.txt", "fixture"));
                var preview = await MinecraftModpackArchive.InspectAsync(source);
                AssertEqual(format, preview.Format); AssertEqual("1.20.1", preview.Game); AssertEqual(InstallLoader.Fabric, preview.Loader!.Value);
                string target = Path.Combine(root, format); Directory.CreateDirectory(target);
                using var fixture = new InstallFixture(PackMetadata());
                var result = await fixture.Install.InstallModpackAsync(new(preview, target));
                AssertTrue(result.IsSuccess, result.Error?.Message ?? format);
                AssertEqual("fixture", await File.ReadAllTextAsync(Path.Combine(result.Value!.InstanceDirectory, "config", "example.txt")));
            }
            string mcbbs = Path.Combine(root, "local-addon.zip");
            var localManifest = JsonNode.Parse(cases[1].Item3)!;
            localManifest["files"] = new JsonArray((JsonNode)new JsonObject { ["type"] = "addon", ["path"] = "config/example.txt", ["hash"] = Sha1Hex("fixture"), ["force"] = false });
            WriteLocalJar(mcbbs, ("mcbbs.packmeta", localManifest.ToJsonString()), ("overrides/config/example.txt", "fixture"));
            AssertEqual(0, (await MinecraftModpackArchive.InspectAsync(mcbbs)).OptionalFiles);
            string optional = Path.Combine(root, "select.mrpack");
            WriteLocalJar(optional, ("modrinth.index.json", MrpackIndex().ToJsonString()), ("overrides/manifest.json", "{}"), ("overrides/resourcepacks/nested/manifest.json", "{}"));
            var pack = await MinecraftModpackArchive.InspectAsync(optional);
            AssertTrue(pack.OptionalEntries.Count > 0);
            string legacyRoot = Path.Combine(root, "legacy-preview"); Directory.CreateDirectory(legacyRoot);
            using (var fixture = new InstallFixture(PackMetadata()))
            {
                var oldPreview = pack with { OptionalEntries = [] };
                var restored = await fixture.Install.InstallModpackAsync(new(oldPreview, legacyRoot, true));
                AssertTrue(restored.IsSuccess, restored.Error?.Message ?? "legacy pinned preview");
                AssertTrue(File.Exists(Path.Combine(restored.Value!.InstanceDirectory, "mods", "optional.jar")));
            }
            string selectedRoot = Path.Combine(root, "selected"); Directory.CreateDirectory(selectedRoot);
            using (var fixture = new InstallFixture(PackMetadata()))
            {
                var result = await fixture.Install.InstallModpackAsync(new(pack, selectedRoot, true) { ExcludedOptionalFiles = pack.OptionalEntries.Select(f => f.Id).ToArray() });
                AssertTrue(result.IsSuccess, result.Error?.Message ?? "");
                AssertTrue(File.Exists(Path.Combine(result.Value!.InstanceDirectory, "mods", "required.jar")));
                AssertFalse(File.Exists(Path.Combine(result.Value.InstanceDirectory, "mods", "optional.jar")));
                string saved = await File.ReadAllTextAsync(Path.Combine(result.Value.InstanceDirectory, "Nexa", "ModpackManaged.json"));
                AssertTrue(saved.Contains(pack.OptionalEntries[0].Id, StringComparison.Ordinal));
            }
            string conflict = Path.Combine(root, "conflict.zip");
            WriteLocalJar(conflict, ("one/modpack.json", cases[0].Item3), ("two/modpack.json", cases[0].Item3));
            await TrustRejectsAsync<InvalidDataException>(() => MinecraftModpackArchive.InspectAsync(conflict));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ParityExportFormatsAndScripts()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root); Directory.CreateDirectory(Path.Combine(instance, "config"));
            await File.WriteAllTextAsync(Path.Combine(instance, "config", "sample.txt"), "public configuration");
            var builder = new XsrStateStoreBuilder(); TaskCenterStateContract.DeclareState(builder); DownloadService.DeclareState(builder); MinecraftProcessStateComposition.DeclareState(builder);
            var store = builder.Build(); using var tasks = new TaskCenterService(store); var exporter = new InstanceModpackExportService(tasks, store);
            foreach (var format in new[] { InstanceExportFormat.CurseForge, InstanceExportFormat.Mcbbs, InstanceExportFormat.MultiMC })
            {
                string path = Path.Combine(root, format + ".zip");
                var result = await exporter.ExportAsync(new(instance, path, "Pack", "1", ["config"]) { Format = format });
                AssertTrue(result.IsSuccess, result.Error?.Message ?? format.ToString());
                var preview = await MinecraftModpackArchive.InspectAsync(path);
                AssertEqual("1.21.1", preview.Game); AssertEqual(InstallLoader.Fabric, preview.Loader!.Value);
                using var archive = ZipFile.OpenRead(path); AssertTrue(archive.Entries.Any(e => e.FullName.EndsWith("config/sample.txt", StringComparison.Ordinal)));
            }
            string launcher = Path.Combine(root, "launcher's & app"); await File.WriteAllTextAsync(launcher, "fixture");
            foreach (string extension in new[] { ".cmd", ".sh" })
            {
                string destination = Path.Combine(root, "launch" + extension);
                var result = await InstanceLaunchScriptService.ExportAsync(new(instance, launcher, destination));
                AssertTrue(result.IsSuccess, result.Error?.Message ?? "");
                string script = await File.ReadAllTextAsync(destination);
                AssertTrue(script.Contains("nexacl://run/", StringComparison.Ordinal)); AssertFalse(script.Contains("accessToken", StringComparison.Ordinal));
                AssertFalse((await InstanceLaunchScriptService.ExportAsync(new(instance, launcher, destination))).IsSuccess);
            }
            string link = InstanceLaunchScriptContract.Link(root, Path.GetFileName(instance));
            AssertTrue(InstanceLaunchScriptContract.TryLink(link, out var decodedRoot, out var decodedInstance)); AssertEqual(root, decodedRoot); AssertEqual("fixture", decodedInstance);
            AssertFalse(InstanceLaunchScriptContract.TryLink(link + "?evil=1", out _, out _));
            AssertFalse(InstanceLaunchScriptContract.TryLink("nexacl://run/Li4/Li4", out _, out _));
            var invalid = await exporter.ExportAsync(new(instance, Path.Combine(root, "invalid.zip"), "Pack", "1", ["config"]) { Format = InstanceExportFormat.Zip, LauncherDirectory = root });
            AssertFalse(invalid.IsSuccess); AssertFalse(File.Exists(Path.Combine(root, "invalid.zip")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ParityPortableBundleExcludesAccountsAndIncludesRuntime()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root), launcher = Path.Combine(root, "published"), java = Path.Combine(root, "java-source");
            Directory.CreateDirectory(launcher); Directory.CreateDirectory(Path.Combine(java, "bin"));
            string executable = OperatingSystem.IsWindows() ? "Nexa.Desktop.exe" : "Nexa.Desktop";
            string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            foreach (string name in new[] { executable, "Nexa.Bootstrap.nfp" }.Concat(ParityPortableModuleNames.Select(n => "nexa-" + n + suffix)))
                await File.WriteAllTextAsync(Path.Combine(launcher, name), "published fixture payload");
            Directory.CreateDirectory(Path.Combine(launcher, "licenses"));
            await File.WriteAllTextAsync(Path.Combine(launcher, "licenses", "third-party.txt"), "third-party legal notice");
            await File.WriteAllTextAsync(Path.Combine(launcher, "accounts.json"), "private account");
            await File.WriteAllTextAsync(Path.Combine(java, "release"), "JAVA_VERSION=21");
            await File.WriteAllTextAsync(Path.Combine(java, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"), "java executable");
            await File.WriteAllTextAsync(Path.Combine(java, "LICENSE"), "license");
            if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(java, "NOTICE"), "LICENSE");
            var builder = new XsrStateStoreBuilder(); TaskCenterStateContract.DeclareState(builder); DownloadService.DeclareState(builder); MinecraftProcessStateComposition.DeclareState(builder);
            var store = builder.Build(); using var tasks = new TaskCenterService(store); var exporter = new InstanceModpackExportService(tasks, store);
            string destination = Path.Combine(root, "portable.zip");
            var result = await exporter.ExportAsync(new(instance + Path.DirectorySeparatorChar, destination, "Portable", "1", [])
            { Format = InstanceExportFormat.Zip, LauncherDirectory = launcher, JavaDirectory = java });
            AssertTrue(result.IsSuccess, result.Error?.Message ?? "");
            using var archive = ZipFile.OpenRead(destination);
            AssertTrue(archive.GetEntry("launcher/" + executable) is not null); AssertTrue(archive.GetEntry("launcher/licenses/third-party.txt") is not null); AssertTrue(archive.GetEntry("java/release") is not null);
            AssertFalse(archive.Entries.Any(e => e.FullName.EndsWith("accounts.json", StringComparison.Ordinal)));
            using var entry = new StreamReader(archive.GetEntry(OperatingSystem.IsWindows() ? "Start.cmd" : "Start.sh")!.Open());
            string script = await entry.ReadToEndAsync(); AssertTrue(script.Contains("--run-instance", StringComparison.Ordinal)); AssertTrue(script.Contains("JAVA_HOME", StringComparison.Ordinal));
            AssertTrue(script.Contains(Path.GetFileName(instance), StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ParityContentInspectionAndNbt()
    {
        string root = CreateTempDirectory();
        try
        {
            // Compound: a signed int, a named list containing a long, an int array, modified UTF-8 string.
            byte[] nbt = [10, 0, 0, 3, 0, 1, 110, 255, 255, 255, 249, 9, 0, 1, 108, 4, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 9, 11, 0, 1, 97, 0, 0, 0, 2, 0, 0, 0, 1, 255, 255, 255, 254, 8, 0, 1, 115, 0, 2, 192, 128, 0];
            foreach (string compression in new[] { "raw", "gzip", "zlib" })
            {
                string path = Path.Combine(root, compression + ".nbt");
                await using (var file = File.Create(path))
                {
                    if (compression == "raw") await file.WriteAsync(nbt);
                    else { await using Stream stream = compression == "gzip" ? new GZipStream(file, CompressionMode.Compress) : new ZLibStream(file, CompressionMode.Compress); await stream.WriteAsync(nbt); }
                }
                var preview = await ContentInspectionService.ReadAsync(new(path)); AssertEqual("NBT", preview.Format); AssertEqual(4, preview.Items.Count); AssertEqual("-7", preview.Items[0].Value);
                var list = await ContentInspectionService.ReadAsync(new(path, Node: "1")); AssertEqual("9", list.Items[0].Value);
                var array = await ContentInspectionService.ReadAsync(new(path, Node: "2")); AssertEqual("-2", array.Items[1].Value);
                AssertEqual("\0", preview.Items[3].Value);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await TrustRejectsAsync<OperationCanceledException>(() => ContentInspectionService.ReadAsync(new(path), cancelled.Token));
            }
            string zip = Path.Combine(root, "pack.zip"); WriteLocalJar(zip, ("pack.mcmeta", "{\"pack\":{\"pack_format\":15}}"), ("shaders/test.fsh", "void main() {}"));
            var inventory = await ContentInspectionService.ReadAsync(new(zip)); AssertEqual(2, inventory.Total);
            var text = await ContentInspectionService.ReadAsync(new(zip, "shaders/test.fsh")); AssertEqual("void main() {}", text.Text);
            string bad = Path.Combine(root, "bad.nbt"); await File.WriteAllBytesAsync(bad, [10, 0, 0, 9, 0, 0, 10, 127, 255, 255, 255]);
            await TrustRejectsAsync<InvalidDataException>(() => ContentInspectionService.ReadAsync(new(bad)));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ParityOfflineTexturesAndNide8Agent()
    {
        byte[] skin = WardrobeCreatePng(), cape = WardrobeCreatePng(height: 32);
        using var local = new OfflineTextureServer();
        var identity = new MinecraftLaunchIdentity("LocalPlayer", new string('1', 32), "0", MinecraftLaunchIdentityMode.Offline)
        { OfflineSkinPng = skin, OfflineCapePng = cape, OfflineSlim = true };
        var endpoint = local.Register(identity);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var metadata = JsonNode.Parse(await http.GetStringAsync(endpoint.Server))!;
        AssertTrue(metadata["signaturePublickey"]!.GetValue<string>().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal));
        var profile = JsonNode.Parse(await http.GetStringAsync(endpoint.Server + "sessionserver/session/minecraft/profile/" + identity.PlayerUuid + "?unsigned=false"))!;
        string encoded = profile["properties"]![0]!["value"]!.GetValue<string>();
        var textures = JsonNode.Parse(Convert.FromBase64String(encoded))!;
        AssertEqual("slim", textures["textures"]!["SKIN"]!["metadata"]!["model"]!.GetValue<string>());
        string skinUrl = textures["textures"]!["SKIN"]!["url"]!.GetValue<string>();
        byte[] returnedSkin = await http.GetByteArrayAsync(skinUrl); AssertTrue(skin.SequenceEqual(returnedSkin));
        using (var key = System.Security.Cryptography.RSA.Create())
        {
            key.ImportFromPem(metadata["signaturePublickey"]!.GetValue<string>());
            AssertTrue(key.VerifyData(Encoding.UTF8.GetBytes(encoded), Convert.FromBase64String(profile["properties"]![0]!["signature"]!.GetValue<string>()), System.Security.Cryptography.HashAlgorithmName.SHA1, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
        }
        using var notFound = await http.GetAsync(new Uri(endpoint.Server).GetLeftPart(UriPartial.Authority) + "/sessionserver/session/minecraft/profile/" + identity.PlayerUuid);
        AssertEqual(HttpStatusCode.NotFound, notFound.StatusCode);
        string root = CreateTempDirectory();
        try
        {
            byte[] jar = [80, 75, 3, 4, 1, 2, 3];
            var handler = new ParityNide8Handler(jar);
            using var transport = new HttpClient(handler); using var fixture = new InstallFixture(PackMetadata());
            using var agent = new AuthlibInjectorProvider(transport, fixture.Downloads);
            string path = await agent.EnsureNide8Async(root, new string('a', 32));
            byte[] downloaded = await File.ReadAllBytesAsync(path); AssertTrue(jar.SequenceEqual(downloaded));
            await File.WriteAllTextAsync(path, "changed"); handler.BadHash = true;
            await TrustRejectsAsync<InvalidDataException>(() => agent.EnsureNide8Async(root, new string('a', 32)));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class ParityNide8Handler(byte[] jar) : HttpMessageHandler
    {
        internal bool BadHash;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            response.Content = request.RequestUri!.Host == "auth.mc-user.com"
                ? new StringContent("{\"jarHash\":\"" + (BadHash ? new string('0', 40) : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(jar))) + "\"}")
                : new ByteArrayContent(jar);
            return Task.FromResult(response);
        }
    }

    private static async ValueTask ParityLanAndNide8Contracts()
    {
        var accounts = CreateAccountService(new ThrowingProfilePort());
        byte[] original = [42];
        var local = new LaunchProfile { Username = "Local", Kind = LaunchProfileKind.Offline, Uuid = MinecraftOfflineIdentity.LegacyMismatchedUuid("Local"), OfflineCustomUuid = true, OfflineSkinPng = original };
        AssertTrue(AddFixtureProfile(accounts, local).IsSuccess);
        int index = accounts.GetViews().Single(p => p.Username == "Local").Index;
        var resolved = await new AccountLaunchIdentityResolver(accounts).ResolveAsync(index, accounts.GetProfile(index).Value!);
        AssertTrue(resolved.IsSuccess); AssertEqual(local.Uuid, resolved.Value!.PlayerUuid);
        original[0] = 0;
        var returned = accounts.GetProfile(index).Value!; AssertEqual((byte)42, returned.OfflineSkinPng[0]);
        returned.OfflineSkinPng[0] = 1; AssertEqual((byte)42, accounts.GetProfile(index).Value!.OfflineSkinPng[0]);
        var expected = accounts.GetProfile(index).Value!;
        AssertTrue(accounts.ReplaceProfile(index, expected with { Username = "LocalUpdated" }, expected).IsSuccess);

        var game = LanGameDiscoveryService.ParseAdvertisement(Encoding.UTF8.GetBytes("[MOTD]LAN 游戏[/MOTD][AD]25565[/AD]"), IPAddress.Parse("192.168.1.2"));
        AssertTrue(game is not null); AssertEqual("192.168.1.2:25565", game!.Address);
        foreach (string port in new[] { "0", "65536", "-1", "example.com:25565", "25565[/AD][AD]1" })
            AssertTrue(LanGameDiscoveryService.ParseAdvertisement(Encoding.UTF8.GetBytes("[MOTD]x[/MOTD][AD]" + port + "[/AD]"), IPAddress.Loopback) is null);
        AssertTrue(LanGameDiscoveryService.ParseAdvertisement("[MOTD]x[/MOTD][AD]25565[/AD]"u8.ToArray(), IPAddress.Parse("8.8.8.8")) is null);
        string id = new('A', 32), server = Nide8Account.ServerFromId(id);
        AssertEqual(new string('a', 32), Nide8Account.ServerId(server)!);
        AssertEqual(server, YggdrasilAuthService.NormalizeYggdrasilServer(server + "/authserver"));
        AssertTrue(Nide8Account.ServerId(server + "/api/yggdrasil") is null);
        var profile = AccountLoginProfiles.FromYggdrasil(new("user", new string('1', 32), "access", server, "Nide8", "client"));
        AssertEqual(LaunchProfileKind.Nide8, profile.Kind);
    }
}
