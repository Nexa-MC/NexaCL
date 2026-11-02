using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void LaunchInstallHomeOwnedProfilesRetainFocusScrollAndLiteralFactsAcrossLocale()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Renderer.ReducedMotion = true;
        const string name = "设置";
        const string detail = "provider <literal> {data secret} {state launch.action.label}";
        AssertTrue(AddFixtureProfile(fixture.Service, new LaunchProfile { Username = name, Info = detail, Kind = LaunchProfileKind.Offline }).IsSuccess);
        for (int index = 1; index < 10; index++)
            AssertTrue(AddFixtureProfile(fixture.Service, new LaunchProfile { Username = "Player" + index, Kind = LaunchProfileKind.Offline }).IsSuccess);
        Emit(fixture.Intents, "ui.account.switch");
        bool translated = false;
        fixture.Shell.Renderer.TextLocalizer = source => !translated ? source : source switch
        {
            "账户" => "Account",
            "切换档案" => "Switch profile",
            "离线账户" => "Offline account",
            "选择 {data name}，{data kind}" => "Choose {data name}: {data kind}",
            "删除档案 {data name}" => "Delete {data name}",
            name => "Unexpected translated username",
            detail => "Unexpected translated provider metadata",
            _ => source,
        };
        var size = new XsrUiSize(900, 650);
        var shell = fixture.Shell;
        var scene = shell.Render(size);
        var workspace = FindEntity(shell, "LaunchHomeWorkspace");
        var owner = shell.Tree.Parent(workspace);
        var rows = FindEntity(shell, "AccountRows");
        var row = FindByKey(shell, scene, "account-row:0").Entity;
        var profileName = FindEntity(shell, "ProfileName:0");
        var profileDetail = FindEntity(shell, "ProfileDetail:0");
        var selection = shell.Tree.GetComponent<XsrUiSelection>(row)!;
        var input = shell.Tree.GetComponent<XsrUiInput>(row)!;
        var scroll = shell.Tree.GetComponent<XsrUiScroll>(rows)!;
        var bodyTransition = shell.Tree.GetComponent<XsrUiTransition>(FindEntity(shell, "AccountBody"))!;
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(owner) is not null);
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(rows) is not null);
        AssertEqual(rows, shell.Tree.Parent(row));
        AssertTrue(shell.Renderer.Focus(row, showIndicator: false));
        scroll.OffsetY = 20;
        shell.Render(size);
        double offset = scroll.OffsetY;
        translated = true;
        scene = shell.Render(size);
        AssertEqual(workspace, FindEntity(shell, "LaunchHomeWorkspace"));
        AssertEqual(owner, shell.Tree.Parent(workspace));
        AssertEqual(row, FindByKey(shell, scene, "account-row:0").Entity);
        AssertEqual(row, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(input, shell.Tree.GetComponent<XsrUiInput>(row)));
        AssertTrue(ReferenceEquals(selection, shell.Tree.GetComponent<XsrUiSelection>(row)));
        AssertTrue(ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(rows)));
        AssertTrue(ReferenceEquals(bodyTransition, shell.Tree.GetComponent<XsrUiTransition>(FindEntity(shell, "AccountBody"))));
        AssertClose(offset, scroll.OffsetY);
        AssertEqual("Switch profile", FindByKey(shell, scene, "AccountHeader").Text);
        AssertEqual(name, FindByKey(shell, scene, "ProfileName:0").Text);
        AssertEqual(detail, FindByKey(shell, scene, "ProfileDetail:0").Text);
        AssertEqual("Choose " + name + ": Offline account", FindByKey(shell, scene, "account-row:0").Label);
        AssertEqual("Delete " + name, FindByKey(shell, scene, "ProfileDelete:0").Label);
        AssertFalse(shell.Tree.GetComponent<XsrUiText>(profileName)!.Localize);
        AssertFalse(shell.Tree.GetComponent<XsrUiText>(profileDetail)!.Localize);
        AssertEqual(10, shell.Tree.Children(rows).Count);
    }

    private static void LaunchInstallHomeWidgetFractionalDotsRetainEntitiesPagerAndTransitions()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell;
        shell.Renderer.ReducedMotion = false;
        var size = new XsrUiSize(900, 650);
        var scene = shell.Render(size);
        var pagerEntity = FindEntity(shell, "LaunchWidgetPager");
        var pager = shell.Tree.GetComponent<XsrUiPager>(pagerEntity)!;
        var about = FindByKey(shell, scene, "WidgetAboutDot").Entity;
        var trivia = FindByKey(shell, scene, "WidgetTriviaDot").Entity;
        var echo = FindByKey(shell, scene, "WidgetEchoDot").Entity;
        var aboutButton = FindByKey(shell, scene, "WidgetAboutIndicator").Entity;
        var aboutInput = shell.Tree.GetComponent<XsrUiInput>(aboutButton)!;
        var transitionEntity = FindEntity(shell, "TriviaMessage");
        var transition = shell.Tree.GetComponent<XsrUiTransition>(transitionEntity)!;
        var progressEntity = FindEntity(shell, "LaunchButtonProgress");
        var progress = shell.Tree.GetComponent<XsrUiProgress>(progressEntity)!;
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(aboutButton)) is not null);
        AssertTrue(shell.Renderer.MovePager(pagerEntity, 1));
        shell.Renderer.SetPagerPresentationPosition(pagerEntity, .25);
        scene = shell.Render(size);
        AssertClose(13.5, FindByKey(shell, scene, "WidgetAboutDot").Rect.Height);
        AssertClose(8.5, FindByKey(shell, scene, "WidgetTriviaDot").Rect.Height);
        AssertClose(6, FindByKey(shell, scene, "WidgetEchoDot").Rect.Height);
        AssertClose(13.5, FindByKey(shell, scene, "WidgetAboutIndicator").Rect.Height);
        AssertEqual((byte)207, shell.Tree.GetComponent<XsrUiVisualStyle>(about)!.Background.Alpha);
        AssertEqual((byte)112, shell.Tree.GetComponent<XsrUiVisualStyle>(trivia)!.Background.Alpha);
        AssertEqual((byte)64, shell.Tree.GetComponent<XsrUiVisualStyle>(echo)!.Background.Alpha);
        AssertClose(44, FindByKey(shell, scene, "WidgetIndicators").Rect.Height);
        shell.Renderer.SetPagerPresentationPosition(pagerEntity, 1.75);
        scene = shell.Render(size);
        AssertClose(6, FindByKey(shell, scene, "WidgetAboutDot").Rect.Height);
        AssertClose(8.5, FindByKey(shell, scene, "WidgetTriviaDot").Rect.Height);
        AssertClose(13.5, FindByKey(shell, scene, "WidgetEchoDot").Rect.Height);
        AssertEqual(about, FindByKey(shell, scene, "WidgetAboutDot").Entity);
        AssertEqual(trivia, FindByKey(shell, scene, "WidgetTriviaDot").Entity);
        AssertEqual(echo, FindByKey(shell, scene, "WidgetEchoDot").Entity);
        AssertTrue(ReferenceEquals(pager, shell.Tree.GetComponent<XsrUiPager>(pagerEntity)));
        AssertTrue(ReferenceEquals(aboutInput, shell.Tree.GetComponent<XsrUiInput>(aboutButton)));
        AssertTrue(ReferenceEquals(transition, shell.Tree.GetComponent<XsrUiTransition>(transitionEntity)));
        AssertTrue(ReferenceEquals(progress, shell.Tree.GetComponent<XsrUiProgress>(progressEntity)));
        AssertClose(1.75, pager.Position);
    }
}
