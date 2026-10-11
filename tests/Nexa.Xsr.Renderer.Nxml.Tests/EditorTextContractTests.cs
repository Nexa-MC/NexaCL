using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void EditorTextContractsPreserveTabsAndReloadClipping()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel>
              <Text Key="description" Content="{data text}" MaxLines="{data lines}" TrimOverflow="true" />
              <TextInput Key="command" Label="command" PreserveTabs="{data tabs}" MaximumLength="32768" Height="30" />
            </StackPanel></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["text"] = "hint", ["lines"] = "2", ["tabs"] = "true" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var text = tree.GetComponent<XsrUiText>(tree.Children(root)[0])!;
        var inputEntity = tree.Children(root)[1]; var input = tree.GetComponent<XsrUiTextInput>(inputEntity)!;
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(slot); renderer.Render();
        renderer.Focus(inputEntity); renderer.InsertText("command\targument");
        AssertEqual("command\targument", input.ReadDraft()); AssertTrue(input.PreserveTabs);
        AssertEqual(2, text.MaxLines); AssertTrue(text.TrimOverflow);
        data["text"] = "updated"; data["lines"] = "1";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(1, text.MaxLines); AssertEqual("command\targument", input.ReadDraft());
        data["tabs"] = "false"; data["text"] = "must not apply";
        AssertThrows<NxmlLoadException>(() => owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default)));
        AssertEqual("updated", text.Content); AssertTrue(input.PreserveTabs);
        AssertThrows<NxmlCompileException>(() => Compile("<Text Content=\"test\" MaxLines=\"-1\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<Text Content=\"test\" PreserveTabs=\"true\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<TextInput Label=\"input\" MaxLines=\"2\" />"));
    }
}
