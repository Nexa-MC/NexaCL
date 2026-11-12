using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class BackupConfirmation : IActionConfirmation
    {
        internal bool Allow = true;
        internal int Count;
        internal Action? BeforeConfirm;
        public ValueTask<bool> ConfirmAsync(ActionConfirmationRequest request, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Count++; BeforeConfirm?.Invoke(); return ValueTask.FromResult(Allow); }
    }
    private static void PluginScopedBackups() => PluginScopedBackupsAsync().GetAwaiter().GetResult();
    private static async Task PluginScopedBackupsAsync()
    {
        var descriptors = new List<MinecraftInstanceDescriptor>();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource(descriptors));
        foreach (var name in new[] { "one", "two" })
        {
            string directory = Path.Combine(fixture.TemporaryDirectory, "minecraft", "versions", name);
            Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "payload.txt"), name);
            var item = Instance(name); descriptors.Add(item with { DirectoryPath = directory, Version = item.Version with { DirectoryPath = directory } });
        }
        await fixture.Library.Service.RefreshAsync();
        bool idle = true;
        var owner = new ContentBackupService(Path.Combine(fixture.TemporaryDirectory, "backup-store"), () => idle);
        var commands = new XsrCommandRouterBuilder(); var queries = new XsrQueryRouterBuilder();
        ContentWorkspaceRuntime.RegisterInstanceBackups(commands, queries, owner);
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands,
            foundationCommands: commands.Build(new NoopDispatchObserver()), foundationQueries: queries.Build(new NoopDispatchObserver()));
        var confirmation = new BackupConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "backup-plugin-storage"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new BackupPluginEntry(); var report = (await app.Runtime.LoadAsync([entry.Source()])).Single(); AssertEqual(LoadState.Active, report.State);
        using var api = entry.Context!.AsAbi100();
        var listed = await api.Minecraft.Instances.List(new(null, new(null, 32)), BackupPluginEntry.Options()); AssertTrue(listed.Success);
        var first = listed.Value.Items.First(p => p.Name == "one"); var second = listed.Value.Items.First(p => p.Name == "two");
        var empty = await api.Backups.List(new(first.Id), BackupPluginEntry.Options()); AssertTrue(empty.Success); AssertEqual(0, empty.Value.Items.Length);
        AssertFalse((await api.Backups.List(new(new("forged")), BackupPluginEntry.Options())).Success);
        AssertFalse((await api.Backups.PrepareCapture(new(first.Id, null, [new("nexa:unknown")], []), BackupPluginEntry.Options())).Success);
        var plan = await Prepare();
        confirmation.Allow = false;
        AssertEqual(OperationState.Failed, (await Capture(plan)).State);
        AssertEqual(0, (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value.Items.Length);
        confirmation.Allow = true;
        plan = await Prepare();
        idle = false; AssertEqual(OperationState.Failed, (await Capture(plan)).State); idle = true;
        plan = await Prepare(); AssertEqual(OperationState.Completed, (await Capture(plan)).State);
        AssertFalse((await api.Backups.Capture(new(plan.Id, plan.Revision, null), BackupPluginEntry.Options())).Success); // Single use.
        var backups = (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value;
        var backup = backups.Items.Single(); AssertEqual(first.Id, backup.Instance); AssertEqual(3L, backup.Size);
        AssertTrue((await api.Backups.Read(new(backup.Id), BackupPluginEntry.Options())).Success);
        AssertTrue((await api.Backups.VerifyOffline(new(backup.Id), BackupPluginEntry.Options())).Value.Valid);
        AssertEqual(1L, (await api.Backups.Statistics(new(first.Id), BackupPluginEntry.Options())).Value.Count);
        AssertEqual(0, (await api.Backups.List(new(second.Id), BackupPluginEntry.Options())).Value.Items.Length);
        AssertEqual(0, (await owner.ListAsync()).Count); // Global store is independent.
        var foreign = new BackupPluginEntry("example.foreign-backups"); AssertEqual(LoadState.Active, (await app.Runtime.LoadAsync([foreign.Source()])).Single().State);
        using (var other = foreign.Context!.AsAbi100()) AssertFalse((await other.Backups.Read(new(backup.Id), BackupPluginEntry.Options())).Success);
        var reader = new BackupPluginEntry("example.read-only-backups", false);
        AssertEqual(LoadState.Active, (await app.Runtime.LoadAsync([reader.Source()])).Single().State);
        using (var readOnly = reader.Context!.AsAbi100())
        {
            var ownInstance = (await readOnly.Minecraft.Instances.List(new(null, new(null, 32)), BackupPluginEntry.Options())).Value.Items.First(p => p.Name == "one");
            var ownPlan = (await readOnly.Backups.PrepareCapture(new(ownInstance.Id, null, [], []), BackupPluginEntry.Options())).Value;
            var denied = await readOnly.Backups.Capture(new(ownPlan.Id, ownPlan.Revision, null), BackupPluginEntry.Options());
            AssertFalse(denied.Success); AssertEqual(AbiErrors.CAPABILITY_DENIED, denied.Error!.Code);
        }
        var defaultPolicy = (await api.Backups.ReadRetention(new(first.Id), BackupPluginEntry.Options())).Value;
        AssertFalse((await api.Backups.SetRetention(new(first.Id, defaultPolicy, empty.Value.Revision), BackupPluginEntry.Options())).Success);
        AssertFalse((await api.Backups.SetRetention(new(first.Id, defaultPolicy with { MaximumBytes = 10 }, backups.Revision), BackupPluginEntry.Options())).Success);
        var policy = new RetentionPolicy(1, 1, 0);
        AssertTrue((await api.Backups.SetRetention(new(first.Id, policy, backups.Revision), BackupPluginEntry.Options())).Success);
        AssertEqual(policy, (await api.Backups.ReadRetention(new(first.Id), BackupPluginEntry.Options())).Value);
        AssertFalse((await api.Backups.SetRetention(new(first.Id, defaultPolicy, backups.Revision), BackupPluginEntry.Options())).Success);
        AssertEqual(defaultPolicy, (await api.Backups.ReadRetention(new(second.Id), BackupPluginEntry.Options())).Value);
        // A concurrent owner mutation between confirmation and commit must fail CAS.
        var current = (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value;
        confirmation.BeforeConfirm = () => owner.ForInstance(descriptors[0].DirectoryPath).SetRetentionAsync(new(false, 2, 2)).GetAwaiter().GetResult();
        AssertFalse((await api.Backups.SetRetention(new(first.Id, defaultPolicy, current.Revision), BackupPluginEntry.Options())).Success);
        confirmation.BeforeConfirm = null;
        AssertEqual(new RetentionPolicy(2, 2, 0), (await api.Backups.ReadRetention(new(first.Id), BackupPluginEntry.Options())).Value);
        // New service instances recover the same durable scope and policy.
        var reopened = new ContentBackupService(Path.Combine(fixture.TemporaryDirectory, "backup-store")).ForInstance(descriptors[0].DirectoryPath);
        var reopenedState = await reopened.ReadScopeAsync();
        AssertEqual(1, reopenedState.Backups.Count);
        var races = await Task.WhenAll(ChangeRetention(new(false, 3, 3)), ChangeRetention(new(false, 4, 4)));
        AssertEqual(1, races.Count(won => won)); // Atomic owner CAS, across separate service objects.
        async Task<bool> ChangeRetention(ContentBackupRetentionPolicy next)
        {
            try { await owner.ForInstance(descriptors[0].DirectoryPath).SetRetentionAsync(next, reopenedState.Revision); return true; }
            catch (IOException) { return false; }
        }
        string export = Path.Combine(fixture.TemporaryDirectory, "backup.nexabackup.json");
        var file = app.Files.GrantPath(report.Identity, export, true);
        var exporting = await api.Backups.Export(new(backup.Id, file), BackupPluginEntry.Options()); AssertTrue(exporting.Success);
        AssertEqual(OperationState.Completed, (await Terminal(exporting.Value)).State); AssertTrue(File.Exists(export));
        AssertFalse((await api.Backups.Export(new(backup.Id, file), BackupPluginEntry.Options())).Success); // No overwrite.
        var imported = await owner.ForInstance(descriptors[1].DirectoryPath).ImportThinAsync(export);
        AssertEqual(1, (await owner.ForInstance(descriptors[1].DirectoryPath).ListAsync()).Count);
        AssertEqual(OperationState.Completed, (await Capture(await Prepare())).State);
        var beforePrune = (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value;
        var prune = (await api.Backups.PreviewPrune(new(first.Id, policy, beforePrune.Revision), BackupPluginEntry.Options())).Value;
        // A capture after preview invalidates the old plan.
        AssertEqual(OperationState.Completed, (await Capture(await Prepare())).State);
        var stale = await api.Backups.Prune(new(prune.Id, prune.Revision, null), BackupPluginEntry.Options()); AssertTrue(stale.Success);
        AssertEqual(OperationState.Failed, (await Terminal(stale.Value)).State);
        beforePrune = (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value;
        prune = (await api.Backups.PreviewPrune(new(first.Id, policy, beforePrune.Revision), BackupPluginEntry.Options())).Value;
        var pruning = await api.Backups.Prune(new(prune.Id, prune.Revision, null), BackupPluginEntry.Options()); AssertTrue(pruning.Success);
        AssertEqual(OperationState.Completed, (await Terminal(pruning.Value)).State);
        AssertEqual(1, (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value.Items.Length);
        AssertEqual(1, (await owner.ForInstance(descriptors[1].DirectoryPath).ListAsync()).Count); // Prune cannot cross scopes.
        AssertFalse((await owner.ForInstance(descriptors[1].DirectoryPath).VerifyOfflineAsync(imported.BackupIdentity)).Ready); // Thin export carries metadata only.
        // Corrupted immutable bytes are detected by offline verification.
        string scopedRoot = Path.Combine(fixture.TemporaryDirectory, "backup-store", "instance-stores");
        string objectPath = Directory.EnumerateFiles(scopedRoot, "*.blob", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(objectPath, "corrupt");
        var surviving = (await api.Backups.List(new(first.Id), BackupPluginEntry.Options())).Value.Items.Single();
        AssertFalse((await api.Backups.VerifyOffline(new(surviving.Id), BackupPluginEntry.Options())).Value.Valid);
        AssertTrue(confirmation.Count > 0);

        async Task<MutationPlan> Prepare()
        {
            var result = await api.Backups.PrepareCapture(new(first.Id, null, [], []), BackupPluginEntry.Options()); AssertTrue(result.Success); return result.Value;
        }
        async Task<OperationRecord> Capture(MutationPlan pending)
        {
            var result = await api.Backups.Capture(new(pending.Id, pending.Revision, null), BackupPluginEntry.Options()); AssertTrue(result.Success); return await Terminal(result.Value);
        }
        async Task<OperationRecord> Terminal(OperationRef operation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var record = (await api.Tasks.Read(new(operation.Id), BackupPluginEntry.Options())).Value;
                if (record.State is OperationState.Completed or OperationState.Failed or OperationState.Cancelled) return record;
                await Task.Delay(5, timeout.Token);
            }
        }
    }
    private sealed class BackupPluginEntry(string id = "example.backups", bool writable = true) : IPluginBootstrapV1
    {
        internal ABIContext? Context;
        private ImmutableArray<string> Permissions => writable ? ["nexa.backups.read", "nexa.backups.write", "nexa.minecraft.instances.read", "nexa.tasks.read"] : ["nexa.backups.read", "nexa.minecraft.instances.read", "nexa.tasks.read"];
        internal static InvocationOptions Options() => new(Guid.NewGuid(), TimeSpan.FromSeconds(5), default) { IdempotencyKey = Guid.NewGuid().ToString("N") };
        internal PluginLoadSource Source() => new(new(1, id, "1.0.0", 1, new("test", "test"), new([], [], "fixture"), [100], [], [], [], Permissions, []), Permissions, _ => ValueTask.FromResult<IPluginBootstrapV1>(this), () => ValueTask.CompletedTask);
        public async ValueTask<AbiReply<PreInitResult>> OnPreInitialize(PreInitContext context)
        {
            var handle = await context.Negotiator.GetABIContext(100, Options()); AssertTrue(handle.Success);
            var result = await new Abi100Draft(handle.Value).Require("nexa.backups/100").Require("nexa.minecraft.instances/100").Require("nexa.tasks/100").Make(context.Negotiator, Guid.NewGuid(), Permissions, default); AssertTrue(result.Success);
            Context = result.Value; return AbiReply<PreInitResult>.Ok(new(Context, [], [], [], []));
        }
        public ValueTask<AbiReply<Unit>> OnInitialize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<Unit>> OnPostInitialize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<CheckupResult>> OnCheckup(ABIContext context, StageContext stage) => ValueTask.FromResult(AbiReply<CheckupResult>.Ok(new(CheckupStatus.Passed, [], [])));
        public ValueTask<AbiReply<Unit>> OnFinalize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<Unit>> OnActivated(ABIContext context, InvocationContext invocation) => Ok();
        public ValueTask<AbiReply<Unit>> OnStop(StopContext context) => Ok();
        public ValueTask<AbiReply<Unit>> OnAbort(AbortContext context) => Ok();
        private static ValueTask<AbiReply<Unit>> Ok() => ValueTask.FromResult(AbiReply<Unit>.Ok(new()));
    }
}
