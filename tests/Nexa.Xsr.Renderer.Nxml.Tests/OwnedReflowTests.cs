using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;
namespace Nexa.Xsr.Renderer.Nxml.Tests;
internal static partial class Program
{
    private static void OwnedMoveRetainsStateAndRejectsInvalidScopes()
    {
        var tree=new XsrUiTree();var store=new XsrStateStoreBuilder().Build();
        var slot=NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"host\" />"),tree,store);
        var owner=new NxmlDynamicContent(tree,store);
        NxmlHostIr Fragment(string source)=>new NxmlDynamicTemplate(source).Compile(new Dictionary<string,string>(),NxmlComponentLibrary.Default);
        var workspace=owner.Append(slot,Fragment("<nxml-dynamic><StackPanel Key=\"workspace\"><Dynamic Key=\"a\"/><Dynamic Key=\"b\"/></StackPanel></nxml-dynamic>"));
        var a=tree.Children(workspace)[0];var b=tree.Children(workspace)[1];
        var content=Fragment("<nxml-dynamic><StackPanel Key=\"card\"><TextInput Key=\"draft\" Label=\"draft\" Height=\"30\"/><Dynamic Key=\"nested\"/></StackPanel></nxml-dynamic>");
        var card=owner.Append(a,content);var inputEntity=tree.Children(card)[0];var input=tree.GetComponent<XsrUiTextInput>(inputEntity)!;
        var nestedSlot=tree.Children(card)[1];var nested=owner.Append(nestedSlot,Fragment("<nxml-dynamic><Text Content=\"kept\"/></nxml-dynamic>"));
        var renderer=new XsrUiRenderer(tree,store);renderer.SetRoot(slot);renderer.Render();
        AssertTrue(renderer.Focus(inputEntity));AssertTrue(renderer.InsertText("live draft"));
        owner.Move(card,b);AssertEqual(b,tree.Parent(card));AssertEqual(0,tree.Children(a).Count);
        AssertEqual(inputEntity,renderer.Focused);AssertEqual("live draft",input.ReadDraft());AssertTrue(tree.IsAlive(nested));
        owner.Reload(card,content);owner.Reorder(b,[card]);AssertEqual("live draft",input.ReadDraft());
        var foreignOwner=new NxmlDynamicContent(tree,store);
        AssertThrows<NxmlLoadException>(()=>foreignOwner.Move(card,a));
        AssertThrows<NxmlLoadException>(()=>owner.Move(card,nestedSlot));
        var other=owner.Append(slot,Fragment("<nxml-dynamic><Dynamic Key=\"other\"/></nxml-dynamic>"));
        AssertThrows<NxmlLoadException>(()=>owner.Move(card,other));AssertEqual(b,tree.Parent(card));
        var anchored=owner.Append(a,Fragment("<nxml-dynamic><StackPanel Key=\"anchored\"><Button Key=\"target\" Label=\"target\"/><Node Recipe=\"Element\" Anchor=\"target\"/></StackPanel></nxml-dynamic>"));
        AssertThrows<NxmlLoadException>(()=>owner.Move(anchored,b));AssertEqual(a,tree.Parent(anchored));
        owner.Remove(card);AssertFalse(tree.IsAlive(nested));AssertThrows<NxmlLoadException>(()=>owner.Move(card,a));
    }
    private static void DeclarativeHorizontalScrollAndResponsiveOrientationRetainContracts()
    {
        var tree=new XsrUiTree();var store=new XsrStateStoreBuilder().Build();
        var slot=NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"host\"/>"),tree,store);var owner=new NxmlDynamicContent(tree,store);
        var template=new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel Key="card" Orientation="{data orientation}"><TextInput Key="draft" Label="draft" Height="30"/>
              <StackPanel Key="track" Scroll="true" Orientation="Horizontal" ShowsHorizontalIndicator="true" UseVerticalWheelForHorizontalScroll="true" ScrollGesture="true" Height="80"/>
            </StackPanel></nxml-dynamic>
            """);
        var data=new Dictionary<string,string>{["orientation"]="Vertical"};
        var card=owner.Append(slot,template.Compile(data,NxmlComponentLibrary.Default));var inputEntity=tree.Children(card)[0];var track=tree.Children(card)[1];
        var scroll=tree.GetComponent<XsrUiScroll>(track)!;scroll.OffsetX=23;
        AssertTrue(scroll.ShowsHorizontalIndicator);AssertTrue(scroll.UseVerticalWheelForHorizontalScroll);AssertTrue(tree.GetComponent<XsrUiScrollGesture>(track)is not null);
        var renderer=new XsrUiRenderer(tree,store);renderer.SetRoot(slot);renderer.Render();scroll.OffsetX=23;renderer.Focus(inputEntity);renderer.InsertText("kept");
        data["orientation"]="Horizontal";owner.Reload(card,template.Compile(data,NxmlComponentLibrary.Default));
        AssertEqual(XsrUiOrientation.Horizontal,tree.GetComponent<XsrUiStackPanel>(card)!.Direction);AssertEqual(inputEntity,renderer.Focused);
        AssertEqual("kept",tree.GetComponent<XsrUiTextInput>(inputEntity)!.ReadDraft());AssertTrue(ReferenceEquals(scroll,tree.GetComponent<XsrUiScroll>(track)));AssertEqual(23d,scroll.OffsetX);
        // Scrolling orientation and wheel routing remain lifetime contracts, rejected before any projection changes.
        var changed=new NxmlDynamicTemplate("<nxml-dynamic><StackPanel Key=\"card\" Orientation=\"Vertical\"><TextInput Key=\"draft\" Label=\"draft\" Height=\"30\"/><StackPanel Key=\"track\" Scroll=\"true\" Orientation=\"Vertical\" ShowsHorizontalIndicator=\"true\" UseVerticalWheelForHorizontalScroll=\"true\" ScrollGesture=\"true\" Height=\"80\"/></StackPanel></nxml-dynamic>");
        AssertThrows<NxmlLoadException>(()=>owner.Reload(card,changed.Compile(new Dictionary<string,string>(),NxmlComponentLibrary.Default)));
        AssertEqual(XsrUiOrientation.Horizontal,tree.GetComponent<XsrUiStackPanel>(card)!.Direction);AssertEqual(23d,scroll.OffsetX);
        foreach(var xml in new[]{"<Text ShowsHorizontalIndicator=\"true\"/>","<StackPanel UseVerticalWheelForHorizontalScroll=\"true\"/>","<StackPanel Scroll=\"true\" ShowsHorizontalIndicator=\"wrong\"/>"})
            AssertThrows<NxmlCompileException>(()=>Compile(xml));
    }
}
