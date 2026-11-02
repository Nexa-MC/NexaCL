using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void FeedbackPrelocalizedLiteralTextNeverReentersLocalizationAcrossDialogAndDetails()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell;
        shell.Renderer.ReducedMotion = true;
        var catalog = new UiLocalizationCatalog();
        catalog.SetLanguage("en");
        const string provider = "设置/<provider>/{state private}/{data literal}.json";
        string title = catalog.Translate("设置") + " · " + provider;
        string message = catalog.Translate("返回") + "\n" + provider;
        List<string> localizedSources = [];
        string Localize(string source)
        {
            localizedSources.Add(source);
            if (source.Contains(provider, StringComparison.Ordinal)) return "ERROR: literal data was localized again";
            return catalog.Translate(source);
        }
        shell.Renderer.TextLocalizer = Localize;
        Guid notification = fixture.Feedback.Error(message, localizeMessage: false);
        var scene = shell.Render(new(900, 700));
        AssertEqual(message, FindByKey(shell, scene, "NotificationMessage").Text);
        var notice = scene.Nodes.Single(node => shell.Tree.Name(node.Entity) == "notification:" + notification.ToString("N")).Entity;
        bool? resolved = null;
        Guid dialog = fixture.Feedback.ShowDialog("literal.prelocalized", title, message, "继续", "取消", accepted => resolved = accepted,
            localizeMessage: false, localizeTitle: false);
        scene = shell.Render(new(900, 700));
        var titleEntity = FindByKey(shell, scene, "DialogTitle").Entity;
        var messageEntity = FindByKey(shell, scene, "DialogMessage").Entity;
        var accept = FindByKey(shell, scene, "DialogAccept").Entity;
        var cancel = FindByKey(shell, scene, "DialogCancel").Entity;
        AssertEqual(title, FindByKey(shell, scene, "DialogTitle").Text);
        AssertEqual(message, FindByKey(shell, scene, "DialogMessage").Text);
        AssertEqual(catalog.Translate("继续"), FindByKey(shell, scene, "DialogAccept").Text);
        AssertEqual(catalog.Translate("取消"), FindByKey(shell, scene, "DialogCancel").Text);
        AssertFalse(FindByKey(shell, scene, "DialogAccept").Text == "继续");
        AssertFalse(FindByKey(shell, scene, "DialogCancel").Text == "取消");
        AssertFalse(localizedSources.Any(source => source.Contains(provider, StringComparison.Ordinal)));

        catalog.SetLanguage("zh-Hant"); shell.Renderer.TextLocalizer = Localize;
        scene = shell.Render(new(800, 600));
        AssertEqual(dialog, fixture.Feedback.Snapshot().Dialog!.Id);
        AssertEqual(titleEntity, FindByKey(shell, scene, "DialogTitle").Entity);
        AssertEqual(messageEntity, FindByKey(shell, scene, "DialogMessage").Entity);
        AssertEqual(accept, FindByKey(shell, scene, "DialogAccept").Entity);
        AssertEqual(cancel, FindByKey(shell, scene, "DialogCancel").Entity);
        AssertEqual(title, FindByKey(shell, scene, "DialogTitle").Text);
        AssertEqual(message, FindByKey(shell, scene, "DialogMessage").Text);
        AssertEqual(catalog.Translate("继续"), FindByKey(shell, scene, "DialogAccept").Text);
        AssertEqual(catalog.Translate("取消"), FindByKey(shell, scene, "DialogCancel").Text);
        AssertEqual(message, FindByKey(shell, scene, "NotificationMessage").Text);
        AssertFalse(localizedSources.Any(source => source.Contains(provider, StringComparison.Ordinal)));
        AssertTrue(fixture.Feedback.ResolveDialog(dialog, false)); AssertEqual(false, resolved);
        scene = shell.Render(new(800, 600));

        AssertTrue(shell.Renderer.Activate(notice));
        scene = shell.Render(new(800, 600));
        var details = fixture.Feedback.Snapshot().Dialog!;
        AssertEqual("notification.detail." + notification.ToString("N"), details.Key);
        AssertFalse(details.LocalizeMessage); AssertFalse(details.LocalizeTitle);
        AssertEqual(message, FindByKey(shell, scene, "DialogMessage").Text);
        AssertEqual(details.Title, FindByKey(shell, scene, "DialogTitle").Text);
        AssertEqual(catalog.Translate(details.AcceptLabel), FindByKey(shell, scene, "DialogAccept").Text);
        AssertFalse(localizedSources.Any(source => source.Contains(provider, StringComparison.Ordinal)));

        catalog.SetLanguage("en"); shell.Renderer.TextLocalizer = Localize;
        scene = shell.Render(new(900, 700));
        AssertEqual(details.Id, fixture.Feedback.Snapshot().Dialog!.Id);
        AssertEqual(message, FindByKey(shell, scene, "DialogMessage").Text);
        AssertEqual(details.Title, FindByKey(shell, scene, "DialogTitle").Text);
        AssertEqual(catalog.Translate(details.AcceptLabel), FindByKey(shell, scene, "DialogAccept").Text);
        AssertFalse(localizedSources.Any(source => source.Contains(provider, StringComparison.Ordinal)));
        AssertTrue(shell.Renderer.Activate(FindByKey(shell, scene, "DialogAccept").Entity));
        scene = shell.Render(new(900, 700));
        AssertTrue(fixture.Feedback.Snapshot().Dialog is null);
        AssertEqual(message, FindByKey(shell, scene, "NotificationMessage").Text);
    }

    private static void FeedbackNotificationDeduplicationKeepsLocalizationSemanticsSeparate()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Renderer.ReducedMotion = true;
        var catalog = new UiLocalizationCatalog(); catalog.SetLanguage("en");
        fixture.Shell.Renderer.TextLocalizer = catalog.Translate;
        const string message = "设置";
        Guid literal = fixture.Feedback.Error(message, localizeMessage: false);
        Guid localized = fixture.Feedback.Error(message, localizeMessage: true);
        var scene = fixture.Shell.Render(new(900, 700));
        AssertEqual(2, fixture.Feedback.Snapshot().Notifications.Count);
        AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(note => note.Id == literal && !note.LocalizeMessage));
        AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(note => note.Id == localized && note.LocalizeMessage));
        var noticeMessages = scene.Nodes.Where(node => fixture.Shell.Tree.Name(node.Entity) == "NotificationMessage").Select(node => node.Text).ToArray();
        AssertEqual(2, noticeMessages.Length); AssertTrue(noticeMessages.Contains(message)); AssertTrue(noticeMessages.Contains(catalog.Translate(message)));
        var literalRoot = scene.Nodes.Single(node => fixture.Shell.Tree.Name(node.Entity) == "notification:" + literal.ToString("N")).Entity;
        var literalMotion = fixture.Shell.Tree.GetComponent<XsrUiOverlayMotion>(literalRoot)!;
        Guid replacement = fixture.Feedback.Error(message, localizeMessage: true);
        scene = fixture.Shell.Render(new(900, 700));
        var notes = fixture.Feedback.Snapshot().Notifications;
        AssertEqual(2, notes.Count); AssertTrue(notes.Any(note => note.Id == literal)); AssertTrue(notes.Any(note => note.Id == replacement));
        AssertFalse(notes.Any(note => note.Id == localized));
        AssertTrue(fixture.Shell.Tree.IsAlive(literalRoot));
        AssertTrue(ReferenceEquals(literalMotion, fixture.Shell.Tree.GetComponent<XsrUiOverlayMotion>(literalRoot)));
        AssertEqual(2, scene.Nodes.Count(node => fixture.Shell.Tree.Name(node.Entity) == "NotificationMessage"));
        Guid literalReplacement = fixture.Feedback.Error(message, localizeMessage: false);
        scene = fixture.Shell.Render(new(900, 700));
        notes = fixture.Feedback.Snapshot().Notifications;
        AssertEqual(2, notes.Count); AssertTrue(notes.Any(note => note.Id == literalReplacement && !note.LocalizeMessage));
        AssertTrue(notes.Any(note => note.Id == replacement && note.LocalizeMessage)); AssertFalse(notes.Any(note => note.Id == literal));
        AssertEqual(2, scene.Nodes.Count(node => fixture.Shell.Tree.Name(node.Entity) == "NotificationMessage"));
    }
}
