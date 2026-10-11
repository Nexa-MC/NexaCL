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
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{

    private static void StagedTaskPageRendersCleanBetweenChanges()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        using DesktopTaskBubblePresenter bubble = new(fixture.Shell, fixture.Store);
        using TaskCenterRuntime taskCenter = TaskCenterRuntimeComposer.Compose(fixture.Foundation.Host);
        using TaskCenterController controller = new(
            fixture.Shell, fixture.Intents, taskCenter.Commands, fixture.Store, bubble);
        TaskCenterService tasks = fixture.Foundation.Host.Tasks;
        using ITaskCenterTask install = tasks.Begin(new TaskCenterStart(
            "install:1", "安装 Minecraft 1.20.1", ["版本信息", "游戏文件"]));
        install.Report("游戏文件", "下载中", 0.4, 2, 5, 100);
        Emit(fixture.Intents, "ui.tasks.open");
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(controller.IsStaged);

        // The staged page must not restyle its cards every frame: with no entry change the
        // second render returns the cached scene. Per-frame SetComponent calls dirty the
        // entity as Structure and spin the render loop (the install-start freeze class).
        XsrUiScene first = fixture.Shell.Render(new XsrUiSize(1280, 800));
        XsrUiScene second = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(ReferenceEquals(first, second));

        // A real entry change still updates the card.
        install.Report("游戏文件", "下载中", 0.8, 4, 5, 100);
        XsrUiScene updated = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertFalse(ReferenceEquals(second, updated));
    }

    private static void InstallFailureLeavesTheIdleTreeClean()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        using DesktopTaskBubblePresenter bubble = new(fixture.Shell, fixture.Store);
        using TaskCenterRuntime taskCenter = TaskCenterRuntimeComposer.Compose(fixture.Foundation.Host);
        using TaskCenterController controller = new(
            fixture.Shell, fixture.Intents, taskCenter.Commands, fixture.Store, bubble);
        using Nexa.Services.Minecraft.Install.MinecraftInstallService install = new(
            fixture.Foundation.Host.Tasks,
            fixture.Foundation.Host.Downloads,
            metadata: new FailingInstallMetadata());
        string root = Path.Combine(fixture.TemporaryDirectory, "failed-install");

        XsrResult<Nexa.Services.Minecraft.Install.MinecraftInstallResult> result = install.InstallAsync(
            new Nexa.Services.Minecraft.Install.MinecraftInstallCommand(root, "1.20.1"))
            .GetAwaiter().GetResult();
        AssertFalse(result.IsSuccess);

        // A failure must not leave a per-frame dirty writer behind: with no state changes,
        // the second render returns the cached scene (reference-equal). A spin would rebuild
        // it every frame — the class of freeze reported after install failures.
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        XsrUiScene first = fixture.Shell.Render(new XsrUiSize(1280, 800));
        XsrUiScene second = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(ReferenceEquals(first, second));
    }

    private static void TaskBubbleFollowsSummaryAndYieldsToPage()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        using DesktopTaskBubblePresenter bubble = new(fixture.Shell, fixture.Store);
        fixture.Shell.Renderer.ReducedMotion = true;
        TaskCenterService tasks = fixture.Foundation.Host.Tasks;

        // No tasks: the dock slot stays empty.
        AssertFalse(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-bubble"));

        using ITaskCenterTask install = tasks.Begin(new TaskCenterStart(
            "install:1", "安装 Minecraft 1.20.1", ["版本信息", "游戏文件"]));
        install.Report("游戏文件", "下载中", 0.4, 2, 5, 100);
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        XsrUiSceneNode root = FindByKey(fixture.Shell, scene, "task-bubble");
        // The bubble hugs the bottom-right dock inset.
        AssertTrue(root.Rect.X >= 1280 - 18 - 48 && root.Rect.X <= 1280 - 18);
        AssertTrue(root.Rect.Y >= 800 - 18 - 48 && root.Rect.Y <= 800 - 18);
        AssertTrue(root.Label!.Contains("40%", StringComparison.Ordinal));

        // The page owns the corner while it is open.
        bubble.SetPageVisible(true);
        AssertFalse(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-bubble"));
        bubble.SetPageVisible(false);
        AssertTrue(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-bubble"));

        // Completion keeps the bubble visible and filled until acknowledged.
        install.Complete("安装完成");
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(Math.Abs(fixture.Shell.Tree.GetComponent<XsrUiProgress>(bubble.FillEntity)!.Target - 1d) < 0.0001);

        AssertTrue(tasks.Dismiss("install:1"));
        AssertFalse(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-bubble"));
    }

    private static void TaskBubbleUsesCompactProgressTrack()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        using DesktopTaskBubblePresenter bubble = new(fixture.Shell, fixture.Store);
        TaskCenterService tasks = fixture.Foundation.Host.Tasks;

        using ITaskCenterTask install = tasks.Begin(new TaskCenterStart(
            "install:2", "安装", ["游戏文件"]));
        install.Report("游戏文件", "下载中", 0.5, 1, 2, 0);
        fixture.Shell.Render(new XsrUiSize(1280, 800));

        XsrUiProgress fill = fixture.Shell.Tree.GetComponent<XsrUiProgress>(bubble.FillEntity)!;
        AssertEqual(XsrUiProgressFillAnchor.Leading, fill.Anchor);
        AssertTrue(Math.Abs(fill.Target - 0.5) < 0.0001);
        AssertFalse(fixture.Shell.Render(new XsrUiSize(1280, 800)).Nodes.Any(node => node.Entity == bubble.FillEntity));
        var dock = FindByKey(fixture.Shell, fixture.Shell.Render(new(1280, 800)), "task-bubble");
        fixture.Shell.Renderer.PointerMoved(new(dock.Rect.X + 24, dock.Rect.Y + 24));
        fixture.Shell.Renderer.SetCapsulePresentationProgress(dock.Entity, 1);
        AssertTrue(fixture.Shell.Render(new XsrUiSize(1280, 800)).Nodes.Any(node => node.Entity == bubble.FillEntity));
        fixture.Shell.Renderer.SetProgressPresentation(bubble.FillEntity, 0.5);
        var presented = fixture.Shell.Render(new XsrUiSize(1280, 800)).Nodes.Single(node => node.Entity == bubble.FillEntity);
        AssertEqual(68d, presented.Rect.Width);
        AssertEqual(3d, presented.Rect.Height);
        // The expanded caption and the bottom icon share one continuous input owner.
        var expanded = FindByKey(fixture.Shell, fixture.Shell.Render(new(1280, 800)), "task-bubble");
        var top = new XsrUiPoint(expanded.Rect.X + 24, expanded.Rect.Y + 12);
        fixture.Shell.Renderer.PointerMoved(top);
        AssertEqual(48d, FindByKey(fixture.Shell, fixture.Shell.Render(new(1280, 800)), "task-bubble").Rect.Height);
        AssertEqual(208d, FindByKey(fixture.Shell, fixture.Shell.Render(new(1280, 800)), "task-bubble").Rect.Width);
        string? emitted = null;
        fixture.Intents.IntentEmitted += (_, e) => emitted = e.Intent.Command.Value;
        AssertTrue(fixture.Shell.Renderer.PointerPressed(top));
        AssertTrue(fixture.Shell.Renderer.PointerReleased(top));
        AssertEqual("ui.tasks.open", emitted);

        // The presented fill is the renderer-owned catch-up value; with the animation clock
        // parked it must still be clamped between zero and the target.
        AssertTrue(fill.Presented >= 0d && fill.Presented <= fill.Target + 0.0001);
    }

    private static void TaskCenterPageReconcilesCardsAndRoutesActions()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        using DesktopTaskBubblePresenter bubble = new(fixture.Shell, fixture.Store);
        using TaskCenterRuntime taskCenter = TaskCenterRuntimeComposer.Compose(fixture.Foundation.Host);
        using TaskCenterController controller = new(
            fixture.Shell, fixture.Intents, taskCenter.Commands, fixture.Store, bubble);
        fixture.Shell.Renderer.ReducedMotion = true;
        TaskCenterService tasks = fixture.Foundation.Host.Tasks;

        using ITaskCenterTask install = tasks.Begin(new TaskCenterStart(
            "install:1", "安装 Minecraft 1.20.1", ["版本信息", "游戏文件"]));
        install.Report("游戏文件", "下载中", 0.5, 2, 5, 2048);
        using ITaskCenterTask skin = tasks.Begin(new TaskCenterStart(
            "download:1", "下载皮肤", ["下载文件"]));
        skin.Complete("已保存");

        Emit(fixture.Intents, "ui.tasks.open");
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(controller.IsStaged);
        AssertEqual("任务中心", FindByKey(fixture.Shell, scene, "TitleSubpage").Text);
        // A clipped zero-width fill must reach the backend so its animation can start.
        AssertTrue(HasKey(fixture.Shell, scene, "TaskCardFill"));
        AssertTrue(HasKey(fixture.Shell, scene, "task-card:install:1"));
        AssertTrue(HasKey(fixture.Shell, scene, "task-card:download:1"));
        AssertTrue(scene.Nodes.Any(node => node.Text is not null && node.Text.Contains(
            "安装 Minecraft 1.20.1", StringComparison.Ordinal)));
        AssertTrue(scene.Nodes.Any(node => node.Text is not null && node.Text.Contains("游戏文件 · 下载中", StringComparison.Ordinal)));
        AssertTrue(scene.Nodes.Any(node => node.Text is not null && node.Text.Contains("50%", StringComparison.Ordinal)));
        // The header aggregates live work; the finished task keeps its card until dismissed.
        AssertTrue(FindByKey(fixture.Shell, scene, "TaskCenterSummary").Text!.Contains(
            "1 个任务进行中", StringComparison.Ordinal));

        AssertFalse(scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("task-card-step:", StringComparison.Ordinal)));
        var disclosure = scene.Nodes.First(node => fixture.Shell.Tree.Name(node.Entity) == "TaskCardDetails");
        AssertTrue(fixture.Shell.Renderer.Activate(disclosure.Entity));
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(scene.Nodes.Any(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("task-card-step:", StringComparison.Ordinal)));
        AssertTrue(fixture.Shell.Renderer.Activate(disclosure.Entity));
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        // Cancel routes through the typed command to the owning token.
        XsrUiEntityId installAction = controller.CardAction("install:1");
        Emit(fixture.Intents, "ui.tasks.card.cancel", installAction);
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(install.CancellationToken.IsCancellationRequested);
        install.Canceled();

        // Dismiss removes a terminal card; clear-finished sweeps the rest.
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        Emit(fixture.Intents, "ui.tasks.card.dismiss", controller.CardAction("download:1"));
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertFalse(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-card:download:1"));
        Emit(fixture.Intents, "ui.tasks.clear-finished");
        fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertFalse(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-card:install:1"));
        AssertTrue(FindByKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "TaskCenterSummary")
            .Text!.Contains("暂无任务", StringComparison.Ordinal));
    }

    private static void TaskCenterPageYieldsBubbleAndReclaimsOnBack()
    {
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        using DesktopTaskBubblePresenter bubble = new(fixture.Shell, fixture.Store);
        using TaskCenterRuntime taskCenter = TaskCenterRuntimeComposer.Compose(fixture.Foundation.Host);
        using TaskCenterController controller = new(
            fixture.Shell, fixture.Intents, taskCenter.Commands, fixture.Store, bubble);
        fixture.Shell.Renderer.ReducedMotion = true;
        TaskCenterService tasks = fixture.Foundation.Host.Tasks;
        using ITaskCenterTask install = tasks.Begin(new TaskCenterStart("install:1", "安装", ["游戏文件"]));

        AssertTrue(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-bubble"));
        Emit(fixture.Intents, "ui.tasks.open");
        // The page owns the corner while staged: bubble hidden, card visible.
        AssertFalse(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-bubble"));
        AssertTrue(HasKey(fixture.Shell, fixture.Shell.Render(new XsrUiSize(1280, 800)), "task-card:install:1"));

        // The shared back affordance pops the page; the next frame reconciles staged state
        // and the bubble reclaims the corner.
        Emit(fixture.Intents, "ui.page.back");
        XsrUiScene scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertFalse(controller.IsStaged);
        AssertFalse(HasKey(fixture.Shell, scene, "task-card:install:1"));
        // The bubble reconciles on the frame after the controller unstaged the page (the
        // unstage happens during this controller's FramePreparing, after the bubble's).
        scene = fixture.Shell.Render(new XsrUiSize(1280, 800));
        AssertTrue(HasKey(fixture.Shell, scene, "task-bubble"));
    }
}
