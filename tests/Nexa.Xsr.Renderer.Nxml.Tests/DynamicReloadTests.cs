using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void DynamicReloadRetainsInteractiveState()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var content = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel Key="body" Scroll="true" Height="100">
              <Text Key="title" Content="{data title}" />
              <TextInput Key="input" Label="input" Height="30" />
              <Progress Key="progress" Height="4" Value="{data value}" />
              <Button Key="action" Label="action" Content="action" Height="30" Enabled="{data enabled}" Command="{data command}" />
              <Text Key="motion" Content="motion" TransitionKey="stable" TransitionOffsetX="12" />
              <Dynamic Key="nested" />
            </StackPanel></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["title"] = "first", ["value"] = "0.25", ["enabled"] = "true", ["command"] = "ui.test.first" };
        var fragment = content.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var children = tree.Children(fragment).ToArray();
        var progress = tree.GetComponent<XsrUiProgress>(children[2])!;
        var scroll = tree.GetComponent<XsrUiScroll>(fragment)!;
        var transition = tree.GetComponent<XsrUiTransition>(children[4])!;
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(slot); renderer.Render();
        AssertEqual(0.25d, progress.Target);
        AssertTrue(renderer.Focus(children[1])); AssertTrue(renderer.InsertText("keep draft"));
        AssertTrue(renderer.EditText(XsrUiTextEdit.SelectAll));
        var input = tree.GetComponent<XsrUiTextInput>(children[1])!;
        var nested = content.Append(children[5], new NxmlDynamicTemplate("""<nxml-dynamic><Text Content="child" /></nxml-dynamic>""").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default));
        data["title"] = "updated"; data["value"] = "0.75"; data["enabled"] = "false"; data["command"] = "ui.test.second";
        content.Reload(fragment, template.Compile(data, NxmlComponentLibrary.Default));
        AssertSequence(children, tree.Children(fragment).ToArray()); AssertTrue(tree.IsAlive(nested));
        AssertEqual(children[1], renderer.Focused); AssertEqual("keep draft", input.ReadDraft());
        AssertEqual(0, input.SelectionStart); AssertEqual(10, input.SelectionEnd);
        AssertTrue(ReferenceEquals(progress, tree.GetComponent<XsrUiProgress>(children[2])));
        AssertTrue(ReferenceEquals(scroll, tree.GetComponent<XsrUiScroll>(fragment)));
        AssertTrue(ReferenceEquals(transition, tree.GetComponent<XsrUiTransition>(children[4])));
        AssertEqual(0.75d, progress.Target); AssertFalse(tree.GetComponent<XsrUiInput>(children[3])!.Enabled);
        AssertEqual("ui.test.second", tree.GetComponent<XsrUiCommandBinding>(children[3])!.Command.ToString());
        var scene = renderer.Render();
        AssertTrue(scene.Nodes.Any(node => node.Entity == children[0] && node.Text == "updated"));
        int invalidations = 0; tree.RenderInvalidated += (_, _) => invalidations++;
        content.Reload(fragment, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(0, invalidations);
    }

    private static void DynamicReloadRejectsEntireInvalidProjection()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        NxmlHostIr Fragment(string body) => new NxmlDynamicTemplate("<nxml-dynamic>" + body + "</nxml-dynamic>").Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
        var original = Fragment("""<StackPanel Key="body"><Text Content="kept" /><Button Label="action" Command="ui.test.first" /></StackPanel>""");
        var root = owner.Append(slot, original);
        var changed = Fragment("""<StackPanel Key="body"><Text Content="must not apply" /><Text Content="wrong shape" /></StackPanel>""");
        AssertThrows<NxmlLoadException>(() => owner.Reload(root, changed));
        AssertEqual("kept", tree.GetComponent<XsrUiText>(tree.Children(root)[0])!.Content);
        AssertThrows<NxmlLoadException>(() => new NxmlDynamicContent(tree, store).Reload(root, original));
        tree.Destroy(root); tree.Create("replacement");
        AssertThrows<NxmlLoadException>(() => owner.Reload(root, original));
    }

    private static void DynamicComponentDataIsExpandedBeforeBinding()
    {
        var components = new NxmlComponentLibrary(new Dictionary<string, string>
        {
            ["Row"] = """<nxml-component><Node Kind="Text" Recipe="Text" Role="Text" Content="{data title}" /></nxml-component>""",
        });
        var template = new NxmlDynamicTemplate("<nxml-dynamic><Row /></nxml-dynamic>");
        var compiled = template.Compile(new Dictionary<string, string> { ["title"] = "<literal>" }, components);
        AssertEqual("<literal>", compiled.Root.Content);
        AssertThrows<NxmlCompileException>(() => template.Compile(new Dictionary<string, string>(), components));
    }
}
