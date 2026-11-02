using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SettingsServerOwnedStatusReloadKeepsEditorDraftCaretAndLiteralAuthority()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
        string instance = "instance-A", address = "literal <address> {state private}";
        var queries = WorldFixtureQueries(fixture);
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (query, _) => new(XsrResult.Success(
            new InstanceManagementSnapshot(query.InstanceDirectory, query.InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("game", "游戏设置"), new("servers", "服务器")], true, ""))));
        queries.Register<InstanceServerListQuery, InstanceServerList>(InstanceServerListContract.Read, (_, _) =>
            new(XsrResult.Success(new InstanceServerList("literal-revision", [new(37, "Fixture", address)]))));
        var pending = new TaskCompletionSource<XsrResult<InstanceServerStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
        InstanceServerStatusQuery? checkedServer = null;
        queries.Register<InstanceServerStatusQuery, InstanceServerStatus>(InstanceServerListContract.Status, (query, _) => { checkedServer = query; return new(pending.Task); });
        InstanceServerListSaveCommand? saved = null;
        var commands = new XsrCommandRouterBuilder();
        commands.Register<InstanceServerListSaveCommand>(InstanceServerListContract.Save, (command, _) => { saved = command; return new(XsrResult.Success()); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            commands.Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 1700));
        void Pump(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { scene = fixture.Shell.Render(new(1100, 1700)); return condition(); }, TimeSpan.FromSeconds(5)));
        void Click(string key) => Emit(fixture.Intents, "ui.settings.management.action", FindByKey(fixture.Shell, scene, key).Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "SettingsNav.servers"));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.servers").Entity);
        Pump(() => scene.Nodes.Any(node => node.Text == address));
        var workspace = FindByKey(fixture.Shell, scene, "ServersWorkspace").Entity;
        var row = FindByKey(fixture.Shell, scene, "ServerEntry.0").Entity;
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(fixture.Shell.Tree.Parent(workspace)) is not null);
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(fixture.Shell.Tree.Parent(row)) is not null);
        Click("Management.编辑"); Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "ServerName"));
        var name = FindByKey(fixture.Shell, scene, "ServerName").Entity;
        fixture.Shell.Renderer.SetTextInputValue(name, "Draft <name> {state private}");
        AssertTrue(fixture.Shell.Renderer.Focus(name)); fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var draft = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(name)!; int caret = draft.SelectionEnd;
        Click("Management.检查状态"); Pump(() => checkedServer is not null);
        AssertEqual(instance, checkedServer!.InstanceDirectory); AssertEqual("literal-revision", checkedServer.ExpectedRevision); AssertEqual(37, checkedServer.SourceIndex);
        AssertEqual(name, fixture.Shell.Renderer.Focused); AssertEqual(caret, draft.SelectionEnd);
        pending.SetResult(XsrResult.Success(new InstanceServerStatus(true, "literal <description> {state private}", "literal <version>", 3, 20, 5)
        { ClientProtocol = 763, ServerProtocol = 764 }));
        Pump(() => scene.Nodes.Any(node => node.Text?.Contains("literal <description> {state private}", StringComparison.Ordinal) == true));
        AssertEqual(workspace, FindByKey(fixture.Shell, scene, "ServersWorkspace").Entity); AssertEqual(row, FindByKey(fixture.Shell, scene, "ServerEntry.0").Entity);
        AssertEqual(name, FindByKey(fixture.Shell, scene, "ServerName").Entity); AssertEqual(name, fixture.Shell.Renderer.Focused);
        AssertEqual("Draft <name> {state private}", draft.ReadDraft()); AssertEqual(caret, draft.SelectionEnd);
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "en"))).IsSuccess);
        fixture.Shell.Renderer.ColorScheme = new(true, XsrUiAccent.Blue); scene = fixture.Shell.Render(new(800, 1700));
        AssertEqual(name, FindByKey(fixture.Shell, scene, "ServerName").Entity); AssertEqual(name, fixture.Shell.Renderer.Focused);
        AssertEqual(caret, draft.SelectionEnd); AssertEqual("Draft <name> {state private}", draft.ReadDraft());
        Click("Management.保存"); Pump(() => saved is not null);
        AssertEqual(instance, saved!.InstanceDirectory); AssertEqual("literal-revision", saved.ExpectedRevision); AssertEqual(37, saved.Entries[0].SourceIndex);
        AssertEqual(address, saved.Entries[0].Address); AssertEqual("Draft <name> {state private}", saved.Entries[0].Name);
    }

    private static void SettingsWorldOwnedSnapshotReloadPreservesIdentityAndRetiresOldResults()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
        string instance = "instance-A", identity = new('a', 64);
        var metadata = new InstanceWorldMetadata("literal <world> {state private}", "1.21.1", 4189, null, 42, 1, 2, false, 100, true, "world-revision", false, []) { DataPacksSupported = true };
        var queries = WorldFixtureQueries(fixture);
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (query, _) => new(XsrResult.Success(
            new InstanceManagementSnapshot(query.InstanceDirectory, query.InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("game", "游戏设置"), new("saves", "世界", query.InstanceDirectory)], true, "")
            { Contents = [new("saves", [new("Fixture", true, 100) { World = metadata }], true, null)] })));
        InstanceWorldSnapshotListQuery? listed = null;
        queries.Register<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(InstanceWorldSnapshotContract.List, (query, _) =>
        { listed = query; return new(XsrResult.Success<IReadOnlyList<InstanceWorldSnapshot>>([new(identity, DateTimeOffset.UtcNow, 3, 127)])); });
        var pending = new TaskCompletionSource<XsrResult<InstanceWorldSnapshotVerification>>(TaskCreationOptions.RunContinuationsAsynchronously);
        InstanceWorldSnapshotVerifyQuery? verified = null;
        queries.Register<InstanceWorldSnapshotVerifyQuery, InstanceWorldSnapshotVerification>(InstanceWorldSnapshotContract.Verify, (query, _) => { verified = query; return new(pending.Task); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()), fixture.Foundation.Commands,
            fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 2200));
        void Pump(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { scene = fixture.Shell.Render(new(1100, 2200)); return condition(); }, TimeSpan.FromSeconds(5)));
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "SettingsNav.saves"));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.saves").Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "ManagementContentDetails.Fixture"));
        Emit(fixture.Intents, "ui.settings.management.action", FindByKey(fixture.Shell, scene, "ManagementContentDetails.Fixture").Entity);
        Pump(() => scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity) == "WorldSnapshots"));
        var workspace = FindByKey(fixture.Shell, scene, "WorldDetails").Entity;
        var snapshots = FindByKey(fixture.Shell, scene, "WorldSnapshots").Entity;
        AssertTrue(scene.Nodes.Any(node => node.Text == metadata.LevelName));
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(fixture.Shell.Tree.Parent(workspace)) is not null);
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(fixture.Shell.Tree.Parent(snapshots)) is not null);
        Emit(fixture.Intents, "ui.settings.management.action", FindByKey(fixture.Shell, scene, "Management.读取快照历史").Entity);
        Pump(() => scene.Nodes.Any(node => node.Text?.Contains("3 个文件 · 127 B", StringComparison.Ordinal) == true));
        AssertEqual(instance, listed!.InstanceDirectory); AssertEqual("Fixture", listed.WorldName);
        var verify = scene.Nodes.Single(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("WorldSnapshots.", StringComparison.Ordinal)
            && fixture.Shell.Tree.Name(node.Entity).EndsWith(".Verify", StringComparison.Ordinal)).Entity;
        var row = fixture.Shell.Tree.Parent(fixture.Shell.Tree.Parent(verify));
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(fixture.Shell.Tree.Parent(row)) is not null);
        AssertTrue(fixture.Shell.Renderer.Focus(FindByKey(fixture.Shell, scene, "Management.锁定编辑").Entity));
        var focused = fixture.Shell.Renderer.Focused;
        Emit(fixture.Intents, "ui.settings.management.action", verify); Pump(() => verified is not null);
        AssertEqual(instance, verified!.InstanceDirectory); AssertEqual("Fixture", verified.WorldName); AssertEqual(identity, verified.Identity);
        AssertEqual(workspace, FindByKey(fixture.Shell, scene, "WorldDetails").Entity); AssertEqual(snapshots, FindByKey(fixture.Shell, scene, "WorldSnapshots").Entity);
        AssertTrue(fixture.Shell.Tree.IsAlive(row)); AssertEqual(focused, fixture.Shell.Renderer.Focused);
        instance = "instance-B"; Pump(() => settings.SelectedSection == "overview");
        pending.SetResult(XsrResult.Success(new InstanceWorldSnapshotVerification(identity, 3, 2, ["obsolete <path> {state private}"])));
        scene = fixture.Shell.Render(new(1100, 2200));
        AssertFalse(scene.Nodes.Any(node => node.Text?.Contains("obsolete <path>", StringComparison.Ordinal) == true));
    }

    private static XsrQueryRouterBuilder WorldFixtureQueries(LaunchPageFixture fixture)
    {
        var queries = new XsrQueryRouterBuilder();
        AssertTrue(fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog));
        AssertTrue(fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective));
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (query, token) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, query, cancellationToken: token));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (query, token) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, query, cancellationToken: token));
        return queries;
    }
}
