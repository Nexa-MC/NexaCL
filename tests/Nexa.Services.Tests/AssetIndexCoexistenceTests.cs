using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask ModpackAssetIndexesCoexist()
    {
        string temporary = CreateTempDirectory();
        try
        {
            string root = Path.Combine(temporary, "game"); Directory.CreateDirectory(root);
            var metadata = PackMetadata(); metadata.HasVerifiedIndexBytes = true;
            string originalId = metadata.VanillaJson["assetIndex"]!["id"]!.GetValue<string>();
            string legacy = Path.Combine(root, "assets", "indexes", originalId + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            await File.WriteAllTextAsync(legacy, "{\"objects\":{}}");
            for (int snapshot = 0; snapshot < 2; snapshot++)
            {
                metadata.AssetIndexJson["virtual"] = false;
                metadata.AssetIndexJson["map_to_resources"] = snapshot == 1;
                byte[] bytes = Encoding.UTF8.GetBytes(metadata.AssetIndexJson.ToJsonString());
                string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(bytes));
                metadata.VanillaJson["assetIndex"]!["sha1"] = hash;
                metadata.VanillaJson["assetIndex"]!["size"] = bytes.Length;
                string archive = Path.Combine(temporary, "snapshot-" + snapshot + ".mrpack");
                var index = MrpackIndex(); index["versionId"] = "snapshot-" + snapshot;
                WriteLocalJar(archive, ("modrinth.index.json", index.ToJsonString()));
                using var fixture = new InstallFixture(metadata);
                var pack = await MinecraftModpackArchive.InspectAsync(archive);
                var result = await fixture.Install.InstallModpackAsync(new(pack, root));
                AssertTrue(result.IsSuccess, result.Error?.Message ?? "install");
                var installed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(result.Value!.InstanceDirectory, pack.InstanceId + ".json")))!;
                AssertEqual(originalId + "-" + hash, installed["assetIndex"]!["id"]!.GetValue<string>());
                var savedBytes = await File.ReadAllBytesAsync(Path.Combine(root, "assets", "indexes", originalId + "-" + hash + ".json"));
                AssertTrue(bytes.SequenceEqual(savedBytes));
                AssertEqual(originalId, metadata.VanillaJson["assetIndex"]!["id"]!.GetValue<string>());
                AssertEqual("{\"objects\":{}}", await File.ReadAllTextAsync(legacy));
            }
            AssertEqual(3, Directory.GetFiles(Path.GetDirectoryName(legacy)!, "*.json").Length);
        }
        finally { Directory.Delete(temporary, true); }
    }
}
