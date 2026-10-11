using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
namespace Nexa.Desktop.Tests;
internal static partial class Program
{
    private static void VersionTemplatesRetainDraftsRowsScopesAndLiteralNames()
    {
        string literal="设置 <literal> {state private}";
        var item=Instance("instance with spaces") with {Metadata=new MinecraftInstanceMetadata{DisplayName=literal}};
        using var fixture=new LaunchPageFixture(new ImmediateInstanceSource([item,Instance("other")]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        var shell=fixture.Shell;shell.Renderer.ReducedMotion=true;
        var language=new UiLocalizationCatalog();language.SetLanguage("zh-Hans");shell.Renderer.TextLocalizer=language.Translate;
        var scene=shell.Render(new(850,500));shell.Renderer.Activate(FindByKey(shell,scene,"InstanceListButton").Entity);
        scene=shell.Render(new(850,500));
        var title=scene.Nodes.Single(node=>node.Text==literal);
        var row=title.Entity;
        while(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(row)) is null)row=shell.Tree.Parent(row);
        AssertTrue(shell.Tree.Name(row).Length<=128);AssertFalse(shell.Tree.Name(row).Any(char.IsWhiteSpace));
        var owner=shell.Tree.Parent(row);AssertTrue(shell.Tree.GetComponent<XsrUiStableContent>(owner) is not null);
        var searchNode=FindByKey(shell,scene,"LibrarySearch");var search=searchNode.Entity;
        AssertEqual(search,shell.Renderer.HitTest(new(searchNode.Rect.X+10,searchNode.Rect.Y+10)));
        shell.Renderer.SetTextInputValue(search,"instance");shell.Renderer.Focus(search,showIndicator:false);shell.Renderer.EditText(XsrUiTextEdit.Left);
        var searchInput=shell.Tree.GetComponent<XsrUiTextInput>(search)!;int searchCaret=searchInput.SelectionEnd;
        var key=fixture.Store.Resolve(MinecraftLibraryContract.StateKey);
        var snapshot=fixture.Store.Read<MinecraftLibrarySnapshot>(key).Value;
        fixture.Store.Publish(key,snapshot with {Revision=snapshot.Revision+1});
        language.SetLanguage("en");scene=shell.Render(new(850,500));
        AssertTrue(shell.Tree.IsAlive(row));AssertTrue(scene.Nodes.Any(node=>node.Entity==row));
        AssertEqual(search,shell.Renderer.Focused);AssertEqual("instance",searchInput.ReadDraft());AssertEqual(searchCaret,searchInput.SelectionEnd);
        AssertTrue(scene.Nodes.Any(node=>node.Text==literal));AssertTrue(FindByKey(shell,scene,"LibraryCount").Text!.Contains('1'));
        AssertFalse(FindByKey(shell,scene,"LibraryCount").Text!.Contains("版本",StringComparison.Ordinal));
        shell.Renderer.Activate(FindByKey(shell,scene,"LibraryChooseDirectory").Entity);scene=shell.Render(new(850,500));
        var popup=FindEntity(shell,"LibraryDropdownLayer");AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(popup)) is not null);
        var rename=scene.Nodes.First(node=>shell.Tree.Name(node.Entity).StartsWith("LibraryDirectoryRename:",StringComparison.Ordinal));
        shell.Renderer.Activate(rename.Entity);scene=shell.Render(new(850,500));
        var input=FindByKey(shell,scene,"LibraryNameInput").Entity;
        shell.Renderer.SetTextInputValue(input,literal);shell.Renderer.Focus(input,showIndicator:false);shell.Renderer.EditText(XsrUiTextEdit.Left);
        var draft=shell.Tree.GetComponent<XsrUiTextInput>(input)!;int caret=draft.SelectionEnd;
        var directories=FindEntity(shell,"LibraryDirectoryRows");var scroll=shell.Tree.GetComponent<XsrUiScroll>(directories)!;
        scroll.OffsetY=17;
        snapshot=fixture.Store.Read<MinecraftLibrarySnapshot>(key).Value;
        var extra=Enumerable.Range(0,12).Select(index=>new MinecraftLibraryDirectory(System.IO.Path.Combine(fixture.TemporaryDirectory,"extra root "+index),Name:"literal folder "+index)).ToArray();
        fixture.Store.Publish(key,snapshot with {Revision=snapshot.Revision+1,Directories=[..snapshot.Directories,..extra]});
        language.SetLanguage("zh-Hant");scene=shell.Render(new(810,470));
        AssertEqual(input,FindByKey(shell,scene,"LibraryNameInput").Entity);AssertEqual(input,shell.Renderer.Focused);
        AssertEqual(literal,draft.ReadDraft());AssertEqual(caret,draft.SelectionEnd);
        AssertTrue(ReferenceEquals(scroll,shell.Tree.GetComponent<XsrUiScroll>(directories)));AssertTrue(scroll.ShowsVerticalIndicator);
        AssertTrue(scroll.OffsetY>=17);
        var anchor=shell.Tree.GetComponent<XsrUiAnchoredOverlay>(FindEntity(shell,"LibraryDropdownCard"))!;
        AssertEqual(FindEntity(shell,"LibraryChooseDirectory"),anchor.Anchor);
        // A covered row cannot select an instance while the directory modal is open.
        Emit(fixture.Intents,"ui.versions.select",row);shell.Render(new(810,470));AssertEqual(fixture.Controller.Versions.Page,shell.Stage.Navigation.Current);
        AssertTrue(shell.Renderer.HandleKey(XsrUiKey.Escape));scene=shell.Render(new(850,500));
        AssertFalse(HasKey(shell,scene,"LibraryNameInput"));
        string next=System.IO.Path.Combine(fixture.TemporaryDirectory,"replacement root");
        fixture.Store.Publish(key,snapshot with {Revision=snapshot.Revision+2,RootDirectory=next,Directories=[new(next)],Instances=[item]});
        scene=shell.Render(new(850,500));AssertFalse(shell.Tree.IsAlive(row));
        Emit(fixture.Intents,"ui.versions.select",row);shell.Render(new(850,500));
        AssertEqual(fixture.Controller.Versions.Page,shell.Stage.Navigation.Current);
    }
}
