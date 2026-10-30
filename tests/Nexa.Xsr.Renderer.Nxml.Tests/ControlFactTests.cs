using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void ReloadedSegmentSelectionTracksDeclaredState()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel SegmentThumb="thumb">
              <ContentHost Key="thumb" TransitionKey="selection" IsVisible="false" />
              <Button Selected="{data home}" Content="Home" />
              <Button Selected="{data theme}" Content="Themes" />
            </StackPanel></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["home"] = "true", ["theme"] = "false" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var children = tree.Children(root).ToArray();
        var track = tree.GetComponent<XsrUiSegmentedTrack>(root)!;
        var motion = tree.GetComponent<XsrUiTransition>(children[0])!;
        AssertEqual(children[1], track.Selected);
        data["home"] = "false"; data["theme"] = "true";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(children[2], track.Selected);
        AssertTrue(ReferenceEquals(track, tree.GetComponent<XsrUiSegmentedTrack>(root)));
        AssertTrue(ReferenceEquals(motion, tree.GetComponent<XsrUiTransition>(children[0])));
        // Unrelated reloads retain the existing motion/interaction state.
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(children[2], track.Selected);
        data["home"] = "true"; data["theme"] = "false";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertEqual(children[1], track.Selected);
        data["home"] = "false";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertFalse(track.Selected.IsAssigned);
    }

    private static void DeclarativeControlFactsRetainLiveObjects()
    {
        var tree = new XsrUiTree();
        var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic>
              <StackPanel Key="track" SegmentThumb="thumb" SelectionRequired="false" Padding="{data padding}">
                <ContentHost Key="thumb" TransitionKey="selection" IsVisible="false" />
                <Button Key="option" Selected="true" SegmentWidth="120" Content="{data text}" Height="32" />
                <CheckBox Key="check" Checked="{data checked}" Content="toggle" />
              </StackPanel>
            </nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["padding"] = "0", ["text"] = "before", ["checked"] = "false" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var children = tree.Children(root).ToArray();
        var track = tree.GetComponent<XsrUiSegmentedTrack>(root)!;
        var group = tree.GetComponent<XsrUiSelectionGroup>(root)!;
        AssertFalse(group.IsSelectionRequired);
        var transition = tree.GetComponent<XsrUiTransition>(children[0])!;
        var reveal = tree.GetComponent<XsrUiSegmentReveal>(children[1])!;
        var toggle = tree.GetComponent<XsrUiToggle>(children[2])!;
        AssertEqual(children[0], track.Thumb);
        AssertEqual(children[1], track.Selected);
        var renderer = new XsrUiRenderer(tree, store);
        renderer.SetSegmentExpanded(children[1], false, immediate: true);
        renderer.SetSegmentRevealProgress(children[1], .4);
        data["padding"] = "12"; data["text"] = "after"; data["checked"] = "true";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(track, tree.GetComponent<XsrUiSegmentedTrack>(root)));
        AssertTrue(ReferenceEquals(group, tree.GetComponent<XsrUiSelectionGroup>(root)));
        AssertTrue(ReferenceEquals(transition, tree.GetComponent<XsrUiTransition>(children[0])));
        AssertTrue(ReferenceEquals(reveal, tree.GetComponent<XsrUiSegmentReveal>(children[1])));
        AssertTrue(ReferenceEquals(toggle, tree.GetComponent<XsrUiToggle>(children[2])));
        AssertEqual(.4, reveal.Progress);
        AssertFalse(reveal.Expanded);
        AssertEqual(48d, tree.GetComponent<XsrUiElement>(children[1])!.Width);
        AssertTrue(toggle.IsChecked);
        AssertEqual("after", tree.GetComponent<XsrUiText>(children[1])!.Content);
    }

    private static void DeclarativeControlFactsRejectInvalidReferencesAtomically()
    {
        foreach (var xml in new[]
        {
            "<Text SegmentThumb=\"thumb\" />", "<Button SegmentWidth=\"0\" />",
            "<Button SegmentWidth=\"NaN\" />", "<StackPanel SegmentWidth=\"4\" />",
            "<Button Checked=\"true\" />", "<CheckBox Checked=\"yes\" />",
            "<ContentHost TransitionMovesSelf=\"true\" />"
        }) AssertThrows<NxmlCompileException>(() => Compile(xml));
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var old = owner.Append(slot, new NxmlDynamicTemplate("<nxml-dynamic><ContentHost Key=\"foreign\" TransitionKey=\"old\" /></nxml-dynamic>")
            .Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default));
        foreach (var source in new[]
        {
            "<nxml-dynamic><StackPanel Key=\"track\" SegmentThumb=\"foreign\" /></nxml-dynamic>",
            "<nxml-dynamic><StackPanel Key=\"track\" SegmentThumb=\"track\" TransitionKey=\"self\" /></nxml-dynamic>",
            "<nxml-dynamic><StackPanel Key=\"track\" SegmentThumb=\"thumb\"><ContentHost Key=\"thumb\" /></StackPanel></nxml-dynamic>"
        })
        {
            var invalid = new NxmlDynamicTemplate(source).Compile(new Dictionary<string, string>(), NxmlComponentLibrary.Default);
            AssertThrows<NxmlLoadException>(() => owner.Append(slot, invalid));
            AssertEqual(1, tree.Children(slot).Count); AssertEqual(old, tree.Children(slot)[0]);
        }
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><StackPanel Key="track" SegmentThumb="thumb">
              <ContentHost Key="thumb" TransitionKey="selection" />
              <Button Key="option" SegmentWidth="{data width}" Content="{data text}" />
            </StackPanel></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["width"] = "120", ["text"] = "retained" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        data["width"] = "140"; data["text"] = "must not apply";
        AssertThrows<NxmlLoadException>(() => owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default)));
        AssertEqual("retained", tree.GetComponent<XsrUiText>(tree.Children(root)[1])!.Content);
    }

    private static void PlaceholderReloadRetainsDraftCaretAndInputContract()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"slot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""
            <nxml-dynamic><TextInput Key="editor" Label="editor" Placeholder="{data hint}" MaximumLength="{data limit}" Height="32" /></nxml-dynamic>
            """);
        var data = new Dictionary<string, string> { ["hint"] = "search", ["limit"] = "128" };
        var root = owner.Append(slot, template.Compile(data, NxmlComponentLibrary.Default));
        var renderer = new XsrUiRenderer(tree, store);
        renderer.SetTextInputValue(root, "literal {state unknown}");
        renderer.SetRoot(slot); renderer.Viewport = new(400, 100); renderer.Render(); renderer.Focus(root, true);
        var input = tree.GetComponent<XsrUiTextInput>(root)!;
        var draft = input.ReadDraft();
        int selectionStart = input.SelectionStart, selectionEnd = input.SelectionEnd;
        data["hint"] = "version name";
        owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default));
        AssertTrue(ReferenceEquals(input, tree.GetComponent<XsrUiTextInput>(root)));
        AssertEqual(draft, input.ReadDraft());
        AssertEqual(selectionStart, input.SelectionStart);
        AssertEqual(selectionEnd, input.SelectionEnd);
        AssertEqual(root, renderer.Focused);
        AssertEqual("version name", input.Placeholder);
        data["hint"] = "must not apply"; data["limit"] = "64";
        AssertThrows<NxmlLoadException>(() => owner.Reload(root, template.Compile(data, NxmlComponentLibrary.Default)));
        AssertEqual("version name", input.Placeholder);
    }

    private static void DeclarativeSiblingThumbAndSelfTransitionLoad()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var root = NxmlUiLoader.LoadDetached(Compile("""
            <ContentHost><ContentHost Key="thumb" TransitionKey="selection" />
              <Dynamic Key="options" SegmentThumb="thumb" Scroll="true" ScrollGesture="true"
                TransitionKey="page" TransitionMovesSelf="true" />
            </ContentHost>
            """), tree, store);
        var children = tree.Children(root);
        AssertEqual(children[0], tree.GetComponent<XsrUiSegmentedTrack>(children[1])!.Thumb);
        AssertTrue(tree.GetComponent<XsrUiTransition>(children[1])!.MovesSelf);
    }
}
