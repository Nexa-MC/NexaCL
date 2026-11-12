using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft.Services;

internal static class MinecraftServiceProtocol
{
    internal static string Identity => "nexa-minecraft-service/1/error-envelope-1/" + ServiceWireSchema.Fingerprint;
    internal const int LanDiscover = 500, ContentInspect = 501, ScriptExport = 502, ResourceInspectModpack = 503, ResourcePackSet = 504, LaunchAttemptControl = 505;
    internal const int Initialize = 0, Versions = 10, Instances = 11, RefreshInstances = 12;
    internal const int Processes = 20, ProcessOutput = 21, CancelProcess = 22, CrashAnalyze = 23;
    internal const int StartProcess = 24, ProcessInteract = 25, ProcessMetrics = 26;
    internal const int JavaInventory = 30, ProcessState = 100, FailureState = 101;
    internal const int TaskRetry = 40, TaskCancel = 41, TaskDismiss = 42, TaskClear = 43, TaskDismissFinished = 44, TaskState = 102;
    internal const int JavaInstall = 50, JavaStop = 51, JavaRecover = 52, JavaProgress = 103;
    internal const int JavaConfigure = 60, JavaManage = 61, JavaManualPreview = 62, JavaManualInstall = 63, JavaManualCancel = 64, JavaManualStatus = 65;
    internal const int JavaInspect = 66, JavaProperties = 67, JavaModules = 68, JavaPluginSelection = 69, JavaPluginRequirements = 76;
    internal const int InstallConfigure = 70, InstallRun = 71, InstallStop = 72, InstallRecover = 73, InstallModpack = 74;
    internal const int InstallEditRead = 75;
    internal const int RecoveryRead = 80, RecoveryRestore = 81, RecoveryResume = 82;
    internal const int LaunchConfigure = 90, LaunchStart = 92, LaunchCancel = 93;
    internal const int LaunchJavaApprove = 94, LaunchJavaMajor = 95, LaunchJavaPath = 96, LaunchPreflightDecide = 97, LaunchSafe = 98;
    internal const int LaunchFinalize = 99, LaunchSafeRead = 104, LaunchDiagnostics = 105, LaunchAuthorized = 106;
    internal const int LaunchState = 110;
    internal const int ObservationState = 120, SampleState = 121, ContextState = 122;
    internal const int Remediation = 130;
    internal const int CatalogConfigure = 140, CatalogRead = 141, CatalogPrefetch = 142, CatalogEligibility = 143, CatalogEditPlan = 144, CatalogDraft = 145;
    internal const int CatalogState = 146, DraftState = 147;
    internal const int CatalogDraftReplace = 148;
    internal const int ModpackUpdatePreview = 150, ModpackUpdate = 151, ModpackInspect = 152, ModpackIdentity = 153, InstallMigration = 154;
    internal const int LocalJarImport = 155, FolderInspect = 156, FolderImport = 157;

    internal static ServiceWireWriter Request(int operation)
    {
        var writer = new ServiceWireWriter(); writer.Int32(1); writer.String(Identity); writer.Int32(operation); return writer;
    }
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation)
    {
        var reader = new ServiceWireReader(bytes);
        if (reader.Int32() != 1 || reader.String() != Identity)
        { reader.Dispose(); throw new InvalidDataException("Incompatible Minecraft service protocol."); }
        operation = reader.Int32(); return reader;
    }
    internal const int ManageInstanceExportOptionsSave = 200;
    internal const int ManageInstanceModpackExportCommand = 201;
    internal const int ManageInstanceServerListSaveCommand = 202;
    internal const int ManageInstanceModEnabledCommand = 203;
    internal const int ManageInstanceContentRemoveCommand = 204;
    internal const int ManageInstanceModRemovalCommand = 205;
    internal const int ManageInstanceContentRestoreCommand = 206;
    internal const int ManageInstanceContentUpdateRollbackCommand = 207;
    internal const int ManageInstanceWorldCopyCommand = 208;
    internal const int ManageInstanceWorldBackupCommand = 209;
    internal const int ManageInstanceWorldLockCommand = 210;
    internal const int ManageInstanceWorldDataPackCommand = 211;
    internal const int ManageInstanceWorldDataPackImportCommand = 212;
    internal const int ManageInstanceWorldDataPackRemoveCommand = 213;
    internal const int ManageInstanceWorldDataPackRestoreCommand = 214;
    internal const int ManageInstanceFileSaveCommand = 215;
    internal const int ManageInstanceIdentitySaveCommand = 216;
    internal const int ManageInstanceScreenshotCropCommand = 217;
    internal const int ManageInstanceWorldSnapshotCaptureCommand = 218;
    internal const int ManageInstanceWorldSnapshotRestoreCommand = 219;
    internal const int ManageInstanceContentIntegrityQuery = 220;
    internal const int ManageInstanceWorldMetadataQuery = 221;
    internal const int ManageInstanceWorldHealthQuery = 222;
    internal const int ManageInstanceScreenshotQuery = 223;
    internal const int ManageInstanceFileListQuery = 224;
    internal const int ManageInstanceFileReadQuery = 225;
    internal const int ManageInstanceFileSavePreviewQuery = 226;
    internal const int ManageInstanceIdentityQuery = 227;
    internal const int ManageInstanceModpackExportQuery = 228;
    internal const int ManageInstanceExportOptionsQuery = 229;
    internal const int ManageInstanceServerListQuery = 230;
    internal const int ManageInstanceServerStatusQuery = 231;
    internal const int ManageInstanceModRemovalQuery = 232;
    internal const int ManageInstanceManagementQuery = 233;
    internal const int ManageInstanceOfflineReadinessQuery = 234;
    internal const int ManageInstanceWorldSnapshotListQuery = 235;
    internal const int ManageInstanceWorldSnapshotVerifyQuery = 236;
    internal const int ManageInstanceWorldRenameCommand = 237;
    internal const int ManageInstanceWorldDataPackOrderCommand = 238;
    internal const int LibraryConfigure = 400, LibraryRefresh = 401, LibraryDirectory = 402, LibraryForget = 403, LibrarySelect = 404, LibraryRename = 405, LibraryDelete = 406, LibraryRememberRenamed = 407, LibraryState = 408;
    internal const int EnvironmentFacts = 450, LocalDocuments = 451;
    internal const int MigrationReceiptAuthorize = 452, MigrationReceiptVerify = 453;
    internal const int ResourceConfigure = 300;
    internal const int ResourceResourceNetworkPolicyQuery = 310;
    internal const int ResourceResourceSearchQuery = 311;
    internal const int ResourceResourceDetailQuery = 312;
    internal const int ResourceResourceChangelogQuery = 313;
    internal const int ResourceResourceIconQuery = 314;
    internal const int ResourceResourceTranslationQuery = 315;
    internal const int ResourceResourceFavoritesQuery = 316;
    internal const int ResourceResourceContentOnlineQuery = 317;
    internal const int ResourceResourceContentOnlineBatchQuery = 318;
    internal const int ResourceResourceInstanceQuery = 319;
    internal const int ResourceResourceModPlanQuery = 320;
    internal const int ResourceResourceModpackOnlineQuery = 321;
    internal const int ResourceResourceModpackPreviewQuery = 322;
    internal const int ResourceResourceFavoriteCommand = 323;
    internal const int ResourceResourceContentUpdateCommand = 324;
    internal const int ResourceResourceContentUpdateBatchCommand = 325;
    internal const int ResourceResourceModInstallCommand = 326;
    internal const int ResourceResourceDownloadCommand = 327;
    internal const int ResourceResourceModpackInstallCommand = 328;
    internal const int ResourceResourceModpackUpdateCommand = 329;
}
