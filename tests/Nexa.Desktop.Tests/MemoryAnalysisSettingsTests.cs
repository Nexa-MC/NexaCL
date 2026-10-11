using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void MemoryAnalysisSettingsAreUnavailable()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true;
        fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1100, 900));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.game").Entity);
        fixture.Shell.Render(new(1100, 900));
        XsrUiEntityId row = default;
        fixture.Shell.Tree.Walk(settings.Page, entity =>
        {
            if (fixture.Shell.Tree.Name(entity) == "SettingsRow.global.game.preflight-memory-analysis") row = entity;
            return true;
        });
        AssertTrue(row.IsAssigned);
        var labels = new List<string>();
        fixture.Shell.Tree.Walk(row, entity =>
        {
            AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiInput>(entity) is null);
            if (fixture.Shell.Tree.GetComponent<XsrUiText>(entity) is { } text) labels.Add(text.Content);
            return true;
        });
        AssertTrue(labels.Contains("内存分析")); AssertTrue(labels.Contains("暂不可用"));
    }
}
