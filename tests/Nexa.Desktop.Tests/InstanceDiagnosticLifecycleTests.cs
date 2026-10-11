using Nexa.Desktop.Ui;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void InstanceLaunchFactsCoalesceAndCancelBeforeReturningToSameScope()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Renderer.ReducedMotion = true;
        string instance = Path.Combine(fixture.TemporaryDirectory, "versions", "launch-facts");
        EnableInstanceDevelopmentMode(fixture, instance);
        var queries = new XsrQueryRouterBuilder();
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, q, cancellationToken: ct));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, q, cancellationToken: ct));
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (q, _) =>
            ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("game", "游戏设置"), new("diagnostics", "实例诊断")], true, ""))));
        queries.Register<DurableInstanceDiagnosticHistoryQuery, IReadOnlyList<DurableDiagnosticEntry>>(DiagnosticWorkspaceContract.InstanceHistory,
            (_, _) => ValueTask.FromResult(XsrResult.Success<IReadOnlyList<DurableDiagnosticEntry>>([])));
        var plan = new TaskCompletionSource<XsrResult<MinecraftLaunchPlanDiagnosticSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var docs = new TaskCompletionSource<XsrResult<InstanceLocalDocumentsSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0; CancellationToken planStop = default, documentsStop = default;
        queries.Register<MinecraftLaunchPlanDiagnosticQuery, MinecraftLaunchPlanDiagnosticSnapshot>(MinecraftLaunchPlanDiagnosticContract.Query, (q, ct) =>
        { AssertEqual(instance, q.InstanceDirectory); reads++; planStop = ct; return new(plan.Task); });
        queries.Register<InstanceLocalDocumentsQuery, InstanceLocalDocumentsSnapshot>(InstanceLocalDocumentsContract.Query, (q, ct) =>
        { AssertEqual(instance, q.InstanceDirectory); reads++; documentsStop = ct; return new(docs.Task); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 6000));
        void Pump(Func<bool> ready) => AssertTrue(SpinWait.SpinUntil(() => { scene = fixture.Shell.Render(new(1100, 6000)); return ready(); }, TimeSpan.FromSeconds(5)));
        void Click(string key, string intent) => Emit(fixture.Intents, intent, FindByKey(fixture.Shell, scene, key).Entity);
        Pump(() => HasKey(fixture.Shell, scene, "SettingsNav.diagnostics"));
        Click("SettingsNav.diagnostics", "ui.settings.section");
        Pump(() => HasKey(fixture.Shell, scene, "AdvancedLaunch.Read"));
        var root = FindByKey(fixture.Shell, scene, "AdvancedLaunchDiagnostics").Entity;
        var read = FindByKey(fixture.Shell, scene, "AdvancedLaunch.Read").Entity;
        Emit(fixture.Intents, "ui.settings.management.action", read);
        Pump(() => reads == 2);
        AssertEqual(root, FindByKey(fixture.Shell, scene, "AdvancedLaunchDiagnostics").Entity);
        AssertEqual(read, FindByKey(fixture.Shell, scene, "AdvancedLaunch.Read").Entity);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(read)!.Enabled);
        Emit(fixture.Intents, "ui.settings.management.action", read);
        scene = fixture.Shell.Render(new(1100, 6000)); AssertEqual(2, reads);
        Click("SettingsNav.overview", "ui.settings.section"); Pump(() => settings.SelectedSection == "overview");
        AssertTrue(planStop.IsCancellationRequested); AssertTrue(documentsStop.IsCancellationRequested);
        Click("SettingsNav.diagnostics", "ui.settings.section"); Pump(() => HasKey(fixture.Shell, scene, "AdvancedLaunch.Read"));
        plan.SetResult(XsrResult.Success(new MinecraftLaunchPlanDiagnosticSnapshot(instance, DateTimeOffset.UtcNow, "java", instance,
            "RETIRED-LAUNCH-FACTS", "Vanilla", 4096, false, ["RETIRED-LAUNCH-FACTS"], [], [], [], false)));
        docs.SetResult(XsrResult.Success(new InstanceLocalDocumentsSnapshot(instance, DateTimeOffset.UtcNow, [], true)));
        scene = fixture.Shell.Render(new(1100, 6000)); scene = fixture.Shell.Render(new(1100, 6000));
        AssertFalse(scene.Nodes.Any(node => node.Text?.Contains("RETIRED-LAUNCH-FACTS", StringComparison.Ordinal) == true));
        AssertEqual(2, reads);
        Emit(fixture.Intents, "ui.settings.management.action", read); scene = fixture.Shell.Render(new(1100, 6000));
        AssertEqual(2, reads);
    }

    private static void InstanceDiagnosticFormatsLocalizeWithoutReparsingMetadata()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        UiLocalizationCatalog localization = new();
        fixture.Shell.Renderer.TextLocalizer = localization.Translate;
        string literal = "运行中 <Node> {data timestamp} {state secret} & 皮肤";
        var locales = new[] { ("en", "Working directory: "), ("zh-Hant", "工作目錄："), ("zh-Hans", "工作目录：") };
        foreach (var (language, prefix) in locales)
        {
            localization.SetLanguage(language);
            var line = DesktopMarkup.Dynamic("SettingsInstanceLaunchWorkingDirectory.d.nxml",
                new Dictionary<string, string> { ["directory"] = literal }, fixture.Shell.Renderer.LocalizeText);
            AssertEqual(prefix + literal, line.Root.Content);
            AssertEqual(0, line.Root.Bindings.Count);
            var summary = DesktopMarkup.Dynamic("SettingsInstanceLaunchTraceStart.d.nxml", new Dictionary<string, string>
            { ["timestamp"] = literal, ["session"] = "fixture-id", ["state"] = literal }, fixture.Shell.Renderer.LocalizeText);
            AssertTrue(summary.Root.Content!.Contains(literal, StringComparison.Ordinal));
            AssertEqual(0, summary.Root.Bindings.Count);
        }
    }
}
