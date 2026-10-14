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
            string operationLiteral = "<operation> {data percent} {state secret}";
            string operationCaption = DesktopPresentationText.Format("ProcessLogsCaption",
                new() { ["instance"] = operationLiteral }, localization.Translate);
            if (operationCaption != operationLiteral + " · Logs"
                || DesktopPresentationText.Format("LogStdout", new() { ["text"] = operationLiteral }) != "[stdout] " + operationLiteral
                || TaskCenterController.FormatSpeed(2048) != "2.0 KB/s")
                throw new InvalidOperationException("External operation text lost literal data or unit format.");
            Console.WriteLine("External operation copy validation passed: localized metadata, literal raw output and external speed units.");
            Console.WriteLine("External localization validation passed: copied language catalog, English translation, no embedded fallback.");
            Console.WriteLine("Dynamic instance diagnostics validation passed: external launch/history assets, retained actions, owned rows and literal metadata.");
            Console.WriteLine("Dynamic runtime validation passed: external metrics, retained probe action, owned slots and literal trace format.");
            Console.WriteLine("Dynamic dock validation passed: overlay ownership, process template, literal text and selection reload.");
        }
        finally { shell.Tree.Destroy(page); }
    }

    private static void ValidateDynamicGlobalOverlays(XsrUiShell shell, Nexa.Xsr.State.XsrStateStore store,
        DesktopUiIntentSink intents, DesktopFeedbackService feedback)
    {
        bool reduced = shell.Renderer.ReducedMotion; shell.Renderer.ReducedMotion = true;
        using var palette = new DesktopCommandPalette(shell, intents, store,
            new Nexa.Xsr.Renderer.Backend.Avalonia.AvaloniaUiPlatformActions(), _ => throw new InvalidOperationException("Validation cannot navigate."));
        try
        {
            if (!palette.Open()) throw new InvalidOperationException("Palette cannot open during validation.");
            shell.Render(new(810, 470));
            var search = palette.Find("CommandPaletteSearch");
            shell.Renderer.SetTextInputValue(search, "settings");
            shell.Render(new(810, 470));
            if (shell.Tree.GetComponent<XsrUiTextInput>(search)?.ReadDraft() != "settings"
                || shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(palette.Find("CommandPalette"))) is null)
                throw new InvalidOperationException("Palette lost its owned workspace or search draft.");
            palette.Close();
            string literal = "literal <global> {data text} {state secret}";
            Guid notification = feedback.Info(literal); var scene = shell.Render(new(810, 470));
            var root = scene.Nodes.Single(node => shell.Tree.Name(node.Entity) == "notification:" + notification.ToString("N")).Entity;
            if (shell.Tree.GetComponent<XsrUiOverlayMotion>(root)?.Kind != XsrUiOverlayMotionKind.Notification
                || shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(root)) is null)
                throw new InvalidOperationException("Notification lost its declared motion or owned slot.");
            feedback.DismissNotification(notification); shell.Render(new(810, 470));
            bool? resolved = null;
            feedback.ShowDialog("global.validation", "settings", literal, "accept", "cancel", value => resolved = value,
                "alternate", static () => { }, localizeMessage: false);
            scene = shell.Render(new(810, 470));
            var layer = scene.Nodes.Single(node => shell.Tree.Name(node.Entity) == "DialogLayer").Entity;
            var viewport = scene.Nodes.Single(node => shell.Tree.Name(node.Entity) == "DialogMessageViewport").Entity;
            var body = scene.Nodes.Single(node => shell.Tree.Name(node.Entity) == "DialogMessage");
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(layer)) is null || body.Text != literal
                || shell.Tree.GetComponent<XsrUiScroll>(viewport)?.ShowsVerticalIndicator != true
                || !shell.Renderer.HandleKey(XsrUiKey.Escape) || resolved != false)
                throw new InvalidOperationException("Dialog lost literal body, owned modal scope or Escape routing.");
            shell.Render(new(810, 470));
            Console.WriteLine("Dynamic global overlay validation passed: owned modal/notification slots, retained palette draft, declared motion and literal dialog body.");
        }
        finally { palette.Close(); shell.Renderer.ReducedMotion = reduced; }
    }

    private static void ValidateDynamicInstanceTools(XsrUiShell shell, XsrUiEntityId slot, NxmlDynamicContent dynamic)
    {
        var data = new Dictionary<string, string>
        {
            ["config"] = "true",
            ["logs"] = "false",
            ["crash"] = "false",
            ["other"] = "false",
            ["configClass"] = "settings-catalog-action instance-tools-area instance-tools-area-selected",
            ["logsClass"] = "settings-catalog-action instance-tools-area",
            ["crashClass"] = "settings-catalog-action instance-tools-area",
            ["otherClass"] = "settings-catalog-action instance-tools-area",
            ["hasError"] = "false",
            ["error"] = "",
            ["loadingList"] = "false",
            ["loadingFile"] = "false",
            ["showList"] = "false",
            ["directory"] = "<literal> {data directory}",
            ["hasParent"] = "false",
            ["incomplete"] = "false",
            ["empty"] = "false",
            ["listPaging"] = "true",
            ["listPage"] = "1",
            ["listPages"] = "2",
            ["hasDocument"] = "true",
            ["editable"] = "true",
            ["manyLines"] = "false",
            ["textPaging"] = "true",
            ["textPage"] = "1",
            ["textPages"] = "2",
            ["shortLine"] = "true",
            ["longLine"] = "false",
            ["canPreview"] = "true",
            ["previewPending"] = "false"
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

    private static void ValidateDynamicVersionSelection(XsrUiShell shell, Nexa.Xsr.State.XsrStateStore store)
    {
        var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("VersionSelectionPage.nxml"), shell.Tree, store);
        var owner = new NxmlDynamicContent(shell.Tree, store);
        var nodes = new Dictionary<string, XsrUiEntityId>();
        void Index(XsrUiEntityId root) => shell.Tree.Walk(root, entity => { nodes[shell.Tree.Name(entity)] = entity; return true; });
        Index(page);
        string literal = "literal <version> {data text}";
        var workspace = owner.Append(nodes["VersionSelectionWorkspace"], DesktopMarkup.Dynamic("VersionSelectionWorkspace.d.nxml",
            new Dictionary<string, string> { ["path"] = literal, ["name"] = literal, ["named"] = "true", ["count"] = "2", ["status"] = "", ["empty"] = "false" })); Index(workspace);
        var popupData = new Dictionary<string, string> { ["visible"] = "false", ["height"] = "160", ["indicator"] = "false", ["renameVisible"] = "true", ["addVisible"] = "false" };
        var popup = owner.Append(nodes["VersionDirectoryWorkspace"], DesktopMarkup.Dynamic("VersionDirectoryDropdown.d.nxml", popupData)); Index(popup);
        var rowData = new Dictionary<string, string>
        {
            ["row"] = "validation-version",
            ["accessible"] = literal,
            ["selected"] = "true",
            ["current"] = "true",
            ["check"] = "validation-check",
            ["iconKey"] = "validation-icon",
            ["icon"] = VersionSelectionController.VersionIcon(Nexa.Services.Minecraft.MinecraftVersionKind.Fabric),
            ["nameKey"] = "validation-name",
            ["name"] = literal,
            ["detailKey"] = "validation-detail",
            ["detail"] = literal,
            ["modify"] = "validation-modify",
            ["settings"] = "validation-settings",
            ["delete"] = "validation-delete",
            ["style"] = "layout-VersionSelectionRow-LibraryRow version-base version-row version-selected version-row-last"
        };
        var row = owner.Append(nodes["LibraryVersionRows"], DesktopMarkup.Dynamic("VersionSelectionRow.d.nxml", rowData)); Index(row);
        shell.Renderer.SetTextInputValue(nodes["LibraryNameInput"], literal);
        rowData["current"] = "false"; owner.Reload(row, DesktopMarkup.Dynamic("VersionSelectionRow.d.nxml", rowData)); owner.Reorder(nodes["LibraryVersionRows"], [row]);
        popupData["indicator"] = "true"; owner.Reload(popup, DesktopMarkup.Dynamic("VersionDirectoryDropdown.d.nxml", popupData));
        if (shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(row)) is null
            || shell.Tree.GetComponent<XsrUiStableContent>(nodes["LibraryVersionRows"]) is null
            || shell.Tree.GetComponent<XsrUiText>(nodes["validation-name"]) is not { Localize: false } text || text.Content != literal
            || shell.Tree.GetComponent<XsrUiTextInput>(nodes["LibraryNameInput"])!.ReadDraft() != literal
            || shell.Tree.GetComponent<XsrUiScroll>(nodes["LibraryDirectoryRows"])?.ShowsVerticalIndicator != true
            || shell.Tree.GetComponent<XsrUiAnchoredOverlay>(nodes["LibraryDropdownCard"])?.Anchor != nodes["LibraryChooseDirectory"])
            throw new InvalidOperationException("Version selection lost owned rows, literal identity, editor draft or viewport/anchor facts.");
        owner.Remove(popup); owner.Remove(workspace); shell.Tree.Destroy(page);
        Console.WriteLine("Dynamic version selection validation passed: owned virtual rows, literal names, retained directory draft and local anchor/viewport facts.");
    }

    private static void ValidateDynamicBootstrap(XsrUiShell shell, Nexa.Xsr.State.XsrStateStore store)
    {
        var owner = new NxmlDynamicContent(shell.Tree, store);
        var account = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("LaunchPage.nxml"), shell.Tree, store);
        var setup = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("FirstRunPage.nxml"), shell.Tree, store);
        var titleShell = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("Shell.nxml"), shell.Tree, store);
        XsrUiEntityId Find(XsrUiEntityId root, string key)
        { XsrUiEntityId found = default; shell.Tree.Walk(root, node => { if (shell.Tree.Name(node) == key) found = node; return true; }); return found; }
        try
        {
            var home = owner.Append(Find(account, "LaunchHomeWorkspaceSlot"), DesktopMarkup.Dynamic("LaunchInstallHomeWorkspace.d.nxml",
                new Dictionary<string, string> { ["avatar"] = LaunchProfilePresentation.Avatar("") }));
            var form = owner.Append(Find(home, "AccountFormSlot"), DesktopMarkup.Dynamic("AccountFormWorkspace.d.nxml"));
            var input = Find(form, "AuthPassword"); var draft = shell.Tree.GetComponent<XsrUiTextInput>(input)!; shell.Renderer.SetTextInputValue(input, "fixture-secret");
            var choice = owner.Append(Find(form, "ImportCandidates"), DesktopMarkup.Dynamic("AccountFormChoice.d.nxml", new Dictionary<string, string>
            { ["key"] = "validation-account-choice", ["text"] = "literal <name> {state private}", ["label"] = "literal path" }));
            owner.Reload(form, DesktopMarkup.Dynamic("AccountFormWorkspace.d.nxml"));
            if (draft.ReadDraft() != "fixture-secret" || shell.Tree.GetComponent<XsrUiText>(choice)?.Content != "literal <name> {state private}")
                throw new InvalidOperationException("Account projection replaced the draft or reparsed literal metadata.");
            var status = new Nexa.Services.Setup.FirstRunStatus(true, "literal {state directory}", false) { TelemetryRequired = true };
            var fragment = owner.Append(shell.Tree.Children(setup).Single(), DesktopMarkup.Dynamic("FirstRunWorkspace.d.nxml", FirstRunController.Project(shell, status, status.DataDirectory, true, 0, false, false, "")));
            var transition = shell.Tree.GetComponent<XsrUiTransition>(Find(fragment, "SetupTitle"));
            for (int step = 1; step < 4; step++) owner.Reload(fragment, DesktopMarkup.Dynamic("FirstRunWorkspace.d.nxml", FirstRunController.Project(shell, status, status.DataDirectory, true, step, false, false, "")));
            if (!ReferenceEquals(transition, shell.Tree.GetComponent<XsrUiTransition>(Find(fragment, "SetupTitle")))
                || shell.Tree.GetComponent<XsrUiText>(Find(fragment, "SetupPath"))?.Content != status.DataDirectory)
                throw new InvalidOperationException("Setup projection replaced motion identity or reparsed a directory.");
            var logo = owner.Append(Find(titleShell, "CustomLauncherLogoSlot"), DesktopMarkup.Dynamic("CustomLauncherLogo.d.nxml", new Dictionary<string, string> { ["visible"] = "false", ["source"] = "" }));
            owner.Reload(logo, DesktopMarkup.Dynamic("CustomLauncherLogo.d.nxml", new Dictionary<string, string> { ["visible"] = "true", ["source"] = "" }));
            if (shell.Tree.GetComponent<XsrUiElement>(logo)?.IsVisible != true) throw new InvalidOperationException("Custom logo projection did not reload.");
            Console.WriteLine("Dynamic bootstrap validation passed: owned account/setup/logo fragments, retained secret draft and step motion, literal metadata.");
        }
        finally { shell.Tree.Destroy(account); shell.Tree.Destroy(setup); shell.Tree.Destroy(titleShell); }
    }

    private static void ValidateDynamicWardrobe(XsrUiShell shell, Nexa.Xsr.State.XsrStateStore store)
    {
        var owner = new NxmlDynamicContent(shell.Tree, store);
        var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("WardrobePage.nxml"), shell.Tree, store);
        var library = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("WardrobeLibraryPage.nxml"), shell.Tree, store);
        XsrUiEntityId Find(XsrUiEntityId root, string key)
        { XsrUiEntityId found = default; shell.Tree.Walk(root, node => { if (shell.Tree.Name(node) == key) found = node; return true; }); return found; }
        try
        {
            string literal = "literal <skin> {state private}";
            var mainData = DesktopWardrobeText.Defaults(library: false);
            var workspace = owner.Append(shell.Tree.Children(page).Single(), DesktopMarkup.Dynamic("WardrobeWorkspace.d.nxml", mainData));
            var card = new Nexa.Services.Accounts.AccountWardrobeCard("validation", literal, literal, new(null, null, false, null, null), true);
            var row = owner.Append(Find(workspace, "WardrobeSkins"), DesktopMarkup.Dynamic("WardrobeCard.d.nxml", WardrobePageController.ProjectCard(shell, card, WardrobePlayerView.Front, 220, 160)));
            var path = Find(workspace, "WardrobeSkinPath"); shell.Renderer.SetTextInputValue(path, literal);
            owner.Reload(row, DesktopMarkup.Dynamic("WardrobeCard.d.nxml", WardrobePageController.ProjectCard(shell, card, WardrobePlayerView.Back, 190, 270)));
            mainData["WardrobeLocalLayer_visible"] = "true"; owner.Reload(workspace, DesktopMarkup.Dynamic("WardrobeWorkspace.d.nxml", mainData));
            if (shell.Tree.GetComponent<XsrUiTextInput>(path)?.ReadDraft() != literal
                || shell.Tree.GetComponent<XsrUiScroll>(Find(workspace, "WardrobeSkins")) is not { ShowsHorizontalIndicator: true, UseVerticalWheelForHorizontalScroll: true }
                || shell.Tree.GetComponent<XsrUiOverlayLayer>(Find(workspace, "WardrobeLocalLayer"))?.IsModal != true)
                throw new InvalidOperationException("Wardrobe lost its owned draft, modal or horizontal scroll facts.");
            var body = owner.Append(shell.Tree.Children(library).Single(), DesktopMarkup.Dynamic("WardrobeLibraryWorkspace.d.nxml", DesktopWardrobeText.Defaults(library: true)));
            var items = Find(body, "WardrobeLibraryItems");
            var a = owner.Append(items, DesktopMarkup.Dynamic("WardrobeLibraryRow.d.nxml", new Dictionary<string, string> { ["key"] = "validation-row-a" }));
            var b = owner.Append(items, DesktopMarkup.Dynamic("WardrobeLibraryRow.d.nxml", new Dictionary<string, string> { ["key"] = "validation-row-b" }));
            var item = new Nexa.Services.Accounts.WardrobeCatalogItem(1, literal, literal, "alex", 1, false, "https://example.invalid/skin", new("https://example.invalid/details"), Nexa.Services.Accounts.WardrobeCatalogKind.Skin);
            var projected = WardrobeLibraryPageController.ProjectCard(shell, item, WardrobePlayerView.Front, true, false);
            var gallery = owner.Append(a, DesktopMarkup.Dynamic("WardrobeLibraryCard.d.nxml", projected)); var preview = Find(gallery, "WardrobeLibraryCard.1.Preview");
            owner.Move(gallery, b); owner.Reload(gallery, DesktopMarkup.Dynamic("WardrobeLibraryCard.d.nxml", projected)); owner.Reorder(b, [gallery]);
            if (shell.Tree.Parent(gallery) != b || preview != Find(gallery, "WardrobeLibraryCard.1.Preview")
                || shell.Tree.GetComponent<XsrUiText>(Find(gallery, "WardrobeLibraryCard.1.Name"))?.Content != literal)
                throw new InvalidOperationException("Wardrobe library reflow lost identity or reparsed metadata.");
            Console.WriteLine("Dynamic wardrobe validation passed: owned cards/modal, retained drafts, responsive orientation and library reflow.");
        }
        finally { shell.Tree.Destroy(page); shell.Tree.Destroy(library); }
    }

    private static void ValidateDynamicResources(XsrUiShell shell, Nexa.Xsr.State.XsrStateStore store, DesktopUiIntentSink intents)
    {
        string literal = "literal <resource> {state private}";
        var project = new Nexa.Services.Resources.ResourceProject("validation", literal, literal, literal, 1200, "https://modrinth.com/project/validation");
        var queries = new Nexa.Xsr.Runtime.XsrQueryRouterBuilder();
        int reads = 0;
        queries.Register<Nexa.Services.Resources.ResourceSearchQuery, Nexa.Services.Resources.ResourceSearchResult>(
            Nexa.Services.Resources.ResourceCatalogContract.Search, (_, _) =>
            { reads++; return ValueTask.FromResult(XsrResult.Success(new Nexa.Services.Resources.ResourceSearchResult([project], 1, 0))); });
        queries.Register<Nexa.Services.Resources.ResourceDetailQuery, Nexa.Services.Resources.ResourceDetail>(
            Nexa.Services.Resources.ResourceCatalogContract.Detail, (_, _) => ValueTask.FromResult(XsrResult.Success(new Nexa.Services.Resources.ResourceDetail(project, "MIT", []))));
        using var controller = new ResourcesPageController(shell, intents, queries.Build(new ValidationDispatchObserver()), store, _ => { });
        var previous = shell.Stage.Navigation.Current;
        try
        {
            shell.Stage.Navigation.Replace(controller.Page);
            for (int frame = 0; frame < 4; frame++) shell.Render(new(1100, 700));
            var search = controller.Find("ResourceSearch");
            shell.Renderer.SetTextInputValue(search, literal); shell.Renderer.Focus(search, showIndicator: false);
            shell.Renderer.EditText(XsrUiTextEdit.Left);
            var draft = shell.Tree.GetComponent<XsrUiTextInput>(search)!; int caret = draft.SelectionEnd;
            var list = controller.Find("ResourceList"); var scroll = shell.Tree.GetComponent<XsrUiScroll>(list);
            shell.Render(new(810, 500));
            var row = controller.Find("ResourceProject.validation");
            if (reads != 1 || !shell.Tree.IsAlive(row) || shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(row)) is null
                || shell.Renderer.Focused != search || draft.ReadDraft() != literal || draft.SelectionEnd != caret
                || !ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(list)))
                throw new InvalidOperationException("Resources lost owned rows, input identity or literal metadata.");
            intents.Emit(XsrSemanticId.Parse("ui.resources.action"), controller.Find("ResourceLoader"), XsrCorrelationId.Create());
            shell.Render(new(810, 500));
            var card = controller.Find("ResourceFilterCard"); var overlay = controller.Find("ResourceFilterOverlay");
            if (shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal != true
                || shell.Tree.GetComponent<XsrUiAnchoredOverlay>(card)?.Anchor != controller.Find("ResourceLoader"))
                throw new InvalidOperationException("Resource filter lost its declared modal/anchor contract.");
            intents.Emit(XsrSemanticId.Parse("ui.resources.action"), controller.Find("ResourceFilterDismiss"), XsrCorrelationId.Create());
            shell.Render(new(810, 500));
            intents.Emit(XsrSemanticId.Parse("ui.resources.action"), controller.Find("ResourceDetails.validation"), XsrCorrelationId.Create());
            for (int frame = 0; frame < 4; frame++) shell.Render(new(810, 500));
            if (shell.Stage.Navigation.Current != controller.DetailPage || !shell.Tree.IsAlive(controller.Find("ResourceDetailHeading")))
                throw new InvalidOperationException("Resource detail did not project through its owned slot.");
            Console.WriteLine("Dynamic resources validation passed: retained input/rows/scroll, literal metadata, modal filters and detail projection.");
        }
        finally
        {
            shell.Stage.Navigation.Replace(previous);
            controller.Dispose();
            if (shell.Tree.IsAlive(controller.Page)) shell.Tree.Destroy(controller.Page);
            if (shell.Tree.IsAlive(controller.DetailPage)) shell.Tree.Destroy(controller.DetailPage);
        }
    }

    private static async Task ValidateDynamicLaunchInstallAsync()
    {
        // This validates production controllers against bounded, typed in-memory service facts.
        // It does not contact a catalogue provider, launch Minecraft or open a native Store/browser.
        string directory = Path.Combine(Path.GetTempPath(), "nexa-nxml-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var uiRuntime = new XsrUiRuntimeContext();
            var observation = new Nexa.Services.Composition.XsrCompositeStateObserver(uiRuntime.StateBridge, null);
            using var host = Nexa.Services.Foundation.FoundationComposer.Compose(
                new Nexa.Services.Settings.InMemorySettingsPort(), Nexa.Services.Settings.LauncherDefaults.CreateSchema(),
                new LaunchInstallValidationProfiles(), observer: observation,
                declareHostState: LaunchPageState.DeclareState, minecraftRootDirectory: directory);
            var foundation = Nexa.Services.Composition.FoundationRuntimeComposer.Compose(host);
            using var minecraft = Nexa.Services.Composition.MinecraftRuntimeComposer.Compose();
            using var library = Nexa.Services.Composition.MinecraftLibraryRuntimeComposer.Compose(host, directory,
                new LaunchInstallValidationInstances());
            using var catalogs = Nexa.Services.Composition.InstallCatalogRuntimeComposer.Compose(host, new LaunchInstallValidationCatalog());
            var intents = new DesktopUiIntentSink();
            var shell = NxmlShellComposer.Compose(host.StateStore, uiRuntime, intentSink: intents);
            shell.Renderer.ReducedMotion = true;
            using var feedback = new DesktopFeedbackService();
            await using var controller = new LaunchPageController(shell, intents, minecraft, foundation.Commands,
                host.StateStore, library, feedback, installCatalogCommands: catalogs.Commands, installCatalogQueries: catalogs.Queries);
            using var subscription = observation.Subscribe(controller.StateObserver);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            controller.Attach();
            await controller.WaitUntilIdle().WaitAsync(timeout.Token).ConfigureAwait(false);
            var home = shell.Stage.Navigation.Current;
            ValidateLaunchInstallHomeAndBedrock(shell, host.StateStore, intents, feedback, home);
            var wide = new XsrUiSize(1100, 700);
            var narrow = new XsrUiSize(810, 500);
            intents.Emit(XsrSemanticId.Parse("ui.navigation.download"), default, XsrCorrelationId.Create());
            shell.Render(wide);
            var installRoot = shell.Stage.Navigation.Current;
            if (!shell.Renderer.Activate(FindLaunchInstallValidationNode(shell, installRoot, "InstallJavaChoice")))
                throw new InvalidOperationException("Java installation choice did not activate.");
            await controller.WaitUntilIdle().WaitAsync(timeout.Token).ConfigureAwait(false);
            var page = shell.Stage.Navigation.Current;
            shell.Render(wide);
            XsrUiEntityId Find(string key) => FindLaunchInstallValidationNode(shell, page, key);
            var workspace = Find("JavaInstallContent");
            var list = Find("JavaMinecraftVersions");
            var input = Find("JavaInstallVersionInput");
            var draft = shell.Tree.GetComponent<XsrUiTextInput>(input)!;
            var track = shell.Tree.GetComponent<XsrUiSegmentedTrack>(Find("JavaInstallPagerTabs"));
            var thumb = shell.Tree.GetComponent<XsrUiTransition>(Find("JavaInstallThumb"));
            var reveal = shell.Tree.GetComponent<XsrUiSegmentReveal>(Find("JavaFabricTab"));
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(workspace)) is null
                || shell.Tree.GetComponent<NxmlDynamicSlot>(list) is null
                || shell.Tree.GetComponent<XsrUiStableContent>(list) is null
                || track?.Thumb != Find("JavaInstallThumb") || thumb is null || reveal is null
                || draft.Placeholder != LaunchInstallText.Raw("input.search"))
                throw new InvalidOperationException("Java installation lost its owned workspace or declarative segment/input facts.");
            shell.Renderer.SetTextInputValue(input, "fixture-511");
            if (!shell.Renderer.Focus(input, showIndicator: false)) throw new InvalidOperationException("Search input cannot focus.");
            shell.Renderer.EditText(XsrUiTextEdit.Left, extendSelection: true);
            int caret = draft.SelectionEnd;
            shell.Render(wide);
            var filtered = Find("CatalogRow:game:fixture-511");
            if (shell.Tree.Parent(filtered) != list || shell.Tree.Children(list).Count != 1
                || draft.ReadDraft() != "fixture-511" || draft.SelectionEnd != caret || shell.Renderer.Focused != input)
                throw new InvalidOperationException("Catalogue search failed to retain its literal draft/caret or replace its owned window.");
            shell.Renderer.SetTextInputValue(input, "");
            shell.Render(wide);
            var selected = Find("CatalogRow:game:1.20.1");
            if (!shell.Renderer.Activate(selected)) throw new InvalidOperationException("Fixture game row did not activate.");
            await controller.WaitUntilIdle().WaitAsync(timeout.Token).ConfigureAwait(false);
            shell.Render(wide);
            if (selected != Find("CatalogRow:game:1.20.1")
                || shell.Tree.GetComponent<XsrUiSelection>(selected)?.IsSelected != true
                || !ReferenceEquals(draft, shell.Tree.GetComponent<XsrUiTextInput>(input))
                || draft.Placeholder != LaunchInstallText.Raw("input.name"))
                throw new InvalidOperationException("Choosing a game replaced a retained entity or failed to reproject its placeholder.");
            shell.Renderer.SetTextInputValue(input, "literal renamed <draft> {state private}");
            shell.Renderer.Focus(input, showIndicator: false);
            shell.Renderer.EditText(XsrUiTextEdit.Left, extendSelection: true);
            caret = draft.SelectionEnd;
            int anchor = draft.SelectionStart;
            var scrollEntity = Find("JavaMinecraftPage");
            var scroll = shell.Tree.GetComponent<XsrUiScroll>(scrollEntity)!;
            var scene = shell.Render(narrow);
            var scrollNode = scene.Nodes.Single(node => node.Entity == scrollEntity);
            if (scrollNode.Scroll is not { } scrolling || scrolling.ContentHeight < 25000
                || shell.Tree.Children(list).Count >= 30)
                throw new InvalidOperationException("Large catalogue did not retain its bounded virtual window and complete scroll extent.");
            if (!shell.Renderer.PointerScroll(new(scrollNode.Rect.X + 50, scrollNode.Rect.Y + 80), 10000))
                throw new InvalidOperationException("Catalogue viewport did not accept a real scroll gesture.");
            scene = shell.Render(narrow);
            if (!ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(scrollEntity)) || scroll.OffsetY <= 0
                || shell.Tree.Children(list).Count >= 30 || draft.SelectionEnd != caret || draft.SelectionStart != anchor)
                throw new InvalidOperationException("Virtual scroll replaced the scroll/input state or exceeded its row budget.");
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark);
                scene = shell.Render(narrow);
                var action = scene.Nodes.Single(node => node.Entity == Find("JavaInstallStart"));
                var pager = scene.Nodes.Single(node => node.Entity == Find("JavaInstallPager"));
                if (shell.Renderer.ColorScheme.IsDark != dark || action.Rect.Height <= 0 || action.Rect.Y < 0
                    || action.Rect.Y + action.Rect.Height > narrow.Height || pager.Rect.Height < 180
                    || shell.Renderer.Focused != input || draft.ReadDraft() != "literal renamed <draft> {state private}"
                    || draft.SelectionEnd != caret || draft.SelectionStart != anchor
                    || !ReferenceEquals(track, shell.Tree.GetComponent<XsrUiSegmentedTrack>(Find("JavaInstallPagerTabs")))
                    || !ReferenceEquals(thumb, shell.Tree.GetComponent<XsrUiTransition>(Find("JavaInstallThumb")))
                    || !ReferenceEquals(reveal, shell.Tree.GetComponent<XsrUiSegmentReveal>(Find("JavaFabricTab"))))
                    throw new InvalidOperationException("Theme/constrained layout lost interactive geometry, drafts or declarative segment state.");
            }
            intents.Emit(XsrSemanticId.Parse("ui.install.page.fabric"), Find("JavaFabricTab"), XsrCorrelationId.Create());
            await controller.WaitUntilIdle().WaitAsync(timeout.Token).ConfigureAwait(false);
            scene = shell.Render(narrow);
            var loader = Find("CatalogRow:loader:0.16.0");
            const string detail = "literal <loader> {data detail} {state private}";
            if (shell.Tree.Parent(loader) != Find("JavaFabricPageRows")
                || shell.Tree.GetComponent<NxmlDynamicSlot>(Find("JavaFabricPageRows")) is null
                || shell.Tree.GetComponent<XsrUiText>(Find("CatalogDetail:loader:0.16.0")) is not { Localize: false } loaderText
                || loaderText.Content != detail || !shell.Renderer.Activate(loader))
                throw new InvalidOperationException("Loader catalogue failed literal owned-row projection or selection.");
            scene = shell.Render(narrow);
            if (shell.Tree.GetComponent<XsrUiSelection>(loader)?.IsSelected != true
                || !ReferenceEquals(track, shell.Tree.GetComponent<XsrUiSegmentedTrack>(Find("JavaInstallPagerTabs")))
                || shell.Tree.GetComponent<XsrUiSegmentReveal>(Find("JavaFabricApiTab"))?.Expanded != true)
                throw new InvalidOperationException("Loader selection did not reveal its compatible addon with retained segment state.");
            var language = new UiLocalizationCatalog();
            language.SetLanguage("en");
            shell.Renderer.TextLocalizer = language.Translate;
            shell.Render(narrow);
            if (shell.Tree.GetComponent<XsrUiText>(Find("CatalogDetail:loader:0.16.0"))?.Content != detail
                || draft.ReadDraft() != "literal renamed <draft> {state private}"
                || draft.SelectionEnd != caret || draft.SelectionStart != anchor)
                throw new InvalidOperationException("Locale reload changed literal catalogue facts or editor draft state.");
            intents.Emit(XsrSemanticId.Parse("ui.page.back"), default, XsrCorrelationId.Create());
            shell.Render(narrow);
            if (shell.Stage.Navigation.Current != installRoot) throw new InvalidOperationException("Installation back navigation failed.");
            Console.WriteLine("NXML primitive segment validation passed: external local thumb/reveal facts retain track, transition and reveal objects through selection/theme/locale.");
            Console.WriteLine("Dynamic launch/install validation passed: actual controllers, controlled 512-game/loader fixtures, owned search/selection, changed placeholder, bounded scroll, retained draft/caret, both themes and 810x500 viewport; no provider/install/game effects.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static XsrUiEntityId FindLaunchInstallValidationNode(XsrUiShell shell, XsrUiEntityId root, string key)
    {
        XsrUiEntityId found = default;
        shell.Tree.Walk(root, entity => { if (shell.Tree.Name(entity) == key) found = entity; return true; });
        if (!found.IsAssigned) throw new InvalidOperationException("Missing launch/install validation element: " + key);
        return found;
    }

    private sealed class LaunchInstallValidationProfiles : Nexa.Services.Accounts.ILaunchProfilePort
    {
        private Nexa.Services.Accounts.LaunchProfileSet _profiles = new()
        {
            Profiles =
            [
                new() { Username = "literal <profile> {data name}", Info = "literal <provider> {state secret}", Kind = Nexa.Services.Accounts.LaunchProfileKind.Offline },
                new() { Username = "FixturePlayer", Kind = Nexa.Services.Accounts.LaunchProfileKind.Offline },
            ],
        };
        public Nexa.Services.Accounts.LaunchProfileSet Load() => _profiles;
        public void Save(Nexa.Services.Accounts.LaunchProfileSet profiles) => _profiles = profiles;
    }

    private sealed class LaunchInstallValidationInstances : Nexa.Services.Minecraft.IMinecraftInstanceSource
    {
        public ValueTask<IReadOnlyList<Nexa.Services.Minecraft.MinecraftInstanceDescriptor>> DiscoverAsync(string minecraftRootDirectory,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Nexa.Services.Minecraft.MinecraftInstanceDescriptor>>([]);
    }

    private sealed class LaunchInstallValidationCatalog : Nexa.Services.Minecraft.Install.IInstallCatalogSource
    {
        public Task<IReadOnlyList<Nexa.Services.Minecraft.Install.InstallCatalogVersion>> GetGamesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Nexa.Services.Minecraft.Install.InstallCatalogVersion>>(Enumerable.Range(0, 512)
                .Select(index => new Nexa.Services.Minecraft.Install.InstallCatalogVersion(index == 0 ? "1.20.1" : "fixture-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), "fixture")).ToArray());
        public Task<IReadOnlyList<Nexa.Services.Minecraft.Install.InstallCatalogVersion>> GetLoadersAsync(
            Nexa.Services.Minecraft.Install.InstallLoader loader, string game, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Nexa.Services.Minecraft.Install.InstallCatalogVersion>>([new("0.16.0", "literal <loader> {data detail} {state private}")]);
    }

    private sealed class ValidationDispatchObserver : Nexa.Xsr.Runtime.IXsrDispatchObserver
    {
        public void OnCompleted(Nexa.Xsr.Runtime.XsrDispatchObservation observation) { }
    }
}
