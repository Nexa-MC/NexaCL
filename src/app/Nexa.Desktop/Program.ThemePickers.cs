using Nexa.Desktop.Ui;
using Nexa.Services.Themes;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static async Task<IReadOnlyList<string>> PickThemeResourcesAsync(AvaloniaUiPlatformActions platform,
        DesktopThemePickerRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Kind == ThemeOptionKind.Directory)
        {
            string? path = await platform.PickThemeDirectoryAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return path is null ? [] : [path];
        }
        var kind = request.Package ? AvaloniaUiThemePickerKind.Package : request.ResourceKind switch
        {
            ThemeResourceKind.Font => AvaloniaUiThemePickerKind.Font,
            ThemeResourceKind.Audio => AvaloniaUiThemePickerKind.Audio,
            ThemeResourceKind.Video => AvaloniaUiThemePickerKind.Video,
            ThemeResourceKind.Icon => AvaloniaUiThemePickerKind.Icon,
            _ => AvaloniaUiThemePickerKind.Image,
        };
        return await platform.PickThemeFilesAsync(kind, request.Kind == ThemeOptionKind.Files, token).ConfigureAwait(false);
    }
}
