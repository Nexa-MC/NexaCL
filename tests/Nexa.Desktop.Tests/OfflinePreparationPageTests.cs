using Nexa.Desktop.Ui;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void OfflinePreparationRepairsThenRechecksTheSelectedInstance()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string instance = Path.Combine(fixture.TemporaryDirectory, "minecraft", "versions", "offline");
        Directory.CreateDirectory(instance);
        var queries = new XsrQueryRouterBuilder();
        Forward<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery);
        Forward<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery);
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query,
            (q, _) => ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory,
                "1.20.1", [], [new("overview", "总览")], true, ""))));
        int reads = 0;
        List<string> repairs = [];
        queries.Register<InstanceOfflineReadinessQuery, InstanceOfflineReadinessReport>(InstanceOfflineReadinessContract.Query, (q, _) =>
        {
            AssertEqual(instance, q.InstanceDirectory);
            reads++;
            bool ready = repairs.Count == 2;
            return ValueTask.FromResult(XsrResult.Success(new InstanceOfflineReadinessReport(instance, DateTimeOffset.UtcNow,
                true, ready, ready ? "21" : null, [new("client", "offline.jar", ready ? OfflineArtifactState.Verified : OfflineArtifactState.Missing, 1, "fixture")])));
        });
        var pending = new TaskCompletionSource<XsrResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new XsrCommandRouterBuilder();
        commands.Register<RemediationRequest>(MachineCapabilityStateContract.RemediationCommand, (request, _) =>
        {
            AssertTrue(request.Confirmed);
            AssertEqual(instance, request.Arguments!["instanceDirectory"]);
            lock (repairs) repairs.Add(request.Id);
            return request.Id == "remediation.game.repair_files" ? new(pending.Task) : ValueTask.FromResult(XsrResult.Success());
        });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            commands.Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true;
        fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("InstanceOfflineReadiness.Check").IsAssigned);
        Click("InstanceOfflineReadiness.Check");
        Wait(() => reads == 1 && fixture.Shell.Tree.GetComponent<XsrUiInput>(Find("InstanceOfflineReadiness.Prepare"))?.Enabled == true);
        Click("InstanceOfflineReadiness.Prepare");
        Wait(() => { lock (repairs) return repairs.Count == 1; });
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("InstanceOfflineReadiness.Busy"))!.IsVisible);
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("InstanceOfflineReadiness.Title"))!.IsVisible);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("InstanceOfflineReadiness.Actions"))!.IsVisible);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("InstanceOfflineReadiness.Report"))!.IsVisible);
        Click("InstanceOfflineReadiness.Prepare");
        AssertEqual(1, repairs.Count);
        pending.SetResult(XsrResult.Success());
        Wait(() => reads == 2 && fixture.Shell.Tree.GetComponent<XsrUiText>(Find("InstanceOfflineReadiness.Result"))?.Content == "本地离线依赖校验通过");
        AssertTrue(repairs.SequenceEqual(["remediation.game.repair_files", "remediation.java.download"]));
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("InstanceOfflineReadiness.Busy"))!.IsVisible);
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("InstanceOfflineReadiness.Report"))!.IsVisible);

        void Forward<TQuery, TResponse>(XsrSemanticId semantic) where TQuery : notnull
        {
            AssertTrue(fixture.Foundation.Queries.TryResolve(semantic, out var route));
            queries.Register<TQuery, TResponse>(semantic, (q, token) => fixture.Foundation.Queries.QueryAsync<TQuery, TResponse>(route, q, cancellationToken: token));
        }
        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == key) found = entity; return true; });
            return found;
        }
        void Click(string key) { Emit(fixture.Intents, "ui.settings.management.action", Find(key)); fixture.Shell.Render(new(1400, 6000)); }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1400, 6000)); return condition(); }, TimeSpan.FromSeconds(10)));
    }
}
