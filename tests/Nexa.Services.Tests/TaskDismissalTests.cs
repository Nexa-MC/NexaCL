using Nexa.Services.Tasks;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Java;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DismissedInstallationStatusSurvivesRestart()
    {
        string root = CreateTempDirectory();
        try
        {
            string stage = Path.Combine(root, ".nexa-install-jobs", Guid.NewGuid().ToString("N"));
            var plan = await InstallTaskJournal.CreateAsync(stage, new(root, "1.20.1", InstanceName: "discarded") { InheritVanilla = false }, default);
            await InstallTaskJournal.WriteStatusAsync(stage, plan, InstallTaskStatus.Dismissed, default);
            using var restarted = new InstallFixture(PackMetadata());
            AssertTrue((await restarted.Install.RecoverPendingAsync(new([root]))).IsSuccess);
            AssertEqual(0, restarted.Tasks.ReadEntries().Count);
            AssertEqual(InstallTaskStatus.Dismissed, await InstallTaskJournal.ReadStatusAsync(stage, plan, default));
            await InstallTaskJournal.WriteStatusAsync(stage, plan, InstallTaskStatus.RollbackRequested, default);
            AssertEqual(InstallTaskStatus.RollbackRequested, await InstallTaskJournal.ReadStatusAsync(stage, plan, default));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask DismissedJavaDoesNotRecover()
    {
        string root = CreateTempDirectory();
        try
        {
            var journal = await JavaInstallJournal.CreateAsync(root, "java-runtime-delta", default);
            await journal.DismissAsync();
            using var tasks = NewTaskCenter(out _);
            using var installer = new JavaRuntimeInstaller(new FakeInstallerMetadataProvider(
                JavaRuntimeInstaller.DetectPlatform().ToMojangKey(), "bin/java.exe", new string('a', 40)), tasks: tasks);
            AssertTrue((await installer.RecoverAsync(root)).IsSuccess);
            AssertEqual(0, tasks.ReadEntries().Count);
            AssertTrue((await JavaInstallJournal.OpenAsync(root, journal.Stage, default)).Dismissed);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask TaskDismissalPersistsBeforeRemoval()
    {
        using var service = NewTaskCenter(out _);
        var task = service.Begin(new("durable", "安装", []), (_, _) => Task.CompletedTask);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SetDismissAction(task, async () => { entered.SetResult(); await release.Task; });
        task.Fail("failed");
        var clearing = service.ClearFinishedAsync();
        AssertFalse(entered.Task.IsCompleted);
        task.Dispose();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(1, service.ReadEntries().Count);
        AssertFalse(service.RequestRetry("durable"));
        AssertFalse(service.Dismiss("durable"));
        release.SetResult();
        AssertEqual(1, await clearing);
        AssertEqual(0, service.ReadEntries().Count);
        using var second = service.Begin(new("durable", "new", []));
        service.SetDismissAction(second, () => throw new IOException("persist failed"));
        second.Fail("failed"); second.Dispose();
        bool rejected = false;
        try { await service.DismissAsync("durable"); } catch (IOException) { rejected = true; }
        AssertTrue(rejected); AssertEqual(1, service.ReadEntries().Count);
    }

    private static async ValueTask DismissedModpackDoesNotRecover()
    {
        string root = CreateTempDirectory();
        try
        {
            string archive = Path.Combine(root, "failed.mrpack");
            WriteLocalJar(archive, ("modrinth.index.json", MrpackIndex(new string('0', 40)).ToJsonString()));
            using (var fixture = new InstallFixture(PackMetadata()))
            {
                var pack = await MinecraftModpackArchive.InspectAsync(archive);
                AssertFalse((await fixture.Install.InstallModpackAsync(new(pack, root))).IsSuccess);
                AssertEqual(1, await fixture.Tasks.ClearFinishedAsync());
            }
            using var restarted = new InstallFixture(PackMetadata());
            AssertTrue((await restarted.Install.RecoverPendingAsync(new([root]))).IsSuccess);
            AssertEqual(0, restarted.Tasks.ReadEntries().Count);
            AssertFalse(Directory.Exists(Path.Combine(root, "versions")) && Directory.EnumerateDirectories(Path.Combine(root, "versions")).Any());
        }
        finally { Directory.Delete(root, true); }
    }
}
