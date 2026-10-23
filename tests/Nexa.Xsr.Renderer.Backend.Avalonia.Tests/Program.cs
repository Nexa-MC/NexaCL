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
    private static readonly (string Name, Action Body)[] TestCases =
    [
        ("startup main thread entry completes without native initialization", StartupMainThreadEntryCompletesWithoutNativeInitialization),
        ("theme recovery before native attachment returns the owned lease", ThemeRecoveryBeforeNativeAttachmentReturnsOwnedLease),
        ("shared press feedback is immediate bounded and available without motion", SharedPressFeedbackIsImmediateBoundedAndAvailableWithoutMotion),
        ("neon theme selection honors explicit and system modes", NeonThemeSelectionHonorsExplicitAndSystemModes),
        ("StartupMediaWarmupSettlesMissingLocalAsset", StartupMediaWarmupSettlesMissingLocalAsset),
        ("ScreenshotCodecCropsActualPixelsAndPreservesSource", ScreenshotCodecCropsActualPixelsAndPreservesSource),
        ("CommandPaletteGesturesRequireExactKeysAndAdmittedCallbacks", CommandPaletteGesturesRequireExactKeysAndAdmittedCallbacks),
        ("WorldEditLeaseRejectsNonMacBeforeLoadingNativeLibraries", WorldEditLeaseRejectsNonMacBeforeLoadingNativeLibraries),
        ("MediaFramesRejectUnboundedInputAndOwnedDecoderPauses", MediaFramesRejectUnboundedInputAndOwnedDecoderPauses),
        ("MprisNativeBusPropertiesAndControlsUseRealAdapter", MprisNativeBusPropertiesAndControlsUseRealAdapter),
        ("unchanged nodes skip apply and retain clip geometry", UnchangedNodesSkipApplyAndRetainClipGeometry),
        ("automation invoke and focus route through the renderer", AutomationInvokeAndFocusRouteThroughRenderer),
        ("navigation peers expose selection and route selection through invoke", NavigationPeersExposeSelectionAndRouteSelection),
        ("settings toggles expose confirmed state and reject disabled activation", SettingsTogglePeersUseConfirmedStateAndDisabledInput),
        ("radio peers expose exclusive group selection", RadioPeersExposeExclusiveGroupSelection),
        ("selection and hover facts present under reduced motion", SelectionAndHoverFactsPresentUnderReducedMotion),
        ("capsules respond to focus press and disabled state", CapsulesRespondToFocusPressAndDisabledState),
        ("capsule spring is no bounce and preserves reversal velocity", CapsuleSpringPreservesReversalVelocity),
        ("feedback overlays expose live semantics and honor reduced motion", FeedbackOverlaysExposeLiveSemanticsAndReducedMotion),
        ("post-navigation double click respects time position button and cancellation", PostNavigationDoubleClickBoundaries),
        ("typeface cache preserves usable platform defaults", TypefaceCachePreservesUsableDefault),
        ("typeface cache probes fallbacks and preserves weight and style", TypefaceCacheProbesFallbacks),
        ("typeface cache reports missing fonts without caching failure", TypefaceCacheDoesNotCacheFailure),
        ("document transfers bound actual bytes and replace atomically", DocumentTransfersBoundActualBytesAndReplaceAtomically),
        ("lifetime: splash never owns the process and main window close terminates", LifetimeSplashNeverOwnsProcessAndMainWindowCloseTerminates),
        ("window entrance preserves prepared input and retires motion", WindowEntrancePreservesPreparedInputAndRetiresMotion),
        ("tray visibility retargets retained content and retires lifetimes", TrayVisibilityRetargetsRetainedContentAndRetiresLifetimes),
        ("memory pressure preserves visible leases and disposes idle resources", MemoryPressurePreservesVisibleLeasesAndDisposesIdle),
        ("VerifyBackgroundFitAndImageOpacityDrawing", VerifyBackgroundFitAndImageOpacityDrawing),
        ("RuntimeDiagnosticsObserveRealCommitsPaintAndAdmission", RuntimeDiagnosticsObserveRealCommitsPaintAndAdmission),
        ("LauncherRenderingPolicySelectsActualNativeOptionsAndPreservesRestartCapture", LauncherRenderingPolicySelectsActualNativeOptionsAndPreservesRestartCapture),
        ("LocalFileAssociationAdmitsRegularPathsAndCapturesIndependentArguments", LocalFileAssociationAdmitsRegularPathsAndCapturesIndependentArguments),
        ("theme SVG rejects external content and draws admitted geometry", ThemeSvgRejectsExternalContentAndDrawsAdmittedGeometry),
        ("theme paint consumes gradient texture shadow opacity and canonical transform", ThemePaintConsumesGradientTextureShadowOpacityAndCanonicalTransform),
        ("theme font bytes shape measure and place input caret consistently", ThemeFontBytesShapeMeasureAndPlaceInputCaretConsistently),
        ("theme native activation retains window tray and retires resources", ThemeNativeActivationRetainsWindowTrayAndRetiresResources),
        ("theme range paint and automation use confirmed scene values", ThemeRangePaintAndAutomationUseConfirmedSceneValues),
        ("theme resource retirement holds snapshot borrow until release", ThemeResourceRetirementHoldsSnapshotBorrowUntilRelease),
        ("theme portable materials produce visible tint without blurring text", ThemePortableMaterialsProduceVisibleTintWithoutBlurringText),
        ("theme decoded budget rejects highly compressible raster collections", ThemeDecodedBudgetRejectsHighlyCompressibleRasterCollections),
        ("theme owner retirement and final native borrow release are concurrent safe", ThemeOwnerRetirementAndFinalNativeBorrowReleaseAreConcurrentSafe),
        ("progress ring shares clock stops and draws a hollow arc", ProgressRingSharesClockStopsAndDrawsHollowArc),
        ("theme SVG tile submits a bounded repeating picture for large viewports", ThemeSvgTileSubmitsBoundedRepeatingPictureForLargeViewports),
        ("startup failure log link follows stage and needs explicit click", StartupFailureLogLinkFollowsStageAndNeedsExplicitClick),
    ];

    private static int Main(string[] args)
    {
        if (args is ["--native-macos-render-probe", var mode, var directory]) return RunMacOSRenderProbe(mode, directory);
        if (args.Contains("--native-startup-smoke")) return RunNativeStartupSmoke();
        if (args.Contains("--native-startup-cancel-smoke")) return RunNativeStartupCancelSmoke();
        if (args.Contains("--native-corner-smoke")) return RunNativeCornerSmoke();
        if (args.Contains("--native-font-smoke")) return RunNativeFontSmoke(args.Contains("--expect-font-fallback"));
        foreach ((string name, Action body) in TestCases)
        {
            body();
            Console.WriteLine($"PASS: {name}");
        }

        Console.WriteLine($"Avalonia scene backend tests passed: {TestCases.Length}.");
        return 0;
    }
}
