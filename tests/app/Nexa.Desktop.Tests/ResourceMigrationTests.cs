using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ResourceOwnedWorkspaceRetainsDraftCaretTracksAndScrollAcrossProjection()
    {
        using var f = new ContinuousResourceHarness();
        var shell = f.Fixture.Shell;
        string literal = "literal <project> {state private}";
        f.Complete(0, [ContinuousProject("literal " + new string('x', 150), literal), .. ContinuousProjects("Kept", 20)], hasMore: false);
        f.Pump(() => f.Rows.Length > 0);
        var scene = f.Scene;
        var input = FindByKey(shell, scene, "ResourceSearch").Entity;
        shell.Renderer.SetTextInputValue(input, literal);
        AssertTrue(shell.Renderer.Focus(input, showIndicator: false));
        shell.Renderer.EditText(XsrUiTextEdit.Left);
        var draft = shell.Tree.GetComponent<XsrUiTextInput>(input)!;
        int caret = draft.SelectionEnd;
        var track = shell.Tree.GetComponent<XsrUiSegmentedTrack>(f.Page.Find("ResourceCategory"))!;
        var scroll = shell.Tree.GetComponent<XsrUiScroll>(f.Page.Find("ResourceList"))!;
        var row = f.Rows.First();
        AssertTrue(shell.Tree.Name(row).Length <= 128);
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(row)) is not null);
        AssertTrue(f.Fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "en"))).IsSuccess);
        scene = shell.Render(new(810, 530));
        AssertEqual(input, shell.Renderer.Focused);
        AssertEqual(literal, draft.ReadDraft()); AssertEqual(caret, draft.SelectionEnd);
        AssertTrue(ReferenceEquals(track, shell.Tree.GetComponent<XsrUiSegmentedTrack>(f.Page.Find("ResourceCategory"))));
        AssertTrue(ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(f.Page.Find("ResourceList"))));
        AssertEqual(1, f.Reads.Count);
        AssertTrue(scene.Nodes.Any(node => node.Text == literal));
        var filters = FindByKey(shell, scene, "ResourceFilters");
        foreach (string key in new[] { "ResourceGame", "ResourceLoader", "ResourceProvider", "ResourceTag", "ResourceHideInstalled", "ResourceHideLibraries" })
        {
            var filter = FindByKey(shell, scene, key);
            AssertTrue(filter.Rect.X >= filters.Rect.X && filter.Rect.X + filter.Rect.Width <= filters.Rect.X + filters.Rect.Width);
        }
        foreach (var accent in Enum.GetValues<XsrUiAccent>())
        {
            shell.Renderer.ColorScheme = new(true, accent); scene = shell.Render(new(810, 530));
            var card = scene.Nodes.Single(node => node.Entity == row);
            var title = scene.Nodes.Single(node => node.Text == literal);
            AssertTrue(AppearanceContrast(title.VisualStyle.Foreground, card.VisualStyle.Background) >= 4.5);
            AssertTrue(title.Rect.X > card.Rect.X && title.Rect.X + title.Rect.Width < card.Rect.X + card.Rect.Width);
        }
        f.ScrollToBottom();
        AssertTrue(shell.Tree.IsAlive(row));
        AssertEqual(f.Page.Find("ResourceRowParking"), shell.Tree.Parent(row));
        // A parked row stays cached but cannot invoke a detail request.
        var oldAction = f.Page.Find("ResourceDetails." + shell.Tree.Name(row)["ResourceProject.".Length..]);
        Emit(f.Fixture.Intents, "ui.resources.action", oldAction); f.RenderFrames(1);
        AssertEqual(0, f.Details.Count);
    }

    private static void ResourceVersionFilterKeepsLiteralAuthorityAndDraftAcrossResize()
    {
        using var f = new ContinuousResourceHarness(); var shell = f.Fixture.Shell;
        f.Complete(0, [], hasMore: false); f.RenderFrames(2);
        string version = "literal <version> {state private} " + new string('x', 150);
        var queries = new XsrQueryRouterBuilder();
        queries.Register<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(MinecraftInstallEditContract.Query,
            (query, _) => ValueTask.FromResult(XsrResult.Success(new MinecraftInstallEditSnapshot(query.RootDirectory, query.InstanceId, version, [], "fingerprint"))));
        f.Page.ConfigureInstanceFilter(queries.Build(new NoopDispatchObserver()), () => new("root", "instance"));
        f.Click("ResourceCurrentInstance"); f.Pump(() => f.Reads.Count == 2);
        AssertEqual(version, f.Reads[1].Query.GameVersion);
        f.Complete(1, [], hasMore: false); f.RenderFrames(2);
        f.Click("ResourceGame"); f.RenderFrames(1);
        var search = f.Page.Find("ResourceFilterSearch");
        shell.Renderer.SetTextInputValue(search, "literal"); shell.Renderer.Focus(search, showIndicator: false);
        shell.Renderer.EditText(XsrUiTextEdit.Left); f.RenderFrames(1);
        var draft = shell.Tree.GetComponent<XsrUiTextInput>(search)!; int caret = draft.SelectionEnd;
        var card = f.Page.Find("ResourceFilterCard");
        var overlay = f.Page.Find("ResourceFilterOverlay");
        var options = f.Page.Find("ResourceFilterOptions"); var scroll = shell.Tree.GetComponent<XsrUiScroll>(options)!;
        var scene = shell.Render(new(810, 420));
        AssertEqual(search, shell.Renderer.Focused); AssertEqual("literal", draft.ReadDraft()); AssertEqual(caret, draft.SelectionEnd);
        AssertEqual(search, f.Page.Find("ResourceFilterSearch")); AssertEqual(card, f.Page.Find("ResourceFilterCard"));
        AssertTrue(ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(options)));
        AssertTrue(shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)!.IsModal);
        AssertEqual(f.Page.Find("ResourceGame"), shell.Tree.GetComponent<XsrUiAnchoredOverlay>(card)!.Anchor);
        AssertTrue(FindByKey(shell, scene, "ResourceFilterCard").Rect.Height <= 200);
        var option = scene.Nodes.Single(node => node.Text == version && shell.Tree.Name(node.Entity).StartsWith("ResourceFilterOption.", StringComparison.Ordinal));
        AssertTrue(shell.Tree.Name(option.Entity).Length <= 128);
        Emit(f.Fixture.Intents, "ui.resources.action", option.Entity); f.Pump(() => f.Reads.Count == 3);
        AssertEqual(version, f.Reads[2].Query.GameVersion);
        AssertFalse(shell.Tree.IsAlive(overlay));
        Emit(f.Fixture.Intents, "ui.resources.action", option.Entity); f.RenderFrames(1);
        AssertEqual(3, f.Reads.Count);
    }
}
