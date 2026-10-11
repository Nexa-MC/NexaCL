using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Nexa.UI.Next;
using Nexa.UI.Next.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task RunNativeContentSelectionPreview(string output)
    {
        Directory.CreateDirectory(output);
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true));
        await startup.InvokeAsync(async () =>
        {
            var window = new Window { Width = 1100, Height = 900, ShowInTaskbar = false };
            window.Show(); await Task.Delay(100);
            _captureContentSelection = fixture =>
            {
                var previous = fixture.Shell.Renderer.ColorScheme;
                foreach (bool dark in WardrobePreviewThemes)
                {
                    fixture.Shell.Renderer.ColorScheme = new(dark);
                    using var surface = new AvaloniaUiSceneSurface(fixture.Shell) { Width = 1100, Height = 900 };
                    window.Content = surface;
                    surface.Measure(new Size(1100, 900)); surface.Arrange(new Rect(0, 0, 1100, 900));
                    for (int frame = 0; frame < 4; frame++) surface.CommitScene();
                    window.UpdateLayout(); surface.UpdateLayout();
                    using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 900), new Vector(96, 96));
                    bitmap.Render(surface);
                    bitmap.Save(Path.Combine(output, dark ? "mods-selected-dark.png" : "mods-selected-light.png"), PngBitmapEncoderOptions.Default);
                    window.Content = null;
                }
                fixture.Shell.Renderer.ColorScheme = previous;
            };
            try { ContentBatchUpdatesDispatchOnlySelectedLiveFiles(); }
            finally { _captureContentSelection = null; window.Close(); }
        });
    }
}
