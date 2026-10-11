using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Services.Tasks;
using Nexa.UI.Next;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{

    private static void LaunchPageReplicatesLegacyLayout()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));

        AssertEqual("下载游戏", FindByKey(fixture.Shell, scene, "LaunchButtonText").Text);
        AssertTrue(FindByKey(fixture.Shell, scene, "LaunchButton").IsClickable);
        AssertEqual("账户", FindByKey(fixture.Shell, scene, "AccountHeader").Text);
        AssertFalse(HasKey(fixture.Shell, scene, "AccountBadgeText"));
        AssertEqual("版本", FindByKey(fixture.Shell, scene, "VersionHeader").Text);
        AssertEqual("关于 Nexa", FindByKey(fixture.Shell, scene, "AboutTitle").Text);

        AssertTrue(FindByKey(fixture.Shell, scene, "AccountHint").Text!.Contains("还没有账户档案", StringComparison.Ordinal));
        AssertFalse(HasKey(fixture.Shell, scene, "AccountName"));
        AssertEqual(
            ReadCell(fixture.Store, LaunchPageState.InstanceSummaryKey),
            FindByKey(fixture.Shell, scene, "VersionName").Text);

        XsrUiSceneNode header = FindByKey(fixture.Shell, scene, "AccountHeader");
        AssertEqual(18, header.VisualStyle.FontSize);
        AssertEqual(600, header.VisualStyle.FontWeight);
        XsrUiSceneNode versionName = FindByKey(fixture.Shell, scene, "VersionName");
        AssertEqual(20, versionName.VisualStyle.FontSize);
        AssertEqual(600, versionName.VisualStyle.FontWeight);
        AssertEqual(
            XsrUiTextAlignment.Center,
            FindByKey(fixture.Shell, scene, "LaunchButton").VisualStyle.TextAlignment);

        AssertEqual("未找到可启动的游戏版本", ReadCell(fixture.Store, LaunchPageState.InstanceSummaryKey));
        // Downloading a game does not require an account. Only launch requires selection.
        AssertEqual("下载游戏", ReadCell(fixture.Store, LaunchPageState.ActionLabelKey));
        AssertTrue(FindByKey(fixture.Shell, scene, "LaunchButton").IsClickable);
        AssertEqual(string.Empty, ReadCell(fixture.Store, LaunchPageState.SelectedInstanceKey));
        AssertFalse(HasKey(fixture.Shell, scene, "LaunchStatus"));
        AssertFalse(HasKey(fixture.Shell, scene, "LaunchFeedback"));
        AssertFalse(HasKey(fixture.Shell, scene, "AccountSummary"));
    }

    private static void LaunchPageUsesCompactGutters()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();

        AssertCompactLaunchGeometry(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), contentWidth: 1120, contentHeight: 716);
        AssertCompactLaunchGeometry(fixture.Shell, fixture.Shell.Render(new XsrUiSize(850, 500)), contentWidth: 690, contentHeight: 416);
        AssertCompactLaunchGeometry(fixture.Shell, fixture.Shell.Render(new XsrUiSize(810, 470)), contentWidth: 650, contentHeight: 386);
    }

    private static void AssertCompactLaunchGeometry(
        XsrUiShell shell,
        XsrUiScene scene,
        double contentWidth,
        double contentHeight)
    {
        const double contentX = 140;
        const double contentY = 68;
        const double columnGap = 16;
        const double rightCardGap = 12;
        const double versionCardHeight = 184;
        double distributableWidth = contentWidth - columnGap;
        double expectedAccountWidth = Math.Min(360, distributableWidth * 0.92 / (0.92 + 1.35));
        double expectedRightWidth = distributableWidth - expectedAccountWidth;

        XsrUiRect account = FindByKey(shell, scene, "CardAccount").Rect;
        XsrUiRect version = FindByKey(shell, scene, "CardVersion").Rect;
        XsrUiRect about = FindByKey(shell, scene, "CardAbout").Rect;
        XsrUiRect accountAdd = FindByKey(shell, scene, "AccountAdd").Rect;
        XsrUiRect accountHeader = FindByKey(shell, scene, "AccountHeaderRow").Rect;
        XsrUiRect accountContent = FindByKey(shell, scene, "AccountContent").Rect;
        XsrUiRect accountBody = FindByKey(shell, scene, "AccountBody").Rect;

        AssertRectClose(new XsrUiRect(contentX, contentY, expectedAccountWidth, contentHeight), account);
        AssertRectClose(
            new XsrUiRect(contentX + expectedAccountWidth + columnGap, contentY, expectedRightWidth, versionCardHeight),
            version);
        AssertRectClose(
            new XsrUiRect(version.X, contentY + versionCardHeight + rightCardGap, expectedRightWidth, contentHeight - versionCardHeight - rightCardGap),
            about);
        AssertClose(accountContent.X + accountContent.Width, accountAdd.X + accountAdd.Width);
        AssertClose(32, accountHeader.Height);
        AssertClose(accountContent.Y + accountContent.Height, accountBody.Y + accountBody.Height);
        AssertTrue(version.Y + version.Height <= about.Y);
    }

    private static void NavigationIntentsRouteBetweenPages()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        using var productPages = new LaunchFixtureNavigationPages(fixture);
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();

        Emit(fixture.Intents, "ui.navigation.settings");
        XsrUiScene settings = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, settings, "SettingsPage"));
        AssertFalse(HasKey(fixture.Shell, settings, "MigrationCard"));
        AssertFalse(HasKey(fixture.Shell, settings, "LaunchButton"));

        Emit(fixture.Intents, "ui.navigation.launch");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        XsrUiScene launch = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, launch, "LaunchButton"));
        AssertFalse(HasKey(fixture.Shell, launch, "MigrationCard"));
    }

    private static void DownloadAndInstanceActionsRouteToInstallationAndVersionManagement()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();

        Emit(fixture.Intents, "ui.launch.primary");
        AssertEqual(XsrSemanticId.Parse("navigation.download"), fixture.Shell.SelectedNavigationId);
        XsrUiScene install = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, install, "InstallPage"));
        AssertTrue(HasKey(fixture.Shell, install, "InstallJavaChoice"));
        AssertFalse(HasKey(fixture.Shell, install, "MigrationCard"));
        AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(notification =>
            notification.Level == DesktopNotificationLevel.Info
            && notification.Message == "请在安装页选择或下载游戏版本。"));

        Emit(fixture.Intents, "ui.navigation.launch");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        Emit(fixture.Intents, "ui.launch.instances");
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, scene, "VersionListPage"));
        AssertEqual("选择版本", FindByKey(fixture.Shell, scene, "TitleSubpage").Text);
        AssertFalse(HasKey(fixture.Shell, scene, "LaunchButton"));
    }

    private static void InstallationEntryPresentsJavaAndBedrockSubpages()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();

        Emit(fixture.Intents, "ui.navigation.download");
        XsrUiScene root = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, root, "InstallPage"));
        XsrUiSceneNode javaChoice = FindByKey(fixture.Shell, root, "InstallJavaChoice");
        XsrUiSceneNode bedrockChoice = FindByKey(fixture.Shell, root, "InstallBedrockChoice");
        AssertEqual("安装 Java 版 Minecraft", javaChoice.Label);
        AssertEqual("安装 Bedrock 版 Minecraft", bedrockChoice.Label);
        AssertEqual("Java", FindByKey(fixture.Shell, root, "InstallJavaTitle").Text);
        AssertEqual("Bedrock", FindByKey(fixture.Shell, root, "InstallBedrockTitle").Text);
        AssertClose(javaChoice.Rect.Width, bedrockChoice.Rect.Width);
        AssertClose(javaChoice.Rect.Height, bedrockChoice.Rect.Height);
        AssertClose(javaChoice.Rect.Y, bedrockChoice.Rect.Y);
        AssertClose(12, bedrockChoice.Rect.X - javaChoice.Rect.X - javaChoice.Rect.Width);
        AssertTrue(javaChoice.Rect.Width > 450 && javaChoice.Rect.Height > 500);
        AssertFalse(HasKey(fixture.Shell, root, "InstallTitle"));
        AssertFalse(HasKey(fixture.Shell, root, "InstallDescription"));
        AssertFalse(root.Nodes.Any(node => node.Label is "InstallJavaChoice" or "InstallBedrockChoice"));
        AssertFalse(root.Nodes.Any(node => node.Label is "Java 版渐变背景图区域" or "Bedrock 版渐变背景图区域"));

        Emit(fixture.Intents, "ui.install.java");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        XsrUiScene java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, java, "JavaInstallPage"));
        AssertEqual("安装 Java 版", FindByKey(fixture.Shell, java, "TitleSubpage").Text);
        AssertFalse(HasKey(fixture.Shell, java, "JavaInstallTitle"));
        AssertFalse(HasKey(fixture.Shell, java, "JavaInstallDescription"));
        XsrUiSceneNode input = FindByKey(fixture.Shell, java, "JavaInstallVersionInput");
        XsrUiSceneNode start = FindByKey(fixture.Shell, java, "JavaInstallStart");
        AssertEqual("", input.TextInput!.Value.DisplayText);
        AssertTrue(start.Rect.X >= input.Rect.X + input.Rect.Width);
        AssertEqual(XsrUiCornerRadii.Pill(40), start.VisualStyle.CornerRadius);
        XsrUiSceneNode pager = FindByKey(fixture.Shell, java, "JavaInstallPager");
        AssertEqual(XsrUiOrientation.Horizontal, pager.Pager!.Value.Direction);
        AssertEqual(0, pager.Pager!.Value.PageIndex);
        XsrUiSceneNode rail = FindByKey(fixture.Shell, java, "JavaInstallPagerTabs");
        AssertTrue(pager.Rect.Y >= rail.Rect.Y + rail.Rect.Height);
        AssertClose(0, FindByKey(fixture.Shell, java, "JavaMinecraftTab").VisualStyle.CornerRadius);
        AssertTrue(input.Rect.Y + input.Rect.Height <= rail.Rect.Y);
        foreach (XsrUiSize size in new[] { new XsrUiSize(1024, 600), new XsrUiSize(850, 520) })
        {
            XsrUiScene compact = fixture.Shell.Render(size);
            XsrUiSceneNode compactPager = FindByKey(fixture.Shell, compact, "JavaInstallPager");
            XsrUiSceneNode compactAction = FindByKey(fixture.Shell, compact, "JavaInstallStart");
            AssertTrue(compactPager.Rect.Width > 400);
            AssertTrue(compactAction.Rect.Y + compactAction.Rect.Height <= compactPager.Rect.Y);
            AssertTrue(compactAction.Rect.Y + compactAction.Rect.Height <= size.Height);
        }
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        pager = FindByKey(fixture.Shell, java, "JavaInstallPager");

        AssertEqual(1, pager.Pager!.Value.PageCount);
        AssertFalse(IsVisible(fixture.Shell, "JavaForgePage"));
        AssertFalse(IsVisible(fixture.Shell, "JavaFabricApiPage"));
        fixture.Shell.Renderer.ReducedMotion = true;
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, java, "CatalogRow:game:1.20.6").Entity));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual(7, FindByKey(fixture.Shell, java, "JavaInstallPager").Pager!.Value.PageCount);
        AssertTrue(IsVisible(fixture.Shell, "JavaFabricTab"));
        AssertTrue(IsVisible(fixture.Shell, "JavaForgeTab"));
        AssertFalse(IsVisible(fixture.Shell, "JavaCleanroomTab"));
        AssertTrue(FindByKey(fixture.Shell, java, "CatalogRow:game:1.20.6").IsSelected);
        Emit(fixture.Intents, "ui.install.page.fabric");
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, java, "CatalogRow:loader:fixture.2").Entity));
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual(3, FindByKey(fixture.Shell, java, "JavaInstallPager").Pager!.Value.PageCount);
        AssertTrue(IsVisible(fixture.Shell, "JavaFabricApiTab"));
        AssertFalse(IsVisible(fixture.Shell, "JavaForgeTab"));
        AssertFalse(IsVisible(fixture.Shell, "JavaQslTab"));
        AssertTrue(FindByKey(fixture.Shell, java, "CatalogRow:loader:fixture.2").IsSelected);
        Emit(fixture.Intents, "ui.install.addon.fabric-api");
        Emit(fixture.Intents, "ui.install.start");
        // Without the install runtime wired (fixture), the start button still refuses loudly
        // instead of silently doing nothing; the real pipeline is covered by services tests.
        AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(notification =>
            notification.Level == DesktopNotificationLevel.Warn && notification.Message.Contains("无法开始安装", StringComparison.Ordinal)));
        Emit(fixture.Intents, "ui.install.loader.vanilla");
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual(7, FindByKey(fixture.Shell, java, "JavaInstallPager").Pager!.Value.PageCount);
        Emit(fixture.Intents, "ui.install.page.forge");
        Emit(fixture.Intents, "ui.install.loader.forge");
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual(3, FindByKey(fixture.Shell, java, "JavaInstallPager").Pager!.Value.PageCount);
        AssertTrue(IsVisible(fixture.Shell, "JavaOptiFineTab"));
        AssertFalse(IsVisible(fixture.Shell, "JavaFabricApiTab"));
        Emit(fixture.Intents, "ui.install.loader.vanilla");
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        Emit(fixture.Intents, "ui.install.page.quilt");
        Emit(fixture.Intents, "ui.install.loader.quilt");
        java = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual(2, FindByKey(fixture.Shell, java, "JavaInstallPager").Pager!.Value.PageCount);
        AssertFalse(IsVisible(fixture.Shell, "JavaQslTab"));
        AssertFalse(IsVisible(fixture.Shell, "JavaFabricApiTab"));

        Emit(fixture.Intents, "ui.page.back");
        XsrUiScene backAtRoot = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, backAtRoot, "InstallPage"));
        Emit(fixture.Intents, "ui.install.bedrock");
        XsrUiScene bedrock = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, bedrock, "BedrockInstallPage"));
        AssertEqual("安装 Bedrock 版", FindByKey(fixture.Shell, bedrock, "TitleSubpage").Text);
        AssertTrue(FindByKey(fixture.Shell, bedrock, "BedrockInstallDescription").Text!
            .Contains("Microsoft Store", StringComparison.Ordinal));
        AssertFalse(HasKey(fixture.Shell, bedrock, "JavaInstallStart"));
    }

    private static void LaunchPageSemanticsNeverExposeInternalKeys()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        XsrUiSceneNode button = FindByKey(fixture.Shell, scene, "LaunchButton");

        AssertEqual("下载游戏", FindByKey(fixture.Shell, scene, "LaunchButtonText").Text);
        AssertEqual("下载游戏", button.Label);
        AssertEqual("选择版本", FindByKey(fixture.Shell, scene, "InstanceListButton").Label);
        AssertFalse(scene.Nodes.Any(node => node.Label is "LaunchButton" or "InstanceListButton" or "VersionName" or "CardAccount"));
    }

    private static void InstanceScanPublishesWithoutForeignTreeMutation()
    {
        ControllableInstanceSource source = new();
        using LaunchPageFixture fixture = new(source, addProfile: true);
        _ = fixture.Shell.Render(new XsrUiSize(1280, 800));
        XsrUiEntityId[] dirtyBeforeWorker = [.. fixture.Shell.Tree.DirtyEntities()];

        Task.Run(() => source.Complete(0, [Instance("worker-result")])).GetAwaiter().GetResult();
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        AssertTrue(dirtyBeforeWorker.SequenceEqual(fixture.Shell.Tree.DirtyEntities()));
        AssertEqual("worker-result", ReadCell(fixture.Store, LaunchPageState.SelectedInstanceKey));

        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual("启动游戏", FindByKey(fixture.Shell, scene, "LaunchButtonText").Text);
        AssertEqual("启动游戏", FindByKey(fixture.Shell, scene, "LaunchButton").Label);
    }

    private static void OlderInstanceScanCannotOverwriteLatestGeneration()
    {
        ControllableInstanceSource source = new();
        using LaunchPageFixture fixture = new(source);
        Task latest = fixture.Controller.RefreshInstancesAsync();
        AssertEqual(2, source.Count);
        AssertTrue(source.TokenAt(0).IsCancellationRequested);

        source.Complete(1, [Instance("new-result")]);
        latest.GetAwaiter().GetResult();
        source.Complete(0, [Instance("old-result")]);
        AssertTrue(SpinWait.SpinUntil(() => source.ReturnedCount == 2, TimeSpan.FromSeconds(2)));
        AssertEqual("new-result", ReadCell(fixture.Store, LaunchPageState.SelectedInstanceKey));
        AssertEqual("new-result", ReadCell(fixture.Store, LaunchPageState.InstanceSummaryKey));
    }

    private static void LaunchPrimaryDispatchesProductStartCommand()
    {
        RecordingStartRoute recording = new();
        using MinecraftRuntime runtime = CreateRecordingRuntime(recording);
        using LaunchPageFixture fixture = new(
            new ImmediateInstanceSource([Instance("playable")]),
            runtime,
            addProfile: true,
            ownsMinecraftRuntime: false);
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();

        Emit(fixture.Intents, "ui.launch.primary");
        AssertTrue(SpinWait.SpinUntil(() => recording.LastCommand is not null, TimeSpan.FromSeconds(2)));
        AssertEqual("playable", recording.LastCommand!.InstanceId);
        AssertEqual(0, recording.LastCommand.AccountIndex);
        AssertTrue(SpinWait.SpinUntil(() => fixture.Feedback.Snapshot().Notifications.Any(notification =>
                notification.Level == DesktopNotificationLevel.Info
                && notification.Message == "Minecraft 已启动。"),
            TimeSpan.FromSeconds(2)));
    }

    private static void ExpandNavigationNeverReplacesTheLaunchPage()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();

        // The rail expand/collapse toggle is shell presentation, not a destination: it must
        // never replace the current content page.
        Emit(fixture.Intents, "ui.navigation.expand");
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, scene, "LaunchButton"));
        AssertFalse(HasKey(fixture.Shell, scene, "MigrationCard"));
    }

    private static void AccountCardListsProfilesAndSwitchesSelection()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]), addProfile: true);
        AssertTrue(fixture.Service.AddProfile(new LaunchProfile
        {
            Username = "Second",
            Kind = LaunchProfileKind.Microsoft,
        }).IsSuccess);

        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual("Player", FindByKey(fixture.Shell, scene, "AccountName").Text);
        AssertEqual("离线账户", FindByKey(fixture.Shell, scene, "AccountKind").Text);
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "AccountSwitch").Entity));
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, scene, "account-row:0"));
        AssertTrue(HasKey(fixture.Shell, scene, "account-row:1"));

        // The first profile is selected by default; switching publishes the fact and updates
        // the account presentation.
        AssertEqual(0, fixture.Service.SelectedIndex);
        AssertEqual("Player", ReadCell(fixture.Store, LaunchPageState.ProfileNameKey));

        XsrUiEntityId secondRow = FindEntity(fixture.Shell, "account-row:1");
        AssertTrue(secondRow.IsAssigned);
        // Text and icon children must route real pointer input to the template's button.
        XsrUiRect name = FindByKey(fixture.Shell, scene, "ProfileName:1").Rect;
        XsrUiPoint click = new(name.X + 8, name.Y + 8);
        AssertTrue(fixture.Shell.Renderer.PointerPressed(click));
        AssertTrue(fixture.Shell.Renderer.PointerReleased(click));
        AssertTrue(SpinWait.SpinUntil(() => fixture.Service.SelectedIndex == 1, TimeSpan.FromSeconds(2)));
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertEqual(1, fixture.Service.SelectedIndex);
        AssertEqual(1, ReadCellInt(fixture.Store, AccountService.SelectedKey));
        AssertEqual("Second", ReadCell(fixture.Store, LaunchPageState.ProfileNameKey));
        AssertFalse(HasKey(fixture.Shell, scene, "AccountSummary"));

        AssertEqual("Second", FindByKey(fixture.Shell, scene, "AccountName").Text);
        AssertEqual("Microsoft 账户", FindByKey(fixture.Shell, scene, "AccountKind").Text);
        AssertFalse(HasKey(fixture.Shell, scene, "account-row:1"));
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "AccountSwitch").Entity));
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(FindByKey(fixture.Shell, scene, "account-row:1").IsSelected);
    }
}
