using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    public Task<string?> PickThemeOptionsFileAsync(CancellationToken token = default) =>
        PickLocalContentAsync("导入主题参数", "JSON", ["*.json"], token);

    public async Task<string?> PickThemeDirectoryAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string? path = Dispatcher.UIThread.CheckAccess()
            ? await PickDirectoryCoreAsync("选择主题目录").ConfigureAwait(true)
            : await Dispatcher.UIThread.InvokeAsync(() => PickDirectoryCoreAsync("选择主题目录"));
        token.ThrowIfCancellationRequested();
        return path;
    }

    public Task<IReadOnlyList<string>> PickThemeFilesAsync(AvaloniaUiThemePickerKind kind, bool multiple,
        CancellationToken cancellationToken = default) => Dispatcher.UIThread.CheckAccess()
        ? PickThemeFilesOnUiAsync(kind, multiple, cancellationToken)
        : Dispatcher.UIThread.InvokeAsync(() => PickThemeFilesOnUiAsync(kind, multiple, cancellationToken));

    private async Task<IReadOnlyList<string>> PickThemeFilesOnUiAsync(AvaloniaUiThemePickerKind kind,
        bool multiple, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_owner?.StorageProvider is not { } storage)
            throw new InvalidOperationException("The native file picker is not ready.");
        string[] patterns = kind switch
        {
            AvaloniaUiThemePickerKind.Package => ["*.ntp", "*.zip"],
            AvaloniaUiThemePickerKind.Image or AvaloniaUiThemePickerKind.Icon => ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.svg", "*.ico"],
            AvaloniaUiThemePickerKind.Font => ["*.ttf", "*.otf"],
            AvaloniaUiThemePickerKind.Audio => ["*.wav", "*.mp3", "*.ogg", "*.flac", "*.m4a"],
            AvaloniaUiThemePickerKind.Video => ["*.mp4", "*.webm", "*.mkv", "*.mov"],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        string title = kind == AvaloniaUiThemePickerKind.Package ? "选择主题包" : "选择主题资源";
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizeContentPicker?.Invoke(title) ?? title,
            AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType(kind.ToString()) { Patterns = patterns }],
        }).ConfigureAwait(true);
        try
        {
            token.ThrowIfCancellationRequested();
            return files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToArray();
        }
        finally { foreach (var file in files) file.Dispose(); }
    }
}
