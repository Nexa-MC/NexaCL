using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void LiteralInputPlaceholdersRespectReloadedLocalizationPolicy()
    {
        var tree = new XsrUiTree();
        var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel>
              <TextInput Key="literal" Label="位置" Placeholder="{data hint}" Localize="{data localize}" Height="32" />
              <TextInput Key="default" Label="位置" Placeholder="路径" Height="32" />
            </StackPanel></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["hint"] = "路径", ["localize"] = "false" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var children = tree.Children(root);
        var literal = children[0];
        var input = tree.GetComponent<XsrUiTextInput>(literal)!;
        var renderer = new XsrUiRenderer(tree, store)
        {
            TextLocalizer = source => source switch { "路径" => "Path", "位置" => "Location", _ => source },
        };
        renderer.SetRoot(slot);
        var scene = renderer.Render();
        AssertEqual("路径", scene.Nodes.Single(node => node.Entity == literal).TextInput!.Value.Placeholder);
        AssertEqual("位置", scene.Nodes.Single(node => node.Entity == literal).Label);
        AssertEqual("Path", scene.Nodes.Single(node => node.Entity == children[1]).TextInput!.Value.Placeholder);
        AssertEqual("Location", scene.Nodes.Single(node => node.Entity == children[1]).Label);

        AssertTrue(renderer.Focus(literal));
        AssertTrue(renderer.InsertText("unsaved path"));
        AssertTrue(renderer.EditText(XsrUiTextEdit.Left));
        AssertTrue(renderer.EditText(XsrUiTextEdit.Left, extendSelection: true));
        int selectionStart = input.SelectionStart, selectionEnd = input.SelectionEnd;
        data["localize"] = "true";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        scene = renderer.Render();
        AssertEqual("Path", scene.Nodes.Single(node => node.Entity == literal).TextInput!.Value.Placeholder);
        AssertEqual("Location", scene.Nodes.Single(node => node.Entity == literal).Label);
        AssertTrue(ReferenceEquals(input, tree.GetComponent<XsrUiTextInput>(literal)));
        AssertEqual("unsaved path", input.ReadDraft());
        AssertEqual(selectionStart, input.SelectionStart);
        AssertEqual(selectionEnd, input.SelectionEnd);
        AssertEqual(literal, renderer.Focused);
    }

    private static void MixedInputPlaceholdersLocalizeFormatsBeforeLiteralData()
    {
        var tree = new XsrUiTree();
        var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><TextInput Key="editor" Label="选择 {data name}"
              Placeholder="位置：{data path}" Localize="false" Height="32" /></nxml-dynamic>
            """);
        List<string> calls = [];
        string Localize(string source)
        {
            calls.Add(source);
            return source switch
            {
                "选择 {data name}" => "Choose {data name}",
                "位置：{data path}" => "Location: {data path}",
                _ => throw new InvalidOperationException("Literal input data reached the localizer."),
            };
        }
        const string path = "路径 <Node> {state missing}";
        var data = new Dictionary<string, string> { ["name"] = "文件夹", ["path"] = path };
        var root = owner.Append(slot, template.CompileLocalized(data, NxmlComponentLibrary.Default, Localize));
        var input = tree.GetComponent<XsrUiTextInput>(root)!;
        var renderer = new XsrUiRenderer(tree, store) { TextLocalizer = Localize };
        renderer.SetRoot(slot);
        var scene = renderer.Render();
        AssertEqual("Location: " + path, scene.Nodes.Single(node => node.Entity == root).TextInput!.Value.Placeholder);
        AssertEqual("Choose 文件夹", scene.Nodes.Single(node => node.Entity == root).Label);
        AssertTrue(renderer.Focus(root));
        AssertTrue(renderer.InsertText("draft {data path}"));
        AssertTrue(renderer.EditText(XsrUiTextEdit.Left));
        int selectionStart = input.SelectionStart, selectionEnd = input.SelectionEnd;

        data["path"] = "新路径 {data untouched}";
        owner.Reload(root, template.CompileLocalized(data, NxmlComponentLibrary.Default, Localize));
        scene = renderer.Render();
        AssertEqual("Location: 新路径 {data untouched}", scene.Nodes.Single(node => node.Entity == root).TextInput!.Value.Placeholder);
        AssertSequence(["选择 {data name}", "位置：{data path}", "选择 {data name}", "位置：{data path}"], calls.ToArray());
        AssertTrue(ReferenceEquals(input, tree.GetComponent<XsrUiTextInput>(root)));
        AssertEqual("draft {data path}", input.ReadDraft());
        AssertEqual(selectionStart, input.SelectionStart);
        AssertEqual(selectionEnd, input.SelectionEnd);
        AssertEqual(root, renderer.Focused);
    }
}
