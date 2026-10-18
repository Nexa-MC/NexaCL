using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ModpackUpdatePagePreviewsRetainsDraftAndAppliesExactSelection()
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
        var modpackQueries = new XsrQueryRouterBuilder(); MinecraftModpackUpdateQuery? requested = null;
        modpackQueries.Register<MinecraftModpackUpdateQuery, MinecraftModpackUpdatePreview>(MinecraftModpackUpdateContract.Preview, (q, _) =>
        {
            requested = q;
            return ValueTask.FromResult(XsrResult.Success(new MinecraftModpackUpdatePreview(q,
                new(q.ArchivePath, new string('A', 64), "Example pack", "2.0", "MRPack", "1.21.1", InstallLoader.Fabric, "0.16.9", "new-id", 1, 0),
                "revision", ["mods/new.jar"], [], ["mods/old.jar"], ["config/local.txt"], [])));
        });
        var modpackCommands = new XsrCommandRouterBuilder(); MinecraftModpackUpdateCommand? applied = null;
        modpackCommands.Register<MinecraftModpackUpdateCommand>(MinecraftModpackUpdateContract.Apply, (command, _) =>
        { applied = command; return ValueTask.FromResult(XsrResult.Success()); });
        EnableInstanceDevelopmentMode(fixture, instance);
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback, () => instance);
        settings.ConfigureModpackUpdates(modpackQueries.Build(new NoopDispatchObserver()), modpackCommands.Build(new NoopDispatchObserver()));
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1000, 1800));
        void Pump(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { scene = fixture.Shell.Render(new(1000, 1800)); return condition(); }, TimeSpan.FromSeconds(5)));
        void Click(string name) => Emit(fixture.Intents, "ui.settings.management.action", FindByKey(fixture.Shell, scene, name).Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "SettingsNav.modpack"));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.modpack").Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "ModpackUpdate.Archive"));
        string path = Path.Combine(fixture.TemporaryDirectory, "new-pack.mrpack");
        var archive = FindByKey(fixture.Shell, scene, "ModpackUpdate.Archive").Entity;
        fixture.Shell.Renderer.SetTextInputValue(archive, path); fixture.Shell.Renderer.Focus(archive);
        Click("ModpackUpdate.Optional"); Pump(() => FindByKey(fixture.Shell, scene, "ModpackUpdate.Optional").IsChecked == true);
        AssertEqual(archive, FindByKey(fixture.Shell, scene, "ModpackUpdate.Archive").Entity);
        AssertEqual(path, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(archive)!.ReadDraft());
        Click("ModpackUpdate.Preview"); Pump(() => requested is not null && FindByKey(fixture.Shell, scene, "ModpackUpdate.Apply").IsEnabled);
        AssertEqual(instance, requested!.InstanceDirectory); AssertEqual(path, requested.ArchivePath); AssertTrue(requested.IncludeOptional);
        Click("ModpackUpdate.Apply"); Pump(() => applied is not null);
        AssertEqual(requested, applied!.Preview.Request); AssertEqual("revision", applied.Preview.Revision);
        fixture.Shell.Renderer.SetTextInputValue(archive, path + ".changed");
        Pump(() => !FindByKey(fixture.Shell, scene, "ModpackUpdate.Apply").IsEnabled);
    }
}
