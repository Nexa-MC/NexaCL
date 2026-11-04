namespace Nexa.Native.Minecraft.Services;

internal static class MinecraftHostOperations
{
    internal const int DescribeDownload = 2000, Download = 2001, DownloadProgress = 2002;
    internal const int SettingsSnapshot = 2100, SettingsPolicyRead = 2101;
    internal const int SettingsRenamePrepare = 2110, SettingsRenameApply = 2111;
    internal const int SettingsRecoveryCapture = 2120, SettingsRecoveryPlan = 2121, SettingsRecoveryApply = 2122;
    internal const int SettingsPolicySet = 2130;
    internal const int AccountProfile = 2300, AccountIdentity = 2301;
    internal const int WindowProbe = 2400, PreflightEvaluate = 2401;
    internal const int RecordObservation = 2402, BeforeProcessStart = 2403;
    internal const int CatalogSource = 2500;
    internal const int InstallationEvent = 2600;
    internal const int LibraryRead = 2900, LibrarySave = 2901, LibraryDiscover = 2902, LibraryCached = 2903;
    internal const int ResourceSearch = 2800, ResourceDetail = 2801, ResourceChangelog = 2802, ResourceVersion = 2803;
    internal const int CaptureWorld = 2700, CropScreenshot = 2701, WorldSnapshotList = 2702, WorldSnapshotCapture = 2703, WorldSnapshotVerify = 2704, WorldSnapshotRestore = 2705;
    internal const int JavaRegistryRead = 2200, JavaRegistryWrite = 2201;
}
