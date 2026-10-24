using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static readonly string[] ExtensionBefore = ["added", "original"];
    private static readonly string[] ExtensionAfter = ["original", "added"];
    private static readonly string[] ExtensionDynamicOrder = ["first", "middle", "last"];
    private static NxmlPreparedDocument Extension(string markup) => new NxmlExtensionProgram("main.nxml",
        new Dictionary<string, string> { ["main.nxml"] = markup }).Compile(new Dictionary<string, string>());

    private static void GlobalNxmlMountsTargetFuturePages()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var root = NxmlUiLoader.LoadDetached(Compile("<StackPanel Key=\"Shell\" />"), tree, store);
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(root);
        using var coordinator = new NxmlPresentationCoordinator(tree, store);
        var document = Extension("<Text Content=\"added\" Height=\"20\" />");
        NxmlPresentationLayer[] layers = [new(Guid.NewGuid(), "#Future/#Items", XsrUiMountOperation.Append, document)];
        coordinator.Update(layers); AssertFalse(renderer.Render().Nodes.Any(node => node.Text == "added"));
        var future = NxmlUiLoader.Load(Compile("<StackPanel Key=\"Future\"><StackPanel Key=\"Items\"><Text Content=\"original\" Height=\"20\" /></StackPanel></StackPanel>"), tree, store, root);
        var items = tree.Children(future)[0]; var original = tree.Children(items).Single();
        coordinator.Update(layers);
        var scene = renderer.Render(); AssertTrue(scene.Nodes.Any(node => node.Text == "added"));
        AssertSequence(new[] { original }, tree.Children(items).ToArray());
        AssertTrue(ReferenceEquals(scene, renderer.Render()));
        foreach (var operation in new[] { XsrUiMountOperation.Before, XsrUiMountOperation.After, XsrUiMountOperation.Prepend })
        {
            var target = operation == XsrUiMountOperation.Prepend ? "#Future/#Items" : "#Future/#Items/#Text";
            coordinator.Update([new(Guid.NewGuid(), target, operation, document)]);
            var texts = renderer.Render().Nodes.Where(node => node.Text is "original" or "added").Select(node => node.Text!).ToArray();
            AssertSequence(operation == XsrUiMountOperation.After ? ExtensionAfter : ExtensionBefore, texts);
        }
        coordinator.Update([]); AssertFalse(renderer.Render().Nodes.Any(node => node.Text == "added"));
        AssertTrue(tree.IsAlive(original));
    }

    private static void GlobalNxmlReplacementRestoresLiveState()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var root = NxmlUiLoader.LoadDetached(Compile("<StackPanel Key=\"Shell\"><StackPanel Key=\"Replace\"><Text Key=\"Value\" Content=\"original\" /></StackPanel></StackPanel>"), tree, store);
        var target = tree.Children(root)[0]; var original = tree.Children(target)[0];
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(root);
        using var coordinator = new NxmlPresentationCoordinator(tree, store);
        var first = new NxmlPresentationLayer(Guid.NewGuid(), "#Replace", XsrUiMountOperation.Replace,
            Extension("<StackPanel><Text Content=\"first\" /></StackPanel>"));
        var second = new NxmlPresentationLayer(Guid.NewGuid(), "#Replace", XsrUiMountOperation.Replace,
            Extension("<StackPanel><Text Content=\"second\" /></StackPanel>"));
        coordinator.Update([first, second]);
        var scene = renderer.Render(); AssertTrue(scene.Nodes.Any(node => node.Text == "second"));
        AssertFalse(scene.Nodes.Any(node => node.Text is "first" or "original"));
        tree.GetComponent<XsrUiText>(original)!.Content = "updated while replaced"; tree.MarkDirty(original, XsrUiDirtyKinds.State);
        coordinator.Update([first]); AssertTrue(renderer.Render().Nodes.Any(node => node.Text == "first"));
        coordinator.Update([]); AssertTrue(renderer.Render().Nodes.Any(node => node.Text == "updated while replaced"));
        var modify = new NxmlPresentationLayer(Guid.NewGuid(), "#Value", XsrUiMountOperation.Modify, Extension("<Text Content=\"modified\" />"));
        coordinator.Update([modify]); AssertTrue(renderer.Render().Nodes.Any(node => node.Entity == original && node.Text == "modified"));
        AssertEqual("updated while replaced", tree.GetComponent<XsrUiText>(original)!.Content);
        coordinator.Update([]); AssertTrue(renderer.Render().Nodes.Any(node => node.Text == "updated while replaced"));
    }

    private static void NxmlExtensionsShareTheHostSuite()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var root = NxmlUiLoader.LoadDetached(Compile("<StackPanel Key=\"Host\" />"), tree, store);
        Dictionary<string, string> files = new(StringComparer.Ordinal)
        {
            ["main.nxml"] = "<StackPanel><FutureControl Key=\"Title\" Content=\"{data title}\" /><TextInput Key=\"Draft\" Label=\"Draft\" Height=\"32\" /><Dynamic Key=\"Rows\" /></StackPanel>",
            ["FutureControl.c.nxml"] = "<nxml-component><Text Class=\"future\" /></nxml-component>",
            ["skin.nss"] = ".future { FontSize: 19; }",
            ["Rows/001.d.nxml"] = "<nxml-dynamic><Text Content=\"{data row}\" /></nxml-dynamic>",
            ["Rows/002.d.nxml"] = "<nxml-dynamic><Text Content=\"last\" /></nxml-dynamic>",
        };
        var program = new NxmlExtensionProgram("main.nxml", files);
        var data = new Dictionary<string, string> { ["title"] = "first", ["row"] = "middle" };
        var exposure = Guid.NewGuid();
        using var coordinator = new NxmlPresentationCoordinator(tree, store);
        var compiled = program.Compile(data);
        AssertTrue(compiled.Root.Root.Children[0].Visual?.FontSize == 19);
        coordinator.Update([new(exposure, "#Host", XsrUiMountOperation.Append, compiled)]);
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(root); var scene = renderer.Render();
        AssertSequence(ExtensionDynamicOrder, scene.Nodes.Where(node => node.Text is not null).Select(node => node.Text!).ToArray());
        var draft = tree.LiveEntities.Single(entity => tree.Name(entity) == "Draft");
        AssertTrue(renderer.Focus(draft)); AssertTrue(renderer.InsertText("keep this"));
        data["title"] = "changed"; data["row"] = "<Text Content=\"literal\" />";
        coordinator.Update([new(exposure, "#Host", XsrUiMountOperation.Append, program.Compile(data))]);
        scene = renderer.Render(); AssertTrue(scene.Nodes.Any(node => node.Text == "changed"));
        AssertTrue(scene.Nodes.Any(node => node.Text == data["row"]));
        AssertEqual(draft, renderer.Focused); AssertEqual("keep this", tree.GetComponent<XsrUiTextInput>(draft)!.ReadDraft());
        AssertTrue(coordinator.TryGetExposure(draft, out var owner)); AssertEqual(exposure, owner);
        coordinator.Update([]); AssertFalse(coordinator.TryGetExposure(draft, out _));
    }

    private static void NxmlPresentationIsLateAndTransactional()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var root = NxmlUiLoader.LoadDetached(Compile("<StackPanel Key=\"Host\" />"), tree, store);
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(root);
        using var coordinator = new NxmlPresentationCoordinator(tree, store);
        bool created = false;
        renderer.FramePreparing += (_, _) =>
        {
            if (created) return; created = true;
            NxmlUiLoader.Load(Compile("<StackPanel Key=\"Late\" />"), tree, store, root);
        };
        NxmlPresentationLayer[] layers = [new(Guid.NewGuid(), "#Late", XsrUiMountOperation.Append, Extension("<Text Content=\"same frame\" />"))];
        renderer.PresentationPreparing += (_, _) => coordinator.Update(layers);
        AssertTrue(renderer.Render().Nodes.Any(node => node.Text == "same frame"));
        int before = tree.Count;
        var bad = new NxmlPresentationLayer(Guid.NewGuid(), "#Late", XsrUiMountOperation.Append,
            Extension("<Text Content=\"{state missing.state}\" />"));
        AssertThrows<NxmlPresentationException>(() => coordinator.Update([.. layers, bad]));
        AssertEqual(before, tree.Count);
        AssertTrue(renderer.Render().Nodes.Any(node => node.Text == "same frame"));
        AssertThrows<NxmlLoadException>(() => NxmlUiLoader.ValidateBindings(bad.Document.Root, store));
        var detached = tree.Create("cycle"); var target = tree.Create("target"); tree.Attach(target, detached);
        AssertThrows<InvalidOperationException>(() => tree.Presentation.MountContent(target, detached, XsrUiMountOperation.Append));
        tree.Destroy(detached);
    }

    private static void NxmlCommandArgumentsReadLocalValues()
    {
        XsrUiTree tree = new(); var store = new XsrStateStoreBuilder().Build();
        var root = NxmlUiLoader.LoadDetached(Compile("<StackPanel><TextInput Key=\"Draft\" Label=\"input\" /><Button Key=\"Submit\" Label=\"submit\" Command=\"test.submit\" ArgumentFrom=\"Draft\" /></StackPanel>"), tree, store);
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(root); renderer.Render();
        var children = tree.Children(root);
        renderer.SetTextInputValue(children[0], "from a local input");
        AssertEqual("from a local input", renderer.ReadIntentArgument(children[1]));
        AssertThrows<NxmlCompileException>(() => Compile("<Button Key=\"Self\" Label=\"self\" ArgumentFrom=\"Self\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<Button Label=\"missing\" ArgumentFrom=\"Missing\" />"));
        AssertThrows<NxmlCompileException>(() => Compile("<StackPanel><Button Key=\"First\" Label=\"first\" ArgumentFrom=\"Second\" /><Button Key=\"Second\" Label=\"second\" ArgumentFrom=\"First\" /></StackPanel>"));
    }
}
