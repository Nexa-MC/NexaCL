using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    public Func<string, string>? LocalizeContentPicker { get; set; }

    public Task<string?> PickWorkspaceJsonAsync(CancellationToken token = default) => PickLocalContentAsync(
        "选择备份或迁移档案", "JSON", ["*.json"], token);

    public Task<string?> PickDataPackFileAsync(CancellationToken token = default) => PickLocalContentAsync(
        "选择数据包", "ZIP", ["*.zip"], token);

    public Task<string?> PickModpackFileAsync(CancellationToken token = default) => PickLocalContentAsync(
        "选择整合包", "Modpack", ["*.mrpack", "*.zip", "*.nexapack"], token);

    private Task<string?> PickLocalContentAsync(string title, string type, string[] patterns, CancellationToken token) =>
        Dispatcher.UIThread.CheckAccess() ? PickLocalContentOnUiThreadAsync(title, type, patterns, token)
            : Dispatcher.UIThread.InvokeAsync(() => PickLocalContentOnUiThreadAsync(title, type, patterns, token));

    private async Task<string?> PickLocalContentOnUiThreadAsync(string title, string type, string[] patterns, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_owner?.StorageProvider is not { } storage) throw new InvalidOperationException("The native file picker is not ready.");
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizeContentPicker?.Invoke(title) ?? title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(type) { Patterns = patterns }],
        });
        token.ThrowIfCancellationRequested();
        using var file = files.Count == 0 ? null : files[0];
        return file?.TryGetLocalPath();
    }
}
