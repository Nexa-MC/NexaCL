using System.Text.Json.Nodes;
using Nexa.Services;
using Nexa.Services.Downloads;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Downloads;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void GameSourceSettingsKeepAuthorityAndRegionBoundaries()
    {
        PersistedMirrorSourcesKeepRegionalAdmission();
        const string original = "https://piston-data.mojang.com/file.jar";
        string[] sources = [original, "https://bmclapi2.bangbang93.com/file.jar"];
        AssertEqual(original, new MinecraftDownloadPolicy().SelectSources(original, sources, new("CN"))[0]);
        AssertEqual(sources[1], new MinecraftDownloadPolicy(Source: "mirrors-first").SelectSources(original, sources, new("CN"))[0]);
        AssertEqual(original, new MinecraftDownloadPolicy(Source: "official-only").SelectSources(original, sources, new("CN")).Single());
        foreach (string country in new[] { "US", "TW", "HK", "MO", "" })
            AssertEqual(original, new MinecraftDownloadPolicy(Source: "mirrors-first").SelectSources(original, sources, new(country)).Single());
        string[] unhashed = MinecraftDownloadSourcePlanner.GetLauncherOrMetaSources(original, true, null);
        AssertEqual(original, new MinecraftDownloadPolicy(Source: "mirrors-first").SelectSources(original, unhashed, new("CN")).Single());
        var port = new InMemorySettingsPort();
        foreach (var pair in new[] { ("0", "mirrors-first"), ("1", "official-first"), ("2", "official-only") })
        {
            port.Save(new Dictionary<string, string> { ["ToolDownloadSource"] = pair.Item1 });
            var (_, policy) = PolicyFixture(port);
            AssertEqual(pair.Item2, MinecraftDownloadPolicy.Read(policy).Source);
            AssertTrue(policy.Set(new("network.game-source", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "official-only"))).IsSuccess);
            var (_, reopened) = PolicyFixture(port);
            AssertEqual("official-only", MinecraftDownloadPolicy.Read(reopened).Source);
            AssertFalse(policy.Set(new("network.game-source", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "unknown-source"))).IsSuccess);
            AssertTrue(policy.Set(new("network.game-source", SettingsLayer.Global, new(SettingsOverrideMode.Inherit))).IsSuccess);
            AssertEqual("official-first", MinecraftDownloadPolicy.Read(policy).Source);
        }
    }

    private static void PersistedMirrorSourcesKeepRegionalAdmission()
    {
        string digest = new('a', 40);
        const string litePath = "com/mumfrey/liteloader/1.12.2-SNAPSHOT/liteloader-1.12.2-SNAPSHOT.jar";
        const string storedLite = "https://bmclapi2.bangbang93.com/maven/" + litePath;
        const string officialLite = "https://repo.liteloader.com/" + litePath;
        JsonObject profile = new()
        {
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "com.mumfrey:liteloader:1.12.2-SNAPSHOT",
                ["url"] = "https://bmclapi2.bangbang93.com/maven/",
                ["downloads"] = new JsonObject { ["artifact"] = new JsonObject { ["sha1"] = digest, ["size"] = 123 } }
            })
        };
        string persisted = profile.ToJsonString();
        MinecraftLibraryToken library = MinecraftLibraryResolver.Resolve(new()
        {
            VersionJson = profile,
            MinecraftRootDirectory = Path.GetTempPath(),
            OperatingSystem = MinecraftLibraryOperatingSystem.Linux,
            Is64BitArchitecture = true
        }).Single();
        AssertEqual(storedLite, library.Url);
        AssertEqual(digest, library.Sha1);
        AssertEqual(123L, library.Size);
        foreach (string country in new[] { "US", "GB", "TW", "HK", "MO", "" })
        {
            RegionalPolicy region = new(country);
            foreach (string? sha in new[] { library.Sha1, null })
            {
                string[] planned = MinecraftDownloadSourcePlanner.GetLibrarySources(library.Url!, true, sha, region);
                AssertEqual(officialLite, planned.Single());
                foreach (string preference in new[] { "official-first", "mirrors-first", "official-only" })
                {
                    var policy = new MinecraftDownloadPolicy(Source: preference);
                    // Install and launch append the original manifest URL as a last resort.
                    AssertEqual(officialLite, policy.SelectSources(library.Url!, planned.Append(library.Url!), region).Single());
                    AssertEqual(officialLite, policy.SelectSources(library.Url!, [library.Url!], region).Single());
                }
            }
            foreach ((string stored, string official) in new[]
            {
                ("https://mirrors.cernet.edu.cn/bmclapi/" + litePath, officialLite),
                ("https://cmcc.mirrors.ustc.edu.cn/bmclapi/" + litePath, officialLite),
                ("https://bmclapi.bangbang93.com/maven/net/minecraftforge/forge/forge.jar", "https://maven.minecraftforge.net/net/minecraftforge/forge/forge.jar"),
                ("https://bmclapi2.bangbang93.com/libraries/net/fabricmc/fabric-loader/loader.jar", "https://maven.fabricmc.net/net/fabricmc/fabric-loader/loader.jar"),
                ("https://download.mcbbs.net/maven/net/neoforged/neoforge/forge.jar", "https://maven.neoforged.net/releases/net/neoforged/neoforge/forge.jar"),
                ("https://bmclapi2.bangbang93.com/v1/objects/" + digest + "/client.jar", "https://piston-data.mojang.com/v1/objects/" + digest + "/client.jar"),
                ("https://bmclapi2.bangbang93.com/v1/packages/" + digest + "/1.20.1.json", "https://piston-meta.mojang.com/v1/packages/" + digest + "/1.20.1.json"),
                ("https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
                ("https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto/rd-132211.json", "https://zkitefly.github.io/unlisted-versions-of-minecraft/rd-132211.json")
            })
            {
                AssertEqual(official, MinecraftDownloadSourcePlanner.OrderSources([stored], [], false, region).Single());
                AssertEqual(official, MinecraftDownloadSourcePlanner.GetLauncherOrMetaSources(stored, true, digest, region).Single());
                AssertEqual(official, new MinecraftDownloadPolicy().SelectSources(stored, [stored], region).Single());
            }
            string storedAsset = "https://bmclapi2.bangbang93.com/assets/aa/" + digest;
            AssertEqual("https://resources.download.minecraft.net/aa/" + digest,
                MinecraftDownloadSourcePlanner.GetAssetSources(storedAsset, false, region).Single());
            foreach (string unavailable in new[]
            {
                "https://bmclapi2.bangbang93.com/optifine/1.12.2/HD_U/G5",
                "https://bmclapi2.bangbang93.com/maven/unknown/authority/file.jar",
                "https://mod.mcimirror.top/curseforge/v1/mods/1/files/2",
                "https://mcim-files.pysio.online/file.jar", storedLite + "?source=unknown", storedLite + "#unknown",
                storedLite.Replace("https://", "https://user@", StringComparison.Ordinal)
            })
            {
                static void Reject(Action action)
                {
                    try { action(); }
                    catch (InvalidDataException) { return; }
                    throw new InvalidOperationException("A persisted regional mirror bypassed admission.");
                }
                Reject(() => MinecraftDownloadSourcePlanner.GetLibrarySources(unavailable, true, digest, region));
                Reject(() => MinecraftDownloadSourcePlanner.GetLibrarySources(unavailable, true, null, region));
                Reject(() => new MinecraftDownloadPolicy().SelectSources(unavailable, [unavailable], region));
            }
            const string unrelated = "https://bmclapi2.bangbang93.com.example.test/custom/file.jar";
            AssertEqual(unrelated, MinecraftDownloadSourcePlanner.GetLibrarySources(unrelated, true, null, region).Single());
        }
        AssertEqual(persisted, profile.ToJsonString());
        AssertEqual(storedLite, new MinecraftDownloadPolicy(Source: "mirrors-first").SelectSources(storedLite, [storedLite], new("CN")).Single());
    }

    private static async ValueTask GameSourceSettingsReachInstallAndLaunchTransfers()
    {
        foreach (string sourcePolicy in new[] { "official-first", "mirrors-first", "official-only" })
        {
            var (_, policy) = PolicyFixture();
            AssertTrue(policy.Set(new("network.game-source", SettingsLayer.Global, new(SettingsOverrideMode.Custom, sourcePolicy))).IsSuccess);
            var requested = new List<string>();
            IDownloadConnection Connect(string source)
            {
                lock (requested) requested.Add(source);
                if (source.EndsWith("/5.json", StringComparison.Ordinal))
                    return new ServingConnection(System.Text.Encoding.UTF8.GetBytes(AssetIndexJson().ToJsonString()));
                return new ServingConnection(PayloadFor(source));
            }
            var metadata = new FakeMetadata { VanillaJson = VanillaJson(), AssetIndexJson = AssetIndexJson() };
            using var install = new InstallFixture(metadata, settingsPolicy: policy, connectionFactory: Connect);
            string root = CreateTempDirectory();
            try
            {
                AssertTrue((await install.Install.InstallAsync(new(root, "1.20.1", null, null))).IsSuccess);
                AssertSources();
                requested.Clear();
                var instance = new MinecraftInstanceDescriptor("1.20.1", Path.Combine(root, "versions", "1.20.1"), "1.20.1",
                    new MinecraftVersionDescriptor("1.20.1", "1.20.1", Path.Combine(root, "versions", "1.20.1", "1.20.1.json"), null, null, null, null,
                        new MinecraftVersionClassification("1.20.1", "release", MinecraftVersionCategory.Release, null)), new MinecraftInstanceMetadata());
                File.Delete(Path.Combine(instance.DirectoryPath, "1.20.1.jar"));
                using var fixture = new CompletionFixture();
                using var completion = new MinecraftLaunchFileCompletion(fixture.Downloads, connectionFactory: Connect, settingsPolicy: policy) { CountryPolicy = new("US") };
                await completion.CompleteAsync(root, instance, new(VanillaJson(), []), new(MinecraftLibraryOperatingSystem.Win32, "10.0", true, false),
                    "offline", null, CancellationToken.None);
                AssertSources();
                void AssertSources()
                {
                    AssertTrue(requested.Count > 0);
                    bool mirror = sourcePolicy == "mirrors-first" && RegionalPolicy.Current.IsMainlandChina;
                    AssertTrue(requested.All(url => url.Contains("bmclapi2", StringComparison.Ordinal) == mirror));
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
