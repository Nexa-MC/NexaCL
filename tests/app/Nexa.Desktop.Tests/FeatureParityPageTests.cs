using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void FeatureParityOptionalSelectionCancelsAndRetainsPerFileChoices()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var pack = new Nexa.Services.Minecraft.Install.MinecraftModpackPreview("source", new string('A', 64), "Pack", "1", "Modrinth", "1.21.1", null, null, "pack", 1, 2)
        { OptionalEntries = [new("path:mods/a.jar", "a.jar"), new("path:mods/b.jar", "b.jar")] };
        var selecting = ModpackOptionalSelection.ChooseAsync(fixture.Feedback, pack, null, default);
        var first = fixture.Feedback.Snapshot().Dialog!; fixture.Feedback.ResolveDialog(first.Id, true);
        AssertTrue(SpinWait.SpinUntil(() => fixture.Feedback.Snapshot().Dialog?.Message == "b.jar", TimeSpan.FromSeconds(5)));
        fixture.Feedback.InvokeDialogAlternate(fixture.Feedback.Snapshot().Dialog!.Id);
        AssertTrue(SpinWait.SpinUntil(() => fixture.Feedback.Snapshot().Dialog?.Key == "modpack.optional.confirm", TimeSpan.FromSeconds(5)));
        fixture.Feedback.ResolveDialog(fixture.Feedback.Snapshot().Dialog!.Id, true);
        AssertTrue(selecting.GetAwaiter().GetResult()!.SequenceEqual(["path:mods/b.jar"]));
        using var stop = new CancellationTokenSource();
        var canceled = ModpackOptionalSelection.ChooseAsync(fixture.Feedback, pack, null, stop.Token); stop.Cancel();
        try { canceled.GetAwaiter().GetResult(); AssertTrue(false); } catch (OperationCanceledException) { }
        AssertTrue(fixture.Feedback.Snapshot().Dialog is null);
        var replaced = ModpackOptionalSelection.ChooseAsync(fixture.Feedback, pack, null, default);
        fixture.Feedback.ShowMessageDialog("other", "other", "other");
        AssertTrue(replaced.GetAwaiter().GetResult() is null); AssertEqual("other", fixture.Feedback.Snapshot().Dialog!.Key);
    }

    private static void FeatureParityLanAutomaticallyJoinsServerListAndRetiresReads()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string instance = Path.Combine(fixture.TemporaryDirectory, "instance");
        var queries = new XsrQueryRouterBuilder();
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, q, cancellationToken: ct));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, q, cancellationToken: ct));
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (q, _) => ValueTask.FromResult(XsrResult.Success(
            new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory, "1.21.1", [], [new("overview", "概览"), new("game", "游戏设置"), new("servers", "服务器")], true, ""))));
        queries.Register<InstanceServerListQuery, InstanceServerList>(InstanceServerListContract.Read, (_, _) => ValueTask.FromResult(XsrResult.Success(new InstanceServerList("revision", [new(0, "Saved", "192.168.1.3")]))));
        int discovered = 0; CancellationToken lanToken = default;
        queries.Register<LanGameDiscoveryQuery, LanGameDiscoverySnapshot>(LanGameDiscoveryContract.Discover, (_, token) =>
        { discovered++; lanToken = token; return ValueTask.FromResult(XsrResult.Success(new LanGameDiscoverySnapshot([new("Untrusted broadcast title", "192.168.1.2:25565"), new("Duplicate", "192.168.1.2:25565"), new("Saved duplicate", "192.168.1.3:25565")]))); });
        var commands = new XsrCommandRouterBuilder(); InstanceServerListSaveCommand? saved = null;
        commands.Register<InstanceServerListSaveCommand>(InstanceServerListContract.Save, (q, _) => { saved = q; return ValueTask.FromResult(XsrResult.Success()); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()), commands.Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback, () => instance);
        string? joined = null; settings.JoinManagementServer = (_, address) => joined = address;
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 2400));
        void Pump(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { scene = fixture.Shell.Render(new(1100, 2400)); return condition(); }, TimeSpan.FromSeconds(5)));
        void Click(string key, string intent) => Emit(fixture.Intents, intent, FindByKey(fixture.Shell, scene, key).Entity);
        Pump(() => scene.Nodes.Any(n => fixture.Shell.Tree.Name(n.Entity) == "SettingsNav.servers"));
        Click("SettingsNav.servers", "ui.settings.section");
        Pump(() => scene.Nodes.Any(n => n.Text == "来自局域网的服务器 192.168.1.2:25565"));
        AssertEqual(1, discovered);
        AssertTrue(saved is null);
        AssertFalse(scene.Nodes.Any(n => fixture.Shell.Tree.Name(n.Entity) is "Servers.LanScan" or "SettingsNav.inspector"));
        AssertFalse(scene.Nodes.Any(n => n.Text is "局域网游戏" or "正在监听局域网广播…" or "发现局域网游戏" or "Untrusted broadcast title"));
        var rows = scene.Nodes.Where(n => fixture.Shell.Tree.Name(n.Entity).StartsWith("Servers.Lan.", StringComparison.Ordinal)
            && fixture.Shell.Tree.Name(n.Entity).EndsWith(".Join", StringComparison.Ordinal)).ToArray();
        AssertEqual(1, rows.Length);
        Emit(fixture.Intents, "ui.settings.management.action", rows[0].Entity); Pump(() => joined is not null); AssertEqual("192.168.1.2:25565", joined);
        Click("SettingsNav.overview", "ui.settings.section"); Pump(() => lanToken.IsCancellationRequested);
    }
}
