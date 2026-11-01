using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask RenameIdentityPreservesUnknownSettingsAndRejectsChangedContent()
    {
        string root = CreateTempDirectory();
        try
        {
            var (store, settings) = PolicyFixture();
            string source = Path.Combine(root, "versions", "old"), target = Path.Combine(root, "versions", "new");
            string from = OperatingSystem.IsWindows() ? source.ToUpperInvariant() : source;
            string to = OperatingSystem.IsWindows() ? target.ToUpperInvariant() : target;
            AssertTrue(settings.Set(new("game.width", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "1440"), source)).IsSuccess);
            var document = JsonNode.Parse(store.ReadBatch().Values[SettingsPolicySchema.StorageKey])!.AsObject();
            document["instances"]![from]!["future-setting"] = JsonNode.Parse("""{"items":[true,"preserved"],"number":1.0}""");
            AssertTrue(store.SetRawValue(SettingsPolicySchema.StorageKey, document.ToJsonString()).IsSuccess);
            var prepared = await settings.PrepareRenameAsync(source, target); AssertTrue(prepared.IsSuccess);
            AssertTrue((await settings.ApplyRenameAsync(prepared.Value, false)).IsSuccess);
            var moved = JsonNode.Parse(store.ReadBatch().Values[SettingsPolicySchema.StorageKey])!;
            AssertEqual("preserved", moved["instances"]![to]!["future-setting"]!["items"]![1]!.GetValue<string>());
            AssertTrue((await settings.ApplyRenameAsync(prepared.Value, true)).IsSuccess);
            AssertTrue(settings.Set(new("game.width", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "1600"), source)).IsSuccess);
            AssertFalse((await settings.ApplyRenameAsync(prepared.Value, false)).IsSuccess);
            AssertEqual("1600", Effective(settings, "game.width", source).Value.Value!);
            AssertEqual(SettingsRenameIdentity.Capture("""{"b":100,"a":1.0}""", null),
                SettingsRenameIdentity.Capture("""{"a":1,"b":1e2}""", null));
            AssertFalse(SettingsRenameIdentity.Capture("[1,2]", null) == SettingsRenameIdentity.Capture("[2,1]", null));
        }
        finally { Directory.Delete(root, true); }
    }
    private static async ValueTask RenameJournalResumesEveryInterruptedPrefixAndRollsBack()
    {
        foreach (bool remote in new[] { false, true })
            foreach (int stop in Enumerable.Range(1, 8))
            {
                string root = CreateTempDirectory();
                try
                {
                    var (_, settings) = PolicyFixture();
                    ISettingsRenameTransactions transactions = remote ? new AsyncRenamePort(settings) : settings;
                    string source = Path.Combine(root, "versions", "old"), target = Path.Combine(root, "versions", "new");
                    Directory.CreateDirectory(source);
                    byte[] before = "{\"id\":\"old\"}"u8.ToArray(), after = "{\"id\":\"new\"}"u8.ToArray();
                    await File.WriteAllBytesAsync(Path.Combine(source, "old.json"), before);
                    await File.WriteAllTextAsync(Path.Combine(source, "old.jar"), "core");
                    await File.WriteAllTextAsync(Path.Combine(source, "options.txt"), "user settings");
                    AssertTrue(settings.Set(new("game.width", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "1440"), source)).IsSuccess);
                    string transaction = Path.Combine(root, ".nexa-rename", Guid.NewGuid().ToString("N"));
                    var journal = await InstanceRenameJournal.PrepareAsync(root, transaction, source, target, "old", "new", true,
                        [(Path.Combine(source, "old.json"), Path.Combine(target, "new.json"), before, after)], transactions, default);
                    var disk = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(transaction, "plan.json")))!;
                    AssertEqual(remote ? 2 : 1, disk["Schema"]!.GetValue<int>());
                    using var cancellation = new CancellationTokenSource();
                    try { await journal.ApplyAsync(transactions, cancellation.Token, count => { if (count == stop) cancellation.Cancel(); }); }
                    catch (OperationCanceledException) { }
                    var reopened = await InstanceRenameJournal.OpenAsync(root, transaction, default);
                    try { await reopened.ApplyAsync(transactions, default); }
                    catch (IOException error) { throw new InvalidOperationException("Interrupted prefix " + stop, error); }
                    AssertFalse(Directory.Exists(source));
                    AssertEqual("core", await File.ReadAllTextAsync(Path.Combine(target, "new.jar")));
                    AssertEqual("user settings", await File.ReadAllTextAsync(Path.Combine(target, "options.txt")));
                    AssertEqual("1440", Effective(settings, "game.width", target).Value.Value!);
                    await reopened.RollbackAsync(transactions, default);
                    AssertFalse(Directory.Exists(target));
                    AssertEqual("core", await File.ReadAllTextAsync(Path.Combine(source, "old.jar")));
                    AssertEqual("1440", Effective(settings, "game.width", source).Value.Value!);
                    byte[] restored = await File.ReadAllBytesAsync(Path.Combine(source, "old.json"));
                    AssertTrue(before.AsSpan().SequenceEqual(restored));
                }
                finally { Directory.Delete(root, true); }
            }
    }
    private sealed class AsyncRenamePort(SettingsPolicyService settings) : ISettingsRenameTransactions
    {
        public async ValueTask<Nexa.Xsr.XsrResult<SettingsRenameReceipt>> PrepareRenameAsync(string source, string destination, CancellationToken token = default)
        { await Task.Yield(); return await settings.PrepareRenameAsync(source, destination, token); }
        public async ValueTask<Nexa.Xsr.XsrResult> ApplyRenameAsync(SettingsRenameReceipt receipt, bool reverse, CancellationToken token = default)
        { await Task.Yield(); return await settings.ApplyRenameAsync(receipt, reverse, token); }
    }

    private static async ValueTask RenameOnlyPendingInstallResumesWithoutReinstalling()
    {
        string root = CreateTempDirectory();
        try
        {
            var (_, settings) = PolicyFixture();
            using var fixture = new InstallFixture(new() { VanillaJson = VanillaJson(), AssetIndexJson = AssetIndexJson() }, settingsPolicy: settings);
            AssertTrue((await fixture.Install.InstallAsync(new(root, "1.20.1"))).IsSuccess);
            var original = await MinecraftInstallEditService.ReadAsync(new(root, "1.20.1"));
            string stage = Path.Combine(root, ".nexa-modify", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            await InstallTaskJournal.CreateAsync(stage, new(root, "1.20.1", InstanceName: "1.20.1", EditFingerprint: original.Fingerprint) { NewInstanceName = "renamed", InheritVanilla = false }, default);
            var metadata = new FakeMetadata();
            using var resumed = new InstallFixture(metadata, settingsPolicy: settings);
            AssertTrue((await resumed.Install.RecoverPendingAsync(new([root]))).IsSuccess);
            AssertEqual(0, metadata.VanillaReads);
            AssertTrue(File.Exists(Path.Combine(root, "versions", "renamed", "renamed.json")));
            AssertFalse(Directory.Exists(Path.Combine(root, "versions", "1.20.1")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ComponentPublicationAndRenameRecoverAsOneTask()
    {
        foreach (bool rollback in new[] { false, true })
        {
            string root = CreateTempDirectory();
            try
            {
                var (_, settings) = PolicyFixture();
                using var fixture = new InstallFixture(new() { VanillaJson = VanillaJson(), AssetIndexJson = AssetIndexJson() }, settingsPolicy: settings);
                AssertTrue((await fixture.Install.InstallAsync(new(root, "1.20.1"))).IsSuccess);
                string source = Path.Combine(root, "versions", "1.20.1"), destination = Path.Combine(root, "versions", "renamed");
                string manifest = Path.Combine(source, "1.20.1.json");
                byte[] originalBytes = await File.ReadAllBytesAsync(manifest);
                var original = await MinecraftInstallEditService.ReadAsync(new(root, "1.20.1"));
                Guid id = Guid.NewGuid();
                string stage = Path.Combine(root, ".nexa-modify", id.ToString("N")); Directory.CreateDirectory(stage);
                await InstallTaskJournal.CreateAsync(stage, new(root, "1.20.1", InstanceName: "1.20.1", EditFingerprint: original.Fingerprint)
                { NewInstanceName = "renamed", InheritVanilla = false }, default);
                var edited = JsonNode.Parse(originalBytes)!.AsObject(); edited["testComponent"] = "new";
                string relative = "versions/1.20.1/1.20.1.json", staged = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await File.WriteAllTextAsync(staged, edited.ToJsonString());
                var publication = await InstallPublicationJournal.PrepareAsync(root, stage, "1.20.1", [relative], new Dictionary<string, string>(), default);
                await publication.ApplyAsync(default);
                byte[] renamedBefore = await File.ReadAllBytesAsync(manifest);
                edited["id"] = "renamed";
                byte[] renamedAfter = System.Text.Encoding.UTF8.GetBytes(edited.ToJsonString());
                var rename = await InstanceRenameJournal.PrepareAsync(root, Path.Combine(stage, ".rename"), source, destination,
                    "1.20.1", "renamed", true, [(manifest, Path.Combine(destination, "renamed.json"), renamedBefore, renamedAfter)], settings, default);
                using var cancellation = new CancellationTokenSource();
                try { await rename.ApplyAsync(settings, cancellation.Token, step => { if (step == 3) cancellation.Cancel(); }); }
                catch (OperationCanceledException) { }
                using var reopened = new InstallFixture(new FakeMetadata(), settingsPolicy: settings);
                if (rollback)
                {
                    await reopened.Install.RollbackInstallationAsync(root, id);
                    byte[] restored = await File.ReadAllBytesAsync(manifest);
                    AssertTrue(originalBytes.AsSpan().SequenceEqual(restored));
                    AssertFalse(Directory.Exists(destination));
                    AssertTrue(File.Exists(Path.Combine(source, "1.20.1.jar")));
                }
                else
                {
                    AssertTrue((await reopened.Install.RecoverPendingAsync(new([root]))).IsSuccess);
                    var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "renamed.json")))!;
                    AssertEqual("new", result["testComponent"]!.GetValue<string>());
                    AssertTrue(File.Exists(Path.Combine(destination, "renamed.jar")));
                    AssertFalse(Directory.Exists(source));
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }

    private static async ValueTask InstanceRenamePreservesDataSettingsAndDependencies()
    {
        string root = CreateTempDirectory();
        try
        {
            var (_, settings) = PolicyFixture();
            using var fixture = new InstallFixture(new() { VanillaJson = VanillaJson(), AssetIndexJson = AssetIndexJson() }, settingsPolicy: settings);
            AssertTrue((await fixture.Install.InstallAsync(new(root, "1.20.1"))).IsSuccess);
            string source = Path.Combine(root, "versions", "1.20.1"), target = Path.Combine(root, "versions", "My Game");
            Directory.CreateDirectory(Path.Combine(source, "saves", "world"));
            File.WriteAllText(Path.Combine(source, "saves", "world", "level.dat"), "world");
            AssertTrue(settings.Set(new("game.jvm", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "-XX:+UseG1GC"), source)).IsSuccess);
            string dependent = Path.Combine(root, "versions", "dependent"); Directory.CreateDirectory(dependent);
            File.WriteAllText(Path.Combine(dependent, "dependent.json"), """{"id":"dependent","inheritsFrom":"1.20.1","jar":"1.20.1"}""");
            var original = await MinecraftInstallEditService.ReadAsync(new(root, "1.20.1"));
            var result = await fixture.Install.InstallAsync(new(root + Path.DirectorySeparatorChar, "1.20.1", InstanceName: "1.20.1", EditFingerprint: original.Fingerprint) { NewInstanceName = "My Game" });
            AssertTrue(result.IsSuccess);
            AssertEqual("My Game", result.Value!.InstanceId);
            AssertFalse(Directory.Exists(source));
            AssertEqual("world", File.ReadAllText(Path.Combine(target, "saves", "world", "level.dat")));
            AssertTrue(File.Exists(Path.Combine(target, "My Game.jar")));
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dependent, "dependent.json")))!;
            AssertEqual("My Game", json["inheritsFrom"]!.GetValue<string>());
            AssertEqual("My Game", json["jar"]!.GetValue<string>());
            AssertEqual("-XX:+UseG1GC", Effective(settings, "game.jvm", target).Value.Value!);
            AssertEqual(SettingsLayer.Builtin, Effective(settings, "game.jvm", source).Source);
            var renamed = await MinecraftInstallEditService.ReadAsync(new(root, "My Game"));
            AssertEqual("1.20.1", renamed.GameVersion);
            // A case-only rename also updates the actual manifest/JAR filenames.
            await MinecraftInstanceRenamer.RenameAsync(root, "My Game", "my game", renamed.GameVersion, renamed.Fingerprint, settings, fixture.Store, default);
            AssertTrue(File.Exists(Path.Combine(root, "versions", "my game", "my game.json")));
            AssertTrue(File.Exists(Path.Combine(root, "versions", "my game", "my game.jar")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask InstanceRenameRejectsActiveGamesAndRollsBackSettingsFailure()
    {
        string root = CreateTempDirectory();
        try
        {
            var port = new PolicyFailingPort();
            var (_, settings) = PolicyFixture(port);
            using var fixture = new InstallFixture(new() { VanillaJson = VanillaJson(), AssetIndexJson = AssetIndexJson() }, settingsPolicy: settings);
            AssertTrue((await fixture.Install.InstallAsync(new(root, "1.20.1"))).IsSuccess);
            string source = Path.Combine(root, "versions", "1.20.1"), target = Path.Combine(root, "versions", "renamed");
            AssertTrue(settings.Set(new("game.width", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "1440"), source)).IsSuccess);
            var original = await MinecraftInstallEditService.ReadAsync(new(root, "1.20.1"));
            Task Rename(string fingerprint) => MinecraftInstanceRenamer.RenameAsync(root, "1.20.1", "renamed", "1.20.1", fingerprint, settings, fixture.Store, default);
            try { await Rename("stale"); throw new InvalidOperationException("Stale edit accepted."); } catch (IOException) { }
            Directory.CreateDirectory(target);
            try { await Rename(original.Fingerprint); throw new InvalidOperationException("Collision accepted."); } catch (IOException) { }
            Directory.Delete(target);
            var state = fixture.Store.Resolve(MinecraftProcessStateComposition.SessionsKey);
            var process = new MinecraftProcessSnapshot(Guid.NewGuid(), "1.20.1", 1, MinecraftProcessState.Running, null, DateTimeOffset.UtcNow, null) { InstanceDirectory = source };
            fixture.Store.PublishDelta(state, new XsrCollectionDelta<MinecraftProcessSnapshot, Guid>(0, [process], []));
            bool blocked = false;
            try { await Rename(original.Fingerprint); } catch (InvalidOperationException) { blocked = true; }
            AssertTrue(blocked);
            AssertFalse((await fixture.Install.InstallAsync(new(root, "1.20.1", InstanceName: "1.20.1", EditFingerprint: original.Fingerprint)
            { NewInstanceName = "renamed" })).IsSuccess);
            AssertEqual(original.Fingerprint, (await MinecraftInstallEditService.ReadAsync(new(root, "1.20.1"))).Fingerprint);
            fixture.Store.PublishDelta(state, new XsrCollectionDelta<MinecraftProcessSnapshot, Guid>(fixture.Store.ReadCollection<MinecraftProcessSnapshot>(state).Revision, [], [process.SessionId]));
            port.Fail = true;
            try { await Rename(original.Fingerprint); throw new InvalidOperationException("Settings failure accepted."); } catch (IOException) { }
            AssertTrue(File.Exists(Path.Combine(source, "1.20.1.jar")));
            AssertEqual(original.Fingerprint, (await MinecraftInstallEditService.ReadAsync(new(root, "1.20.1"))).Fingerprint);
            AssertFalse(Directory.Exists(target));
            AssertEqual("1440", Effective(settings, "game.width", source).Value.Value!);
        }
        finally { Directory.Delete(root, true); }
    }
}
