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
    internal const int RecordObservation = 2402;
    internal const int JavaRegistryRead = 2200, JavaRegistryWrite = 2201;
}
