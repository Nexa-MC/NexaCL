using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftOfflineIdentity))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionManifestEntry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionCategory))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftAprilFoolsDescriptor))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionClassification))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionClassifier))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionDescriptor))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.ModLoaders.MinecraftModLoaderKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.ModLoaders.MinecraftModLoaderDescriptor))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftLibraryOperatingSystem))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftLibraryResolutionRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftLibraryToken))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftLibraryNameFragment))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftClasspathPlanRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftClasspathPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftClasspathRuleRegistry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchIdentityMode))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchIdentity))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchTokenContext))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostBootstrapRequest))]

[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountCharacterChoice))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountChooseCharacterCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountDiscoverImportsCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountErrors))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountImportCandidate))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountImportCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLaunchIdentityResolver))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLoginCancelCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLoginPhase))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLoginProfiles))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLoginProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLoginSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountLoginStartCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountOnboardingOptions))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountOnboardingRoutes))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountOnboardingService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountOnboardingState))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountProviderId))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountRefreshSkinsCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountService))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountSkinService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.AccountSkinSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.IAccountLaunchIdentityResolver))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.ILaunchProfilePort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.ILittleSkinOAuthService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.IMicrosoftMinecraftAuthService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.IProfileDataProtector))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LaunchProfile))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LaunchProfileFilePort))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LaunchProfileKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LaunchProfileSet))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LaunchProfileView))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LegacyProfileImport))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinAuthorizationRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinClosetItem))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinDeviceCodeInfo))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinMinecraftSession))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinOAuthConfiguration))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinOAuthService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinOAuthTokens))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinPlayer))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinProfile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinTextureKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.LittleSkinTextureUploadResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MicrosoftDeviceCodeInfo))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MicrosoftMinecraftAuthService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MicrosoftMinecraftLoginResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MinecraftCapeService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MinecraftOwnedCape))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MinecraftSkinService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.MinecraftSkinUploadResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.ProtectedLaunchProfilePort))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.YggdrasilAuthLoginRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.YggdrasilAuthLoginResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Accounts.YggdrasilAuthService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.Capability<>))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityAvailability))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityConfidence))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityDefinition<>))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityDerivation<>))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityPermissionState))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityPreflightEngine))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityPreflightIssue))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityPreflightReport))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityPresentationCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityRegistry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.CapabilityStability))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.DisplayCapabilityProbe))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.DisplayCapabilityProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.EmulatedExecutionDerivation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.EstimateResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.FormFactorCapabilityProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.FormFactorCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.GpuProbes))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ICapability))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ICapabilityDefinition))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ICapabilityDerivation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ICapabilityProjection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.IMachineCapabilityProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.IRemediationAvailability))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.IRemediationHandler))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.InputCapabilityProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.InputCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.InputDeviceFeature))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.InputUsageKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.InputUsageSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.InputUsageTracker))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.LaunchPolicyCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.LaunchPolicyProjection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.LaunchPreflightQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineCapabilityBroker))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineCapabilityCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineCapabilityQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineCapabilityRefresh))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineCapabilitySnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineCapabilityStateContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineDerivedRules))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineEnvironmentCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineHardwareCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MachineInstanceCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MemoryDerivedRules))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.NativeExecutionDerivation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.OnlineWorkingSetModel))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.OnlineWorkingSetModelClient))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.OnlineWorkingSetModelSession))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.OnlineWorkingSetModelStore))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.OnlineWorkingSetProjection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.PreflightCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.PreflightCertainty))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.PreflightIssueDefinition))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.PreflightProjection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.PreflightSeverity))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.RemediationCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.RemediationDefinition))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.RemediationRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.RemediationResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.RemediationService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceEstimateCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceEstimateSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceEstimateStatus))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceEstimator))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceEstimatorProfile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceEstimatorProjection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceObservationHistory))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ResourceObservationSample))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadAttemptError))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadConnectionInfo))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadProgress))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadStage))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadTransferResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.DownloadTransferView))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.FileDownloadWriter))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.IDownloadConnection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.IDownloadWriter))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Downloads.ISegmentedDownloadConnection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Files.AppFolders))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Files.DefaultMinecraftRootProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Files.FolderNames))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Files.IMinecraftRootProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Files.LauncherStorageLocation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Files.SafeFilePort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.AccountRemoveProfileCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.AccountSelectProfileCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.AccountUpsertProfileCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.FoundationRouteIds))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.SettingsGetQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.SettingsSetCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Foundation.TelemetryConsentCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.ConsoleLogSink))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.DiagnosticHttpHandler))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.ExceptionDiagnostics))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.FileLogSink))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.ILogOperationSink))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.ILogSink))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.LogEntry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.LogLevel))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.LogOperation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.LogRedactor))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Logging.LogService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetIndexNameRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetIndexRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetIndexResolution))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetIndexResolver))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetListRequest))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetListResolver))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Assets.MinecraftAssetToken))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Crash.MinecraftLaunchFaultAnalyzer))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Crash.MinecraftLaunchFaultCode))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Crash.MinecraftLaunchFaultReport))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Crash.MinecraftMissingDependency))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Crash.MinecraftMissingDependencyParser))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Crash.MinecraftRepairActionKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetDownloadFile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetDownloadPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetDownloadPlanRequest))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetDownloadPlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetFileState))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetIndexDownloadPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftAssetIndexDownloadPlanRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftClientDownloadFailureReason))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftClientDownloadPlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftClientJarDownloadFile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftClientJarDownloadPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftClientJarDownloadPlanRequest))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftDownloadSourcePlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftExpectedFile))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Downloads.MinecraftFileVerifier))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.IMinecraftInstanceSource))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.ForgeInstallService))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.HttpInstallCatalogSource))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.IInstallCatalogSource))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.IMinecraftInstallMetadataSource))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.IMinecraftLoaderInstaller))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallBuildEligibility))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallBuildSelection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogPrefetchCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogReadCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogRoutes))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogState))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogStateContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCatalogVersion))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallCompatibility))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallDownload))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallEligibilityContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallEligibilityQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallEligibilityResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallLoader))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.InstallLoaderEligibility))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.LocalJarAction))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.LocalJarArtifact))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftFolderImportCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftFolderImportContract))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftFolderImportService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftFolderInspectQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftFolderInspection))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftFolderKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallAddon))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditPlanContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditPlanQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditPlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallEditSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallManagedFile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallRecoveryCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallRoutes))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftInstallStopCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftLoaderInstallRequest))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftLocalJarCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftLocalJarContract))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftLocalJarService))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftModpackArchive))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftModpackCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftModpackContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Install.MinecraftModpackPreview))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.AutoSelectJavaPreference))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.ExistingJavaPreference))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.HttpJavaRuntimeMetadataProvider))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.IJavaRuntimeInstaller))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.IJavaRuntimeLocator))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.IJavaRuntimeMetadataProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.IJavaRuntimePathProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaAcquisitionBlockReason))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaArchitecture))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaBrand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaInstallRecoverCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaInstallRoutes))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaInstallStopCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaInstallation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaPreference))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaPreferenceParser))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRequirementFailureReason))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRequirementResolution))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeAcquisitionDecision))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeAcquisitionPlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeArchitecture))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeCandidate))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeDownloadFile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeDownloadPlan))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeDownloadPlanService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeInstallProgress))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeInstaller))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimeOperatingSystem))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimePackageDescriptor))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimePackagePlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaRuntimePlatform))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaSelectionFailureReason))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaSelectionResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaSelectionService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaSource))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.JavaVersionRange))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.LocalJavaRuntimeLocator))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.MinecraftJavaRequirementRequest))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.MinecraftJavaRequirementResolver))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.UseGlobalJavaPreference))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Java.UseRelativeJavaPreference))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.AuthlibInjectorProvider))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.IAuthlibInjectorProvider))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.LaunchPreflightDecision))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.LaunchPreflightGate))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.LaunchPreflightPrompt))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftClientJarResolution))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftClientJarResolutionRequest))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftClientJarResolver))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchCoordinator))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchExecutor))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchFileCompletion))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchPlanner))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchPlatform))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchPreparation))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchProgressPublisher))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchProgressSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchProgressState))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchStageReport))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftLaunchStages))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftResolvedVersionManifests))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Launch.MinecraftVersionJsonReader))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftClasspathPlanner))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftLibraryResolver))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Libraries.MinecraftNativesExtractor))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceContentEntry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceContentRemoveCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceContentRestoreCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceContentService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceContentSnapshot))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceContentTrash))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceManagementContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceManagementPage))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceManagementQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceManagementService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceManagementSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceModEnabledCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceModRemovalCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceModRemovalPreview))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceModRemovalQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceModRemovalService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryChange))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryChangeKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryReport))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryRestoreCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryResumeCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoverySnapshotSummary))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceRecoveryStorage))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Management.InstanceTrashedContent))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftCancelLaunchCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftCancelProcessCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftCrashAnalyzeQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftDecideJavaAcquisitionCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftGameVersion))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftInstanceDescriptor))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftInstanceDiscovery))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftInstanceMetadata))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftInstanceMetadataStore))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftInstancesQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLaunchCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryDeleteCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryDirectory))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryDirectoryCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryForgetCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryRefreshCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryRenameCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibrarySelectCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibrarySnapshot))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftOptionsReader))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftOptionsSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftRouteIds))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftRuleContext))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftRuleEvaluator))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftSelectJavaCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftSelectJavaVersionCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftStartCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionDiscovery))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionPaths))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionScheme))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftVersionsQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.ModLoaders.MinecraftModLoaderDetector))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.IJvmHost))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.IMinecraftProcessPort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.IMinecraftWindowProbe))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostBootstrap))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostCapabilityCatalog))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostControlResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostEnvironment))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostObservation))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmHostStateContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmRunContext))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmRunSample))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.JvmRunSettings))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.LaunchModIdentity))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.LaunchModInventory))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.LaunchModInventoryReader))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftProcessFailure))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftProcessService))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftProcessSession))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftProcessSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftProcessState))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftProcessStateComposition))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftWindowProbe))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.MinecraftWindowProbeResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.ObservationCapabilityCatalog))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.Process.SystemMinecraftProcessPort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Network.NetworkEndpointProbe))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Network.NetworkProbeResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Network.NetworkProbeService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Processes.OwnedInstallerProcess))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.RegionalPolicy))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.CurseForgeResourceCatalog))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.IResourceCatalogSource))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.IResourceFileSource))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.MergedResourceCatalog))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceCatalogContract))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceCatalogService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceContentOnline))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceContentOnlineQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceContentOnlineService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDependency))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDependencyPlanner))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDependencyVerifier))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDetail))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDetailQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDownloadCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceDownloadService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceFavoriteCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceFavoritesQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceFavoritesService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceFavoritesSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceFile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceIconQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceIconResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceIconService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceInstallPlan))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceInstalledFile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceInstanceContext))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceInstanceQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceInstanceService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceModInstallCommand))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceModInstallService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceModPlanQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceOptionalDependency))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceOrder))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceProject))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceProvider))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceProviderHttp))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceReference))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceSearchQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceSearchResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceTranslation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceTranslationQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceTranslationService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Resources.ResourceVersion))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Rollouts.RolloutService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Rollouts.RolloutSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Rollouts.RolloutStateContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.ISettingsPort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.InMemorySettingsPort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.LauncherDefaults))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.LauncherSettingsJsonPort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingDefinition))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingValueType))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingValues))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsApplyTiming))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsBatchCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCapabilityAvailability))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCatalogEntry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCatalogEntryKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCatalogPage))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCatalogQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsCatalogSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsEffectiveQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsEffectiveSnapshot))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsEffectiveValue))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsErrors))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsExportQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsFilePort))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsImportCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsImportPreview))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsImportQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsLayer))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsMutation))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsOverride))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsOverrideMode))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsPolicyContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsPolicyDefinition))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsPolicySchema))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsPolicyService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsPreviewQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsSchema))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsSchemaBuilder))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Settings.SettingsValueKind))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Setup.FirstRunCompleteCommand))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Setup.FirstRunContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Setup.FirstRunQuery))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Setup.FirstRunService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Setup.FirstRunStatus))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.ITaskCenterTask))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterCancelResult))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterEntry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterEntryState))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterRoutes))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterService))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterStart))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterStateContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterStep))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskCenterSummary))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Tasks.TaskStagePlanner))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.CloudflareTelemetryTransport))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.DiagnosticTelemetry))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.ITelemetryTransport))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.LauncherTelemetryPolicy))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.LauncherTelemetrySession))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.TelemetryEvent))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.TelemetryEventCatalog))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.TelemetryLevel))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Telemetry.TelemetryService))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.HDiffPatchTool))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.IProcessLauncher))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.IProcessRunner))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.IUpdateSignatureVerifier))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.LauncherBuildIdentity))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.LocalBlockSource))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.NexaUpdateContract))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.NexaUpdateOffer))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.NexaUpdateQuery))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.NexaUpdateService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.NexaUpdateStatus))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.PreparedLauncherUpdate))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.ProcessLauncher))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.ProcessRunner))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateArchiveLimits))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBlock))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBlockCodec))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBlockDelta))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBlockFile))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBlockFull))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBlockMap))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateBuildIdentity))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateChannel))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateChunkProfile))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateChunkSlice))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateChunker))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateChunkingParameters))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateDiscoveryResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateDiscoveryService))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateDistributionLayout))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateDistributionOptions))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateEligibility))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateEligibilityDecision))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateEligibilityResult))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateFileEntry))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateGpgVerifier))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateInstallPlan))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateJsonContext))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateLocalBlockIndex))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePackage))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePackagePlanner))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchApplier))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchDto))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchIndexDto))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchIndexSource))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchStep))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchStrategyDto))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePatchVariantDto))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePayloadExtractor))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdatePlannerOptions))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateRestartScheduler))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateScatterPatchManifest))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateScatterPatchOperation))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateStaging))]
#endif
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateVcdiff))]
#endif
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateVersion))]
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.UpdateVersionStage))]
#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Updates.VerifiedUpdateInventory))]
#endif

[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftErrors))]

[assembly: TypeForwardedTo(typeof(Nexa.Services.Minecraft.MinecraftLibraryRoutes))]

#if !NEXA_NATIVE_SERVICES
[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.MinecraftPrimaryInstanceScope))]
#endif

[assembly: TypeForwardedTo(typeof(Nexa.Services.Capabilities.ModCatalog))]
