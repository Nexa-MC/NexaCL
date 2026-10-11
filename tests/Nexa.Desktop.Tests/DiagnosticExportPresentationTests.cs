using Nexa.Desktop.Ui;
using Nexa.UI.Next;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void DiagnosticExportRequiresClickCoalescesRequestsAndCancelsOnDispose()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        CancellationToken observed = default;
        settings.ExportDiagnostics = token => { requests++; observed = token; return done.Task; };
        fixture.Shell.Renderer.ReducedMotion = true;
        fixture.Controller.SettingsPage = settings.Page;
        Emit(fixture.Intents, "ui.navigation.settings");
        var scene = fixture.Shell.Render(new(1000, 650));
        Emit(fixture.Intents, "ui.settings.section", FindEntity(fixture.Shell, "SettingsNav.about"));
        scene = fixture.Shell.Render(new(1000, 650));
        AssertEqual(0, requests);
        var sections = FindByKey(fixture.Shell, scene, "SettingsSections").Entity;
        fixture.Shell.Tree.GetComponent<XsrUiScroll>(sections)!.OffsetY = 10000;
        fixture.Shell.Tree.MarkDirty(sections, XsrUiDirtyKinds.Layout);
        scene = fixture.Shell.Render(new(1000, 650));
        var button = FindByKey(fixture.Shell, scene, "Management.导出诊断包").Entity;
        Emit(fixture.Intents, "ui.settings.management.action", button);
        fixture.Shell.Render(new(1000, 650));
        Emit(fixture.Intents, "ui.settings.management.action", button);
        fixture.Shell.Render(new(1000, 650));
        AssertEqual(1, requests);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(button)!.Enabled);
        settings.Dispose();
        AssertTrue(observed.IsCancellationRequested);
        done.SetResult(true);
        AssertTrue(SpinWait.SpinUntil(() => !settings.DiagnosticExportInProgress, 5000));
        AssertFalse(fixture.Feedback.Snapshot().Notifications.Any(item => item.Message.Contains("已导出", StringComparison.Ordinal)));
    }

    private static void DiagnosticExportSharesNavigationStateAndRestoresRetry()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        int requests = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        settings.ExportDiagnostics = cancellation => { requests++; token = cancellation; return done.Task; };
        double viewportHeight = 1600;
        var scene = fixture.Shell.Render(new(1000, 1600));
        Switch("privacy"); AssertEqual(0, requests);
        var export = FindByKey(fixture.Shell, scene, "Management.导出诊断包").Entity;
        AssertFalse(HasKey(fixture.Shell, scene, "Management.取消诊断包导出"));
        Emit(fixture.Intents, "ui.settings.management.action", export); Render();
        AssertEqual(1, requests); AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(export)!.Enabled);
        AssertTrue(HasKey(fixture.Shell, scene, "Management.取消诊断包导出"));
        Switch("about");
        var about = FindByKey(fixture.Shell, scene, "Management.导出诊断包").Entity;
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(about)!.Enabled);
        Emit(fixture.Intents, "ui.settings.management.action", about); Render(); AssertEqual(1, requests);
        viewportHeight = 650;
        var sections = FindByKey(fixture.Shell, scene, "SettingsSections").Entity;
        fixture.Shell.Tree.GetComponent<XsrUiScroll>(sections)!.OffsetY = 10000;
        fixture.Shell.Tree.MarkDirty(sections, XsrUiDirtyKinds.Layout);
        Render();
        var cancel = FindByKey(fixture.Shell, scene, "Management.取消诊断包导出").Entity;
        double offset = fixture.Shell.Tree.GetComponent<XsrUiScroll>(FindByKey(fixture.Shell, scene, "SettingsSections").Entity)!.OffsetY;
        AssertTrue(offset > 0);
        Emit(fixture.Intents, "ui.settings.management.action", cancel); Render();
        AssertTrue(token.IsCancellationRequested); AssertEqual("正在取消…", FindByKey(fixture.Shell, scene, "Management.导出诊断包").Text);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(cancel)!.Enabled);
        done.SetResult(true); // A callback that ignores cancellation must not announce success.
        AssertTrue(SpinWait.SpinUntil(() => { Render(); return fixture.Shell.Tree.GetComponent<XsrUiInput>(about)!.Enabled; }, 5000));
        AssertFalse(HasKey(fixture.Shell, scene, "Management.取消诊断包导出"));
        AssertEqual(offset, fixture.Shell.Tree.GetComponent<XsrUiScroll>(FindByKey(fixture.Shell, scene, "SettingsSections").Entity)!.OffsetY);
        AssertFalse(fixture.Feedback.Snapshot().Notifications.Any(item => item.Message.Contains("已导出", StringComparison.Ordinal)));
        settings.ExportDiagnostics = _ => { requests++; return Task.FromException<bool>(new IOException("Destination unavailable")); };
        Emit(fixture.Intents, "ui.settings.management.action", about); Render();
        AssertEqual(2, requests); AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiInput>(about)!.Enabled);
        AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(item => item.Message.Contains("未导出", StringComparison.Ordinal)));
        settings.ExportDiagnostics = _ => { requests++; return Task.FromResult(false); };
        Emit(fixture.Intents, "ui.settings.management.action", about); Render(); AssertEqual(3, requests);

        void Render() => scene = fixture.Shell.Render(new(1000, viewportHeight));
        void Switch(string section)
        { Emit(fixture.Intents, "ui.settings.section", FindEntity(fixture.Shell, "SettingsNav." + section)); Render(); }
    }

    private static void DiagnosticExportLeavingSettingsRetiresSuccessfulCompletion()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        settings.ExportDiagnostics = cancellation => { token = cancellation; return done.Task; };
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1000, 1600));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.privacy").Entity);
        scene = fixture.Shell.Render(new(1000, 1600));
        Emit(fixture.Intents, "ui.settings.management.action", FindByKey(fixture.Shell, scene, "Management.导出诊断包").Entity);
        fixture.Shell.Render(new(1000, 1600));
        Emit(fixture.Intents, "ui.navigation.launch");
        fixture.Shell.Render(new(1000, 1600)); AssertTrue(token.IsCancellationRequested);
        done.SetResult(true);
        AssertTrue(SpinWait.SpinUntil(() => !settings.DiagnosticExportInProgress, 5000));
        AssertFalse(fixture.Feedback.Snapshot().Notifications.Any(item => item.Message.Contains("已导出", StringComparison.Ordinal)));
    }
}
