using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    public async Task<(string? Text, byte[]? Image)> ReadExtensionClipboardAsync(CancellationToken token = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.ReadExtensionClipboard, null, token).ConfigureAwait(false), WindowPlatformCommand.ReadExtensionClipboard);
        string? text = result.String(); byte[]? image = result.Boolean() ? result.Buffer() : null; result.End(); return (text, image);
    }
    public Task WriteExtensionClipboardAsync(string? text, byte[]? image, CancellationToken token = default) =>
        PlatformEffectAsync(WindowPlatformCommand.WriteExtensionClipboard, writer => { writer.String(text); writer.Boolean(image is not null); if (image is not null) writer.Buffer(image); }, token);
    public async Task<(string Id, int Width, int Height, double Scale)[]> ReadExtensionDisplaysAsync(CancellationToken token = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.ReadExtensionDisplays, null, token).ConfigureAwait(false), WindowPlatformCommand.ReadExtensionDisplays);
        int count = result.Int32(); if (count is < 0 or > 32) throw new InvalidDataException("Invalid native display count.");
        var displays = new (string, int, int, double)[count];
        for (int i = 0; i < count; i++) displays[i] = (result.String() ?? throw new InvalidDataException("Missing display identity."), result.Int32(), result.Int32(), result.Double());
        result.End(); return displays;
    }
    private Task<byte[]> PlatformAsync(WindowPlatformCommand command, Action<UiWireWriter>? arguments, CancellationToken token)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Platform);
        writer.Int32((int)command); arguments?.Invoke(writer);
        return _operations.InvokeAsync(writer.Finish(), token);
    }
    private static UiWireReader PlatformResult(byte[] bytes, WindowPlatformCommand expected)
    {
        var reader = WindowControlProtocol.Read(bytes, out int kind);
        if (kind == (int)WindowCommand.Platform && reader.Int32() == (int)expected) return reader;
        reader.Dispose(); throw new InvalidDataException("Invalid native platform response.");
    }
    private async Task PlatformEffectAsync(WindowPlatformCommand command, Action<UiWireWriter>? arguments, CancellationToken token)
    {
        using var result = PlatformResult(await PlatformAsync(command, arguments, token).ConfigureAwait(false), command);
        result.End();
    }
    private async Task<string?> PlatformStringAsync(WindowPlatformCommand command, Action<UiWireWriter>? arguments, CancellationToken token)
    {
        using var result = PlatformResult(await PlatformAsync(command, arguments, token).ConfigureAwait(false), command);
        string? value = result.String(); result.End(); return value;
    }

    public Task SetWindowResizeEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.Resize, writer => { writer.Boolean(enabled); }, cancellationToken);

    public Task SetImmersivePresentationAsync(bool enabled, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.Immersive, writer => { writer.Boolean(enabled); }, cancellationToken);

    public Task SetAnimationFrameRateAsync(int framesPerSecond, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.FrameRate, writer => { writer.Int32(framesPerSecond); }, cancellationToken);

    public Task SetClientScrimOpacityAsync(double opacity, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.Scrim, writer => { writer.Double(opacity); }, cancellationToken);

    public Task HideWindowAsync(CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.Hide, null, cancellationToken);

    public Task MinimizeWindowAsync(CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.Minimize, null, cancellationToken);

    public Task RestoreWindowAsync(CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.Restore, null, cancellationToken);

    public Task OpenMinecraftStoreAsync(CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.MinecraftStore, null, cancellationToken);

    public Task ReportControllerInputAsync(CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.ControllerInput, null, cancellationToken);

    public Task CancelPostNavigationDoubleClickAsync(CancellationToken cancellationToken = default)
        => CancelDoubleClickAsync(cancellationToken);

    public Task OpenDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.OpenDirectory, writer => { writer.String(path); }, cancellationToken);

    public Task OpenLocalFileAsync(string path, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.OpenFile, writer => { writer.String(path); }, cancellationToken);

    public Task OpenHttpsUriAsync(Uri uri, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.OpenHttps, writer => { writer.String(uri.AbsoluteUri); }, cancellationToken);

    public Task CopyTextAsync(string text, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.CopyText, writer => { writer.String(text); }, cancellationToken);

    public Task SetThemeModeAsync(XsrUiThemeMode mode, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.ThemeMode, writer => { writer.XsrUiThemeMode(mode); }, cancellationToken);

    public Task SetDesktopPolicyAsync(AvaloniaUiDesktopPolicy policy, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.DesktopPolicy, writer => { writer.Buffer(WindowValueChannel.EncodeDesktopPolicy(policy)); }, cancellationToken);

    public Task SetWindowAppearanceAsync(int opacityPercent, bool blur, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.WindowAppearance, writer => { writer.Int32(opacityPercent); writer.Boolean(blur); }, cancellationToken);

    public Task CopyScreenshotAsync(string path, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.CopyScreenshotPath, writer => { writer.String(path); }, cancellationToken);

    public Task CopyScreenshotAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.CopyScreenshotBytes, writer => { writer.Buffer(encoded); }, cancellationToken);

    public Task ShareScreenshotAsync(string path, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.ShareScreenshot, writer => { writer.String(path); writer.Boolean(false); }, cancellationToken);

    public Task ShareScreenshotAsync(string path, long expectedSize, long expectedModifiedUtcTicks, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.ShareScreenshot, writer => { writer.String(path); writer.Boolean(true); writer.Int64(expectedSize); writer.Int64(expectedModifiedUtcTicks); }, cancellationToken);

    public Task PreviewScreenshotAsync(string path, long expectedSize, long expectedModifiedUtcTicks, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.PreviewScreenshot, writer => { writer.String(path); writer.Int64(expectedSize); writer.Int64(expectedModifiedUtcTicks); }, cancellationToken);

    public Task<string?> PickDirectoryAsync(string? title = null, CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickDirectory, writer => { writer.String(title); }, cancellationToken);

    public Task<string?> PickDownloadDirectoryAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickDownload, null, cancellationToken);

    public Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickExport, null, cancellationToken);

    public Task<string?> PickStorageDirectoryAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickStorage, null, cancellationToken);

    public Task<string?> PickJavaFileAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickJava, null, cancellationToken);

    public Task<string?> PickJsonFileAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickJson, null, cancellationToken);

    public Task<string?> PickSkinFileAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickSkin, null, cancellationToken);

    public Task<string?> PickThemeDirectoryAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickThemeDirectory, null, cancellationToken);

    public Task<string?> PickThemeOptionsFileAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickThemeOptions, null, cancellationToken);

    public Task<string?> PickExtensionFileAsync(string title, int kind, string[] extensions, CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickExtensionFile, writer =>
        { writer.String(title); writer.Int32(kind); writer.Count(extensions.Length); foreach (string extension in extensions) writer.String(extension); }, cancellationToken);

    public Task<string?> PickWorkspaceJsonAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickWorkspace, null, cancellationToken);

    public Task<string?> PickDataPackFileAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickDataPack, null, cancellationToken);

    public Task<string?> PickModpackFileAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.PickModpack, null, cancellationToken);

    public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default)
        => PlatformStringAsync(WindowPlatformCommand.ReadClipboard, null, cancellationToken);

    public async Task<bool> SetTaskbarProgressAsync(bool active, double progress, CancellationToken cancellationToken = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.TaskbarProgress, writer =>
        {
            writer.Boolean(active); writer.Boolean(double.IsFinite(progress)); if (double.IsFinite(progress)) writer.Double(progress);
        }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.TaskbarProgress);
        bool applied = result.Boolean(); result.End(); return applied;
    }

    public Task SetGpuAvailableBudgetAsync(long? bytes, CancellationToken cancellationToken = default)
        => PlatformEffectAsync(WindowPlatformCommand.GpuBudget, writer =>
        { writer.Boolean(bytes.HasValue); if (bytes.HasValue) writer.Int64(bytes.Value); }, cancellationToken);

    public async Task<bool> NotifyNativeAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.Notify, writer =>
        { writer.String(title); writer.String(message); }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.Notify);
        bool sent = result.Boolean(); result.End(); return sent;
    }

    public async Task<IReadOnlyList<string>> PickThemeFilesAsync(AvaloniaUiThemePickerKind kind, bool multiple,
        CancellationToken cancellationToken = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.PickThemeFiles, writer =>
        { writer.AvaloniaUiThemePickerKind(kind); writer.Boolean(multiple); }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.PickThemeFiles);
        return ReadStrings(result);
    }

    public async Task<IReadOnlyList<string>> LocalizeDesktopCaptionsAsync(IReadOnlyList<string> captions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captions);
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.LocalizeCaptions, writer =>
        { writer.Count(captions.Count); foreach (string caption in captions) writer.String(caption); }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.LocalizeCaptions);
        return ReadStrings(result);
    }

    public async Task<string?> ReadJsonDocumentAsync(int maximumBytes, string? title = null, CancellationToken cancellationToken = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.ReadDocument, writer =>
        { writer.Int32(maximumBytes); writer.String(title); }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.ReadDocument);
        string? document = result.Boolean() ? UiWireLimits.Utf8.GetString(result.Buffer()) : null;
        result.End(); return document;
    }

    public async Task<bool> SaveJsonDocumentAsync(string document, int maximumBytes, string? title = null,
        CancellationToken cancellationToken = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.SaveDocument, writer =>
        { writer.Buffer(UiWireLimits.Utf8.GetBytes(document)); writer.Int32(maximumBytes); writer.String(title); }, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.SaveDocument);
        bool saved = result.Boolean(); result.End(); return saved;
    }

    private static string[] ReadStrings(UiWireReader result)
    {
        int count = result.Count();
        if (count < 0) throw new InvalidDataException("Missing native string collection.");
        string[] strings = new string[count];
        for (int i = 0; i < count; i++) strings[i] = result.String() ?? throw new InvalidDataException("Missing native string.");
        result.End(); return strings;
    }
}
