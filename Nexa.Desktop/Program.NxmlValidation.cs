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
                ["row"] = "diagnostic-fixture", ["idKey"] = "diagnostic-id", ["metadataKey"] = "diagnostic-metadata", ["id"] = "fixture",
                ["owner"] = literal, ["kind"] = "Cell", ["revision"] = "1", ["availability"] = "Available"
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
                ["row"] = "platform-fact", ["label"] = "fixture", ["badgeKey"] = "platform-badge", ["badge"] = literal,
                ["badgeStyle"] = "platform-badge", ["evidence"] = "fixture"
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
            Console.WriteLine("Dynamic dock validation passed: overlay ownership, process template, literal text and selection reload.");
        }
        finally { shell.Tree.Destroy(page); }
    }

}
