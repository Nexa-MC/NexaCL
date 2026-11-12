using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginCompletionInstall() => PluginCompletionInstallAsync().GetAwaiter().GetResult();
    private static async Task PluginCompletionInstallAsync()
    {
        var descriptors = new List<MinecraftInstanceDescriptor>();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource(descriptors), installSource: new PluginInstallSource());
        await fixture.Library.Service.RefreshAsync();
        string root = Path.Combine(fixture.TemporaryDirectory, "minecraft");
        var recoveries = new List<MinecraftInstallRecoveryRecord> { new(root, "new:" + Guid.NewGuid().ToString("N"), "pending", "1") };
        var commands = new XsrCommandRouterBuilder(); var queries = new XsrQueryRouterBuilder(); int resumed = 0, dismissed = 0, installed = 0;
        commands.Register<MinecraftInstallRecoveryRecordCommand>(MinecraftInstallRecoveryRecordsContract.Apply, (v, _) =>
        {
            var record = recoveries.Single(); AssertEqual(record.RootDirectory, v.RootDirectory); AssertEqual(record.Identity, v.Identity); AssertEqual(record.Revision, v.ExpectedRevision);
            if (v.Discard) dismissed++; else resumed++; recoveries.Clear(); return ValueTask.FromResult(XsrResult.Success());
        });
        commands.Register<MinecraftInstallCommand>(MinecraftInstallRoutes.Run, async (v, token) =>
        {
            installed++; AssertEqual("1.21.1", v.GameVersion); string path = Path.Combine(v.RootDirectory, "versions", v.InstanceName!); Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, v.InstanceName + ".json"), """{"id":"templated","_minecraftVersion":"1.21.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""", token);
            var descriptor = Instance(v.InstanceName!); descriptors.Add(descriptor with { DirectoryPath = path, Version = descriptor.Version with { DirectoryPath = path } }); return XsrResult.Success();
        });
        queries.Register<MinecraftInstallRecoveryListQuery, MinecraftInstallRecoveryListing>(MinecraftInstallRecoveryRecordsContract.List,
            (v, _) => ValueTask.FromResult(XsrResult.Success(new MinecraftInstallRecoveryListing(recoveries.Where(r => v.RootDirectories.Contains(r.RootDirectory)).ToArray()))));
        queries.Register<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(MinecraftInstallEditContract.Query,
            (v, _) => ValueTask.FromResult(File.Exists(Path.Combine(v.RootDirectory, "versions", v.InstanceId, v.InstanceId + ".json"))
                ? XsrResult.Success(new MinecraftInstallEditSnapshot(v.RootDirectory, v.InstanceId, "1.21.1", [], "fingerprint"))
                : XsrResult.Failure<MinecraftInstallEditSnapshot>(MinecraftErrors.InstanceNotFound(v.InstanceId))));
        var queryRouter = queries.Build(new NoopDispatchObserver());
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands);
        business.BindInstall(fixture.InstallCatalog.Commands, queryRouter, commands.Build(new NoopDispatchObserver()), recoveryQueries: queryRouter);
        var confirmation = new BackupConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "completion-install"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new CompletionPluginEntry("example.templates"); var foreign = new CompletionPluginEntry("example.foreign-templates");
        var options = CompletionPluginEntry.Options;
        var template = new InstallTemplate(new("example.templates:plain"), "1.21.1", [], [], new("example.templates:create"));
        entry.Callbacks.Add("install", async input =>
        {
            var invocation = InstallTemplateInvocation.Read(ActionRequest.Read(input).Inputs); AssertEqual(template.Id, invocation.Template.Id);
            using var api = entry.Context!.AsAbi100();
            var plan = CompletionValue(await api.Minecraft.Instances.PrepareCreate(invocation.Request with { Template = new("nexa:instance/java") }, options()));
            var completion = await CompletionTerminal(api, await api.Minecraft.Instances.CommitCreate(new(plan.Id, plan.Revision, null), options())); AssertEqual(OperationState.Completed, completion.State);
            return AbiReply<AbiValue>.Ok(Empty.Write(new()));
        });
        entry.Initialize = async api =>
        {
            CompletionValue(await api.Actions.Register(new(new("example.templates:install"), Abi100Schemas.InstallTemplateInvocation, Abi100Schemas.Empty, [], [], IdempotencyMode.RequiredKey, true, false, ConfirmationRequirement.None, null, new("Install template", []), entry.Handlers["install"]), options()));
            var node = new WorkflowNode("install", WorkflowNodeKind.Action, new("example.templates:install"), new AbiValue.Null(), [], 1, 1, 0, ParallelCompletion.All, [], Abi100Schemas.InstallTemplateInvocation, Abi100Schemas.Empty);
            CompletionValue(await api.Workflows.Register(new(template.Workflow, Abi100Schemas.InstallTemplateInvocation, Abi100Schemas.Empty, [node], "template-v1", true, ConcurrencyPolicy.RejectDuplicate, 8, 32), options()));
            CompletionValue(await api.Minecraft.Install.RegisterTemplate(template, options()));
        };
        foreach (var load in await app.Runtime.LoadAsync([entry.Source(), foreign.Source()])) if (load.State != LoadState.Active) throw new InvalidOperationException("Install completion load: " + load.Error);
        using var api = entry.Context!.AsAbi100(); using var other = foreign.Context!.AsAbi100();
        var recovery = CompletionValue(await api.Minecraft.Install.ListRecoveries(new(null, 16), options())).Items.Single();
        AssertFalse((await other.Minecraft.Install.ResumeRecovery(new(recovery.Id), options())).Success);
        recoveries[0] = recoveries[0] with { Revision = "2" }; AssertFalse((await api.Minecraft.Install.ResumeRecovery(new(recovery.Id), options())).Success);
        recovery = CompletionValue(await api.Minecraft.Install.ListRecoveries(new(null, 16), options())).Items.Single();
        confirmation.Allow = false; AssertEqual(OperationState.Failed, (await CompletionTerminal(api, await api.Minecraft.Install.ResumeRecovery(new(recovery.Id), options()))).State); AssertEqual(0, resumed);
        confirmation.Allow = true; AssertEqual(OperationState.Completed, (await CompletionTerminal(api, await api.Minecraft.Install.ResumeRecovery(new(recovery.Id), options()))).State); AssertEqual(1, resumed);
        recoveries.Add(new(root, "new:" + Guid.NewGuid().ToString("N"), "dismiss", "1"));
        recovery = CompletionValue(await api.Minecraft.Install.ListRecoveries(new(null, 16), options())).Items.Single();
        CompletionValue(await api.Minecraft.Install.DiscardRecovery(new(recovery.Id), options())); AssertEqual(1, dismissed);
        AssertFalse((await api.Minecraft.Install.DiscardRecovery(new(recovery.Id), options())).Success);
        var roots = CompletionValue(await api.Minecraft.Instances.ListRoots(new(null, 16), options()));
        var request = new InstanceCreateRequest(template.Id, "templated", roots.Items.Single().Root, "1.21.1", null);
        var prepare = CompletionValue(await api.Minecraft.Instances.PrepareCreate(request, options()));
        var foreignRoot = CompletionValue(await other.Minecraft.Instances.ListRoots(new(null, 16), options())).Items.Single().Root;
        AssertFalse((await other.Minecraft.Instances.PrepareCreate(request with { Root = foreignRoot }, options())).Success);
        var completed = await CompletionTerminal(api, await api.Minecraft.Instances.CommitCreate(new(prepare.Id, prepare.Revision, null), options()));
        if (completed.State != OperationState.Completed) throw new InvalidOperationException("Template: " + completed.Failure);
        AssertEqual(1, installed); AssertTrue(File.Exists(Path.Combine(root, "versions", "templated", "templated.json")));
        AssertFalse((await api.Minecraft.Instances.CommitCreate(new(prepare.Id, prepare.Revision, null), options())).Success);
    }
}
