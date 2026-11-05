using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask AssetArchivePathsRetainLinkAdmission()
    {
        string temporary = CreateTempDirectory();
        try
        {
            string physical = Path.Combine(temporary, "physical"); Directory.CreateDirectory(physical);
            string pack = Path.Combine(physical, "pack.mrpack"), jar = Path.Combine(physical, "patch.jar");
            WriteLocalJar(pack, ("modrinth.index.json", MrpackIndex().ToJsonString()));
            WriteLocalJar(jar, ("example.class", "class"));
            _ = await MinecraftModpackArchive.InspectAsync(pack);
            _ = await MinecraftLocalJarService.InspectAsync(jar);
            if (OperatingSystem.IsMacOS())
            {
                foreach (string alias in new[] { "/var", "/tmp", "/etc" })
                {
                    AssertTrue(Nexa.Core.PathIdentity.IsMacOSSystemDirectoryAlias(alias));
                }
            }
            if (OperatingSystem.IsWindows())
            {
                string junction = Path.Combine(temporary, "source-junction");
                var command = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "/d", "/c", "mklink", "/J", junction, physical }) command.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(command)!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token); AssertEqual(0, process.ExitCode);
                AssertEqual(pack, (await MinecraftModpackArchive.InspectAsync(Path.Combine(junction, "pack.mrpack"))).Path);
                AssertEqual(jar, (await MinecraftLocalJarService.InspectAsync(Path.Combine(junction, "patch.jar"))).Path);
            }
            if (!OperatingSystem.IsWindows())
            {
                string directoryLink = Path.Combine(temporary, "linked"), packLink = Path.Combine(temporary, "linked.mrpack"), jarLink = Path.Combine(temporary, "linked.jar");
                Directory.CreateSymbolicLink(directoryLink, physical);
                File.CreateSymbolicLink(packLink, pack); File.CreateSymbolicLink(jarLink, jar);
                AssertFalse(Nexa.Core.PathIdentity.IsMacOSSystemDirectoryAlias(directoryLink));
                string chained = Path.Combine(temporary, "chained"); Directory.CreateSymbolicLink(chained, "linked");
                AssertEqual(pack, (await MinecraftModpackArchive.InspectAsync(Path.Combine(chained, "pack.mrpack"))).Path);
                string cycle = Path.Combine(temporary, "cycle.mrpack"); File.CreateSymbolicLink(cycle, cycle);
                await TrustRejectsAsync<IOException>(() => MinecraftModpackArchive.InspectAsync(cycle));
                string missing = Path.Combine(temporary, "missing.jar"); File.CreateSymbolicLink(missing, Path.Combine(temporary, "absent.jar"));
                await TrustRejectsAsync<IOException>(() => MinecraftLocalJarService.InspectAsync(missing));
                AssertEqual(pack, (await MinecraftModpackArchive.InspectAsync(Path.Combine(directoryLink, "pack.mrpack"))).Path);
                AssertEqual(jar, (await MinecraftLocalJarService.InspectAsync(Path.Combine(directoryLink, "patch.jar"))).Path);
                AssertEqual(pack, (await MinecraftModpackArchive.InspectAsync(packLink)).Path);
                AssertEqual(jar, (await MinecraftLocalJarService.InspectAsync(jarLink)).Path);
            }
        }
        finally { Directory.Delete(temporary, true); }
    }

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
