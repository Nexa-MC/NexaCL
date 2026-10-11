using Nexa.Desktop.Ui;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static XsrQueryRouterBuilder PlatformPresentationQueries(LaunchPageFixture fixture)
    {
        var queries = new XsrQueryRouterBuilder();
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (query, token) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, query, cancellationToken: token));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (query, token) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, query, cancellationToken: token));
        return queries;
    }

    private static void PlatformListsPreserveUnknownStatusAndLiteralValues()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var devices = InputCatalog.InputGyroscopeDevices;
        var packs = MachineInstanceCatalog.MinecraftSettingsResourcePacks;
        var current = new MachineCapabilitySnapshot(1, now,
        [devices.Unavailable(CapabilityAvailability.PermissionDenied, now, "设备查询被拒绝"),
            packs.Unavailable(CapabilityAvailability.DependencyMissing, now, "资源包设置尚未读取"),
            InputCatalog.InputKeyboardAvailable.Unavailable(CapabilityAvailability.NotImplemented, now, "")]);
        var queries = PlatformPresentationQueries(fixture);
        queries.Register<MachineCapabilityQuery, MachineCapabilitySnapshot>(MachineCapabilityStateContract.SnapshotQuery,
            (_, _) => ValueTask.FromResult(XsrResult.Success(current)));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 1400));
        void PumpUntil(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { scene = fixture.Shell.Render(new(1100, 1400)); return condition(); }, TimeSpan.FromSeconds(5)));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.platform").Entity);
        PumpUntil(() => HasKey(fixture.Shell, scene, "PlatformCapabilityList." + packs.Id));
        string[] List(string id)
        {
            var list = FindByKey(fixture.Shell, scene, "PlatformCapabilityList." + id).Entity;
            AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(list) is not null);
            return fixture.Shell.Tree.Children(list).Select(entity => fixture.Shell.Tree.GetComponent<XsrUiText>(entity)!.Content).ToArray();
        }
        AssertEqual("设备查询被拒绝", List(devices.Id).Single());
        AssertEqual("资源包设置尚未读取", List(packs.Id).Single());
        const string deviceName = "Controller <state> {data secret}";
        const string packName = "file/资源包 {state private}.zip";
        current = new(2, now, [devices.Observe([new(deviceName, true), new("USB controller", false)], now, "fixture"),
            packs.Observe([packName, "second.zip"], now, "fixture"),
            InputCatalog.InputKeyboardAvailable.Unavailable(CapabilityAvailability.NotImplemented, now, "")]);
        fixture.Store.Publish(fixture.Store.Resolve(MachineCapabilityStateContract.RevisionKey), 2L);
        PumpUntil(() => List(devices.Id).FirstOrDefault()?.StartsWith(deviceName, StringComparison.Ordinal) == true);
        AssertEqual(deviceName + " · 可用", List(devices.Id)[0]);
        AssertEqual("USB controller · 不可用", List(devices.Id)[1]);
        AssertEqual(packName, List(packs.Id)[0]);
        foreach (double width in new[] { 760d, 1100d })
        {
            scene = fixture.Shell.Render(new(width, 1400));
            foreach (string id in new[] { devices.Id, packs.Id })
            {
                var list = FindByKey(fixture.Shell, scene, "PlatformCapabilityList." + id);
                var row = FindByKey(fixture.Shell, scene, "PlatformCapability." + id);
                AssertTrue(Math.Abs(list.Rect.X + list.Rect.Width - row.Rect.X - row.Rect.Width) < .01);
            }
            var badge = FindByKey(fixture.Shell, scene, "PlatformCapabilityBadge.input.keyboard.available");
            var style = fixture.Shell.Tree.GetComponent<XsrUiVisualStyle>(badge.Entity)!;
            AssertEqual(XsrUiTextAlignment.Center, style.TextAlignment);
            AssertTrue(badge.Rect.Width >= 96 && badge.Rect.Width <= 330);
            AssertEqual("尚未接入", badge.Text);
        }
        current = new(3, now, [devices.Observe([], now, "fixture"), packs.Observe([], now, "fixture")]);
        fixture.Store.Publish(fixture.Store.Resolve(MachineCapabilityStateContract.RevisionKey), 3L);
        PumpUntil(() => List(packs.Id).SingleOrDefault() == "默认资源");
        AssertEqual("未检测到已连接设备", List(devices.Id).Single());
    }

    private static void PlatformRepairRejectsRetiredConfirmationAndJavaPick()
    {
        var first = Instance("platform-repair-first"); var second = Instance("platform-repair-second");
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([first, second]));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var repair = RemediationCatalog.Definitions().Single(definition => definition.Id == "remediation.game.repair_files");
        var select = RemediationCatalog.Definitions().Single(definition => definition.Id == "remediation.java.select");
        var snapshot = new MachineCapabilitySnapshot(1, now,
            [((CapabilityDefinition<bool>)repair).Observe(true, now, "fixture"), ((CapabilityDefinition<bool>)select).Observe(true, now, "fixture")]);
        var issue = new CapabilityPreflightIssue("GAME_FILES_MISSING", PreflightSeverity.Warning, "game", PreflightCertainty.Verified,
            false, ["fixture"], remediations: [repair.Id]);
        var javaIssue = new CapabilityPreflightIssue("JAVA_MISSING", PreflightSeverity.Warning, "java", PreflightCertainty.Verified,
            false, ["fixture"], remediations: [select.Id]);
        var report = new CapabilityPreflightReport([issue, javaIssue], [issue, javaIssue], PreflightSeverity.Warning);
        var queries = PlatformPresentationQueries(fixture);
        queries.Register<MachineCapabilityQuery, MachineCapabilitySnapshot>(MachineCapabilityStateContract.SnapshotQuery,
            (_, _) => ValueTask.FromResult(XsrResult.Success(snapshot)));
        queries.Register<LaunchPreflightQuery, CapabilityPreflightReport>(MachineCapabilityStateContract.PreflightQuery,
            (_, _) => ValueTask.FromResult(XsrResult.Success(report)));
        List<RemediationRequest> writes = [];
        var commands = new XsrCommandRouterBuilder();
        commands.Register<RemediationRequest>(MachineCapabilityStateContract.RemediationCommand,
            (request, _) => { writes.Add(request); return ValueTask.FromResult(XsrResult.Success()); });
        var picker = new TaskCompletionSource<string?>();
        int picks = 0;
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            commands.Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback)
        { PickRemediationJava = () => { picks++; return picker.Task; } };
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 2200));
        void PumpUntil(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { scene = fixture.Shell.Render(new(1100, 2200)); return condition(); }, TimeSpan.FromSeconds(5)));
        void Navigate(string page)
        {
            Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav." + page).Entity);
            scene = fixture.Shell.Render(new(1100, 2200));
        }
        void Ready() => PumpUntil(() => HasKey(fixture.Shell, scene, "PlatformRemediation." + repair.Id));
        void Click(string id) => Emit(fixture.Intents, "ui.settings.platform.remediation",
            FindByKey(fixture.Shell, scene, "PlatformRemediation." + id).Entity);
        Navigate("platform"); Ready(); Click(repair.Id);
        var retired = fixture.Feedback.Snapshot().Dialog ?? throw new InvalidOperationException("Missing repair confirmation.");
        Navigate("general"); Navigate("platform"); Ready();
        AssertTrue(fixture.Feedback.ResolveDialog(retired.Id, true));
        scene = fixture.Shell.Render(new(1100, 2200)); AssertEqual(0, writes.Count);
        // A scope change must retire a reviewed action before a new UI frame even runs.
        Click(repair.Id); var wrongScope = fixture.Feedback.Snapshot().Dialog!;
        string root = fixture.Store.Read<MinecraftLibrarySnapshot>(fixture.Store.Resolve(MinecraftLibraryContract.StateKey)).Value!.RootDirectory;
        AssertTrue(fixture.Library.Service.SelectInstance(root, second.Id).IsSuccess);
        AssertTrue(fixture.Feedback.ResolveDialog(wrongScope.Id, true)); AssertEqual(0, writes.Count);
        scene = fixture.Shell.Render(new(1100, 2200)); Ready();
        Click(repair.Id); var current = fixture.Feedback.Snapshot().Dialog!;
        AssertTrue(fixture.Feedback.ResolveDialog(current.Id, true));
        PumpUntil(() => writes.Count == 1);
        AssertEqual(second.DirectoryPath, writes[0].Arguments!["instanceDirectory"]);
        AssertTrue(writes[0].Confirmed);
        Click(select.Id); AssertEqual(1, picks);
        Navigate("general"); Navigate("platform"); Ready();
        picker.SetResult(Path.GetFullPath("retired-platform-java"));
        AssertTrue(SpinWait.SpinUntil(() => picker.Task.IsCompleted, TimeSpan.FromSeconds(5)));
        scene = fixture.Shell.Render(new(1100, 2200)); AssertEqual(1, writes.Count);
        picker = new(); Click(select.Id); AssertEqual(2, picks);
        string executable = Path.GetFullPath("current-platform-java"); picker.SetResult(executable);
        PumpUntil(() => writes.Count == 2);
        AssertEqual(executable, writes[1].Arguments!["executable"]);
        AssertEqual(second.DirectoryPath, writes[1].Arguments!["instanceDirectory"]);
    }
}
