using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask ModpackUpdatePublicationResumesAndRollsBackAtomically()
    {
        foreach (bool resume in new[] { false, true })
        {
            string root = CreateTempDirectory();
            try
            {
                const string id = "pack";
                string instance = Path.Combine(root, "versions", id), stage = Path.Combine(root, ".nexa-modify", Guid.NewGuid().ToString("N"));
                string relative = "versions/pack/config/config.txt", added = "versions/pack/mods/new.jar";
                Directory.CreateDirectory(Path.Combine(instance, "config"));
                await File.WriteAllTextAsync(Path.Combine(instance, "config", "config.txt"), "original");
                Directory.CreateDirectory(Path.Combine(stage, "versions", id, "config")); Directory.CreateDirectory(Path.Combine(stage, "versions", id, "mods"));
                await File.WriteAllTextAsync(Path.Combine(stage, relative), "updated"); await File.WriteAllTextAsync(Path.Combine(stage, added), "verified-new-file");
                var journal = await InstallPublicationJournal.PrepareAsync(root, stage, id, [relative, added], new Dictionary<string, string>(), default, modpackUpdate: true);
                using var stop = new CancellationTokenSource();
                try { await journal.ApplyAsync(stop.Token, (done, _) => { if (done == 1) stop.Cancel(); }); }
                catch (OperationCanceledException) { }
                var reopened = await InstallPublicationJournal.OpenAsync(root, stage, default);
                AssertTrue(reopened.IsModpackUpdate);
                if (resume) await reopened.ApplyAsync(default); else await reopened.RollbackAsync(default);
                AssertEqual(resume ? "updated" : "original", await File.ReadAllTextAsync(Path.Combine(root, relative)));
                AssertEqual(resume, File.Exists(Path.Combine(root, added)));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    private static void WriteUpdatePack(string path, string version, bool next, bool badHash = false, bool freshDownload = false)
    {
        var index = MrpackIndex(); index["versionId"] = version;
        index["files"] = badHash ? new JsonArray(PackFile("mods/broken.jar", "required", new string('0', 40)))
            : freshDownload ? new JsonArray(PackFile("mods/fresh.jar", "required")) : new JsonArray();
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open())) writer.Write(index.ToJsonString());
        foreach (var (name, bytes) in new[]
        {
            (next ? "mods/main-2.jar" : "mods/main-1.jar", ResourceJar("main")),
            ("config/default.txt", Encoding.UTF8.GetBytes(next ? "new-default" : "old-default")),
            ("config/local.txt", Encoding.UTF8.GetBytes(next ? "new-local" : "old-local")),
            ("config/Nexa/managed.txt", Encoding.UTF8.GetBytes(next ? "new-nested" : "old-nested")),
        })
        { using var output = zip.CreateEntry("overrides/" + name).Open(); output.Write(bytes); }
        if (!next) { using var output = zip.CreateEntry("overrides/mods/obsolete.jar").Open(); output.Write(ResourceJar("obsolete")); }
    }

    private static async ValueTask ModpackUpdatesPreserveUserFilesAndRejectStaleOrInvalidInputs()
    {
        foreach (string scenario in new[] { "success", "legacy", "stale", "archive-change", "download-error", "modified", "replace-modified", "cancel", "cancel-preparing", "running" })
        {
            string temporary = CreateTempDirectory();
            try
            {
                string root = Path.Combine(temporary, "game"); Directory.CreateDirectory(root);
                string oldPack = Path.Combine(temporary, "old.mrpack"), newPack = Path.Combine(temporary, "new.mrpack");
                WriteUpdatePack(oldPack, "1.0", false);
                // Cancellation during preparation needs a fresh object; existing dependencies
                // are served from the verified session cache without opening a connection.
                WriteUpdatePack(newPack, "2.0", true, scenario == "download-error", scenario == "cancel-preparing");
                using var stop = new CancellationTokenSource();
                bool cancelPreparing = false;
                using var fixture = new InstallFixture(PackMetadata(), connectionFactory: url =>
                {
                    if (cancelPreparing) stop.Cancel();
                    return new ServingConnection(PayloadFor(url));
                });
                var original = await MinecraftModpackArchive.InspectAsync(oldPack);
                var installed = await fixture.Install.InstallModpackAsync(new(original, root)); AssertTrue(installed.IsSuccess);
                string instance = installed.Value!.InstanceDirectory;
                Directory.CreateDirectory(Path.Combine(instance, "saves", "world"));
                await File.WriteAllTextAsync(Path.Combine(instance, "saves", "world", "level.dat"), "user-world");
                await File.WriteAllTextAsync(Path.Combine(instance, "options.txt"), "user-options");
                await File.WriteAllTextAsync(Path.Combine(instance, "config", "local.txt"), "user-local");
                await File.WriteAllBytesAsync(Path.Combine(instance, "mods", "extra.jar"), ResourceJar("extra"));
                var metadata = new MinecraftInstanceMetadataStore();
                await metadata.UpdateAsync(instance, value => value with { DisplayName = "My instance", Notes = "keep notes" });
                if (scenario == "legacy") File.Delete(Path.Combine(instance, "Nexa", "ModpackManaged.json"));
                if (scenario is "modified" or "replace-modified") await File.WriteAllBytesAsync(Path.Combine(instance, "mods", "main-1.jar"), ResourceJar("custom-main"));
                var query = new MinecraftModpackUpdateQuery(instance, newPack, scenario == "legacy" ? oldPack : null,
                    ReplaceModifiedMods: scenario == "replace-modified");
                var preview = await fixture.Install.PreviewModpackUpdateAsync(query);
                AssertTrue(preview.Preserved.Contains("config/local.txt"));
                if (scenario == "modified") AssertTrue(preview.Conflicts.Contains("mods/main-1.jar"));
                if (scenario == "stale") await File.WriteAllTextAsync(Path.Combine(instance, "config", "default.txt"), "changed-after-preview");
                if (scenario == "archive-change") await File.AppendAllTextAsync(newPack, "changed-after-preview");
                if (scenario == "running")
                {
                    var process = new MinecraftProcessSnapshot(Guid.NewGuid(), original.InstanceId, 123, MinecraftProcessState.Running, null, DateTimeOffset.UtcNow, null) { InstanceDirectory = instance };
                    fixture.Store.PublishDelta(fixture.Store.Resolve(MinecraftProcessStateComposition.SessionsKey), new XsrCollectionDelta<MinecraftProcessSnapshot, Guid>(0, [process], []));
                }
                if (scenario == "cancel") await stop.CancelAsync();
                cancelPreparing = scenario == "cancel-preparing";
                var result = await fixture.Install.UpdateModpackAsync(new(preview), stop.Token);
                bool success = scenario is "success" or "legacy" or "replace-modified";
                AssertTrue(!success || result.IsSuccess, scenario + ": " + result.Error?.Message);
                AssertEqual(success, result.IsSuccess);
                AssertEqual("user-world", await File.ReadAllTextAsync(Path.Combine(instance, "saves", "world", "level.dat")));
                AssertEqual("user-options", await File.ReadAllTextAsync(Path.Combine(instance, "options.txt")));
                AssertEqual("user-local", await File.ReadAllTextAsync(Path.Combine(instance, "config", "local.txt")));
                AssertEqual(success ? "new-nested" : "old-nested", await File.ReadAllTextAsync(Path.Combine(instance, "config", "Nexa", "managed.txt")));
                AssertTrue(File.Exists(Path.Combine(instance, "mods", "extra.jar")));
                AssertEqual(success, File.Exists(Path.Combine(instance, "mods", "main-2.jar")));
                AssertEqual(!success, File.Exists(Path.Combine(instance, "mods", "main-1.jar")));
                AssertEqual(!success, File.Exists(Path.Combine(instance, "mods", "obsolete.jar")));
                var saved = await metadata.LoadAsync(instance);
                AssertEqual(success ? "2.0" : "1.0", saved.ModpackVersion); AssertEqual("My instance", saved.DisplayName); AssertEqual("keep notes", saved.Notes);
                var tasks = fixture.Store.ReadCollection<TaskCenterEntry>(fixture.Store.Resolve(TaskCenterStateContract.EntriesKey)).Items;
                AssertEqual(2, tasks.Count); AssertTrue(tasks.All(task => task.IsTerminal));
                if (scenario is "cancel" or "cancel-preparing")
                    AssertEqual(TaskCenterEntryState.Canceled, tasks.Single(task => task.TaskId.StartsWith("modpack.update:", StringComparison.Ordinal)).State);
                AssertTrue(fixture.InstalledRoots.All(value => value == root));
                Console.WriteLine("PASS modpack update scenario: " + scenario);
            }
            finally { Directory.Delete(temporary, recursive: true); }
        }
    }
}
