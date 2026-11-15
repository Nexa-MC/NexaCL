using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    public Task<string?> PickExtensionFileAsync(string title, int kind, string[] extensions, CancellationToken token = default)
    {
        ValidateExternalPicker(title, kind, extensions, 256);
        return PickExternalFileAsync(title, kind, extensions, token);
    }

    /// <summary>Native picker for a Host-attributed external request; the Host supplies its source label.</summary>
    public Task<string?> PickExternalFileAsync(string title, int kind, string[] extensions, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ValidateExternalPicker(title, kind, extensions, 4096);
        string[] filters = extensions.ToArray();
        return Dispatcher.UIThread.CheckAccess() ? PickExtensionFileOnUiAsync(title, kind, filters, token)
            : Dispatcher.UIThread.InvokeAsync(() => PickExtensionFileOnUiAsync(title, kind, filters, token));
    }

    private static void ValidateExternalPicker(string title, int kind, string[] extensions, int titleLimit)
    {
        if (title is null || title.Length > titleLimit || title.Any(char.IsControl) || kind is < 0 or > 2
            || extensions is null || extensions.Length > 32
            || extensions.Any(extension => extension is null || extension.Length is < 1 or > 32
                || extension.All(character => character == '.')
                || extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.')))
            throw new ArgumentException("Invalid file picker title, kind or filter.");
    }

    private async Task<string?> PickExtensionFileOnUiAsync(string title, int kind, string[] extensions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_owner?.StorageProvider is not { } storage) throw new InvalidOperationException("The native file picker is not ready.");
        string[] patterns = extensions.Length == 0 ? ["*"] : extensions.Select(e => "*." + e.TrimStart('.')).ToArray();
        IStorageItem? selected;
        switch (kind)
        {
            case 0:
                var files = await storage.OpenFilePickerAsync(new() { Title = title, AllowMultiple = false, FileTypeFilter = [new("Files") { Patterns = patterns }] });
                selected = files.Count == 0 ? null : files[0]; break;
            case 1:
                var folders = await storage.OpenFolderPickerAsync(new() { Title = title, AllowMultiple = false });
                selected = folders.Count == 0 ? null : folders[0]; break;
            case 2:
                selected = await storage.SaveFilePickerAsync(new() { Title = title, ShowOverwritePrompt = true, FileTypeChoices = [new("Files") { Patterns = patterns }] }); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        using (selected) { token.ThrowIfCancellationRequested(); return selected?.TryGetLocalPath(); }
    }

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
