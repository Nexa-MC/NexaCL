using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr;
namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void WardrobeOwnedCardsRetainDraftsAcrossLanguageAndOrientation()
    {
        using var fixture = LibraryFixture(LaunchProfileKind.Microsoft); int reads = 0;
        var language = new UiLocalizationCatalog(); language.SetLanguage("zh-Hans"); fixture.Shell.Renderer.TextLocalizer = language.Translate;
        var queries = new XsrQueryRouterBuilder(); string title = "literal <skin> {state private}";
        queries.Register<AccountWardrobeQuery, AccountWardrobeSnapshot>(AccountWardrobeContract.Read, (_, _) =>
        {
            reads++; return ValueTask.FromResult(XsrResult.Success(WardrobeDesktopSnapshot(fixture, "Owned") with
            { Skins = [new("history " + new string('a', 180), title, "此前使用", new(null, null, false, null, null), true)] }));
        });
        using var page = new WardrobePageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback);
        page.ConfigureFilePicker(_ => Task.FromResult<string?>(null)); fixture.Shell.Stage.Navigation.Replace(page.Page);
        AssertTrue(SpinWait.SpinUntil(() => { fixture.Shell.Render(new(820, 600)); return WardrobeText(fixture, "WardrobeSkinCount") == "1 项"; }, TimeSpan.FromSeconds(5)));
        var slot = FindEntity(fixture.Shell, "WardrobeSkins"); AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(slot) is not null);
        var row = fixture.Shell.Tree.Children(slot).Single(); AssertTrue(fixture.Shell.Tree.Name(row).Length <= 128);
        var body = fixture.Shell.Tree.Children(row)[0]; AssertEqual(XsrUiOrientation.Horizontal, fixture.Shell.Tree.GetComponent<XsrUiStackPanel>(body)!.Direction);
        WardrobeEmit(fixture, "ui.wardrobe.browse", "WardrobeBrowse"); fixture.Shell.Render(new(820, 600));
        var input = FindEntity(fixture.Shell, "WardrobeSkinPath"); string path = "literal <path> {state secret}.png";
        fixture.Shell.Renderer.SetTextInputValue(input, path); AssertTrue(fixture.Shell.Renderer.Focus(input, showIndicator: false)); fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var draft = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!; int caret = draft.SelectionEnd;
        language.SetLanguage("en"); fixture.Shell.Render(new(1600, 1200));
        AssertEqual(row, fixture.Shell.Tree.Children(slot).Single()); AssertEqual(input, fixture.Shell.Renderer.Focused);
        AssertEqual(path, draft.ReadDraft()); AssertEqual(caret, draft.SelectionEnd); AssertEqual(1, reads);
        AssertEqual(XsrUiOrientation.Vertical, fixture.Shell.Tree.GetComponent<XsrUiStackPanel>(body)!.Direction);
        AssertEqual("1 items", WardrobeText(fixture, "WardrobeSkinCount"));
        AssertTrue(fixture.Shell.Tree.Children(fixture.Shell.Tree.Children(body)[1]).Any(node => fixture.Shell.Tree.GetComponent<XsrUiText>(node)?.Content == title));
        var scroll = fixture.Shell.Tree.GetComponent<XsrUiScroll>(slot)!; AssertTrue(scroll.ShowsHorizontalIndicator); AssertTrue(scroll.UseVerticalWheelForHorizontalScroll);
        foreach (var accent in Enum.GetValues<XsrUiAccent>())
        {
            fixture.Shell.Renderer.ColorScheme = new(true, accent); var scene = fixture.Shell.Render(new(820, 600));
            var panel = FindByKey(fixture.Shell, scene, "WardrobeLocalPanel");
            var label = FindByKey(fixture.Shell, scene, "WardrobeLocalTitle");
            AssertTrue(AppearanceContrast(label.VisualStyle.Foreground, panel.VisualStyle.Background) >= 4.5);
            AssertTrue(label.Rect.X - panel.Rect.X >= 19 && label.Rect.Y - panel.Rect.Y >= 19);
        }

    }
    private static void WardrobeLibraryOwnedReflowRetainsSearchViewAndFocus()
    {
        using var fixture = LibraryFixture(LaunchProfileKind.Microsoft); var language = new UiLocalizationCatalog(); language.SetLanguage("zh-Hans"); fixture.Shell.Renderer.TextLocalizer = language.Translate;
        var queries = LibraryQueries(fixture); int reads = 0;
        queries.Register<WardrobeCatalogQuery, WardrobeCatalogPage>(WardrobeCatalogContract.Read, (query, _) =>
        { reads++; return ValueTask.FromResult(XsrResult.Success(LibraryPage(query, Enumerable.Range(1, 12).Select(i => LibraryItem(i, WardrobeCatalogKind.Skin) with { Name = "literal <skin> {data name}" }).ToArray()))); });
        using var page = new WardrobeLibraryPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback);
        fixture.Shell.Stage.Navigation.Replace(page.Page); LibraryPump(fixture, () => LibraryVisible(fixture, "WardrobeLibraryItems"));
        var card = FindEntity(fixture.Shell, "WardrobeLibraryCard.1"); var oldParent = fixture.Shell.Tree.Parent(FindEntity(fixture.Shell, "WardrobeLibraryCard.3"));
        LibraryEmit(fixture, "ui.wardrobe.library.action", "WardrobeLibraryCard.1.View"); fixture.Shell.Render(new(810, 600));
        var preview = FindEntity(fixture.Shell, "WardrobeLibraryCard.1.Preview"); var raster = fixture.Shell.Tree.GetComponent<XsrUiImage>(preview)!.Raster;
        var input = FindEntity(fixture.Shell, "WardrobeLibrarySearch"); string literal = "{state private} <query>";
        fixture.Shell.Renderer.SetTextInputValue(input, literal); AssertTrue(fixture.Shell.Renderer.Focus(input, showIndicator: false)); fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        int caret = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!.SelectionEnd;
        language.SetLanguage("en"); fixture.Shell.Render(new(1600, 1200));
        AssertEqual(card, FindEntity(fixture.Shell, "WardrobeLibraryCard.1")); AssertEqual(preview, FindEntity(fixture.Shell, "WardrobeLibraryCard.1.Preview"));
        AssertTrue(ReferenceEquals(raster, fixture.Shell.Tree.GetComponent<XsrUiImage>(preview)!.Raster)); AssertEqual(input, fixture.Shell.Renderer.Focused);
        AssertEqual(literal, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft()); AssertEqual(caret, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!.SelectionEnd);
        AssertFalse(oldParent == fixture.Shell.Tree.Parent(FindEntity(fixture.Shell, "WardrobeLibraryCard.3"))); AssertEqual(1, reads);
        AssertEqual("Front · Change view", LibraryText(fixture, "WardrobeLibraryCard.1.View"));
        AssertEqual("literal <skin> {data name}", LibraryText(fixture, "WardrobeLibraryCard.1.Name"));
        foreach (var accent in Enum.GetValues<XsrUiAccent>())
        {
            fixture.Shell.Renderer.ColorScheme = new(true, accent); var scene = fixture.Shell.Render(new(1600, 1200));
            var surface = FindByKey(fixture.Shell, scene, "WardrobeLibraryCard.1");
            var label = FindByKey(fixture.Shell, scene, "WardrobeLibraryCard.1.Name");
            var choice = FindByKey(fixture.Shell, scene, "WardrobeLibrarySkin");
            AssertTrue(AppearanceContrast(label.VisualStyle.Foreground, surface.VisualStyle.Background) >= 4.5);
            AssertTrue(AppearanceContrast(choice.VisualStyle.Foreground, choice.VisualStyle.Background) >= 4.5);
            AssertEqual(383d, surface.Rect.Height);
            var body = FindByKey(fixture.Shell, scene, "WardrobeLibraryCard.1.Body");
            AssertTrue(body.Rect.X - surface.Rect.X >= 13 && body.Rect.Y - surface.Rect.Y >= 10);
        }

    }
}
