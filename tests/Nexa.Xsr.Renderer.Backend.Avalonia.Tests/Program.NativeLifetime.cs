using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{

    private sealed class LifetimeProbeApp : Application
    {
        public static Func<Stream?>? SplashIconFactory { get; set; }

        public static ShutdownMode ObservedShutdownMode { get; private set; }

        public static Window? ObservedMainWindow { get; private set; }

        public static int ObservedWindowCount { get; private set; }

        public static bool ReachedEndOfLifetime { get; private set; }

        public static bool RunRailReducedMotionScenario { get; set; }

        public static double ObservedRailProgressAfterCollapse { get; internal set; } = double.NaN;

        public static double ObservedPageEnterProgressAfterReducedMotion { get; internal set; } = double.NaN;

        public static bool ObservedPageEnterStarted { get; internal set; }

        public static bool ObservedAllStaggeredChildrenSettled { get; internal set; } = true;
        public static Exception? Failure { get; internal set; }

        public static void MarkTerminated() => ReachedEndOfLifetime = true;

        public static void Reset(bool withSplash)
        {
            SplashIconFactory = withSplash ? TinyPng : (Func<Stream?>?)null;
            ObservedShutdownMode = default;
            ObservedMainWindow = null;
            ObservedWindowCount = 0;
            ReachedEndOfLifetime = false;
            RunRailReducedMotionScenario = false;
            ObservedRailProgressAfterCollapse = double.NaN;
            ObservedPageEnterProgressAfterReducedMotion = double.NaN;
            ObservedPageEnterStarted = false;
            ObservedAllStaggeredChildrenSettled = true;
            Failure = null;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                XsrStateStore store = new XsrStateStoreBuilder().Build();
                XsrUiShell shell = XsrUiShellComposer.Compose(store);
                // Reduced motion keeps the startup handoff synchronous for the test.
                shell.Renderer.ReducedMotion = true;
                AvaloniaUiShellWindow window = AvaloniaUiShellLifetime.Compose(
                    desktop,
                    shell,
                    SplashIconFactory?.Invoke(),
                    null);

                ObservedShutdownMode = desktop.ShutdownMode;
                ObservedMainWindow = desktop.MainWindow;
                ObservedWindowCount = desktop.Windows.Count;

                if (RunRailReducedMotionScenario)
                {
                    _ = RunMotionScenariosAsync(window, shell, window.Surface);
                    return;
                }

                // Closing the main window exercises the automatic lifetime contract: the run
                // must terminate and return to the caller.
                Dispatcher.UIThread.Post(
                    () => window.Close(),
                    DispatcherPriority.Background);
            }

            base.OnFrameworkInitializationCompleted();
        }


    }

    private static void LifetimeSplashNeverOwnsProcessAndMainWindowCloseTerminates()
    {
        LifetimeProbeApp.Reset(withSplash: true);
        LifetimeProbeApp.RunRailReducedMotionScenario = true;
        AppBuilder.Configure<LifetimeProbeApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .StartWithClassicDesktopLifetime([]);

        // Reaching this line at all proves the lifetime terminated on main-window close instead
        // of leaving the process running under OnExplicitShutdown.
        LifetimeProbeApp.MarkTerminated();
        if (LifetimeProbeApp.Failure is { } failure) throw new InvalidOperationException("Native bridge scenario failed.", failure);
        AssertTrue(LifetimeProbeApp.ReachedEndOfLifetime);
        AssertEqual(0, AvaloniaUiRasterPool.Shared.Retained.Leases);
        AssertEqual(0, AvaloniaUiRasterPool.Shared.Retained.Entries);
        AssertEqual(ShutdownMode.OnMainWindowClose, LifetimeProbeApp.ObservedShutdownMode);
        AssertTrue(LifetimeProbeApp.ObservedMainWindow is AvaloniaUiShellWindow);
        var main = (AvaloniaUiShellWindow)LifetimeProbeApp.ObservedMainWindow!;
        AssertTrue(main.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent));
        if (main.UsesCompositedEdges)
            AssertTrue(main.Background is ISolidColorBrush { Color.A: 0 });
        else
            AssertTrue(main.Background is LinearGradientBrush background && background.GradientStops.All(stop => stop.Color.A == 255));
        // Under reduced motion the handoff is synchronous: the splash closed the moment the
        // shell window took over the icon, leaving exactly the shell window alive — and its
        // close did not terminate the process early, proving the splash never owned the
        // lifetime.
        AssertEqual(1, LifetimeProbeApp.ObservedWindowCount);

        // ReducedMotionCancelsRunningRailMotion: after the mid-flight policy flip and the
        // collapse, the settled progress must still be the collapsed fact.
        AssertEqual(0, LifetimeProbeApp.ObservedRailProgressAfterCollapse);

        // ReducedMotionSettlesRunningPageEnter: the page enter animation started and the
        // mid-flight policy flip settled it to the final state.
        AssertTrue(LifetimeProbeApp.ObservedPageEnterStarted);
        AssertEqual(1, LifetimeProbeApp.ObservedPageEnterProgressAfterReducedMotion);
        AssertTrue(LifetimeProbeApp.ObservedAllStaggeredChildrenSettled);
    }

    private static async Task RunMotionScenariosAsync(
        AvaloniaUiShellWindow window,
        XsrUiShell shell,
        AvaloniaUiSceneSurface surface)
    {
        try
        {
            await Task.Delay(30).ConfigureAwait(true);
            VerifyMemoryPressureObservationOwnership();
            VerifyRawTouchInputAndPointerOwnership();
            VerifyNativeWindowPointerRouting(window, shell, surface);
            VerifyTypefaceRenderingAndInput();
            VerifyNativeContextMenusPreserveLeftClickAndPasswordRules();
            VerifyTrayCloseAndExplicitExitAdmission(window, shell);
            await VerifyPreparedWindowEntranceAsync(window).ConfigureAwait(true);
            await VerifyTrayVisibilityAsync(window).ConfigureAwait(true);
            VerifyAccessibleContentAndNativeFocus(window, shell, surface);
            VerifyPointerCursorProjection(window, shell, surface);
            VerifyPostNavigationDoubleClickRouting(window);
            await VerifyOverlayReorderAndReentry(shell, surface);
            await VerifyPlatformClipboard(window).ConfigureAwait(true);
            await VerifyConfigurableMotionFrameClock().ConfigureAwait(true);
            VerifyNativeTextEditing(window, shell, surface);
            VerifyNativeScrollbarPointerRouting(window, shell, surface);
            await VerifyNativeSettingsControls(shell, surface);
            VerifyNativeAppearancePreferences(window, shell, surface);
            await VerifyNativeBrandAssetsAndCloseDecoration().ConfigureAwait(true);
            await VerifyNativeThemeActivationAsync().ConfigureAwait(true);
            VerifyReentrantRemovalCommit(shell, surface);
            await VerifyTransitionGroupsAndMedia(shell, surface);
            VerifySharedRasterBudget();
            VerifyCompositeRasterDrawing();
            VerifyWindowActionFeedback(window, surface);
            await VerifySpringIgnoresStaleSceneReads().ConfigureAwait(true);
            await VerifyCapsuleGeometryClock(shell, surface).ConfigureAwait(true);
            await VerifySegmentScrollClock(shell, surface).ConfigureAwait(true);
            await VerifyPagerNativeDragAndClock(window, shell, surface).ConfigureAwait(true);
            await VerifyNativeFileDragThreshold(window, shell, surface).ConfigureAwait(true);
            // ReducedMotionCancelsRunningRailMotion: start the expansion normally, flip the
            // policy mid-flight, then collapse. The shell snaps the progress to the collapsed
            // fact and the running track must never write the expansion back over it.
            shell.SetNavigationExpanded(true);
            await Task.Delay(30).ConfigureAwait(true);
            shell.Renderer.ReducedMotion = true;
            shell.SetNavigationExpanded(false);
            await Task.Delay(300).ConfigureAwait(true);
            LifetimeProbeApp.ObservedRailProgressAfterCollapse = shell.RailPresentationProgress;

            // ReducedMotionSettlesRunningPageEnter: build a page outside the navigator, swap
            // it in, and confirm the enter animation started; flipping the policy then settles
            // the running enter track to its final state.
            shell.Renderer.ReducedMotion = false;
            XsrUiEntityId page = shell.Tree.Create("scenario-page");
            shell.Tree.SetComponent(page, new XsrUiStackPanel(XsrUiOrientation.Vertical));
            shell.Tree.SetComponent(page, new XsrUiSemantic(XsrUiSemanticRole.Page, "scenario"));
            List<XsrUiEntityId> scenarioTexts = [];
            for (int index = 0; index < 8; index++)
            {
                XsrUiEntityId text = shell.Tree.Create($"scenario-page-text-{index}");
                shell.Tree.SetComponent(text, new XsrUiText($"场景页面 {index}"));
                shell.Tree.SetComponent(text, new XsrUiSemantic(XsrUiSemanticRole.Text, "场景页面"));
                shell.Tree.Attach(text, page);
                scenarioTexts.Add(text);
            }

            shell.Stage.Navigation.Replace(page);

            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            double started = 1;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(16).ConfigureAwait(true);
                if (surface.TryGetPresentedEnterProgress(scenarioTexts[0], out started) && started < 1)
                {
                    break;
                }
            }

            LifetimeProbeApp.ObservedPageEnterStarted = started < 1;

            shell.Renderer.ReducedMotion = true;
            deadline = DateTime.UtcNow.AddSeconds(2);
            double settled = double.NaN;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(16).ConfigureAwait(true);
                if (surface.TryGetPresentedEnterProgress(scenarioTexts[0], out settled) && settled >= 1)
                {
                    break;
                }
            }

            LifetimeProbeApp.ObservedPageEnterProgressAfterReducedMotion = settled;

            // Every staggered child — including ones whose delay outlived earlier frames — must
            // settle at the final state.
            LifetimeProbeApp.ObservedAllStaggeredChildrenSettled = scenarioTexts.All(text =>
                surface.TryGetPresentedEnterProgress(text, out double value) && value >= 1);
            VerifyCloseDoesNotRestoreShadows(window, shell, surface);
        }
        catch (Exception error)
        {
            LifetimeProbeApp.Failure = error;
        }
        finally
        {
            window.Close();
        }
    }
}
