using Nexa.Services.Composition;
using Nexa.Services.Setup;
using Nexa.Services.Tasks;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationShutdownWaitsForOwnedCleanup()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            string source = Path.Combine(temporary, "prism"), destination = Path.Combine(temporary, "imported");
            await MigrationWriteAsync(source, "instance.cfg", "[General]\nname=Owned cleanup\n");
            await MigrationWriteAsync(source, "mmc-pack.json",
                """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"}]}""");
            await MigrationWriteAsync(source, ".minecraft/saves/world/level.dat", "source-world");
            using var migration = LauncherMigrationRuntime.CreateService(async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { cleaning.TrySetResult(); await release.Task; }
            });
            var preview = await migration.PreviewAsync(new(LauncherMigrationSourceKind.Prism, source, destination));
            var commandBuilder = new XsrCommandRouterBuilder();
            var queryBuilder = new XsrQueryRouterBuilder();
            var tasks = NewTaskCenter(out _);
            await using var lifetime = LauncherMigrationRuntime.Register(commandBuilder, queryBuilder, migration, tasks);
            try
            {
                var observer = new RecordingDispatchObserver();
                var commands = commandBuilder.Build(observer); var queries = queryBuilder.Build(observer);
                AssertTrue(commands.TryResolve(LauncherMigrationContract.Apply, out var apply));
                var command = new LauncherMigrationApplyCommand(preview, [preview.Instances.Single().Id]);
                var running = commands.Dispatch(apply, command).Completion;
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var queued = commands.Dispatch(apply, command).Completion;
                var shutdown = lifetime.DisposeAsync().AsTask();
                await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(10));
                AssertTrue(!shutdown.IsCompleted, "Shutdown must wait for the entered handler's cleanup.");
                AssertTrue(queries.TryResolve(LauncherMigrationContract.Preview, out var route));
                AssertFalse((await queries.QueryAsync<LauncherMigrationQuery, LauncherMigrationPreview>(route, preview.Query)).IsSuccess);
                AssertTrue(queries.TryResolve(LauncherMigrationContract.Discover, out var discover));
                AssertFalse((await queries.QueryAsync<LauncherMigrationDiscoveryQuery, LauncherMigrationDiscoveryResult>(discover, new())).IsSuccess);
                AssertFalse((await commands.Dispatch(apply, command).Completion).IsSuccess);
                release.TrySetResult();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
                AssertFalse((await running).IsSuccess); AssertFalse((await queued).IsSuccess);
                AssertTrue(tasks.ReadEntries().All(item => item.IsTerminal));
                AssertFalse(Directory.Exists(destination));
                AssertEqual(0, Directory.GetDirectories(temporary, ".nexa-migration-stage-*").Length);
                AssertEqual("source-world", await File.ReadAllTextAsync(Path.Combine(source, ".minecraft", "saves", "world", "level.dat")));
            }
            finally { release.TrySetResult(); }
        }
        finally { release.TrySetResult(); Directory.Delete(temporary, true); }
    }

    private static async ValueTask LauncherMigrationShutdownPreservesCommittedSuccess()
    {
        string temporary = CreateLauncherMigrationTempDirectory();
        try
        {
            string source = Path.Combine(temporary, "source"), destination = Path.Combine(temporary, "imported");
            await MigrationWriteAsync(source, "versions/test/test.json", """{"id":"test","mainClass":"main"}""");
            using var migration = LauncherMigrationRuntime.CreateService();
            var preview = await migration.PreviewAsync(new(LauncherMigrationSourceKind.Minecraft, source, destination));
            IAsyncDisposable? lifetime = null; Task? shutdown = null; int stopping = 0;
            var publication = new MigrationShutdownObserver(() =>
            {
                if (Directory.Exists(destination) && Interlocked.CompareExchange(ref stopping, 1, 0) == 0)
                    shutdown = lifetime!.DisposeAsync().AsTask();
            });
            var state = new XsrStateStoreBuilder(); TaskCenterStateContract.DeclareState(state);
            var tasks = new TaskCenterService(state.Build(publication));
            var commandBuilder = new XsrCommandRouterBuilder(); var queryBuilder = new XsrQueryRouterBuilder();
            await using var owned = LauncherMigrationRuntime.Register(commandBuilder, queryBuilder, migration, tasks);
            lifetime = owned;
            var commands = commandBuilder.Build(new RecordingDispatchObserver());
            AssertTrue(commands.TryResolve(LauncherMigrationContract.Apply, out var apply));
            var result = await commands.Dispatch(apply, new LauncherMigrationApplyCommand(preview, [preview.Instances.Single().Id]))
                .Completion.WaitAsync(TimeSpan.FromSeconds(10));
            AssertTrue(result.IsSuccess, "Cancellation after publication must preserve the committed result.");
            AssertTrue(shutdown is not null); await shutdown!.WaitAsync(TimeSpan.FromSeconds(10));
            AssertEqual(1, (await migration.ReadReceiptAsync(destination)).Instances.Count);
            AssertTrue(tasks.ReadEntries().All(item => item.IsTerminal));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private sealed class MigrationShutdownObserver(Action changed) : IXsrStateObserver
    {
        public void OnChanged(XsrStateChange change) => changed();
    }
}
