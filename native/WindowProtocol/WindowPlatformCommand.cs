namespace Nexa.Native.Window.Wire;

internal enum WindowPlatformCommand
{
    Resize = 1,
    Immersive = 2,
    FrameRate = 3,
    GpuBudget = 4,
    Scrim = 5,
    Hide = 6,
    Minimize = 7,
    Restore = 8,
    Close = 9,
    MinecraftStore = 10,
    ControllerInput = 11,
    CancelDoubleClick = 12,
    OpenDirectory = 13,
    OpenFile = 14,
    OpenHttps = 15,
    CopyText = 16,
    ReadClipboard = 17,
    Notify = 18,
    CopyScreenshotPath = 19,
    CopyScreenshotBytes = 20,
    ShareScreenshot = 21,
    PreviewScreenshot = 22,
    PickDirectory = 23,
    PickDownload = 24,
    PickExport = 25,
    PickStorage = 26,
    PickJava = 27,
    PickJson = 28,
    PickSkin = 29,
    PickThemeDirectory = 30,
    PickThemeOptions = 31,
    PickWorkspace = 32,
    PickDataPack = 33,
    PickModpack = 34,
    PickThemeFiles = 35,
    ReadDocument = 36,
    SaveDocument = 37,
    ThemeMode = 38,
    DesktopPolicy = 39,
    WindowAppearance = 40,
    TaskbarProgress = 41,
    LocalizeCaptions = 42,
    Facts = 43,
}

internal static class WindowPlatformContract
{
    internal static bool IsSynchronous(WindowPlatformCommand command) => command is
        WindowPlatformCommand.Resize
        or WindowPlatformCommand.Immersive
        or WindowPlatformCommand.FrameRate
        or WindowPlatformCommand.GpuBudget
        or WindowPlatformCommand.Scrim
        or WindowPlatformCommand.Hide
        or WindowPlatformCommand.Minimize
        or WindowPlatformCommand.Restore
        or WindowPlatformCommand.Close
        or WindowPlatformCommand.MinecraftStore
        or WindowPlatformCommand.ControllerInput
        or WindowPlatformCommand.CancelDoubleClick
        or WindowPlatformCommand.OpenDirectory
        or WindowPlatformCommand.OpenFile
        or WindowPlatformCommand.OpenHttps
        or WindowPlatformCommand.ThemeMode
        or WindowPlatformCommand.DesktopPolicy
        or WindowPlatformCommand.WindowAppearance
        or WindowPlatformCommand.TaskbarProgress
        or WindowPlatformCommand.Facts;
}
