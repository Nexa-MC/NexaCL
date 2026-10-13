using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void GlobalOverlaysRetainOwnedSlotsFocusDraftsAndMotion()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell; shell.Renderer.ReducedMotion = true;
        var catalog = new UiLocalizationCatalog(); catalog.SetLanguage("zh-Hans"); shell.Renderer.TextLocalizer = catalog.Translate;
        var platform = new AvaloniaUiPlatformActions();
        using var palette = new DesktopCommandPalette(shell, fixture.Intents, fixture.Store, platform, _ => throw new InvalidOperationException("Unexpected route"));
        AssertTrue(palette.Open()); var scene = shell.Render(new(810, 470));
        var search = palette.Find("CommandPaletteSearch");
        var workspace = palette.Find("CommandPalette");
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(workspace)) is not null);
        shell.Renderer.SetTextInputValue(search, "settings"); shell.Renderer.Focus(search, showIndicator: false);
        shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = shell.Tree.GetComponent<XsrUiTextInput>(search)!; int caret = input.SelectionEnd;
        var row = palette.Find("CommandPaletteCommand.settings");
        foreach (bool dark in new[] { false, true })
        {
            shell.Renderer.ColorScheme = new(dark);
            scene = shell.Render(new(810, 470));
            var title = FindByKey(shell, scene, "CommandPaletteTitle");
            var dismiss = FindByKey(shell, scene, "CommandPaletteClose");
            AssertEqual(shell.Renderer.ColorScheme.Foreground(new(52, 61, 74)), title.VisualStyle.Foreground);
            AssertTrue(dismiss.VisualStyle.Background != dismiss.VisualStyle.Foreground);
            AssertEqual(shell.Renderer.ColorScheme.AccentText, dismiss.VisualStyle.Foreground);
        }
        shell.Renderer.ColorScheme = default;
        catalog.SetLanguage("en"); scene = shell.Render(new(810, 470));
        AssertEqual(row, palette.Find("CommandPaletteCommand.settings"));
        AssertEqual(search, shell.Renderer.Focused); AssertEqual("settings", input.ReadDraft()); AssertEqual(caret, input.SelectionEnd);
        AssertEqual("Settings", scene.Nodes.Single(node => node.Entity == row).Text);
        AssertTrue(shell.Renderer.HandleKey(XsrUiKey.Escape)); AssertFalse(palette.IsOpen);
        fixture.Feedback.Info("literal <xml> {state fake}"); scene = shell.Render(new(810, 470));
        var notice = scene.Nodes.Single(node => node.Role == XsrUiSemanticRole.Status).Entity;
        var noticeMotion = shell.Tree.GetComponent<XsrUiOverlayMotion>(notice)!;
        var close = FindByKey(shell, scene, "NotificationClose").Entity;
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(notice)) is not null);
        catalog.SetLanguage("zh-Hant"); scene = shell.Render(new(810, 470));
        AssertEqual(close, FindByKey(shell, scene, "NotificationClose").Entity);
        AssertTrue(ReferenceEquals(noticeMotion, shell.Tree.GetComponent<XsrUiOverlayMotion>(notice)));
        AssertEqual("literal <xml> {state fake}", FindByKey(shell, scene, "NotificationMessage").Text);
        var previous = FindByKey(shell, scene, "LaunchButton").Entity; shell.Renderer.Focus(previous);
        string body = string.Join('\n', Enumerable.Range(1, 40).Select(index => "literal line " + index + " {data text}"));
        int alternate = 0; bool? resolved = null;
        fixture.Feedback.ShowDialog("overlay.fixture", "设置", body, "自动下载", "取消", value => resolved = value,
            "选择 Java", () => alternate++, localizeMessage: false);
        scene = shell.Render(new(810, 470));
        var dialogRoot = FindByKey(shell, scene, "DialogLayer").Entity;
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(dialogRoot)) is not null);
        var accept = FindByKey(shell, scene, "DialogAccept").Entity;
        var pick = FindByKey(shell, scene, "DialogAlternate").Entity;
        var card = FindByKey(shell, scene, "DialogCard").Entity;
        var cardMotion = shell.Tree.GetComponent<XsrUiOverlayMotion>(card)!;
        var messageViewport = FindByKey(shell, scene, "DialogMessageViewport").Entity;
        var scroll = shell.Tree.GetComponent<XsrUiScroll>(messageViewport)!; scroll.OffsetY = 17;
        catalog.SetLanguage("en"); scene = shell.Render(new(810, 470));
        AssertEqual(accept, FindByKey(shell, scene, "DialogAccept").Entity); AssertEqual(accept, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(cardMotion, shell.Tree.GetComponent<XsrUiOverlayMotion>(card)));
        AssertEqual(17d, scroll.OffsetY); AssertEqual(body, FindByKey(shell, scene, "DialogMessage").Text);
        Emit(fixture.Intents, "ui.feedback.dialog.accept", previous); AssertTrue(fixture.Feedback.Snapshot().Dialog is not null);
        Emit(fixture.Intents, "ui.feedback.dialog.alternate", pick); AssertEqual(1, alternate);
        AssertTrue(shell.Renderer.HandleKey(XsrUiKey.Escape)); AssertEqual(false, resolved);
        scene = shell.Render(new(810, 470)); AssertEqual(previous, shell.Renderer.Focused);
        fixture.Feedback.ShowMessageDialog("overlay.replacement", "设置", "literal"); scene = shell.Render(new(810, 470));
        Guid replacement = fixture.Feedback.Snapshot().Dialog!.Id;
        Emit(fixture.Intents, "ui.feedback.dialog.accept", accept); AssertEqual(replacement, fixture.Feedback.Snapshot().Dialog!.Id);
        var hiddenCancel = FindEntity(shell, "DialogCancel");
        Emit(fixture.Intents, "ui.feedback.dialog.cancel", hiddenCancel); AssertEqual(replacement, fixture.Feedback.Snapshot().Dialog!.Id);
    }
}
