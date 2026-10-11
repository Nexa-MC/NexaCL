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

}
