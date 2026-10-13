using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Logging;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void RuntimeTraceWorkspaceCapturesOncePagesMetadataAndRejectsRetiredActions()
    {
        var trace = new RuntimeTraceSession();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), stateObserver: trace);
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
        var queries = new XsrQueryRouterBuilder();
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (query, token) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, query, cancellationToken: token));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (query, token) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, query, cancellationToken: token));
        int captures = 0;
        RuntimeTraceSnapshot? captured = null;
        var pending = new TaskCompletionSource<XsrResult<RuntimeTraceSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;
        queries.Register<RuntimeTraceQuery, RuntimeTraceSnapshot>(RuntimeTraceContract.Read, (_, token) =>
        { captures++; capturedToken = token; captured = trace.Capture(); return new ValueTask<XsrResult<RuntimeTraceSnapshot>>(pending.Task); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true;
        fixture.Shell.Stage.Navigation.Replace(settings.Page);
        // Privacy preferences precede the developer capture card. Fit that complete card
        // so this assertion tests its 32-row pages, rather than viewport clipping.
        XsrUiSize viewport = new(1100, 6000);
        var scene = fixture.Shell.Render(viewport);
        void PumpUntil(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { scene = fixture.Shell.Render(viewport); return condition(); }, TimeSpan.FromSeconds(5)));
        void Click(string key) => Emit(fixture.Intents, "ui.settings.diagnostics.trace", FindByKey(fixture.Shell, scene, key).Entity);
        PumpUntil(() => HasKey(fixture.Shell, scene, "SettingsNav.privacy"));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.privacy").Entity);
        PumpUntil(() => HasKey(fixture.Shell, scene, "RuntimeTrace.Capture"));
        AssertEqual(0, captures);
        var revision = fixture.Foundation.Host.SettingsPolicy.Read(new()).Value!.Revision;
        var secretKey = fixture.Store.Resolve(LaunchPageState.WidgetHintKey);
        for (int index = 0; index < 80; index++) fixture.Store.Publish(secretKey, "private-state-content");
        var retainedCapture = FindByKey(fixture.Shell, scene, "RuntimeTrace.Capture").Entity;
        var retainedFilter = FindByKey(fixture.Shell, scene, "RuntimeTrace.State").Entity;
        Click("RuntimeTrace.Capture");
        PumpUntil(() => captures == 1);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(retainedFilter)!.Enabled);
        Click("RuntimeTrace.Capture"); scene = fixture.Shell.Render(viewport); AssertEqual(1, captures);
        pending.SetResult(XsrResult.Success(captured!));
        PumpUntil(() => fixture.Shell.Tree.GetComponent<XsrUiInput>(retainedFilter)!.Enabled);
        AssertEqual(retainedCapture, FindByKey(fixture.Shell, scene, "RuntimeTrace.Capture").Entity);
        Click("RuntimeTrace.State"); scene = fixture.Shell.Render(viewport);
        int VisibleRows() => scene.Nodes.Count(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("RuntimeTrace.Identity.", StringComparison.Ordinal));
        AssertEqual(32, VisibleRows());
        var traceCard = FindByKey(fixture.Shell, scene, "RuntimeTrace");
        AssertTrue(traceCard.Rect.Y + traceCard.Rect.Height <= viewport.Height);
        AssertTrue(captured is not null);
        var expected = SettingsPageController.FilterRuntimeTrace(captured!, "state");
        AssertTrue(expected.Count >= 80);
        Click("RuntimeTrace.Next"); scene = fixture.Shell.Render(viewport);
        AssertEqual(32, VisibleRows());
        AssertTrue(FindByKey(fixture.Shell, scene, "RuntimeTrace.Detail.1").Text!.StartsWith(
            expected[32].Detail + " · cid=" + expected[32].CorrelationId + " · ticks="
            + expected[32].MonotonicTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        AssertEqual(1, captures);
        AssertEqual(retainedFilter, FindByKey(fixture.Shell, scene, "RuntimeTrace.State").Entity);
        fixture.Store.Publish(secretKey, "private-state-content-new");
        for (int index = 0; index < 4; index++) scene = fixture.Shell.Render(viewport);
        AssertEqual(1, captures);
        AssertFalse(scene.Nodes.Any(node => node.Text?.Contains("private-state-content", StringComparison.Ordinal) == true));
        AssertEqual(revision, fixture.Foundation.Host.SettingsPolicy.Read(new()).Value!.Revision);
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click("RuntimeTrace.Capture"); PumpUntil(() => captures == 2);
        var stale = FindByKey(fixture.Shell, scene, "RuntimeTrace.Capture").Entity;
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.general").Entity);
        scene = fixture.Shell.Render(viewport);
        Emit(fixture.Intents, "ui.settings.diagnostics.trace", stale);
        scene = fixture.Shell.Render(viewport);
        AssertEqual(2, captures);
        AssertTrue(capturedToken.IsCancellationRequested);
        pending.SetResult(XsrResult.Success(captured!));
        scene = fixture.Shell.Render(viewport);
        AssertFalse(HasKey(fixture.Shell, scene, "RuntimeTrace"));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.privacy").Entity);
        PumpUntil(() => settings.SelectedSection == "privacy");
        AssertFalse(HasKey(fixture.Shell, scene, "RuntimeTrace.Summary"));
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("developer.enabled", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "false"))).IsSuccess);
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.privacy").Entity);
        PumpUntil(() => settings.SelectedSection == "privacy");
        AssertFalse(HasKey(fixture.Shell, scene, "RuntimeTrace"));
    }
}
