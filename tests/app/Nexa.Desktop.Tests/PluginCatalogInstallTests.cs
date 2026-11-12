using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task ExercisePluginCatalogInstall(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke,
        PluginBusinessRoutes business, InstanceSummary instance)
    {
        var source = new PluginCatalogSource(); var queries = new XsrQueryRouterBuilder(); var commands = new XsrCommandRouterBuilder();
        int executions = 0; bool changed = false;
        var version = (await source.DetailAsync(new("first"), default)).Versions.Single();
        queries.Register<ResourceDetailQuery, ResourceDetail>(ResourceCatalogContract.Detail, async (v, t) => XsrResult.Success(await source.DetailAsync(v, t)));
        queries.Register<ResourceModPlanQuery, ResourceInstallPlan>(ResourceCatalogContract.PlanMod, (v, t) =>
        {
            t.ThrowIfCancellationRequested(); return ValueTask.FromResult(XsrResult.Success(new ResourceInstallPlan([changed ? version with { Id = "changed" } : version], [], false)));
        });
        commands.Register<ResourceModInstallCommand>(ResourceCatalogContract.InstallMod, (v, t) =>
        {
            t.ThrowIfCancellationRequested(); AssertEqual(ResourceInstallPlanIdentity.Fingerprint([version]), v.ExpectedPlanFingerprint); executions++;
            return ValueTask.FromResult(XsrResult.Success());
        });
        business.BindCatalog(commands.Build(new NoopDispatchObserver()), queries.Build(new NoopDispatchObserver()));
        var input = new CatalogInstallRequest(instance.Id, [new(new(new("nexa:modrinth"), "first"), "v1")], instance.Revision);
        var prepared = await invoke("Catalog", "PlanInstall", CatalogInstallRequest.Write(input)); AssertTrue(prepared.Success);
        var plan = ContentPlan.Read(prepared.Value); AssertEqual(1, plan.Changes.Length);
        var executed = await invoke("Catalog", "ExecutePlan", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null))); AssertTrue(executed.Success);
        AssertEqual(OperationState.Completed, await Wait(OperationRef.Read(executed.Value)));
        AssertEqual(1, executions);
        AssertFalse((await invoke("Catalog", "ExecutePlan", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null)))).Success);
        var stale = await invoke("Minecraft.Content", "PlanInstall", ContentChangeRequest.Write(new(instance.Id, [], input.Versions, instance.Revision))); AssertTrue(stale.Success);
        plan = ContentPlan.Read(stale.Value); changed = true;
        executed = await invoke("Minecraft.Content", "Execute", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null))); AssertTrue(executed.Success);
        AssertEqual(OperationState.Failed, await Wait(OperationRef.Read(executed.Value))); AssertEqual(1, executions);
        async Task<OperationState> Wait(OperationRef operation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var result = await invoke("Tasks", "Read", EntityRequest.Write(new(operation.Id))); AssertTrue(result.Success);
                var state = OperationRecord.Read(result.Value).State;
                if (state is OperationState.Completed or OperationState.Failed or OperationState.Cancelled or OperationState.OutcomeUnknown) return state;
                await Task.Delay(5, timeout.Token);
            }
        }
    }
}
