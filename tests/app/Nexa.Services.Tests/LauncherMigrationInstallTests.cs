using Nexa.Services.Composition;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationInstallerProducesDiscoverableRelocatedInstances()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "prism"), destination = Path.Combine(temporary, "imported");
            await MigrationWriteAsync(source, "instance.cfg", "[General]\nname=迁入的世界\n");
            await MigrationWriteAsync(source, "mmc-pack.json",
                """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"}]}""");
            await MigrationWriteAsync(source, ".minecraft/saves/世界/level.dat", "world-data");
            var vanilla = VanillaJson(); vanilla["mainClass"] = "net.minecraft.client.main.Main";
            using InstallFixture fixture = new(new FakeMetadata { VanillaJson = vanilla, AssetIndexJson = AssetIndexJson() });
            using var migration = LauncherMigrationRuntime.CreateService((request, token) =>
                LauncherMigrationRuntime.InstallAsync(fixture.Install, request, token));
            var preview = await migration.PreviewAsync(new(LauncherMigrationSourceKind.Prism, source, destination));
            var receipt = await migration.ApplyAsync(new(preview, [preview.Instances.Single().Id]));
            var imported = receipt.Instances.Single();
            var discovered = await new MinecraftInstanceDiscovery().DiscoverAsync(imported.MinecraftRootDirectory);
            AssertTrue(discovered.Any(item => item.Id == imported.InstanceId));
            AssertTrue(File.Exists(Path.Combine(imported.MinecraftRootDirectory, "versions", imported.InstanceId, imported.InstanceId + ".jar")));
            AssertEqual("world-data", await File.ReadAllTextAsync(Path.Combine(imported.MinecraftRootDirectory,
                "versions", imported.InstanceId, "saves", "世界", "level.dat")));
            AssertFalse(Directory.Exists(Path.Combine(imported.MinecraftRootDirectory, ".nexa-install-jobs")));
            AssertTrue(fixture.Tasks.ReadEntries().All(item => item.IsTerminal));
            await migration.UndoAsync(await migration.ReadReceiptAsync(destination));
            AssertTrue(File.Exists(Path.Combine(source, ".minecraft", "saves", "世界", "level.dat")));
            AssertFalse(Directory.Exists(destination));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationInstallerRefusesPendingAndForeignRootReceipts()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            await InvalidMigrationReceiptAsync(() => MinecraftInstallService.PrepareMigrationRootAsync(temporary, default));
            string root = Path.Combine(temporary, ".nexa-migration-stage-" + Guid.NewGuid().ToString("N"), "instance");
            string stage = Path.Combine(root, ".nexa-install-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(stage, ".task"));
            await InstallTaskJournal.CreateAsync(stage, new(root, "1.20.1", InstanceName: "fixture") { InheritVanilla = false }, default);
            await File.WriteAllTextAsync(Path.Combine(stage, ".task", "execution.lock"), "");
            await InvalidMigrationReceiptAsync(() => MinecraftInstallService.PrepareMigrationRootAsync(root, default));
            AssertTrue(File.Exists(Path.Combine(stage, ".task", "plan.json")));
            AssertTrue(File.Exists(Path.Combine(stage, ".task", "execution.lock")));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static async Task InvalidMigrationReceiptAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected an invalid migration installation receipt.");
    }
}
