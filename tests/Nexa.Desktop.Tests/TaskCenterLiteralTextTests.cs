using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Tasks;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void TaskCenterProviderDetailsAndErrorsStayLiteralWhileStagesTranslate()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Renderer.ReducedMotion = true;
        var catalog = new UiLocalizationCatalog(); catalog.SetLanguage("en");
        const string detail = "设置/<provider>/{state secret}/{data path}.jar";
        const string error = "配置/<provider>/{data error}.json: failed <read>";
        const string terminal = "保留原文件 <provider>/{state terminal}.jar";
        List<string> translatedSources = [];
        string Localize(string source)
        {
            translatedSources.Add(source);
            return source.Contains("<provider>", StringComparison.Ordinal) ? "ERROR: provider fact entered localization" : catalog.Translate(source);
        }
        fixture.Shell.Renderer.TextLocalizer = Localize;
        using var bubble = new DesktopTaskBubblePresenter(fixture.Shell, fixture.Store);
        using var runtime = TaskCenterRuntimeComposer.Compose(fixture.Foundation.Host);
        using var controller = new TaskCenterController(fixture.Shell, fixture.Intents, runtime.Commands, fixture.Store, bubble);
        // Typed fixture state includes a provider-owned failed step, independently of any
        // concrete service's current phase planner. No command or task contract changes.
        var entries = fixture.Store.Resolve(TaskCenterStateContract.EntriesKey);
        var before = fixture.Store.ReadCollection<TaskCenterEntry>(entries);
        AssertTrue(fixture.Store.PublishDelta(entries, new XsrCollectionDelta<TaskCenterEntry, string>(before.Revision,
            [new("literal-running", "Fixture running", "游戏文件", detail, .5, 1, 2, 0, TaskCenterEntryState.Running,
                Steps: [new("游戏文件", detail, .5, TaskCenterEntryState.Running), new("版本信息", "", 0, TaskCenterEntryState.Waiting)]),
             new("literal-failed", "Fixture failed", "游戏文件", terminal, .5, 1, 2, 0, TaskCenterEntryState.Failed, error,
                Steps: [new("版本信息", "", 1, TaskCenterEntryState.Finished), new("游戏文件", error, .5, TaskCenterEntryState.Failed)])], [])).IsApplied);
        fixture.Store.Publish(fixture.Store.Resolve(TaskCenterStateContract.SummaryKey), new TaskCenterSummary(1, 2, .5, 0, 1));
        Emit(fixture.Intents, "ui.tasks.open");
        var scene = fixture.Shell.Render(new(1280, 900));
        AssertTrue(fixture.Shell.Renderer.Activate(Node("TaskCardDetails", "literal-running").Entity));
        scene = fixture.Shell.Render(new(1280, 900));
        AssertTrue(fixture.Shell.Renderer.Activate(Node("TaskCardDetails", "literal-failed").Entity));
        scene = fixture.Shell.Render(new(1280, 900));
        var runningCard = FindByKey(fixture.Shell, scene, "task-card:literal-running").Entity;
        var failedCard = FindByKey(fixture.Shell, scene, "task-card:literal-failed").Entity;
        var runningStep = FindByKey(fixture.Shell, scene, "task-card-step:literal-running:0").Entity;
        var failedStep = FindByKey(fixture.Shell, scene, "task-card-step:literal-failed:1").Entity;
        var action = controller.CardAction("literal-running"); AssertTrue(fixture.Shell.Renderer.Focus(action));
        CheckLiteralFacts();
        AssertFalse(catalog.Translate("游戏文件") == "游戏文件");
        AssertTrue(translatedSources.Contains("游戏文件")); AssertTrue(translatedSources.Contains("版本信息"));
        catalog.SetLanguage("zh-Hant"); fixture.Shell.Renderer.TextLocalizer = Localize;
        scene = fixture.Shell.Render(new(810, 900));
        CheckLiteralFacts();
        AssertEqual(runningCard, FindByKey(fixture.Shell, scene, "task-card:literal-running").Entity);
        AssertEqual(failedCard, FindByKey(fixture.Shell, scene, "task-card:literal-failed").Entity);
        AssertEqual(runningStep, FindByKey(fixture.Shell, scene, "task-card-step:literal-running:0").Entity);
        AssertEqual(failedStep, FindByKey(fixture.Shell, scene, "task-card-step:literal-failed:1").Entity);
        AssertEqual(action, controller.CardAction("literal-running")); AssertEqual(action, fixture.Shell.Renderer.Focused);

        void CheckLiteralFacts()
        {
            AssertEqual(catalog.Translate("游戏文件") + " · " + detail, Node("TaskCardStage", "literal-running").Text);
            AssertEqual(terminal, Node("TaskCardStage", "literal-failed").Text);
            AssertEqual(error, Node("TaskCardError", "literal-failed").Text);
            AssertEqual("● " + catalog.Translate("游戏文件") + " · " + detail, FindByKey(fixture.Shell, scene, "task-card-step:literal-running:0").Text);
            AssertEqual("✕ " + catalog.Translate("游戏文件") + " · " + error, FindByKey(fixture.Shell, scene, "task-card-step:literal-failed:1").Text);
            AssertFalse(translatedSources.Any(source => source.Contains("<provider>", StringComparison.Ordinal)));
        }
        XsrUiSceneNode Node(string key, string task) => scene.Nodes.Single(node => fixture.Shell.Tree.Name(node.Entity) == key && BelongsTo(node.Entity, task));
        bool BelongsTo(XsrUiEntityId entity, string task)
        {
            while (entity.IsAssigned && fixture.Shell.Tree.IsAlive(entity))
            {
                if (fixture.Shell.Tree.Name(entity) == "task-card:" + task) return true;
                entity = fixture.Shell.Tree.Parent(entity);
            }
            return false;
        }
    }
}
