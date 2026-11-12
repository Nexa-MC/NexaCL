using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginManagedJavaInstallation() => PluginManagedJavaInstallationAsync().GetAwaiter().GetResult();
    private static async Task PluginManagedJavaInstallationAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var queries = new XsrQueryRouterBuilder(); var commands = new XsrCommandRouterBuilder();
        long revision = 1; int executions = 0; bool installed = false;
        string executable = Path.Combine(fixture.TemporaryDirectory, "java"); await File.WriteAllTextAsync(executable, "fixture");
        var candidate = new JavaRuntimeCandidate(new JavaInstallation(fixture.TemporaryDirectory, executable, null, new Version(21, 0), JavaBrand.Microsoft, JavaArchitecture.X64, true, true));
        var preview = new JavaManualPreview(Guid.NewGuid(), 21, "Mojang", "21.0", "linux", fixture.TemporaryDirectory, 3, 500, new string('a', 64), ["https://example.test/LICENSE"], DateTimeOffset.UtcNow.AddMinutes(5));
        queries.Register<JavaRuntimeInventoryQuery, JavaRuntimeInventorySnapshot>(JavaRuntimeInventoryContract.Query, (_, _) => ValueTask.FromResult(XsrResult.Success(new JavaRuntimeInventorySnapshot(installed ? [candidate] : []) { RegistryRevision = revision, ManagedRuntimes = installed ? [new(executable, fixture.TemporaryDirectory, "managed")] : [] })));
        queries.Register<JavaManualPreviewQuery, JavaManualPreview>(JavaManualDownloadContract.Preview, (v, _) => ValueTask.FromResult(v.Major == 21 ? XsrResult.Success(preview) : XsrResult.Failure<JavaManualPreview>(Nexa.Services.Minecraft.MinecraftErrors.InvalidRequest("Unavailable"))));
        queries.Register<JavaManualStatusQuery, JavaManualReceipt>(JavaManualDownloadContract.Status, (v, _) => ValueTask.FromResult(XsrResult.Success(new JavaManualReceipt(v.PreviewId, JavaManualInstallStatus.Installed, 1, 3, 3, "", executable, "21.0", JavaBrand.Microsoft, JavaArchitecture.X64))));
        commands.Register<JavaManualInstallCommand>(JavaManualDownloadContract.Install, (v, _) =>
        { AssertEqual(preview.Id, v.PreviewId); AssertTrue(v.AcceptLicense); executions++; installed = true; revision++; return ValueTask.FromResult(XsrResult.Success()); });
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands, foundationCommands: commands.Build(new NoopDispatchObserver()), foundationQueries: queries.Build(new NoopDispatchObserver()));
        var confirmation = new PluginAdapterConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "java-plugin-storage"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new JavaPluginEntry(); var report = (await app.Runtime.LoadAsync([entry.Source()])).Single(); AssertEqual(LoadState.Active, report.State);
        using var api = entry.Context!.AsAbi100();
        var listed = await api.Minecraft.Java.ListManagedDistributions(new(null, 16), JavaPluginEntry.Options()); AssertTrue(listed.Success);
        var offer = listed.Value.Items.Single(); AssertEqual(3L, offer.Files); AssertEqual(500L, offer.Bytes); AssertEqual(1, offer.Licenses.Length);
        AssertFalse((await api.Minecraft.Java.PrepareManagedInstall(new(offer.Id, new("forged")), JavaPluginEntry.Options())).Success);
        var plan = (await api.Minecraft.Java.PrepareManagedInstall(new(offer.Id, offer.Revision), JavaPluginEntry.Options())).Value;
        AssertTrue(plan.Summary.MessageKey.Contains("https://example.test/LICENSE", StringComparison.Ordinal));
        revision++;
        var stale = await api.Minecraft.Java.Install(new(plan.Id, plan.Revision, null), JavaPluginEntry.Options()); AssertTrue(stale.Success);
        AssertEqual(OperationState.Failed, (await Terminal(stale.Value)).State); AssertEqual(0, executions); AssertEqual(0, confirmation.Count);
        listed = await api.Minecraft.Java.ListManagedDistributions(new(null, 16), JavaPluginEntry.Options()); AssertTrue(listed.Success); offer = listed.Value.Items.Single();
        plan = (await api.Minecraft.Java.PrepareManagedInstall(new(offer.Id, offer.Revision), JavaPluginEntry.Options())).Value;
        var result = await api.Minecraft.Java.Install(new(plan.Id, plan.Revision, null), JavaPluginEntry.Options()); AssertTrue(result.Success);
        var complete = await Terminal(result.Value); AssertEqual(OperationState.Completed, complete.State); AssertTrue(JavaRuntime.Read(complete.Result).Managed);
        AssertEqual(1, executions); AssertEqual(1, confirmation.Count);
        AssertFalse((await api.Minecraft.Java.Install(new(plan.Id, plan.Revision, null), JavaPluginEntry.Options())).Success);
        async Task<OperationRecord> Terminal(OperationRef operation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var record = (await api.Tasks.Read(new(operation.Id), JavaPluginEntry.Options())).Value;
                if (record.State is OperationState.Completed or OperationState.Failed or OperationState.Cancelled) return record;
                await Task.Delay(5, timeout.Token);
            }
        }
    }
    private sealed class JavaPluginEntry : IPluginBootstrapV1
    {
        internal ABIContext? Context;
        private static readonly ImmutableArray<string> Permissions = ["nexa.minecraft.java.read", "nexa.minecraft.java.write", "nexa.tasks.read"];
        internal static InvocationOptions Options() => new(Guid.NewGuid(), TimeSpan.FromSeconds(5), default) { IdempotencyKey = Guid.NewGuid().ToString("N") };
        internal PluginLoadSource Source() => new(new(1, "example.java", "1.0.0", 1, new("test", "test"), new([], [], "fixture"), [100], [], [], [], Permissions, []), Permissions, _ => ValueTask.FromResult<IPluginBootstrapV1>(this), () => ValueTask.CompletedTask);
        public async ValueTask<AbiReply<PreInitResult>> OnPreInitialize(PreInitContext context)
        {
            var handle = await context.Negotiator.GetABIContext(100, Options()); AssertTrue(handle.Success);
            var result = await new Abi100Draft(handle.Value).Require("nexa.minecraft.java/100").Require("nexa.tasks/100").Make(context.Negotiator, Guid.NewGuid(), Permissions, default); AssertTrue(result.Success);
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
