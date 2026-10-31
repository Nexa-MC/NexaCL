using System.IO.Compression;
using System.Text.Json.Nodes;
using Nexa.Services.Downloads;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask InstanceModpackZipExportsOnlySelectedRuntimeReferences()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root);
            string baseDirectory = CreateVersionDirectory(root, "base", new JsonObject
            {
                ["id"] = "base",
                ["mainClass"] = "Main",
                ["_minecraftVersion"] = "1.21.1",
                ["assetIndex"] = new JsonObject { ["id"] = "test-index" },
                ["libraries"] = new JsonArray(new JsonObject { ["name"] = "example:runtime:1.0" })
            });
            string leafPath = Path.Combine(instance, "fixture.json");
            var leaf = JsonNode.Parse(await File.ReadAllTextAsync(leafPath))!;
            leaf["inheritsFrom"] = "base"; await File.WriteAllTextAsync(leafPath, leaf.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(baseDirectory, "base.jar"), ResourceJar("base"));
            Directory.CreateDirectory(Path.Combine(instance, "mods"));
            await File.WriteAllBytesAsync(Path.Combine(instance, "mods", "example.jar"), ResourceJar("example"));
            foreach (string relative in new[] { "libraries/example/runtime/1.0/runtime-1.0.jar", "libraries/net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar", "libraries/unrelated/secret.jar" })
            {
                string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, ResourceJar("library"));
            }
            byte[] asset = "language"u8.ToArray(); string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(asset)).ToLowerInvariant();
            string assetPath = Path.Combine(root, "assets", "objects", hash[..2], hash);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!); await File.WriteAllBytesAsync(assetPath, asset);
            Directory.CreateDirectory(Path.Combine(root, "assets", "indexes"));
            await File.WriteAllTextAsync(Path.Combine(root, "assets", "indexes", "test-index.json"), new JsonObject
            { ["objects"] = new JsonObject { ["lang/en_us.json"] = new JsonObject { ["hash"] = hash, ["size"] = asset.Length } } }.ToJsonString());
            var builder = new XsrStateStoreBuilder(); TaskCenterStateContract.DeclareState(builder); DownloadService.DeclareState(builder); MinecraftProcessStateComposition.DeclareState(builder);
            var store = builder.Build(); using var tasks = new TaskCenterService(store); var exporter = new InstanceModpackExportService(tasks, store);
            var command = new InstanceModpackExportCommand(instance, Path.Combine(root, "content.zip"), "ZIP Pack", "1", ["mods"]) { Format = InstanceExportFormat.Zip };
            var plain = await exporter.ExportAsync(command); AssertTrue(plain.IsSuccess, plain.Error?.Message ?? "");
            using (var zip = ZipFile.OpenRead(command.DestinationPath))
            {
                AssertTrue(zip.GetEntry("nexa-export.json") is not null); AssertTrue(zip.GetEntry("modrinth.index.json") is null);
                AssertTrue(zip.GetEntry(".minecraft/versions/base/base.json") is not null);
                AssertTrue(zip.GetEntry(".minecraft/versions/fixture/fixture.json") is not null);
                AssertTrue(zip.Entries.Any(entry => entry.FullName.EndsWith("mods/example.jar", StringComparison.Ordinal)));
                AssertFalse(zip.Entries.Any(entry => entry.FullName.EndsWith("base.jar", StringComparison.Ordinal) || entry.FullName.Contains("libraries/", StringComparison.Ordinal)));
            }
            var bundled = command with { DestinationPath = Path.Combine(root, "bundled.zip"), IncludeGameCore = true, IncludeLibraries = true, IncludeAssets = true };
            var result = await exporter.ExportAsync(bundled); AssertTrue(result.IsSuccess, result.Error?.Message ?? "");
            using (var zip = ZipFile.OpenRead(bundled.DestinationPath))
            {
                AssertTrue(zip.GetEntry(".minecraft/versions/base/base.jar") is not null);
                AssertTrue(zip.GetEntry(".minecraft/libraries/example/runtime/1.0/runtime-1.0.jar") is not null);
                AssertTrue(zip.GetEntry(".minecraft/assets/objects/" + hash[..2] + "/" + hash) is not null);
                AssertFalse(zip.Entries.Any(entry => entry.FullName.Contains("unrelated", StringComparison.Ordinal)));
            }
            string preset = Path.Combine(root, "zip-options.json");
            var options = new InstanceExportOptions("ZIP Pack", "1", "", ["mods"], []) { Format = InstanceExportFormat.Zip, IncludeGameCore = true, IncludeLibraries = true, IncludeAssets = true };
            await InstanceModpackExportService.SaveOptionsAsync(new(preset, options), default);
            var loaded = await InstanceModpackExportService.LoadOptionsAsync(new(preset), default);
            AssertEqual(InstanceExportFormat.Zip, loaded.Format); AssertTrue(loaded.IncludeGameCore && loaded.IncludeLibraries && loaded.IncludeAssets);
            AssertFalse((await exporter.ExportAsync(bundled with { Format = InstanceExportFormat.Mrpack, DestinationPath = Path.Combine(root, "invalid.mrpack") })).IsSuccess);
            File.Delete(assetPath);
            string failed = Path.Combine(root, "missing.zip");
            AssertFalse((await exporter.ExportAsync(bundled with { DestinationPath = failed })).IsSuccess); AssertFalse(File.Exists(failed));
            leaf["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "example:private:1",
                ["downloads"] = new JsonObject
                { ["artifact"] = new JsonObject { ["path"] = "accounts.json" } }
            });
            await File.WriteAllTextAsync(leafPath, leaf.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(root, "libraries", "accounts.json"), "private");
            string privateTarget = Path.Combine(root, "private.zip");
            AssertFalse((await exporter.ExportAsync(command with { DestinationPath = privateTarget, IncludeLibraries = true })).IsSuccess);
            AssertFalse(File.Exists(privateTarget));
            AssertFalse(Directory.EnumerateFiles(root, ".nexa-export-*").Any());
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask InstanceModpackExportRoundTripsSelectionAndRetainsExistingTargets()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root); Directory.CreateDirectory(Path.Combine(instance, "mods")); Directory.CreateDirectory(Path.Combine(instance, "config")); Directory.CreateDirectory(Path.Combine(instance, "saves", "World"));
            await File.WriteAllBytesAsync(Path.Combine(instance, "mods", "a.jar.disabled"), ResourceJar("a"));
            await File.WriteAllTextAsync(Path.Combine(instance, "config", "selected.cfg"), "config");
            await File.WriteAllTextAsync(Path.Combine(instance, "config", "accounts.json"), "private");
            await File.WriteAllTextAsync(Path.Combine(instance, "config", "test.log"), "log");
            await File.WriteAllTextAsync(Path.Combine(instance, "saves", "World", "level.dat"), "world");
            var preview = await InstanceModpackExportService.PreviewAsync(new(instance));
            AssertEqual(1, preview.Categories.Single(c => c.Id == "config").FileCount);
            AssertTrue(preview.Files.Any(file => file.Path == "config/selected.cfg"));
            var builder = new XsrStateStoreBuilder(); TaskCenterStateContract.DeclareState(builder); DownloadService.DeclareState(builder); MinecraftProcessStateComposition.DeclareState(builder);
            var store = builder.Build(); var tasks = new TaskCenterService(store); var exporter = new InstanceModpackExportService(tasks, store);
            string destination = Path.Combine(root, "export.mrpack"); var command = new InstanceModpackExportCommand(instance, destination, "示例整合包", "1.2.3", ["mods", "config"]);
            var result = await exporter.ExportAsync(command); AssertTrue(result.IsSuccess, result.Error?.Message ?? "");
            var pack = await MinecraftModpackArchive.InspectAsync(destination); AssertEqual("示例整合包", pack.Name); AssertEqual("1.2.3", pack.Version); AssertEqual("1.21.1", pack.Game);
            using (var archive = ZipFile.OpenRead(destination))
            {
                AssertTrue(archive.GetEntry("overrides/mods/a.jar.disabled") is not null);
                AssertTrue(archive.GetEntry("overrides/config/selected.cfg") is not null);
                AssertFalse(archive.Entries.Any(e => e.FullName.Contains("accounts", StringComparison.Ordinal) || e.FullName.Contains("saves", StringComparison.Ordinal) || e.FullName.EndsWith(".log", StringComparison.Ordinal)));
            }
            byte[] original = await File.ReadAllBytesAsync(destination);
            var options = new InstanceExportOptions("pack", "2", "description", ["mods", "config"], ["config/selected.cfg"]);
            string optionsPath = Path.Combine(root, "export-options.json");
            AssertTrue((await InstanceModpackExportService.SaveOptionsAsync(new(optionsPath, options), default)).IsSuccess);
            var loaded = await InstanceModpackExportService.LoadOptionsAsync(new(optionsPath), default);
            AssertTrue(loaded.ExcludedPaths.SequenceEqual(options.ExcludedPaths)); AssertEqual(options.Name, loaded.Name);
            string filteredPath = Path.Combine(root, "filtered.mrpack");
            AssertTrue((await exporter.ExportAsync(command with { DestinationPath = filteredPath, ExcludedPaths = loaded.ExcludedPaths, Summary = loaded.Summary })).IsSuccess);
            using (var filtered = ZipFile.OpenRead(filteredPath))
            {
                AssertTrue(filtered.GetEntry("overrides/mods/a.jar.disabled") is not null);
                AssertTrue(filtered.GetEntry("overrides/config/selected.cfg") is null);
                using var reader = new StreamReader(filtered.GetEntry("modrinth.index.json")!.Open());
                AssertEqual("description", JsonNode.Parse(await reader.ReadToEndAsync())!["summary"]!.GetValue<string>());
            }
            AssertFalse((await exporter.ExportAsync(command)).IsSuccess); AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(original));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            AssertFalse((await exporter.ExportAsync(command with { DestinationPath = Path.Combine(root, "canceled.mrpack") }, canceled.Token)).IsSuccess);
            AssertFalse(File.Exists(Path.Combine(root, "canceled.mrpack")));
            AssertFalse((await exporter.ExportAsync(command with { DestinationPath = Path.Combine(root, "bad.mrpack"), Categories = ["../logs"] })).IsSuccess);
            AssertFalse(Directory.EnumerateFiles(root, ".nexa-export-*").Any());
            AssertEqual(0, store.Read<TaskCenterSummary>(store.Resolve(TaskCenterStateContract.SummaryKey)).Value!.ActiveCount);
        }
        finally { Directory.Delete(root, true); }
    }
}
