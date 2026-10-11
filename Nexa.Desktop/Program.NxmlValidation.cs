using Nexa.Desktop.Ui;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static async Task ValidateDynamicNetworkPageAsync(XsrUiShell shell, SettingsPageController settings, DesktopUiIntentSink intents)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await settings.PrepareStartupAsync(timeout.Token).ConfigureAwait(false);
        var previous = shell.Stage.Navigation.Current;
        shell.Stage.Navigation.Replace(settings.Page);
        shell.Render(new(1280, 800));
        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav.network"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        if (settings.SelectedSection != "network") throw new InvalidOperationException("Network validation did not navigate.");
        await settings.PrepareVisibleStartupAsync(timeout.Token).ConfigureAwait(false);
        shell.Render(new(1280, 800));
        intents.Emit(XsrSemanticId.Parse("ui.settings.proxy.mode"), Find("SettingsProxyMode.2"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(Find("SettingsProxyHint")) is null
            || shell.Tree.GetComponent<XsrUiElement>(Find("SettingsProxyManual"))?.IsVisible == false
            || shell.Tree.GetComponent<XsrUiTextInput>(Find("SettingsInput.network.proxy-password"))?.IsPassword != true)
            throw new InvalidOperationException("Dynamic proxy template validation failed.");
        Console.WriteLine("Dynamic network validation passed: external fragment, slot, manual protocol and protected password input.");
        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav.game"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        await settings.PrepareVisibleStartupAsync(timeout.Token).ConfigureAwait(false);
        shell.Render(new(1280, 800));
        var selector = Find("SettingsSelector.game.window-mode");
        var argument = Find("SettingsArgument.game.arguments.0");
        shell.Renderer.SetTextInputValue(argument, "--demo");
        intents.Emit(XsrSemanticId.Parse("ui.settings.argument.add"), Find("SettingsArgumentAdd.game.arguments"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        var hook = Find("SettingsHookLine.game.pre-launch.0");
        shell.Renderer.SetTextInputValue(hook, "fixture\targument");
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(selector) is null
            || shell.Tree.GetComponent<XsrUiSegmentedTrack>(selector) is null
            || shell.Tree.GetComponent<XsrUiTextInput>(Find("SettingsArgument.game.arguments.0"))?.ReadDraft() != "--demo"
            || shell.Tree.GetComponent<XsrUiTextInput>(Find("SettingsArgument.game.arguments.1")) is null
            || shell.Tree.GetComponent<XsrUiTextInput>(hook)?.ReadDraft() != "fixture\targument")
            throw new InvalidOperationException("Dynamic settings editor validation failed.");
        Console.WriteLine("Dynamic settings validation passed: selector slot, argument add/draft and lossless command tabs; no settings were saved.");
        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav.java"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        var inventoryBody = Find("SettingsJavaInventory.Body");
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(inventoryBody) is null)
            throw new InvalidOperationException("Java inventory did not use its external dynamic body.");
        shell.Tree.Walk(settings.Page, entity =>
        {
            string key = shell.Tree.Name(entity);
            if (key.StartsWith("JavaDiagnostics", StringComparison.Ordinal) || key.StartsWith("JavaManual", StringComparison.Ordinal)
                || key.StartsWith("JavaPolicy", StringComparison.Ordinal))
                throw new InvalidOperationException("Retired Java panels reappeared.");
            return true;
        });
        Console.WriteLine("Dynamic Java validation passed: external inventory header and owned body slot; no registry changes.");
        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav.about"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        if (!Find("AboutCopyright").IsAssigned || !Find("AboutOpenSource").IsAssigned || !Find("AboutThanks").IsAssigned)
            throw new InvalidOperationException("External about content did not load.");
        Console.WriteLine("Dynamic about validation passed: external copyright, license and acknowledgements.");
        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav.storage"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(Find("SettingsStorageDirectories.Body")) is null
            || shell.Tree.GetComponent<XsrUiCommandBinding>(Find("SettingsStorageCleanTemporary")) is null
            || shell.Tree.GetComponent<XsrUiCommandBinding>(Find("SettingsImport")) is null
            || shell.Tree.GetComponent<XsrUiCommandBinding>(Find("SettingsReset")) is null)
            throw new InvalidOperationException("External storage or settings transfer controls did not load.");
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(Find("ContentWorkspace.Backups")) is null
            || shell.Tree.GetComponent<NxmlDynamicSlot>(Find("DiagnosticAi.Editor")) is null
            || shell.Tree.GetComponent<XsrUiCommandBinding>(Find("ContentWorkspace.Migrate")) is null)
            throw new InvalidOperationException("External backup or diagnostic workspaces did not load.");
        Console.WriteLine("Dynamic storage validation passed: owned directory, backup and AI slots, migration, cleanup and transfer controls; no filesystem changes.");
        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav.general"), XsrCorrelationId.Create());
        shell.Render(new(1280, 800));
        if (shell.Tree.GetComponent<XsrUiText>(Find("SettingsGeneral.Protocol.Details"))?.Localize != false)
            throw new InvalidOperationException("Protocol observation details lost literal presentation.");
        Console.WriteLine("Dynamic general validation passed: external protocol observation with literal details.");
        shell.Stage.Navigation.Replace(previous);

        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId found = default;
            shell.Tree.Walk(settings.Page, entity => { if (shell.Tree.Name(entity) == key) found = entity; return true; });
            if (!found.IsAssigned) throw new InvalidOperationException("Missing dynamic network element: " + key);
            return found;
        }
    }
    private static void ValidateDynamicTaskPage(XsrUiShell shell, TaskCenterController controller,
        Nexa.Services.Tasks.TaskCenterService tasks, DesktopUiIntentSink intents)
    {
        var previous = shell.Stage.Navigation.Current;
        string id = "nxml-smoke:" + Guid.NewGuid().ToString("N");
        using var task = tasks.Begin(new(id, "动态模板验证", ["读取", "写入"]));
        try
        {
            task.Report("写入", "验证", 0.25, 1, 4, 0);
            intents.Emit(XsrSemanticId.Parse("ui.tasks.open"), default, XsrCorrelationId.Create());
            shell.Render(new(1280, 800));
            XsrUiEntityId card = default, details = default, fill = default;
            shell.Tree.Walk(controller.Page, entity =>
            {
                if (shell.Tree.Name(entity) == "task-card:" + id) card = entity;
                return true;
            });
            if (!card.IsAssigned) throw new InvalidOperationException("Dynamic task card did not load.");
            shell.Tree.Walk(card, entity =>
            {
                if (shell.Tree.Name(entity) == "TaskCardDetails") details = entity;
                if (shell.Tree.Name(entity) == "TaskCardFill") fill = entity;
                return true;
            });
            intents.Emit(XsrSemanticId.Parse("ui.tasks.card.details"), details, XsrCorrelationId.Create());
            shell.Render(new(1280, 800));
            var progress = shell.Tree.GetComponent<XsrUiProgress>(fill);
            task.Report("写入", "更新", 0.75, 3, 4, 0);
            shell.Render(new(1280, 800));
            if (!shell.Tree.IsAlive(card) || progress?.Target != 0.75
                || !ReferenceEquals(progress, shell.Tree.GetComponent<XsrUiProgress>(fill)))
                throw new InvalidOperationException("Dynamic task reload lost retained state.");
            Console.WriteLine("Dynamic task validation passed: card, nested steps, retained progress and live reload.");
        }
        finally
        {
            task.Complete(); tasks.Dismiss(id);
            shell.Stage.Navigation.Replace(previous);
        }
    }

    private static void ValidateDynamicDock(XsrUiShell shell, Nexa.Xsr.State.XsrStateStore store)
    {
        using var layer = new DesktopDynamicLayer(shell, store, "ShellBubbleLayer.nxml");
        var process = layer.Append("ProcessStopBubble.d.nxml", new Dictionary<string, string>
        { ["key"] = "validation-power", ["command"] = "ui.validation.power", ["caption"] = "验证进程" });
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(process)) is null)
            throw new InvalidOperationException("The process bubble left its dynamic owner.");
        var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("ProcessLogPage.nxml"), shell.Tree, store);
        try
        {
            var dynamic = new NxmlDynamicContent(shell.Tree, store);
            var data = new Dictionary<string, string>
            { ["key"] = "validation-log", ["text"] = "[stdout] {state literal}", ["selected"] = "false", ["style"] = "process-log-row" };
            var row = dynamic.Append(shell.Tree.Children(page).Single(), DesktopMarkup.Dynamic("ProcessLogRow.d.nxml", data));
            data["selected"] = "true";
            dynamic.Reload(row, DesktopMarkup.Dynamic("ProcessLogRow.d.nxml", data));
            if (shell.Tree.GetComponent<XsrUiSelection>(row)?.IsSelected != true
                || shell.Tree.GetComponent<XsrUiText>(row)?.Localize != false
                || shell.Tree.GetComponent<XsrUiText>(row)?.Content != data["text"])
                throw new InvalidOperationException("Literal log row projection failed.");
            var inspector = dynamic.Append(shell.Tree.Children(page).Single(), DesktopMarkup.Dynamic("SettingsStateInspector.d.nxml",
                new Dictionary<string, string> { ["previous"] = "false", ["next"] = "true" }));
            XsrUiEntityId body = default;
            shell.Tree.Walk(inspector, entity => { if (shell.Tree.Name(entity) == "SettingsDiagnostics.State.Body") body = entity; return true; });
            var literal = "<state> {data count} {state secret}";
            var entry = dynamic.Append(body, DesktopMarkup.Dynamic("SettingsStateEntry.d.nxml", new Dictionary<string, string>
            {
                ["row"] = "diagnostic-fixture",
                ["idKey"] = "diagnostic-id",
                ["metadataKey"] = "diagnostic-metadata",
                ["id"] = "fixture",
                ["owner"] = literal,
                ["kind"] = "Cell",
                ["revision"] = "1",
                ["availability"] = "Available"
            }, shell.Renderer.LocalizeText));
            XsrUiEntityId metadata = default;
            shell.Tree.Walk(entry, entity => { if (shell.Tree.Name(entity) == "diagnostic-metadata") metadata = entity; return true; });
            if (shell.Tree.GetComponent<XsrUiText>(metadata) is not { Localize: false } text || !text.Content.Contains(literal, StringComparison.Ordinal))
                throw new InvalidOperationException("Diagnostic format did not preserve literal metadata.");
            Console.WriteLine("Dynamic diagnostic validation passed: external inspector, named text format and literal metadata.");
            var group = dynamic.Append(shell.Tree.Children(page).Single(), DesktopMarkup.Dynamic("SettingsPlatformGroup.d.nxml",
                new Dictionary<string, string> { ["group"] = "platform-fixture", ["card"] = "platform-card", ["body"] = "platform-body", ["title"] = "fixture" }));
            XsrUiEntityId platformBody = default;
            shell.Tree.Walk(group, entity => { if (shell.Tree.Name(entity) == "platform-body") platformBody = entity; return true; });
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(platformBody) is null)
                throw new InvalidOperationException("Platform group lost its owned body.");
            var platformRow = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsPlatformCapability.d.nxml", new Dictionary<string, string>
            {
                ["row"] = "platform-fact",
                ["label"] = "fixture",
                ["badgeKey"] = "platform-badge",
                ["badge"] = literal,
                ["badgeStyle"] = "platform-badge",
                ["evidence"] = "fixture"
            }));
            XsrUiEntityId badge = default;
            shell.Tree.Walk(platformRow, entity => { if (shell.Tree.Name(entity) == "platform-badge") badge = entity; return true; });
            if (shell.Tree.GetComponent<XsrUiText>(badge) is not { Localize: false } badgeText || badgeText.Content != literal
                || shell.Tree.GetComponent<XsrUiVisualStyle>(badge)?.TextAlignment != XsrUiTextAlignment.Center)
                throw new InvalidOperationException("Platform badge lost literal or centered presentation.");
            var device = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsPlatformDeviceAvailable.d.nxml",
                new Dictionary<string, string> { ["name"] = literal }, shell.Renderer.LocalizeText));
            if (shell.Tree.GetComponent<XsrUiText>(device) is not { Localize: false } deviceText || !deviceText.Content.Contains(literal, StringComparison.Ordinal))
                throw new InvalidOperationException("Platform device format reparsed literal input.");
            var report = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsPlatformPreflight.d.nxml",
                new Dictionary<string, string> { ["severity"] = "fixture" }, shell.Renderer.LocalizeText));
            XsrUiEntityId issues = default;
            shell.Tree.Walk(report, entity => { if (shell.Tree.Name(entity) == "PlatformPreflightIssues") issues = entity; return true; });
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(issues) is null)
                throw new InvalidOperationException("Preflight issues lost their dynamic owner.");
            Console.WriteLine("Dynamic platform validation passed: owned group/issues slots, centered literal badge and named device format.");
            var runtime = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsRuntimeScheduler.d.nxml"));
            XsrUiEntityId runtimeBody = default;
            shell.Tree.Walk(runtime, entity => { if (shell.Tree.Name(entity) == "SettingsRuntime.Scheduler.Body") runtimeBody = entity; return true; });
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(runtimeBody) is null)
                throw new InvalidOperationException("Runtime metrics lost their owned fields.");
            var fields = dynamic.Append(runtimeBody, DesktopMarkup.Dynamic("SettingsRuntimeSchedulerFields.d.nxml", new Dictionary<string, string>
            {
                ["PendingState"] = "0",
                ["ActiveTracks"] = "0",
                ["ClockRunning"] = literal,
                ["TargetRate"] = "60",
                ["ObservedRate"] = literal,
                ["MotionPolicy"] = literal
            }));
            dynamic.Reload(fields, DesktopMarkup.Dynamic("SettingsRuntimeSchedulerFields.d.nxml", new Dictionary<string, string>
            {
                ["PendingState"] = "1",
                ["ActiveTracks"] = "0",
                ["ClockRunning"] = literal,
                ["TargetRate"] = "60",
                ["ObservedRate"] = literal,
                ["MotionPolicy"] = literal
            }));
            var advanced = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsAdvancedDelay.d.nxml", new Dictionary<string, string> { ["enabled"] = "true" }));
            XsrUiEntityId advancedStatus = default, probe = default;
            shell.Tree.Walk(advanced, entity =>
            {
                if (shell.Tree.Name(entity) == "AdvancedWorkspace.Delay.Status") advancedStatus = entity;
                if (shell.Tree.Name(entity) == "AdvancedWorkspace.Delay.Start") probe = entity;
                return true;
            });
            dynamic.Append(advancedStatus, DesktopMarkup.Dynamic("SettingsAdvancedDelayLoading.d.nxml"));
            dynamic.Reload(advanced, DesktopMarkup.Dynamic("SettingsAdvancedDelay.d.nxml", new Dictionary<string, string> { ["enabled"] = "false" }));
            if (!shell.Tree.IsAlive(probe) || shell.Tree.GetComponent<XsrUiInput>(probe)?.Enabled != false)
                throw new InvalidOperationException("Probe action lost identity during reload.");
            var trace = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsRuntimeTraceEntry.d.nxml", new Dictionary<string, string>
            {
                ["row"] = "trace-fixture",
                ["identityKey"] = "trace-identity",
                ["detailKey"] = "trace-detail",
                ["kind"] = "Operation",
                ["semantic"] = "minecraft.fixture",
                ["detail"] = literal,
                ["correlation"] = "fixture",
                ["ticks"] = "1",
                ["result"] = "Success"
            }, shell.Renderer.LocalizeText));
            XsrUiEntityId traceDetail = default;
            shell.Tree.Walk(trace, entity => { if (shell.Tree.Name(entity) == "trace-detail") traceDetail = entity; return true; });
            if (shell.Tree.GetComponent<XsrUiText>(traceDetail) is not { Localize: false } traceText || !traceText.Content.StartsWith(literal, StringComparison.Ordinal))
                throw new InvalidOperationException("Trace format reparsed literal metadata.");
            var launchData = new Dictionary<string, string>
            {
                ["readEnabled"] = "true",
                ["status"] = literal,
                ["showStatus"] = "true",
                ["hasFacts"] = "true",
                ["paging"] = "true",
                ["page"] = "1",
                ["pages"] = "3",
                ["previousEnabled"] = "false",
                ["nextEnabled"] = "true"
            };
            foreach (string tab in new[] { "plan", "classpath", "natives", "manifest", "metadata", "lockfile", "trace" })
            {
                launchData[tab + "Style"] = "settings-catalog-action instance-launch-tab";
                launchData[tab + "Selected"] = tab == "plan" ? "true" : "false";
            }
            var launchFacts = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsInstanceLaunchFacts.d.nxml", launchData, shell.Renderer.LocalizeText));
            XsrUiEntityId launchRows = default, readFacts = default;
            shell.Tree.Walk(launchFacts, entity =>
            {
                if (shell.Tree.Name(entity) == "AdvancedLaunch.Rows") launchRows = entity;
                if (shell.Tree.Name(entity) == "AdvancedLaunch.Read") readFacts = entity;
                return true;
            });
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(launchRows) is null)
                throw new InvalidOperationException("Instance launch facts lost their owned row slot.");
            var documentLine = dynamic.Append(launchRows, DesktopMarkup.Dynamic("SettingsInstanceLaunchDocument.d.nxml",
                new Dictionary<string, string> { ["path"] = literal, ["status"] = literal }, shell.Renderer.LocalizeText));
            launchData["readEnabled"] = "false"; launchData["page"] = "2";
            dynamic.Reload(launchFacts, DesktopMarkup.Dynamic("SettingsInstanceLaunchFacts.d.nxml", launchData, shell.Renderer.LocalizeText));
            if (!shell.Tree.IsAlive(readFacts) || shell.Tree.GetComponent<XsrUiInput>(readFacts)?.Enabled != false
                || !shell.Tree.IsAlive(documentLine) || shell.Tree.Parent(documentLine) != launchRows
                || shell.Tree.GetComponent<XsrUiText>(documentLine) is not { Localize: false } documentText
                || !documentText.Content.Contains(literal, StringComparison.Ordinal))
                throw new InvalidOperationException("Instance launch reload lost input identity, owned rows or literal document metadata.");
            var historyData = new Dictionary<string, string>
            {
                ["pending"] = "false",
                ["hasError"] = "false",
                ["error"] = "",
                ["showStatus"] = "false",
                ["LaunchVisible"] = "true",
                ["CrashVisible"] = "true",
                ["OperationVisible"] = "false",
                ["LaunchPaging"] = "true",
                ["CrashPaging"] = "false",
                ["OperationPaging"] = "false",
                ["LaunchPage"] = "1",
                ["CrashPage"] = "1",
                ["OperationPage"] = "1",
                ["LaunchPages"] = "2",
                ["CrashPages"] = "1",
                ["OperationPages"] = "1"
            };
            var history = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsInstanceHistory.d.nxml", historyData, shell.Renderer.LocalizeText));
            XsrUiEntityId historyNext = default;
            shell.Tree.Walk(history, entity => { if (shell.Tree.Name(entity) == "InstanceDiagnostics.LaunchHistory.Next") historyNext = entity; return true; });
            historyData["LaunchPage"] = "2";
            dynamic.Reload(history, DesktopMarkup.Dynamic("SettingsInstanceHistory.d.nxml", historyData, shell.Renderer.LocalizeText));
            if (!shell.Tree.IsAlive(historyNext))
                throw new InvalidOperationException("History pagination lost action identity.");
            var systemEvent = dynamic.Append(platformBody, DesktopMarkup.Dynamic("SettingsInstanceSystemEvent.d.nxml",
                new Dictionary<string, string> { ["message"] = literal }));
            if (shell.Tree.GetComponent<XsrUiText>(systemEvent) is not { Localize: false, MaxLines: 4 } eventText || eventText.Content != literal)
                throw new InvalidOperationException("Bounded system event lost literal text semantics.");
            ValidateDynamicInstanceTools(shell, platformBody, dynamic);
            var localization = new UiLocalizationCatalog();
            localization.SetLanguage("en");
            if (localization.Translate("设置") != "Settings"
                || typeof(UiLocalizationCatalog).Assembly.GetManifestResourceNames().Contains("Nexa.Desktop.Ui.Localization.messages.json", StringComparer.Ordinal))
                throw new InvalidOperationException("Published language bundle is missing or still embedded.");
            Console.WriteLine("External localization validation passed: copied language catalog, English translation, no embedded fallback.");
            Console.WriteLine("Dynamic instance diagnostics validation passed: external launch/history assets, retained actions, owned rows and literal metadata.");
            Console.WriteLine("Dynamic runtime validation passed: external metrics, retained probe action, owned slots and literal trace format.");
            Console.WriteLine("Dynamic dock validation passed: overlay ownership, process template, literal text and selection reload.");
        }
        finally { shell.Tree.Destroy(page); }
    }

    private static void ValidateDynamicInstanceTools(XsrUiShell shell, XsrUiEntityId slot, NxmlDynamicContent dynamic)
    {
        var data = new Dictionary<string, string>
        {
            ["config"] = "true", ["logs"] = "false", ["crash"] = "false", ["other"] = "false",
            ["configClass"] = "settings-catalog-action instance-tools-area instance-tools-area-selected",
            ["logsClass"] = "settings-catalog-action instance-tools-area",
            ["crashClass"] = "settings-catalog-action instance-tools-area",
            ["otherClass"] = "settings-catalog-action instance-tools-area",
            ["hasError"] = "false", ["error"] = "", ["loadingList"] = "false", ["loadingFile"] = "false",
            ["showList"] = "false", ["directory"] = "<literal> {data directory}", ["hasParent"] = "false",
            ["incomplete"] = "false", ["empty"] = "false", ["listPaging"] = "true", ["listPage"] = "1", ["listPages"] = "2",
            ["hasDocument"] = "true", ["editable"] = "true", ["manyLines"] = "false",
            ["textPaging"] = "true", ["textPage"] = "1", ["textPages"] = "2",
            ["shortLine"] = "true", ["longLine"] = "false", ["canPreview"] = "true", ["previewPending"] = "false"
        };
        var root = dynamic.Append(slot, DesktopMarkup.Dynamic("SettingsInstanceFiles.d.nxml", data, shell.Renderer.LocalizeText));
        XsrUiEntityId editor = default, rows = default, action = default;
        shell.Tree.Walk(root, entity =>
        {
            if (shell.Tree.Name(entity) == "FileWorkspaceLineEditor") editor = entity;
            if (shell.Tree.Name(entity) == "FileWorkspace.Lines") rows = entity;
            if (shell.Tree.Name(entity) == "FileWorkspace.Preview") action = entity;
            return true;
        });
        string literal = "\tvalue=<state> {data secret}";
        shell.Renderer.SetTextInputValue(editor, literal);
        var line = dynamic.Append(rows, DesktopMarkup.Dynamic("SettingsInstanceFileLine.d.nxml",
            new Dictionary<string, string> { ["row"] = "validation-file-line", ["number"] = "1", ["content"] = literal }));
        data["textPage"] = "2"; data["canPreview"] = "false"; data["previewPending"] = "true";
        dynamic.Reload(root, DesktopMarkup.Dynamic("SettingsInstanceFiles.d.nxml", data, shell.Renderer.LocalizeText));
        if (shell.Tree.GetComponent<XsrUiTextInput>(editor) is not { PreserveTabs: true, MaximumLength: 32768 } input
            || input.ReadDraft() != literal || !shell.Tree.IsAlive(line) || shell.Tree.Parent(line) != rows
            || shell.Tree.GetComponent<NxmlDynamicSlot>(rows) is null || shell.Tree.GetComponent<XsrUiInput>(action)?.Enabled != false
            || shell.Tree.GetComponent<XsrUiText>(line) is not { Localize: false } text || text.Content != "1  " + literal)
            throw new InvalidOperationException("External file tools lost input identity, tabs, ownership or literal data.");
        var language = new UiLocalizationCatalog(); language.SetLanguage("en");
        var caption = DesktopMarkup.Dynamic("SettingsInstanceFileEditableCaption.d.nxml",
            new Dictionary<string, string> { ["path"] = literal, ["encoding"] = "utf8" }, language.Translate).Root.Content;
        if (caption != literal + " · utf8 · Editable configuration")
            throw new InvalidOperationException("File metadata must localize before literal substitution.");
        var development = dynamic.Append(slot, DesktopMarkup.Dynamic("SettingsInstanceDevelopment.d.nxml"));
        XsrUiEntityId control = default;
        shell.Tree.Walk(development, entity => { if (shell.Tree.Name(entity) == "InstanceDevelopmentMode.Control") control = entity; return true; });
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(control) is null)
            throw new InvalidOperationException("Development control lost its dynamic owner.");
        var timeline = dynamic.Append(slot, DesktopMarkup.Dynamic("SettingsInstanceTimeline.d.nxml", new Dictionary<string, string>
        { ["incomplete"] = "false", ["empty"] = "false", ["hasPoints"] = "true", ["more"] = "false", ["hasComparison"] = "false", ["changes"] = "0" }));
        XsrUiEntityId points = default;
        shell.Tree.Walk(timeline, entity => { if (shell.Tree.Name(entity) == "RecoveryTimeline.Rows") points = entity; return true; });
        dynamic.Append(points, DesktopMarkup.Dynamic("SettingsInstanceTimelineDeltaBaseline.d.nxml", new Dictionary<string, string>
        { ["row"] = "validation-timeline", ["time"] = literal, ["files"] = "2", ["size"] = "4 MiB", ["delta"] = "+1" }, language.Translate));
        Console.WriteLine("Dynamic instance tools validation passed: retained editor/tabs, literal localized metadata and owned file/development/timeline slots.");
    }

}
