using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void StartupFailureLogLinkFollowsStageAndNeedsExplicitClick()
    {
        int opens = 0;
        using var card = new AvaloniaUiStartupCard(static text => text, openLog: () => opens++);
        var link = card.GetLogicalDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == "StartupLogLink");
        string path = Path.Combine(Path.GetTempPath(), "20261023T0400000000000Z.log");
        card.SetFailureLog(path);
        card.SetStage("loading", false, false);
        AssertFalse(link.IsVisible);
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertEqual(0, opens);
        card.SetStage("failed", true, true);
        AssertTrue(link.IsVisible && link.IsEnabled);
        AssertEqual(0, opens);
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertEqual(1, opens);
        card.Relocalize(text => text == "查看日志" ? "View log" : text);
        AssertEqual("View log：" + Path.GetFileName(path), ((TextBlock)link.Content!).Text);
        card.SetFailureLog(null);
        AssertFalse(link.IsEnabled);
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertEqual(1, opens);
        AssertEqual("日志文件不可用", ((TextBlock)link.Content!).Text);
    }
}
