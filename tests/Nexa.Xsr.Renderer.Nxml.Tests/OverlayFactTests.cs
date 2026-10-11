using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;
namespace Nexa.Xsr.Renderer.Nxml.Tests;
internal static partial class Program
{
    private static void DeclarativeOverlayFactsRetainRuntimeState()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var content = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><Node Kind="Overlay" Recipe="StackLayout" Role="Status" Key="body" Label="overlay"
              Scroll="true" ShowsVerticalIndicator="true" StickToEnd="false" ScrollGesture="true"
              Clickable="true" Focusable="true" Enabled="{data enabled}" Command="ui.test.open"
              OverlayMotion="Notification" LiveSetting="{data urgency}" DismissCommand="ui.test.close" Height="80">
              <Text Key="message" Content="{data text}" Height="240" />
            </Node></nxml-dynamic>
            """);
        var data = new Dictionary<string,string> { ["enabled"]="true", ["urgency"]="Polite", ["text"]="first" };
        var root = content.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var motion = tree.GetComponent<XsrUiOverlayMotion>(root)!;
        var input = tree.GetComponent<XsrUiInput>(root)!;
        var scroll = tree.GetComponent<XsrUiScroll>(root)!;
        var live = tree.GetComponent<XsrUiLiveRegion>(root)!;
        scroll.OffsetY=19; motion.IsClosing=true;
        AssertTrue(scroll.ShowsVerticalIndicator); AssertFalse(scroll.StickToEnd);
        AssertTrue(tree.GetComponent<XsrUiScrollGesture>(root) is not null);
        AssertEqual("ui.test.open",tree.GetComponent<XsrUiCommandBinding>(root)!.Command.ToString());
        AssertEqual("ui.test.close",tree.GetComponent<XsrUiDismissBinding>(root)!.Command.ToString());
        data["text"]="literal {state unknown}"; data["enabled"]="false"; data["urgency"]="Assertive";
        content.Reload(root,template.Compile(data,NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(motion,tree.GetComponent<XsrUiOverlayMotion>(root)));
        AssertTrue(ReferenceEquals(input,tree.GetComponent<XsrUiInput>(root)));
        AssertTrue(ReferenceEquals(scroll,tree.GetComponent<XsrUiScroll>(root)));
        AssertTrue(ReferenceEquals(live,tree.GetComponent<XsrUiLiveRegion>(root)));
        AssertTrue(motion.IsClosing); AssertEqual(19d,scroll.OffsetY); AssertFalse(input.Enabled);
        AssertEqual(XsrUiLiveSetting.Assertive,live.Setting);
    }
    private static void DeclarativeOverlayFactsRejectInvalidValuesAndContractChanges()
    {
        foreach (string xml in new[] {
            "<StackPanel OverlayMotion=\"1\" />", "<StackPanel OverlayMotion=\"unexpected\" />",
            "<StackPanel LiveSetting=\"99\" />", "<Text ShowsVerticalIndicator=\"true\" />",
            "<StackPanel StickToEnd=\"true\" />", "<StackPanel Scroll=\"true\" ScrollGesture=\"yes\" />",
            "<StackPanel DismissCommand=\"not a route\" />",
            "<Dynamic Key=\"wrong\" Layout=\"Overlay\" Scroll=\"true\" StickToEnd=\"true\" />",
            "<Dynamic Key=\"wrong\" Layout=\"HorizontalPager\" Scroll=\"true\" ShowsVerticalIndicator=\"true\" />" })
            AssertThrows<NxmlCompileException>(()=>Compile(xml));
        var tree=new XsrUiTree();var store=new XsrStateStoreBuilder().Build();
        var slot=NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"),tree,store);
        var owner=new NxmlDynamicContent(tree,store);
        var template=new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel Key="body" Scroll="true" StickToEnd="{data stick}"
             OverlayMotion="{data motion}" DismissCommand="{data dismiss}">
              <Text Content="{data text}" />
            </StackPanel></nxml-dynamic>
            """);
        var data=new Dictionary<string,string> { ["stick"]="false",["motion"]="Notification",["dismiss"]="ui.test.close",["text"]="retained" };
        var root=owner.Append(slot,template.Compile(data,NxmlComponentLibrary.Default));
        var motion=tree.GetComponent<XsrUiOverlayMotion>(root)!;
        foreach(var (key,value) in new[] { ("stick","true"),("motion","Dialog"),("dismiss","ui.test.other") })
        {
            var changed=new Dictionary<string,string>(data) { [key]=value,["text"]="must not apply" };
            AssertThrows<NxmlLoadException>(()=>owner.Reload(root,template.Compile(changed,NxmlComponentLibrary.Default)));
            AssertEqual("retained",tree.GetComponent<XsrUiText>(tree.Children(root)[0])!.Content);
            AssertTrue(ReferenceEquals(motion,tree.GetComponent<XsrUiOverlayMotion>(root)));
        }
    }
}
