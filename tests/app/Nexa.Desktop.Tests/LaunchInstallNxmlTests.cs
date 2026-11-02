using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void LaunchInstallOwnedCatalogRetainsSelectionDraftFocusAndScroll()
    {
        var source = new LargeInstallSource();
        source.Loader.SetResult([new("build-1", "fixture")]);
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), installSource: source);
        fixture.Shell.Renderer.ReducedMotion = true;
        Emit(fixture.Intents, "ui.navigation.download");
        Emit(fixture.Intents, "ui.install.java");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        var size = new XsrUiSize(1024, 600);
        var scene = fixture.Shell.Render(size);
        var workspace = FindEntity(fixture.Shell, "JavaInstallContent");
        var slot = fixture.Shell.Tree.Parent(workspace);
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(slot) is not null);
        var rows = FindEntity(fixture.Shell, "JavaMinecraftVersions");
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(rows) is not null);
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiStableContent>(rows) is not null);
        var row = FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity;
        AssertEqual(rows, fixture.Shell.Tree.Parent(row));
        var input = FindByKey(fixture.Shell, scene, "JavaInstallVersionInput").Entity;
        var inputState = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!;
        var track = fixture.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(FindEntity(fixture.Shell, "JavaInstallPagerTabs"))!;
        var thumbMotion = fixture.Shell.Tree.GetComponent<XsrUiTransition>(track.Thumb)!;
        var scrollEntity = FindEntity(fixture.Shell, "JavaMinecraftPage");
        var scrollState = fixture.Shell.Tree.GetComponent<XsrUiScroll>(scrollEntity)!;
        AssertTrue(fixture.Shell.Renderer.Focus(row));
        AssertTrue(fixture.Shell.Renderer.Activate(row));
        scene = fixture.Shell.Render(size);
        AssertEqual(row, FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity);
        AssertTrue(FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").IsSelected);
        // Membership changes intentionally rebase pager focus, as an ordinary page selection does.
        AssertEqual(FindEntity(fixture.Shell, "JavaInstallPager"), fixture.Shell.Renderer.Focused);
        AssertTrue(fixture.Shell.Renderer.Focus(row));
        fixture.Controller.SetStartupOffline();
        fixture.Shell.Render(size);
        AssertEqual(row, fixture.Shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(inputState, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)));
        AssertTrue(ReferenceEquals(track, fixture.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(FindEntity(fixture.Shell, "JavaInstallPagerTabs"))));
        AssertTrue(ReferenceEquals(thumbMotion, fixture.Shell.Tree.GetComponent<XsrUiTransition>(track.Thumb)));
        AssertTrue(ReferenceEquals(scrollState, fixture.Shell.Tree.GetComponent<XsrUiScroll>(scrollEntity)));

        fixture.Shell.Renderer.SetTextInputValue(input, "literal renamed draft");
        fixture.Shell.Renderer.Focus(input);
        AssertTrue(fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left));
        int caret = inputState.SelectionEnd;
        double offset = scrollState.OffsetY;
        fixture.Controller.SetStartupOffline();
        scene = fixture.Shell.Render(size);
        AssertEqual("literal renamed draft", inputState.ReadDraft());
        AssertEqual(caret, inputState.SelectionEnd);
        AssertEqual(input, fixture.Shell.Renderer.Focused);
        AssertClose(offset, scrollState.OffsetY);
        AssertEqual(row, FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity);
        AssertTrue(fixture.Shell.Tree.Children(rows).Count < 30);
        foreach (var child in fixture.Shell.Tree.Children(rows))
            AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiSelection>(child) is not null
                || fixture.Shell.Tree.Name(child).StartsWith("CatalogBefore.", StringComparison.Ordinal)
                || fixture.Shell.Tree.Name(child).StartsWith("CatalogAfter.", StringComparison.Ordinal));
    }

    private static void LaunchInstallCatalogLiteralDataUsesBoundedIdentity()
    {
        const string version = "provider {data version} <Text /> " + "unsafe";
        const string detail = "provider <Button Command=\"ui.page.back\" /> {state launch.action.label}";
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), installSource: new LiteralInstallCatalogSource(version, detail));
        fixture.Shell.Renderer.ReducedMotion = true;
        Emit(fixture.Intents, "ui.navigation.download");
        Emit(fixture.Intents, "ui.install.java");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        var scene = fixture.Shell.Render(new(900, 600));
        var row = scene.Nodes.Single(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("CatalogRow:game:", StringComparison.Ordinal));
        AssertTrue(fixture.Shell.Tree.Name(row.Entity).Length <= 128);
        AssertTrue(scene.Nodes.Any(node => node.Text == version));
        AssertTrue(row.Label!.Contains(version, StringComparison.Ordinal));
        AssertEqual(1, fixture.Shell.Tree.Children(FindEntity(fixture.Shell, "JavaMinecraftVersions")).Count);
        AssertFalse(scene.Nodes.Any(node => node.Text == "unsafe"));
        var text = scene.Nodes.Single(node => node.Text == version).Entity;
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiText>(text)!.Localize);
    }

    private static void LaunchInstallCatalogLocaleReloadRetainsLiteralDraftAndOwnedRows()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), regionalPolicy: new("US"));
        fixture.Shell.Renderer.ReducedMotion = true;
        Emit(fixture.Intents, "ui.navigation.download");
        Emit(fixture.Intents, "ui.install.java");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        var size = new XsrUiSize(900, 600);
        var scene = fixture.Shell.Render(size);
        var row = FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity;
        var input = FindByKey(fixture.Shell, scene, "JavaInstallVersionInput").Entity;
        fixture.Shell.Renderer.SetTextInputValue(input, "1.20");
        fixture.Shell.Renderer.Focus(input);
        AssertTrue(fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left, extendSelection: true));
        AssertTrue(fixture.Shell.Renderer.SetTextPreedit(input, "literal <preedit> {state secret}"));
        var draft = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!;
        int start = draft.SelectionStart, end = draft.SelectionEnd;
        var text = fixture.Shell.Tree.GetComponent<XsrUiText>(FindEntity(fixture.Shell, "CatalogName:game:1.20.1"))!;
        fixture.Shell.Renderer.TextLocalizer = source => source switch
        {
            "开始安装" => "Start install",
            "开始安装 Java 版 Minecraft" => "Install Java Minecraft",
            "安装 Java 版" => "Install Java",
            "正在获取版本…" => "Loading versions",
            "搜索版本" => "Search versions",
            "正式版" => "Release",
            "选择 {data version}" => "Choose {data version}",
            "Minecraft，当前页" => "Minecraft current",
            "Start install" or "Install Java Minecraft" or "Install Java" or "Release" => "Unexpected second translation",
            _ => source,
        };
        scene = fixture.Shell.Render(size);
        AssertEqual(input, FindByKey(fixture.Shell, scene, "JavaInstallVersionInput").Entity);
        AssertEqual(row, FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity);
        AssertEqual(input, fixture.Shell.Renderer.Focused);
        AssertEqual("1.20", draft.ReadDraft());
        AssertEqual(start, draft.SelectionStart);
        AssertEqual(end, draft.SelectionEnd);
        AssertEqual("literal <preedit> {state secret}", FindByKey(fixture.Shell, scene, "JavaInstallVersionInput").TextInput!.Value.Preedit);
        AssertEqual("Search versions", FindByKey(fixture.Shell, scene, "JavaInstallVersionInput").TextInput!.Value.Placeholder);
        AssertEqual("Start install", FindByKey(fixture.Shell, scene, "JavaInstallStart").Text);
        AssertEqual("Choose 1.20.1", FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Label);
        AssertEqual("1.20.1", text.Content);
        AssertFalse(text.Localize);
        AssertTrue(ReferenceEquals(text, fixture.Shell.Tree.GetComponent<XsrUiText>(FindEntity(fixture.Shell, "CatalogName:game:1.20.1"))));
    }

    private sealed class LiteralInstallCatalogSource(string version, string detail) : IInstallCatalogSource
    {
        public Task<IReadOnlyList<InstallCatalogVersion>> GetGamesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<InstallCatalogVersion>>([new(version, detail)]);
        public Task<IReadOnlyList<InstallCatalogVersion>> GetLoadersAsync(InstallLoader loader, string game, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<InstallCatalogVersion>>([]);
    }
}
