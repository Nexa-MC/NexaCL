using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class PluginInstallSource : IInstallCatalogSource
    {
        public Task<IReadOnlyList<InstallCatalogVersion>> GetGamesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<InstallCatalogVersion>>([
            new("1.21.1", "release") { ReleasedAt = DateTimeOffset.Parse("2024-08-08T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture) }]);
        public Task<IReadOnlyList<InstallCatalogVersion>> GetLoadersAsync(InstallLoader loader, string game, CancellationToken token) => Task.FromResult<IReadOnlyList<InstallCatalogVersion>>([new("0.16.0", "Fabric")]);
    }
    private static async Task ExercisePluginInstall(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke, EntityId instance)
    {
        var games = await invoke("Minecraft.Install", "ListGameVersions", PageRequest.Write(new(null, 16))); AssertTrue(games.Success);
        AssertEqual(2024, Page<GameVersion>.Read(games.Value).Items.Single().ReleasedAt.Year);
        var loaders = await invoke("Minecraft.Install", "ListLoaderVersions", LoaderVersionRequest.Write(new(new("nexa:loader/fabric"), "1.21.1", new(null, 16)))); AssertTrue(loaders.Success);
        AssertEqual("0.16.0", Page<LoaderVersion>.Read(loaders.Value).Items.Single().Version);
        var read = await invoke("Minecraft.Install", "ReadDraft", EntityRequest.Write(new(instance))); AssertTrue(read.Success);
        var draft = InstallDraft.Read(read.Value);
        var update = await invoke("Minecraft.Install", "UpdateDraft", InstallDraftUpdate.Write(new(draft, draft.Revision))); AssertTrue(update.Success);
        AssertFalse((await invoke("Minecraft.Install", "UpdateDraft", InstallDraftUpdate.Write(new(draft, draft.Revision)))).Success);
        draft = InstallDraft.Read(update.Value);
        var validate = await invoke("Minecraft.Install", "ValidateDraft", InstallDraft.Write(draft)); AssertTrue(validate.Success); AssertTrue(ValidationReport.Read(validate.Value).Valid);
        var resolved = await invoke("Minecraft.Install", "ResolvePlan", InstallDraft.Write(draft)); AssertTrue(resolved.Success);
        var plan = InstallPlan.Read(resolved.Value); AssertTrue(plan.Steps.Length > 0);
        var preview = await invoke("Minecraft.Install", "PreviewPlan", EntityRequest.Write(new(plan.Plan.Id))); AssertTrue(preview.Success);
        AssertEqual(plan.Plan.InputsHash, InstallPlan.Read(preview.Value).Plan.InputsHash);
        var executed = await invoke("Minecraft.Install", "ExecutePlan", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null))); AssertTrue(executed.Success);
        var op = OperationRef.Read(executed.Value); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var task = await invoke("Tasks", "Read", EntityRequest.Write(new(op.Id))); AssertTrue(task.Success); var record = OperationRecord.Read(task.Value);
            if (record.State == OperationState.Completed) break;
            AssertFalse(record.State is OperationState.Failed or OperationState.Cancelled or OperationState.OutcomeUnknown);
            await Task.Delay(5, timeout.Token);
        }
        AssertFalse((await invoke("Minecraft.Install", "ExecutePlan", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null)))).Success);
    }
}
