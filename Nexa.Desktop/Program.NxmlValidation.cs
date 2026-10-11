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
            Console.WriteLine("Dynamic dock validation passed: overlay ownership, process template, literal text and selection reload.");
        }
        finally { shell.Tree.Destroy(page); }
    }

}
