using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Resources;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void OnlineModpackPageRetainsDraftRejectsStalePreviewAndUsesOnlineUpdate()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string instance = Path.Combine(fixture.TemporaryDirectory, "instance-A");
        var queries = new XsrQueryRouterBuilder();
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, q, cancellationToken: ct));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, q, cancellationToken: ct));
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (q, _) =>
            ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("modpack", "整合包与导出")], true, ""))));
        var localQueries = new XsrQueryRouterBuilder();
        localQueries.Register<MinecraftModpackUpdateQuery, MinecraftModpackUpdatePreview>(MinecraftModpackUpdateContract.Preview, (_, _) =>
            throw new InvalidOperationException("Online previews must use the resource route."));
        var resourceQueries = new XsrQueryRouterBuilder();
        ResourceVersion Version(string id) => new(id, id, id, "release", ["1.21.1"], ["fabric"], "2026-02-01T00:00:00Z", "https://modrinth.com/modpack/Pack")
        { Provider = ResourceProvider.Modrinth, ProjectId = "Pack", File = new("pack.mrpack", "https://cdn.modrinth.com/data/Pack/pack.mrpack", 64, null, new string('A', 128)) };
        int reads = 0, previewReads = 0; bool stale = false;
        var pending = new TaskCompletionSource<XsrResult<ResourceModpackPreview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        ResourceModpackPreviewQuery? requested = null;
        resourceQueries.Register<ResourceModpackOnlineQuery, ResourceModpackOnline>(ResourceModpackContract.Online, (q, _) =>
        {
            reads++;
            return ValueTask.FromResult(XsrResult.Success(new ResourceModpackOnline(q, new string('B', 64),
                new("Pack", "Example <pack>", "Literal {data metadata}", "Author", 42, "https://modrinth.com/modpack/Pack")
                { Kind = ResourceKind.Modpack, Sources = [new(ResourceProvider.Modrinth, "Pack")] }, Version("Old"), [Version("New")], null)
            { IsStale = stale, UpdateVersion = stale ? null : Version("New") }));
        });
        ResourceModpackPreview MakePreview(ResourceModpackPreviewQuery q) => new(q, Version("New"), new(
            new(instance, Path.Combine(fixture.TemporaryDirectory, "cache", "new.mrpack"), IncludeOptional: q.IncludeOptional, ReplaceModifiedMods: q.ReplaceModifiedMods),
            new("cache", new string('C', 64), "New pack", "2.0", "Modrinth", "1.21.1", InstallLoader.Fabric, "0.16.9", "new-id", 1, 0),
            "revision", ["mods/new.jar"], [], ["mods/old.jar"], ["config/local.txt"], []));
        resourceQueries.Register<ResourceModpackPreviewQuery, ResourceModpackPreview>(ResourceModpackContract.Preview, (q, _) =>
        { requested = q; return ++previewReads == 1 ? new(pending.Task) : ValueTask.FromResult(XsrResult.Success(MakePreview(q))); });
        var commands = new XsrCommandRouterBuilder(); ResourceModpackUpdateCommand? applied = null;
        commands.Register<ResourceModpackUpdateCommand>(ResourceModpackContract.Update, (q, _) =>
        { applied = q; return ValueTask.FromResult(XsrResult.Success()); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback, () => instance);
        settings.ConfigureModpackUpdates(localQueries.Build(new NoopDispatchObserver()), new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()));
        settings.ConfigureOnlineContent(resourceQueries.Build(new NoopDispatchObserver()), _ => { }, commands.Build(new NoopDispatchObserver()));
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1000, 1800));
        void Pump(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { scene = fixture.Shell.Render(new(1000, 1800)); return condition(); }, TimeSpan.FromSeconds(5)));
        void Click(string name) => Emit(fixture.Intents, "ui.settings.management.action", FindByKey(fixture.Shell, scene, name).Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "SettingsNav.overview"));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.overview").Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("ModpackOnline.Preview.", StringComparison.Ordinal)));
        string Action() => fixture.Shell.Tree.Name(scene.Nodes.First(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("ModpackOnline.Preview.", StringComparison.Ordinal)).Entity);
        var archive = FindByKey(fixture.Shell, scene, "ModpackUpdate.Archive").Entity;
        fixture.Shell.Renderer.SetTextInputValue(archive, "local draft");
        Click(Action()); Pump(() => requested is not null);
        fixture.Shell.Renderer.SetTextInputValue(archive, "edited while preparing"); fixture.Shell.Render(new(1000, 1800));
        pending.SetResult(XsrResult.Success(MakePreview(requested!)));
        Pump(() => HasKey(fixture.Shell, scene, "ModpackUpdate.Apply") && !FindByKey(fixture.Shell, scene, "ModpackUpdate.Apply").IsEnabled && FindByKey(fixture.Shell, scene, Action()).IsEnabled);
        AssertEqual("edited while preparing", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(archive)!.ReadDraft());
        Click(Action()); Pump(() => FindByKey(fixture.Shell, scene, "ModpackUpdate.Apply").IsEnabled);
        AssertEqual(2, previewReads); AssertEqual(new string('B', 64), requested!.InstalledArchiveSha256);
        Click("ModpackUpdate.Apply"); Pump(() => applied is not null && reads >= 2);
        AssertEqual(requested, applied!.Preview.Request); AssertEqual("revision", applied.Preview.Installation.Revision);
        int retainedReads = reads; for (int i = 0; i < 5; i++) scene = fixture.Shell.Render(new(1000, 1800)); AssertEqual(retainedReads, reads);
        stale = true; Click("ModpackUpdate.OnlineRefresh"); Pump(() => reads > retainedReads && !FindByKey(fixture.Shell, scene, Action()).IsEnabled);
        AssertFalse(FindByKey(fixture.Shell, scene, "ModpackUpdate.Apply").IsEnabled);
    }
}
