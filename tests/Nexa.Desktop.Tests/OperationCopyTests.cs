using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Tasks;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void OperationCopyLocalizesFormatsAndPreservesLiteralMetadata()
    {
        var catalog = new UiLocalizationCatalog();
        string literal = "<metadata> {data percent} {state secret}";
        foreach (string language in new[] { "zh-Hans", "zh-Hant", "en" })
        {
            catalog.SetLanguage(language);
            string text = DesktopPresentationText.Format("TaskHeaderRemaining", new()
            { ["header"] = literal, ["remaining"] = "12" }, catalog.Translate);
            AssertTrue(text.Contains(literal, StringComparison.Ordinal));
            AssertTrue(text.Contains("12", StringComparison.Ordinal));
            AssertEqual(1, Count(text, "{state secret}"));
            string caption = DesktopPresentationText.Format("ProcessLogsCaption", new() { ["instance"] = literal }, catalog.Translate);
            AssertEqual(literal + (language == "en" ? " · Logs" : language == "zh-Hant" ? " · 日誌" : " · 日志"), caption);
        }
        string output = "\tline\r\n<xml> {data text} \u001b[31m";
        AssertEqual("[stdout] " + output, DesktopPresentationText.Format("LogStdout", new() { ["text"] = output }));
        AssertEqual("[stderr] " + output, DesktopPresentationText.Format("LogStderr", new() { ["text"] = output }));
        AssertEqual("0 B/s", TaskCenterController.FormatSpeed(0));
        AssertEqual("2.0 KB/s", TaskCenterController.FormatSpeed(2048));
        AssertEqual("1.0 GB/s", TaskCenterController.FormatSpeed(1024L * 1024 * 1024));
        bool rejected = false;
        try { _ = DesktopPresentationText.Format("LogStdout", new() { ["text"] = new string('x', 1024 * 1024 + 1) }); }
        catch (Nexa.Xsr.Renderer.Nxml.NxmlCompileException) { rejected = true; }
        AssertTrue(rejected);
        static int Count(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    }
    private static void OperationCopyRetainsTaskAndLogIdentityAcrossLanguageChanges()
    {
        var catalog = new UiLocalizationCatalog(); catalog.SetLanguage("zh-Hans");
        using (var fixture = new LaunchPageFixture(new ImmediateInstanceSource([])))
        {
            fixture.Shell.Renderer.TextLocalizer = catalog.Translate; fixture.Shell.Renderer.ReducedMotion = true;
            using var bubble = new DesktopTaskBubblePresenter(fixture.Shell, fixture.Store);
            using var runtime = TaskCenterRuntimeComposer.Compose(fixture.Foundation.Host);
            using var controller = new TaskCenterController(fixture.Shell, fixture.Intents, runtime.Commands, fixture.Store, bubble);
            using var task = fixture.Foundation.Host.Tasks.Begin(new("copy-locale", "literal {data secret}", ["first", "second"]));
            using var other = fixture.Foundation.Host.Tasks.Begin(new("copy-locale-other", "other", ["only"]));
            task.Report("first", "literal <metadata>", .5, 1, 2, 2048);
            Emit(fixture.Intents, "ui.tasks.open");
            var scene = fixture.Shell.Render(new(1280, 800));
            var action = controller.CardAction("copy-locale");
            XsrUiSceneNode CardNode(string key) => scene.Nodes.Single(node =>
                fixture.Shell.Tree.Name(node.Entity) == key && BelongsToCard(node.Entity));
            bool BelongsToCard(XsrUiEntityId entity)
            {
                while (entity.IsAssigned && fixture.Shell.Tree.IsAlive(entity))
                {
                    if (fixture.Shell.Tree.Name(entity) == "task-card:copy-locale") return true;
                    entity = fixture.Shell.Tree.Parent(entity);
                }
                return false;
            }
            var fill = CardNode("TaskCardFill").Entity;
            var progress = fixture.Shell.Tree.GetComponent<XsrUiProgress>(fill);
            fixture.Shell.Renderer.Focus(action, showIndicator: false);
            catalog.SetLanguage("en"); scene = fixture.Shell.Render(new(1280, 800));
            AssertEqual(action, controller.CardAction("copy-locale")); AssertEqual(action, fixture.Shell.Renderer.Focused);
            AssertTrue(ReferenceEquals(progress, fixture.Shell.Tree.GetComponent<XsrUiProgress>(fill)));
            AssertTrue(FindByKey(fixture.Shell, scene, "TaskCenterSummary").Text!.Contains("tasks in progress", StringComparison.Ordinal));
            AssertEqual("1/2 files", CardNode("TaskCardFiles").Text);
            AssertEqual("literal {data secret}", CardNode("TaskCardTitle").Text);
            catalog.SetLanguage("zh-Hant"); scene = fixture.Shell.Render(new(1280, 800));
            AssertEqual(action, controller.CardAction("copy-locale"));
            AssertEqual("1/2 個檔案", CardNode("TaskCardFiles").Text);
        }
        int reads = 0;
        Guid session = Guid.NewGuid();
        using var logRuntime = ProcessLogRuntime((q, _) =>
        {
            Interlocked.Increment(ref reads);
            return ValueTask.FromResult(XsrResult.Success(new MinecraftProcessOutputSnapshot(q.SessionId,
                "literal <metadata> {data percent}", MinecraftProcessState.Running, 1, 0,
                [new(1, MinecraftProcessOutputChannel.Stdout, "literal {state fake}")])));
        });
        using var logsFixture = new LaunchPageFixture(new ImmediateInstanceSource([]), logRuntime, ownsMinecraftRuntime: false);
        logsFixture.Shell.Renderer.TextLocalizer = catalog.Translate; logsFixture.Shell.Renderer.ReducedMotion = true;
        using var logs = new ProcessLogPageController(logsFixture.Shell, logsFixture.Intents, logRuntime.Queries, logsFixture.Store, logsFixture.Feedback);
        PublishLogSessions(logsFixture, new MinecraftProcessSnapshot(session, "literal <metadata> {data percent}", 42, MinecraftProcessState.Running, null, DateTimeOffset.UtcNow, null));
        logsFixture.Shell.Stage.Navigation.Push(logs.Page); logs.Open(session); logs.WaitUntilIdle().GetAwaiter().GetResult();
        var logScene = logsFixture.Shell.Render(new(1000, 700));
        var search = FindByKey(logsFixture.Shell, logScene, "ProcessLogSearch").Entity;
        logsFixture.Shell.Renderer.SetTextInputValue(search, "literal"); logsFixture.Shell.Renderer.Focus(search, showIndicator: false);
        logsFixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = logsFixture.Shell.Tree.GetComponent<XsrUiTextInput>(search)!;
        int caret = input.SelectionEnd;
        var row = FindByKey(logsFixture.Shell, logScene, "ProcessLogEntry:1").Entity;
        Emit(logsFixture.Intents, "ui.process.log.select", row); logsFixture.Shell.Render(new(1000, 700));
        catalog.SetLanguage("en"); logScene = logsFixture.Shell.Render(new(1000, 700));
        AssertEqual(1, reads); AssertEqual(row, FindByKey(logsFixture.Shell, logScene, "ProcessLogEntry:1").Entity);
        AssertTrue(logsFixture.Shell.Tree.GetComponent<XsrUiSelection>(row)!.IsSelected);
        AssertEqual(search, logsFixture.Shell.Renderer.Focused); AssertEqual("literal", input.ReadDraft()); AssertEqual(caret, input.SelectionEnd);
        AssertTrue(FindByKey(logsFixture.Shell, logScene, "ProcessLogStatus").Text!.Contains("1 selected", StringComparison.Ordinal));
        AssertTrue(FindByKey(logsFixture.Shell, logScene, "ProcessLogSession").Text!.Contains("literal <metadata> {data percent}", StringComparison.Ordinal));
        AssertEqual("[stdout] literal {state fake}", FindByKey(logsFixture.Shell, logScene, "ProcessLogEntry:1").Text);
    }
}
