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
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--desktop-instance-file-peer", var fileDirectory, var file])
        {
            var primary = Nexa.Desktop.DesktopSingleInstance.AcquireAsync(fileDirectory, Nexa.Desktop.DesktopDestination.Install, file).GetAwaiter().GetResult();
            Environment.ExitCode = primary is null ? 0 : 11;
            primary?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }
        if (args is ["--desktop-instance-peer", var directory])
        {
            var primary = Nexa.Desktop.DesktopSingleInstance.AcquireAsync(directory, Nexa.Desktop.DesktopDestination.Settings).GetAwaiter().GetResult();
            Environment.ExitCode = primary is null ? 0 : 11;
            primary?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }
        if (Environment.GetEnvironmentVariable("NEXA_COUNTRY") is null) Environment.SetEnvironmentVariable("NEXA_COUNTRY", "CN");
        if (args is ["--native-nxml-settings-preview", var nxmlPreviewTheme, var nxmlPreviewDirectory])
        {
            Environment.ExitCode = RunNativeNxmlSettingsPreview(nxmlPreviewTheme, nxmlPreviewDirectory);
            return;
        }
        if (args is ["--native-screenshot-preview", var screenshotPreviewTheme, var screenshotPreviewDirectory])
        {
            Environment.ExitCode = RunNativeScreenshotPreview(screenshotPreviewTheme, screenshotPreviewDirectory);
            return;
        }
        if (args is ["--native-wardrobe-setup-preview", var output])
        {
            RunWardrobeSetupPreview(output).GetAwaiter().GetResult();
            return;
        }
        if (args is ["--native-settings-preview", var settingsPreviewTheme, var settingsPreviewDirectory])
        {
            Environment.ExitCode = RunNativeSettingsPreview(settingsPreviewTheme, settingsPreviewDirectory);
            return;
        }
        if (args is ["--native-content-selection-preview", var contentSelectionOutput])
        { RunNativeContentSelectionPreview(contentSelectionOutput).GetAwaiter().GetResult(); return; }
        if (args is ["--native-resource-preview", var previewTheme, var previewDirectory])
        {
            Environment.ExitCode = RunNativeResourcePreview(previewTheme, previewDirectory);
            return;
        }
        if (args.Contains("--native-resource-scroll-smoke"))
        {
            Environment.ExitCode = RunNativeResourceScrollSmoke();
            return;
        }
        if (args.Contains("--soak"))
        {
            Environment.ExitCode = RunDesktopSoak(args);
            return;
        }
        if (args.Contains("--window-integration-smoke"))
        {
            WindowPropertyStoreRoundTripsAppId();
            Console.WriteLine("PASS: native window property store roundtrip");
            return;
        }
        int failures = 0;
        var cases = args is ["--filter", var filter] ? TestCases.Where(item => item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray() : TestCases;
        if (cases.Length == 0) throw new ArgumentException("No desktop tests match the requested filter.");
        foreach ((string name, Action body) in cases)
        {
            try
            {
                body();
                Console.WriteLine($"PASS: {name}");
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {name}\n{error}");
            }
        }

        Console.WriteLine($"Desktop composition tests passed: {cases.Length - failures}; failed: {failures}.");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private static readonly (string Name, Action Body)[] TestCases =
    [
        ("LaunchInstallBedrockWorkspaceRetainsControlsStatusAndFocusAcrossProjection", LaunchInstallBedrockWorkspaceRetainsControlsStatusAndFocusAcrossProjection),
        ("LaunchInstallBedrockUnsupportedReloadRetainsDisabledStore", LaunchInstallBedrockUnsupportedReloadRetainsDisabledStore),
        ("LaunchInstallBedrockPlaceholderLoadsCompleteOwnedWorkspace", LaunchInstallBedrockPlaceholderLoadsCompleteOwnedWorkspace),
        ("LaunchInstallHomeOwnedProfilesRetainFocusScrollAndLiteralFactsAcrossLocale", LaunchInstallHomeOwnedProfilesRetainFocusScrollAndLiteralFactsAcrossLocale),
        ("LaunchInstallHomeWidgetFractionalDotsRetainEntitiesPagerAndTransitions", LaunchInstallHomeWidgetFractionalDotsRetainEntitiesPagerAndTransitions),
        ("LaunchInstallOwnedCatalogRetainsSelectionDraftFocusAndScroll", LaunchInstallOwnedCatalogRetainsSelectionDraftFocusAndScroll),
        ("LaunchInstallCatalogLiteralDataUsesBoundedIdentity", LaunchInstallCatalogLiteralDataUsesBoundedIdentity),
        ("LaunchInstallCatalogLocaleReloadRetainsLiteralDraftAndOwnedRows", LaunchInstallCatalogLocaleReloadRetainsLiteralDraftAndOwnedRows),
        ("CustomIdentityProjectionPreservesLiveDraftsAndOwnedNotes", CustomIdentityProjectionPreservesLiveDraftsAndOwnedNotes),
        ("CustomUpdateProjectionRetainsCardAndRejectsRetiredActions", CustomUpdateProjectionRetainsCardAndRejectsRetiredActions),
        ("CustomContentWorkspaceRetainsAiDraftsThroughConfirmationAndClearsHiddenSecrets", () => CustomContentWorkspaceRetainsAiDraftsThroughConfirmationAndClearsHiddenSecrets().AsTask().GetAwaiter().GetResult()),
        ("SettingsServerOwnedStatusReloadKeepsEditorDraftCaretAndLiteralAuthority", SettingsServerOwnedStatusReloadKeepsEditorDraftCaretAndLiteralAuthority),
        ("SettingsWorldOwnedSnapshotReloadPreservesIdentityAndRetiresOldResults", SettingsWorldOwnedSnapshotReloadPreservesIdentityAndRetiresOldResults),
        ("ContentScreenshotOwnedModalRetainsDraftFocusScrollAndMotion", ContentScreenshotOwnedModalRetainsDraftFocusScrollAndMotion),
        ("ContentUnsafeScreenshotNamesStayLiteralAndKeepTypedFileIdentity", ContentUnsafeScreenshotNamesStayLiteralAndKeepTypedFileIdentity),
        ("ContentProjectionFailureLeavesRetainedWorkspaceAndNextReloadUsable", ContentProjectionFailureLeavesRetainedWorkspaceAndNextReloadUsable),
        ("SettingsRecoveryWorkspaceRetainsControlsScrollAndLiteralRows", SettingsRecoveryWorkspaceRetainsControlsScrollAndLiteralRows),
        ("SettingsRecoveryConfirmationsRetireAcrossScopeChanges", SettingsRecoveryConfirmationsRetireAcrossScopeChanges),
        ("NetworkProbeReloadPreservesProxyDraftCaretFocusAndControlIdentity", NetworkProbeReloadPreservesProxyDraftCaretFocusAndControlIdentity),
        ("ProductCopyNamedFormatsLocalizeBeforeLiteralProviderSubstitution", ProductCopyNamedFormatsLocalizeBeforeLiteralProviderSubstitution),
        ("FeedbackPrelocalizedLiteralTextNeverReentersLocalizationAcrossDialogAndDetails", FeedbackPrelocalizedLiteralTextNeverReentersLocalizationAcrossDialogAndDetails),
        ("FeedbackNotificationDeduplicationKeepsLocalizationSemanticsSeparate", FeedbackNotificationDeduplicationKeepsLocalizationSemanticsSeparate),
        ("TaskCenterProviderDetailsAndErrorsStayLiteralWhileStagesTranslate", TaskCenterProviderDetailsAndErrorsStayLiteralWhileStagesTranslate),
        ("SettingsTransferExportRetainsUnsavedArgumentRowsCaretAndScroll", SettingsTransferExportRetainsUnsavedArgumentRowsCaretAndScroll),
        ("StorageBusyReloadRetainsCatalogDraftCaretAndOwnedDirectoryRows", StorageBusyReloadRetainsCatalogDraftCaretAndOwnedDirectoryRows),
        ("RecoveryReloadKeepsTimelineOfflineControlsAndPendingReadAcrossSnapshots", RecoveryReloadKeepsTimelineOfflineControlsAndPendingReadAcrossSnapshots),
        ("memory analysis settings show unavailable without a switch", MemoryAnalysisSettingsAreUnavailable),
        ("instance development mode keeps advanced pages scoped and safety available", InstanceDevelopmentModeKeepsAdvancedPagesScoped),
        ("proxy protocols round trip PAC and compact direct mode", ProxyProtocolSelectionRoundTripsPacAndKeepsDirectModeCompact),
        ("StartupAppearanceReadsDurableThemeAndMotionPreferences", StartupAppearanceReadsDurableThemeAndMotionPreferences),
        ("StartupAppearanceRejectsInvalidDocumentsWithoutWrites", StartupAppearanceRejectsInvalidDocumentsWithoutWrites),
        ("StartupAppearanceFallbackModesApplyOnlyWithoutCommittedCells", StartupAppearanceFallbackModesApplyOnlyWithoutCommittedCells),
        ("StartupReadinessOrdersAndReusesFiniteSteps", StartupReadinessOrdersAndReusesFiniteSteps),
        ("StartupReadinessBoundsOfflineWorkAndKeepsCancellation", StartupReadinessBoundsOfflineWorkAndKeepsCancellation),
        ("StartupReadinessPropagatesMandatoryFailure", StartupReadinessPropagatesMandatoryFailure),
        ("StartupSettingsMetadataPreparesRetainedPageBeforeNavigation", StartupSettingsMetadataPreparesRetainedPageBeforeNavigation),
        ("ResourceOwnedWorkspaceRetainsDraftCaretTracksAndScrollAcrossProjection", ResourceOwnedWorkspaceRetainsDraftCaretTracksAndScrollAcrossProjection),
        ("ResourceVersionFilterKeepsLiteralAuthorityAndDraftAcrossResize", ResourceVersionFilterKeepsLiteralAuthorityAndDraftAcrossResize),
        ("ResourcePagesRenewCachedMetadataOnceAndRetireLateDetail", ResourcePagesRenewCachedMetadataOnceAndRetireLateDetail),
        ("ResourceContinuousListAppendsOnceAndRetainsRowsScrollAndDraft", ResourceContinuousListAppendsOnceAndRetainsRowsScrollAndDraft),
        ("ResourceContinuousListPreservesRowsAndExplicitlyRetriesTheSamePage", ResourceContinuousListPreservesRowsAndExplicitlyRetriesTheSamePage),
        ("ResourceContinuousListRetiresSearchAndSourceGenerations", ResourceContinuousListRetiresSearchAndSourceGenerations),
        ("ResourceContinuousListKeepsHiddenBatchesAndSourceQualifiedIdentities", ResourceContinuousListKeepsHiddenBatchesAndSourceQualifiedIdentities),
        ("ResourceContinuousListBoundsRowsProjectsAndViewportMedia", ResourceContinuousListBoundsRowsProjectsAndViewportMedia),
        ("ResourceContinuousListRenewsEachRetainedPageWithoutReplacingOthers", ResourceContinuousListRenewsEachRetainedPageWithoutReplacingOthers),
        ("ResourceContinuousListRenewsOverlappingPagesWithoutLosingSourceFacts", ResourceContinuousListRenewsOverlappingPagesWithoutLosingSourceFacts),
        ("InstanceIdentityDraftSurvivesScopedFactRebuildsAndRetiresOnNavigation", InstanceIdentityDraftSurvivesScopedFactRebuildsAndRetiresOnNavigation),
        ("DialogLiteralBodiesPreserveCatalogMatchesAndRefreshTranslationPolicy", DialogLiteralBodiesPreserveCatalogMatchesAndRefreshTranslationPolicy),
        ("ContentIntegrityDetailsShowActualFactsAndRetireScopedReads", ContentIntegrityDetailsShowActualFactsAndRetireScopedReads),
        ("LaunchNavigationRejectsUnknownAndUnboundWithoutChangingPage", LaunchNavigationRejectsUnknownAndUnboundWithoutChangingPage),
        ("LaunchNavigationBindingsBorrowRealPagesAndPreserveRepeatedAssignments", LaunchNavigationBindingsBorrowRealPagesAndPreserveRepeatedAssignments),
        ("developer diagnostic formats follow language without translating metadata", DeveloperDiagnosticFormatsFollowLanguageWithoutLocalizingMetadata),
        ("DiagnosticAiWorkspaceRebuildsCommittedAdmissionAndCancelsRetiredSend", () => DiagnosticAiWorkspaceRebuildsCommittedAdmissionAndCancelsRetiredSend().AsTask().GetAwaiter().GetResult()),
        ("HardwareAdviceUsesSameCapturedEstimatePreflightAndNeverWritesConfiguration", HardwareAdviceUsesSameCapturedEstimatePreflightAndNeverWritesConfiguration),
        ("platform lists preserve unknown status and literal values", PlatformListsPreserveUnknownStatusAndLiteralValues),
        ("platform repair rejects retired confirmation and Java pick", PlatformRepairRejectsRetiredConfirmationAndJavaPick),
        ("StartupHintsConsumeCommittedChoiceAndKeepLaunchProgress", StartupHintsConsumeCommittedChoiceAndKeepLaunchProgress),
        ("ManualNetworkProbeUiCancelsAndKeepsObservedFactsLiteral", ManualNetworkProbeUiCancelsAndKeepsObservedFactsLiteral),
        ("ResourceSourcePolicyDisablesLocalOverrideAndRestoresIt", ResourceSourcePolicyDisablesLocalOverrideAndRestoresIt),
        ("MediaMenuDeduplicatesAndRetiresOnlyOwnedItems", MediaMenuDeduplicatesAndRetiresOnlyOwnedItems),
        ("AdvancedLaunchFactsPageUsesReadOnlyTypedQueriesAndPagedSnapshots", AdvancedLaunchFactsPageUsesReadOnlyTypedQueriesAndPagedSnapshots),
        ("RecoveryTimelineOrdersPersistedPointsBoundsRowsAndRetiresInstanceHistory", () => RecoveryTimelineOrdersPersistedPointsBoundsRowsAndRetiresInstanceHistory().AsTask().GetAwaiter().GetResult()),
        ("AdvancedWorkspaceDispatchesFiniteRoutesAndCancelsRetiredDelay", AdvancedWorkspaceDispatchesFiniteRoutesAndCancelsRetiredDelay),
        ("AdvancedWorkspaceReloadPreservesOtherCapturesAndFocus", AdvancedWorkspaceReloadPreservesOtherCapturesAndFocus),
        ("AdvancedSettingsSnapshotRedactsSecretsPathsCommandsAndCopiesCapturedRows", AdvancedSettingsSnapshotRedactsSecretsPathsCommandsAndCopiesCapturedRows),
        ("RuntimeTraceWorkspaceCapturesOncePagesMetadataAndRejectsRetiredActions", RuntimeTraceWorkspaceCapturesOncePagesMetadataAndRejectsRetiredActions),
        ("InstanceDiagnosticsJoinScopePreserveUnknownAndUseDurableHistory", InstanceDiagnosticsJoinScopePreserveUnknownAndUseDurableHistory),
        ("instance launch facts coalesce and retire before same-scope return", InstanceLaunchFactsCoalesceAndCancelBeforeReturningToSameScope),
        ("instance diagnostic formats localize and preserve literal metadata", InstanceDiagnosticFormatsLocalizeWithoutReparsingMetadata),
        ("external language catalog rejects empty incomplete and oversized data", ExternalLanguageCatalogRejectsEmptyIncompleteAndOversizedData),
        ("ReducedMotionConsumesTypedCommittedPreferenceAndLegacyOr", ReducedMotionConsumesTypedCommittedPreferenceAndLegacyOr),
        ("DeveloperRuntimeDiagnosticsCaptureActualSceneAndRemainReadOnly", DeveloperRuntimeDiagnosticsCaptureActualSceneAndRemainReadOnly),
        ("BackgroundPresentationPreservesVideoPriorityAndRoutedAppearance", BackgroundPresentationPreservesVideoPriorityAndRoutedAppearance),
        ("ScreenshotWaterfallLayoutKeepsRatiosAndBoundedWindows", ScreenshotWaterfallLayoutKeepsRatiosAndBoundedWindows),
        ("ScreenshotGalleryRealizesVisibleWaterfallAndKeepsModalScroll", ScreenshotGalleryRealizesVisibleWaterfallAndKeepsModalScroll),
        ("ScreenshotPreviewRoutesIdentityRetiresReadsAndPreservesCropDrafts", ScreenshotPreviewRoutesIdentityRetiresReadsAndPreservesCropDrafts),
        ("ScreenshotVisibleReadsAreBoundedCachedAndRetired", ScreenshotVisibleReadsAreBoundedCachedAndRetired),
        ("ScreenshotPreviewWaitsForOriginalBeforeCropAndRetiresRefresh", ScreenshotPreviewWaitsForOriginalBeforeCropAndRetiresRefresh),
        ("ScreenshotVisibleCarriersSurviveLruEvictionWithoutRepeatedReads", ScreenshotVisibleCarriersSurviveLruEvictionWithoutRepeatedReads),
        ("ScreenshotThumbnailQuotaIncludesRealizedImagesAndReadReservations", ScreenshotThumbnailQuotaIncludesRealizedImagesAndReadReservations),
        ("WorldHealthAndScreenshotTimelineUseScopedActualRoutes", WorldHealthAndScreenshotTimelineUseScopedActualRoutes),
        ("CommandRoutesForwardNumericDestinationsAndSafeBootstrapRefusesOwner", CommandRoutesForwardNumericDestinationsAndSafeBootstrapRefusesOwner),
        ("SystemPreferencesConsumeRoutedCommittedChanges", SystemPreferencesConsumeRoutedCommittedChanges),
        ("CommandPaletteDefersToModalDecisionsAndClosesOnNavigation", CommandPaletteDefersToModalDecisionsAndClosesOnNavigation),
        ("CommandPaletteFiltersLocalizedRoutesAndRetiresSources", CommandPaletteFiltersLocalizedRoutesAndRetiresSources),
        ("CommandLineAdmitsOnlyFiniteNavigationAndExplicitSafeMode", CommandLineAdmitsOnlyFiniteNavigationAndExplicitSafeMode),
        ("FileWorkspaceRequiresPreviewBeforeExplicitSaveAndPreservesOtherLines", FileWorkspaceRequiresPreviewBeforeExplicitSaveAndPreservesOtherLines),
        ("FileWorkspaceRetiresLateReadsPreservesPagingAndRejectsEditedPreview", FileWorkspaceRetiresLateReadsPreservesPagingAndRejectsEditedPreview),
        ("GlobalOverlaysRetainOwnedSlotsFocusDraftsAndMotion", GlobalOverlaysRetainOwnedSlotsFocusDraftsAndMotion),
        ("OperationCopyLocalizesFormatsAndPreservesLiteralMetadata", OperationCopyLocalizesFormatsAndPreservesLiteralMetadata),
        ("OperationCopyRetainsTaskAndLogIdentityAcrossLanguageChanges", OperationCopyRetainsTaskAndLogIdentityAcrossLanguageChanges),
        ("OfflineReadinessCoalescesRetiresScopesAndInvalidatesSettingsRevision", OfflineReadinessCoalescesRetiresScopesAndInvalidatesSettingsRevision),
        ("InstanceIdentityEditorWritesTypedMetadataAndShowsServerRequirements", InstanceIdentityEditorWritesTypedMetadataAndShowsServerRequirements),
        ("ContentBatchUpdatesDispatchOnlySelectedLiveFiles", ContentBatchUpdatesDispatchOnlySelectedLiveFiles),
        ("LaunchProfileSettingsEditNamedAndTemporaryLayers", LaunchProfileSettingsEditNamedAndTemporaryLayers),
        ("SystemRegistrationsAndCustomAppearancePreserveBounds", SystemRegistrationsAndCustomAppearancePreserveBounds),
        ("SystemAutostartIsOwnedReversibleAndLocal", SystemAutostartIsOwnedReversibleAndLocal),
        ("single instance waits for closing owner before reopening", DesktopSingleInstanceWaitsForClosingOwner),
        ("desktop branding and safe default single-instance bootstrap", DesktopBrandAndBootstrapDefaults),
        ("desktop protocol routes and registration preserve arguments", DesktopProtocolRoutesAndRegistrationCommands),
        ("single instance forwards across processes bounds queue and releases", DesktopSingleInstanceForwardsAcrossProcessesAndReleases),
        ("wardrobe locale captions preserve dynamic arguments", WardrobeLocalesTranslateDynamicCaptionsAndPreserveArguments),
        ("wardrobe localized scenes preserve literal provider names", WardrobeScenesLocalizeControlsAndKeepProviderNamesLiteral),
        ("wardrobe library previews visible rows and preserves views", WardrobeLibraryLoadsOnlyVisiblePublicPreviewsAndPreservesViews),
        ("wardrobe library effects require live owning sources", WardrobeLibraryNativeEffectsRequireLiveOwningSources),
        ("wardrobe library navigation cancels admitted mutation", WardrobeLibraryNavigationCancelsAdmittedMutation),
        ("wardrobe library navigation restores titles and focus", WardrobePageLibraryNavigationRestoresTitlesAndFocus),
        ("wardrobe matches dev tracks and responsive player rail", WardrobePageMatchesDevTracksAndResponsivePlayerRail),
        ("wardrobe admits own rename without retry and rejects selection ABA", WardrobePageConsumesAdmittedRenameWithoutRetryAndRejectsSelectionAba),
        ("wardrobe keeps admitted write rename pending and retires ABA", WardrobePageKeepsAdmittedWriteRenamePendingAndRetiresAba),
        ("wardrobe rejects foreign disabled and retired sources", WardrobePageRejectsForeignDisabledAndRetiredSources),
        ("wardrobe separates read failure empty and browser handoff", WardrobePageSeparatesReadFailureFromEmptyAndAdmitsBrowserHandoff),
        ("wardrobe library matches dev filters pagination and links", WardrobeLibraryMatchesDevFiltersPaginationAndLinks),
        ("wardrobe library admits own rename and rejects stale identities", WardrobeLibraryAdmitsOwnProfileRenameAndRejectsStaleIdentities),
        ("wardrobe library preserves own mutation rename and retires selection ABA", WardrobeLibraryKeepsOwnMutationRenameAndRetiresSelectionAba),
        ("wardrobe library admits apply and respects provider restrictions", WardrobeLibraryAdmitsApplyAndRespectsProviderRestrictions),
        ("wardrobe library retires late catalogue images and accounts", WardrobeLibraryRetiresLateCataloguesImagesAndAccountReplies),
        ("wardrobe library separates loading empty failure and bounds cards", WardrobeLibrarySeparatesLoadingEmptyFailureAndBoundsCards),
        ("wardrobe player projects seven views and modern layers", WardrobePlayerProjectsSevenViewsAndModernLayers),
        ("wardrobe player preserves slim legacy and fallback models", WardrobePlayerPreservesSlimLegacyAndFallbackModels),
        ("wardrobe player composes independent cape and scaled textures", WardrobePlayerComposesIndependentCapeAndScaledTextures),
        ("process logs read selected session and copy redacted pages", ProcessLogViewerReadsSelectedSessionAndCopiesRedactedPages),
        ("process logs cancel retired reads and reject wrong sessions", ProcessLogViewerCancelsRetiredReadsAndRejectsWrongSessions),
        ("process output captures bounded redacted streams and ended sessions", ProcessOutputQueryCapturesBoundedRedactedStreamsAndEndedSessions),
        ("developer diagnostics require committed mode and remain read only", DeveloperDiagnosticsAreReadOnlyAndRequireCommittedDeveloperMode),
        ("developer state inspector pages snapshots and rejects retired actions", DeveloperStateInspectorPagesSnapshotAndRejectsRetiredActions),
        ("developer renderer diagnostics refresh on demand and resume", DeveloperRendererDiagnosticsRefreshOnlyOnDemandAndResumeOnReturn),
        ("developer state metadata bounds rows and never formats values", DeveloperStateMetadataProjectionBoundsRowsAndNeverFormatsValues),
        ("wardrobe previews and dispatches typed mutations", WardrobePagePreviewsAndDispatchesTypedMutations),
        ("wardrobe retires late queries pickers and previews", WardrobePageRetiresLateQueriesPickersAndPreviews),
        ("wardrobe refreshes admission after a failed mutation", WardrobePageRefreshesAdmissionAfterFailedMutation),
        ("wardrobe paginates bounded cape inventories", WardrobePagePaginatesCapeInventory),
        ("Bedrock official actions require visible owned buttons", BedrockInstallOfficialActionsRequireVisibleOwnedButtons),
        ("Bedrock unsupported platform opens only official website", BedrockInstallUnsupportedPlatformOnlyOpensOfficialWebsite),
        ("Bedrock native faults allow retry and disposed actions retire", BedrockInstallNativeFaultsAllowRetryAndDisposedActionsRetire),
        ("Bedrock effects cannot publish into retired presentation", BedrockInstallEffectCannotPublishIntoRetiredPresentation),
        ("appearance session applies committed modes and survives failed saves", AppearanceSessionAppliesCommittedModesAndSurvivesFailedSaves),
        ("appearance scene preserves colors geometry and caching", AppearanceSceneProjectionPreservesColorsGeometryAndCaching),
        ("appearance controls use confirmed accessible modes", AppearanceControlsUseConfirmedAccessibleModes),
        ("disk log controls apply policy with explicit actions", DiskLogControlsApplyPolicyAndRequireExplicitActions),
        ("disk log export coalesces cancellation and shutdown", DiskLogExportCoalescesOffersCancellationAndCancelsOnDispose),
        ("disk log action failures hide exception paths", DiskLogActionFailuresHideExceptionPaths),
        ("launch hook settings send exact commands and wait policy", LaunchHookSettingsEditorsSendExactCommandsAndWaitPolicy),
        ("launch hook lines preserve separators tabs drafts and paging", LaunchHookLinesPreserveExactSeparatorsTabsDraftsAndPaging),
        ("launch wrapper editor preserves bounded long commands", LaunchWrapperEditorPreservesBoundedLongCommands),
        ("launch hook failed save retains multiline draft and committed script", LaunchHookSaveFailureKeepsMultilineDraftAndCommittedScript),
        ("launch hook failed inheritance preserves drafts across revisions and can apply", LaunchHookFailedInheritancePreservesDraftAcrossRevisionsAndCanApply),
        ("managed Java deletion confirms captured identity and retires", ManagedJavaDeletionConfirmsCapturedIdentityAndRetiresOnNavigation),
        ("storage settings preview cancel and queue before closing", StorageSettingsPreviewsCancelsAndQueuesBeforeClosing),
        ("storage settings retire late confirmation on navigation", StorageSettingsNavigationRetiresLateConfirmation),
        ("storage cleanup requires preview and preserves recovery cards", StorageCleanupRequiresPreviewConfirmationAndPreservesRecoveryCards),
        ("proxy drafts apply atomically and reject stale revisions", ProxyDraftsApplyAtomicallyRejectStaleRevisionsAndMaskCredentials),
        ("log preferences use settings controls and apply immediately", LogPreferencesUseSettingsControlsAndApplyImmediately),
        ("game window preferences supervise hidden and overlapping sessions", GameWindowPreferencesSuperviseHiddenAndOverlappingSessions),
        ("game window preferences use settings selectors", GameWindowPreferencesUseSettingsSelectors),
        ("content refresh publishes small batches and combines update checks", ContentRefreshPublishesSmallBatchesAndCombinesUpdateChecks),
        ("updates check outside settings and retire old channels", UpdatePreferencesCheckOutsideSettingsAndRetireOldChannels),
        ("disabled startup updates keep manual check", DisabledStartupUpdatesKeepManualCheck),
        ("resource language uses original metadata and rejects late Chinese translations", ResourceLanguageUsesOriginalMetadataAndRejectsLateChineseTranslations),
        ("Java registration picker retires on navigation and dispatches captured revision", JavaRegistrationPickerRetiresOnNavigationAndDispatchesCapturedRevision),
        ("Presentation settings apply outside navigation and preserve activity policy", PresentationSettingsApplyWithoutNavigation),
        ("retired low power facts cannot override presentation preferences", RetiredLowPowerFactsCannotOverridePresentationPreferences),
        ("Java vendor selector fits and scrolls in a narrow window", JavaVendorSelectorFitsAndScrollsInNarrowWindow),
        ("Settings pointer selection scrolls fully visible options", SettingsPointerSelectionScrollsFullyVisibleOptions),
        ("version settings use scoped controls and inset forms", VersionSettingsUseScopedControlsAndInsetForms),
        ("instance Java inventory writes only the selected instance", InstanceJavaInventoryWritesOnlySelectedInstance),
        ("automatic update restart follows selected version and rollback", AutomaticUpdateRestartFollowsSelectedVersionAndRollback),
        ("automatic update resumes on startup without opening settings", AutomaticUpdateResumesOnStartupWithoutOpeningSettings),
        ("automatic update retry retains protected transaction channel", AutomaticUpdateRetryRetainsProtectedChannel),
        ("resource module renders literal text without actions and retires without space", ResourceModuleRendersLiteralTextWithoutActionsAndRetiresWithoutSpace),
        ("resource caption patch reads state and restores text and accessibility", ResourceCaptionPatchReadsStateAndRestoresTextAndAccessibility),
        ("resource signal adapter catches only search and retires with session", ResourceSignalAdapterCatchesOnlySearchAndRetiresWithSession),
        ("soak idle driver coalesces invalidations and retires", SoakIdleDriverCoalescesInvalidationsAndRetires),
        ("soak state attribution excludes warmup bounds entries and retires", SoakStateAttributionExcludesWarmupBoundsEntriesAndRetires),
        ("soak allocation meter aligns counts and rejects invalid use", SoakAllocationMeterAlignsCountsAndRejectsInvalidUse),
        ("diagnostic export requires explicit click coalesces and cancels", DiagnosticExportRequiresClickCoalescesRequestsAndCancelsOnDispose),
        ("diagnostic export shares navigation state and restores retry", DiagnosticExportSharesNavigationStateAndRestoresRetry),
        ("diagnostic export leaving settings retires successful completion", DiagnosticExportLeavingSettingsRetiresSuccessfulCompletion),
        ("storage locations follow active root without guessing Java", StorageLocationsPresentationFollowsActiveRootWithoutGuessingJava),
        ("storage locations tolerate missing library and route", StorageLocationsPresentationWithoutLibraryOrRouteStaysSafe),
        ("storage locations retry changed scope and retire stale facts", StorageLocationsPresentationRetriesScopeAndDropsRetiredFacts),
        ("launch state projections skip idle frames and wake on change", LaunchProjectionsDoNotRepeatOnIdleFrames),
        ("language catalog covers locales templates and fallbacks", LanguageCatalogCoversLocalesTemplatesAndFallbacks),
        ("language settings switch live without rebuilding controls", LanguageSettingSwitchesLiveAndPreservesControls),
        ("first run uses system language without settings state", FirstRunUsesSystemLanguageWithoutSettingsState),
        ("region formatting uses startup policy and restores culture", RegionFormattingUsesStartupPolicyAndRestoresCulture),
        ("region selector preserves custom cultures and saves presets", RegionSelectorPreservesCustomCulturesAndSavesPresets),
        ("settings transfer previews cancels applies and filters export", SettingsTransferPreviewsCancelsAppliesAndFiltersExport),
        ("settings reset requires confirmation and retires late decisions", SettingsResetRequiresConfirmationAndRetiresLateDecisions),
        ("settings priority selector uses readable choices and instance scope", SettingsPrioritySelectorUsesReadableChoicesAndInstanceScope),
        ("Java inventory page retires late reads and persists default selection", JavaInventoryPageRetiresLateReadsAndPersistsDefaultSelection),
        ("settings instance reset restores inheritance without native file callbacks", SettingsInstanceResetRestoresInheritanceWithoutNativeFileCallbacks),
        ("settings import rejects stale revision and late confirmation", SettingsImportRejectsStaleRevisionAndLateConfirmation),
        ("settings import discards late read and rejects wrong scope", SettingsImportDiscardsLateReadAndRejectsWrongScope),
        ("settings transfer uses instance scope and discards changed identity", SettingsTransferUsesInstanceScopeAndDiscardsChangedIdentity),
        ("installed content online info preserves local details and rejects stale results", InstalledContentOnlineInfoKeepsLocalDetailsAndDiscardsStaleResults),
        ("resource optional dependencies wait for user choice", ResourceOptionalDependenciesWaitForUserChoice),
        ("resources page downloads by identity and projects Chinese text", ResourcesPageDownloadsByIdentityAndProjectsChineseText),
        ("resource primitive function patch changes presentation only", ResourcePrimitiveFunctionPatchChangesPresentationOnly),
        ("resource interactive module presents live state and cancels retired source", ResourceInteractiveModulePresentsLiveStateAndCancelsRetiredSource),
        ("resource function patch rewrites actual list and detail captions", ResourceFunctionPatchRewritesActualListAndDetailCaptions),
        ("resources page uses service queries and preserves search", ResourcesPageUsesServiceQueriesAndPreservesSearch),
        ("resource icons arrive without rebuilding search or rows", ResourceIconsArriveWithoutRebuildingSearchOrRows),
        ("resource pages release images and retire canceled icon generations", ResourcePagesReleaseImagesAndRetireCanceledIconGenerations),
        ("resources page discards superseded search and restores navigation", ResourcesPageDiscardsSupersededSearchAndRestoresNavigation),
        ("Install exit failure allows retry after worker becomes terminal", InstallExitFailureAllowsRetryAfterWorkerBecomesTerminal),
        ("Install exit waits for stop and preserves stay choice", InstallExitWaitsForStopAndPreservesStayChoice),
        ("Java only installation guards close until stopped", JavaOnlyInstallationGuardsCloseUntilStopped),
        ("Startup install recovery owns dispatch and shutdown", StartupInstallRecoveryOwnsDispatchAndShutdown),
        ("first run keeps drafts and commits only at finish", FirstRunKeepsDraftsAndCommitsOnlyAtFinish),
        ("account owned rows retain drafts and reject hidden sources", AccountOwnedRowsRetainDraftsAndRejectHiddenSources),
        ("wardrobe owned cards preserve path drafts and responsive orientation", WardrobeOwnedCardsRetainDraftsAcrossLanguageAndOrientation),
        ("wardrobe library owned reflow retains search view and focus", WardrobeLibraryOwnedReflowRetainsSearchViewAndFocus),
        ("first run owned workspace retains transitions and retires picker", FirstRunOwnedWorkspaceRetainsTransitionsAndRetiresPicker),
        ("product PXML shell accepts native window metrics", ProductNxmlShellAcceptsNativeWindowMetrics),
        ("Version settings isolate overrides and restore inheritance", VersionSettingsAreScopedAndRestoreInheritance),
        ("Instance management projects content with bounded realized rows", InstanceManagementShowsContentAndBoundsRealizedRows),
        ("Instance content graph bounds relations and retires stale instances", ContentGraphPageBoundsRelationsAndRetiresOldInstances),
        ("Modpack export preserves fields and dispatches selected scope", ModpackExportPagePreservesFieldsAndDispatchesSelectedScope),
        ("Server pages edit through sealed commands and join transiently", ServerPageEditsThroughSealedCommandsAndJoinsTransiently),
        ("Online content lists retain search focus and reject retired instances", OnlineContentListRetainsFocusAndRejectsRetiredInstances),
        ("Installed content lists associate icons versions and pack updates", InstalledContentListsAssociateIconsVersionsAndPackUpdates),
        ("Settings page keeps the final navigation and compact layout", SettingsPageUsesFinalNavigationAndCompactLayout),
        ("Settings page saves through Services without losing draft focus", SettingsPageSavesThroughServicesAndPreservesDraftFocus),
        ("Settings memory Auto and Java acquisition choices reach durable routes", SettingsMemoryAutoAndJavaAcquisitionReachRoutes),
        ("Settings developer toggle preserves scroll position and focus", SettingsDeveloperToggleKeepsPositionAndFocus),
        ("Settings argument rows support add remove and apply", SettingsArgumentRowsSupportAddRemoveAndApply),
        ("Settings platform reads and refreshes Service snapshots", SettingsPlatformReadsAndRefreshesServiceSnapshot),
        ("Settings inline choices commit in both directions", SettingsInlineSelectorCommitsBothDirections),
        ("version row actions keep selection distinct", VersionRowActionsKeepSelectionDistinct),
        ("Java choice page routes selected major and launch action tracks progress", JavaChoiceAndLaunchProgressStayInteractive),
        ("process controls address sessions and show Service crash reports", ProcessControlsProjectServiceFacts),
        ("install catalog checks bridge selection and cleanup", InstallCatalogChecksBridgeSelection),
        ("install catalog returns to selected game and loader", InstallCatalogReturnsToSelectedGameAndLoader),
        ("install catalog virtualizes and prefetches off UI thread", InstallCatalogVirtualizesAndPrefetchesOffUiThread),
        ("Windows property store roundtrips the game AUMID", WindowPropertyStoreRoundTripsAppId),
        ("version click returns immediately and double click opens directory", VersionClickReturnsImmediatelyAndDoubleClickOpensDirectory),
        ("version transfer selection supports toggle range and filter cleanup", VersionTransferSelectionSupportsRanges),
        (nameof(VersionTemplatesRetainDraftsRowsScopesAndLiteralNames), VersionTemplatesRetainDraftsRowsScopesAndLiteralNames),
        ("version selection bounds large lists and keeps logical transfers", VersionSelectionBoundsLargeListsAndKeepsLogicalTransfers),
        ("version selection uses the captured directory for launch and restores each selection", VersionSelectionUsesDirectoryQualifiedLaunch),
        ("version list remains compact searchable and scrollable with distinct icons", VersionListKeepsCompactGeometryAndIcons),
        ("directory pickers cannot publish after leaving the version page", VersionDirectoryPickerDiscardsLateResult),
        ("imported profiles remain visible when the picker reopens", ImportedProfilesRemainVisibleWhenPickerReopens),
        ("account forms share one header and create offline profiles", AccountFormsUseOneHeaderAndCreateOfflineProfiles),
        ("Microsoft onboarding reuses services and rejects late cancelled completion", MicrosoftOnboardingUsesServiceAndDiscardsLateCancellation),
        ("packaged account client ids preserve LittleSkin login in CI artifacts", PackagedAccountClientIdsMergeWithRuntimeConfiguration),
        ("third-party onboarding masks passwords and uses the chosen server", ThirdPartyOnboardingMasksPasswordsAndUsesConfiguredServer),
        ("LittleSkin onboarding selects characters with separate token kinds", LittleSkinOnboardingChoosesCharacterAndKeepsTokenKindsSeparate),
        ("account failures and stale file pickers stay in the current view", AccountFailuresAndLateFilePickerStayInCurrentView),
        ("launch page replicates the legacy card layout with bound facts", LaunchPageReplicatesLegacyLayout),
        ("launch page uses compact gutters across wide, default, and minimum windows", LaunchPageUsesCompactGutters),
        ("navigation intents route between launch and placeholder pages", NavigationIntentsRouteBetweenPages),
        ("download and instance actions route to installation and version management", DownloadAndInstanceActionsRouteToInstallationAndVersionManagement),
        ("installation entry presents Java and Bedrock subpages with a horizontal catalog", InstallationEntryPresentsJavaAndBedrockSubpages),
        ("launch page semantics never expose internal entity keys", LaunchPageSemanticsNeverExposeInternalKeys),
        ("instance scan publishes state without mutating the tree from its worker", InstanceScanPublishesWithoutForeignTreeMutation),
        ("an older instance scan cannot overwrite the latest generation", OlderInstanceScanCannotOverwriteLatestGeneration),
        ("launch primary dispatches the product start command", LaunchPrimaryDispatchesProductStartCommand),
        ("expand navigation never replaces the launch page", ExpandNavigationNeverReplacesTheLaunchPage),
        ("account card lists profiles and switches the selection", AccountCardListsProfilesAndSwitchesSelection),
        ("rail animation retains content and version card containment", RailAnimationRetainsContentAndCardContainment),
        ("account roster publication stays on the render thread and preserves rows", AccountRosterUpdatesAtFrameBoundary),
        ("long account rosters scroll within the card", AccountRosterScrollsWithinCard),
        ("selected profile is used by the product launch route", SelectedProfileIsUsedByLaunch),
        ("unavailable launch cannot be invoked by pointer keyboard or automation", UnavailableLaunchCannotBeInvoked),
        ("operation log dispatch success traces at debug", DispatchSuccessLogsDebugTrace),
        ("operation log dispatch failure logs warn with code", DispatchFailureLogsWarnWithCode),
        ("operation log state logs real time but quiet domains stay silent", StateChangesLogRealTimeButQuietDomainsStaySilent),
        ("operation log composite fans out to both observers", CompositeStateObserverFansOutToBothObservers),
        ("operation log lifecycle and scheduler log at their tiers", LifecycleAndSchedulerLogAtTheirTiers),
        // XSR-712: launching overlay.
        ("launch overlay shows reset facts when launch starts", LaunchOverlayShowsResetFactsWhenLaunchStarts),
        ("launch overlay narrates progress cells", LaunchOverlayNarratesProgressCells),
        ("launch overlay closes on failure", LaunchOverlayClosesOnFailure),
        ("launch overlay cancel hides overlay", LaunchOverlayCancelHidesOverlay),
        ("launch overlay prompts before the java download", LaunchOverlayPromptsBeforeJavaDownload),
        ("unsupported profile does not spin feedback", UnsupportedProfileDoesNotSpinFeedback),
        ("crash changes pages stay bounded and reject late updates", CrashChangesPagesRemainBoundedAndLateUpdatesCannotReopenDialog),
        ("crash changes query uses the failed process instance", CrashChangesQueryUsesFailedProcessInstance),
        ("launched cancel button becomes back", LaunchedCancelButtonBecomesBack),
        ("version subpages have independent routes and restore navigation focus", VersionSubpagesHaveIndependentRoutesAndRestoreFocus),
        ("Install editor locks base version and resets on exit", InstallEditorLocksBaseVersionAndResetsOnExit),
        ("pointer focus does not draw keyboard focus rings", PointerFocusDoesNotDrawKeyboardFocusRings),
        ("capsules occupy their presented width and remain beside the version name", CapsulesOccupyPresentedWidth),
        ("launch widgets preserve original content and page through real intents", LaunchWidgetsPreserveOriginalContent),
        ("profile presentation uses Apple hierarchy inside the experimental layout", ProfilePresentationUsesAppleHierarchy),
        ("account capsules and wardrobe navigation preserve geometry", AccountCapsulesAndWardrobeRoutePreserveGeometry),
        ("capsules ignore geometry-only hover moves", CapsulesIgnoreGeometryOnlyHoverMoves),
        ("navigation keeps controls stable and immediately reachable", NavigationKeepsControlsStableAndImmediatelyReachable),
        ("skin route publishes media through host state into the rendered profile", SkinRoutePublishesIntoRenderedProfile),
        ("delete actions persist only the requested profile and reject stale rows", DeleteActionsPersistAndRejectStaleRows),
        ("trivia yields to visibility activity and launch quiet without changing preferences", TriviaYieldsToVisibilityWindowActivityAndLaunchQuiet),
        ("trivia rotates every three seconds without foreign tree writes and stops on disposal", TriviaTimerPublishesOnlyStateAndStops),
        ("install failure leaves the idle tree clean", InstallFailureLeavesTheIdleTreeClean),
        ("staged task page renders clean between changes", StagedTaskPageRendersCleanBetweenChanges),
        ("bubbles share a vertical dock and closing bubbles release slots", BubblesShareVerticalDockAndReleaseHiddenSlots),
        ("task bubble follows the center summary and yields to the page", TaskBubbleFollowsSummaryAndYieldsToPage),
        ("task bubble shows a compact track with aggregated progress", TaskBubbleUsesCompactProgressTrack),
        ("task center page reconciles cards and routes cancel dismiss and clear", TaskCenterPageReconcilesCardsAndRoutesActions),
        ("task center page yields the bubble and reclaims it on back", TaskCenterPageYieldsBubbleAndReclaimsOnBack),
        ("operational feedback uses the shared lower-left notification surface", OperationalFeedbackUsesLowerLeftNotification),
        ("notification levels keep exact lifetimes and permanent errors remain closable", NotificationLevelsKeepExactLifetimes),
        ("notifications share one lower-left surface and every level closes manually", NotificationsShareLowerLeftSurfaceAndCloseManually),
        ("notification summaries open complete one-action scrollable dialogs", NotificationSummaryOpensCompleteScrollableDialog),
        ("notification overflow remains bottom-pinned and recovers without disappearing", NotificationOverflowRecoversWithoutDisappearing),
        ("launch overlay closes immediately on success", LaunchOverlayClosesImmediatelyOnSuccess),
        ("closing notifications leave stack flow before their exit settles", ClosingNotificationReflowsImmediately),
        ("notification timers request render without mutating the UI tree", NotificationTimersStayOffTheRenderTree),
        ("dialog stays inside the window traps the page and restores focus on escape", DialogStaysInsideWindowAndRestoresFocus),
    ];
}
