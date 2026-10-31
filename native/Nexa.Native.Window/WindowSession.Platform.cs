using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private async Task<byte[]> ExecutePlatformAsync(WindowPlatformCommand command, UiWireReader reader, CancellationToken token)
    {
        if (WindowPlatformContract.IsSynchronous(command)) return ExecutePlatformSynchronous(command, reader, token);
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Platform);
        writer.Int32((int)command);
        switch (command)
        {
            case WindowPlatformCommand.CopyText:
                {
                    string text = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    await _actions.CopyTextAsync(text);
                    break;
                }
            case WindowPlatformCommand.ReadClipboard:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.ReadClipboardTextAsync(token));
                    break;
                }
            case WindowPlatformCommand.Notify:
                {
                    string title = Required(reader.String()), message = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.Boolean(await _actions.NotifyNativeAsync(title, message, token));
                    break;
                }
            case WindowPlatformCommand.CopyScreenshotPath:
                {
                    string path = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    await _actions.CopyScreenshotAsync(path);
                    break;
                }
            case WindowPlatformCommand.CopyScreenshotBytes:
                {
                    byte[] bytes = reader.Buffer();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    await _actions.CopyScreenshotAsync(bytes);
                    break;
                }
            case WindowPlatformCommand.ShareScreenshot:
                {
                    string path = Required(reader.String());
                    bool observed = reader.Boolean();
                    long size = observed ? reader.Int64() : 0, ticks = observed ? reader.Int64() : 0;
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    if (observed) await _actions.ShareScreenshotAsync(path, size, ticks);
                    else await _actions.ShareScreenshotAsync(path);
                    break;
                }
            case WindowPlatformCommand.PreviewScreenshot:
                {
                    string path = Required(reader.String());
                    long size = reader.Int64(), ticks = reader.Int64();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    await AvaloniaUiPlatformActions.PreviewScreenshotAsync(path, size, ticks);
                    break;
                }
            case WindowPlatformCommand.PickDirectory:
                {
                    string? title = reader.String();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await (title is null ? _actions.PickDirectoryAsync() : _actions.PickDirectoryAsync(title)));
                    break;
                }
            case WindowPlatformCommand.PickDownload:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickDownloadDirectoryAsync());
                    break;
                }
            case WindowPlatformCommand.PickExport:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickExportDirectoryAsync());
                    break;
                }
            case WindowPlatformCommand.PickStorage:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickStorageDirectoryAsync());
                    break;
                }
            case WindowPlatformCommand.PickJava:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickJavaFileAsync());
                    break;
                }
            case WindowPlatformCommand.PickJson:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickJsonFileAsync());
                    break;
                }
            case WindowPlatformCommand.PickSkin:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickSkinFileAsync(token));
                    break;
                }
            case WindowPlatformCommand.PickThemeDirectory:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickThemeDirectoryAsync(token));
                    break;
                }
            case WindowPlatformCommand.PickThemeOptions:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickThemeOptionsFileAsync(token));
                    break;
                }
            case WindowPlatformCommand.PickWorkspace:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickWorkspaceJsonAsync(token));
                    break;
                }
            case WindowPlatformCommand.PickDataPack:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickDataPackFileAsync(token));
                    break;
                }
            case WindowPlatformCommand.PickModpack:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.String(await _actions.PickModpackFileAsync(token));
                    break;
                }
            case WindowPlatformCommand.PickThemeFiles:
                {
                    var kind = reader.AvaloniaUiThemePickerKind();
                    bool multiple = reader.Boolean();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    var paths = await _actions.PickThemeFilesAsync(kind, multiple, token);
                    writer.Count(paths.Count); foreach (string path in paths) writer.String(path);
                    break;
                }
            case WindowPlatformCommand.ReadDocument:
                {
                    int budget = reader.Int32(); string? title = reader.String();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    string? document = await _actions.ReadJsonDocumentAsync(budget, title is null ? null : () => title, token);
                    writer.Boolean(document is not null);
                    if (document is not null) writer.Buffer(UiWireLimits.Utf8.GetBytes(document));
                    break;
                }
            case WindowPlatformCommand.SaveDocument:
                {
                    string document = UiWireLimits.Utf8.GetString(reader.Buffer());
                    int budget = reader.Int32(); string? title = reader.String();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.Boolean(await _actions.SaveJsonDocumentAsync(document, budget, title is null ? null : () => title, token));
                    break;
                }
            case WindowPlatformCommand.LocalizeCaptions:
                {
                    int count = reader.Count();
                    if (count < 0) throw new InvalidDataException("Missing captions.");
                    string[] captions = new string[count];
                    for (int i = 0; i < count; i++) captions[i] = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    var localized = await _actions.LocalizeDesktopCaptionsAsync(captions, token);
                    writer.Count(localized.Count); foreach (string caption in localized) writer.String(caption);
                    break;
                }
            default: throw new InvalidDataException("Unsupported operation for this window platform channel.");
        }
        return writer.Finish();
    }
    private byte[] ExecutePlatformSynchronous(WindowPlatformCommand command, UiWireReader reader, CancellationToken token)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Platform);
        writer.Int32((int)command);
        switch (command)
        {
            case WindowPlatformCommand.Resize:
                {
                    bool enabled = reader.Boolean();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetWindowResizeEnabled(enabled);
                    break;
                }
            case WindowPlatformCommand.Immersive:
                {
                    bool enabled = reader.Boolean();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetImmersivePresentation(enabled);
                    break;
                }
            case WindowPlatformCommand.FrameRate:
                {
                    int rate = reader.Int32();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetAnimationFrameRate(rate);
                    break;
                }
            case WindowPlatformCommand.GpuBudget:
                {
                    long? budget = reader.Boolean() ? reader.Int64() : null;
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    if (budget < 0) throw new InvalidDataException("Invalid GPU budget.");
                    AvaloniaUiPlatformActions.SetGpuAvailableBudget(budget);
                    break;
                }
            case WindowPlatformCommand.Scrim:
                {
                    double opacity = reader.Double();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetClientScrimOpacity(opacity);
                    break;
                }
            case WindowPlatformCommand.Hide:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.HideWindow();
                    break;
                }
            case WindowPlatformCommand.Minimize:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.MinimizeWindow();
                    break;
                }
            case WindowPlatformCommand.Restore:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.RestoreWindow();
                    break;
                }
            case WindowPlatformCommand.Close:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.RequestClose();
                    break;
                }
            case WindowPlatformCommand.MinecraftStore:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.OpenMinecraftStore();
                    break;
                }
            case WindowPlatformCommand.ControllerInput:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.ReportControllerInput();
                    break;
                }
            case WindowPlatformCommand.CancelDoubleClick:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.CancelPostNavigationDoubleClick();
                    break;
                }
            case WindowPlatformCommand.OpenDirectory:
                {
                    string path = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.OpenDirectory(path);
                    break;
                }
            case WindowPlatformCommand.OpenFile:
                {
                    string path = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.OpenLocalFile(path);
                    break;
                }
            case WindowPlatformCommand.OpenHttps:
                {
                    string uri = Required(reader.String());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.OpenHttpsUri(new Uri(uri, UriKind.Absolute));
                    break;
                }
            case WindowPlatformCommand.ThemeMode:
                {
                    var mode = reader.XsrUiThemeMode();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetThemeMode(mode);
                    break;
                }
            case WindowPlatformCommand.DesktopPolicy:
                {
                    var policy = WindowValueChannel.DecodeDesktopPolicy(reader.Buffer());
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetDesktopPolicy(policy);
                    break;
                }
            case WindowPlatformCommand.WindowAppearance:
                {
                    int opacity = reader.Int32(); bool blur = reader.Boolean();
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    _actions.SetWindowAppearance(opacity, blur);
                    break;
                }
            case WindowPlatformCommand.TaskbarProgress:
                {
                    bool active = reader.Boolean();
                    double progress = reader.Boolean() ? reader.Double() : double.NaN;
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.Boolean(_actions.SetTaskbarProgress(active, progress));
                    break;
                }
            case WindowPlatformCommand.Facts:
                {
                    reader.End();
                    token.ThrowIfCancellationRequested();
                    writer.Boolean(_actions.IsWindowActive);
                    writer.Byte(_actions.IsSystemDark switch { false => 0, true => 1, null => 2 });
                    writer.Int64(AvaloniaUiPlatformActions.DoubleClickInterval.Ticks);
                    writer.Int64(_actions.ThemeAppearanceGeneration);
                    var material = _actions.ThemeMaterialPresentation; writer.Boolean(material is not null);
                    if (material is not null) writer.Buffer(WindowValueChannel.EncodeMaterial(material));
                    break;
                }
            default: throw new InvalidDataException("Unsupported operation for this window platform channel.");
        }
        return writer.Finish();
    }
}
