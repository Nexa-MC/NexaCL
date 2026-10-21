using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly bool[] InstallGuidePreviewThemes = [false, true];
    private static readonly XsrUiSize[] InstallGuidePreviewSizes = [new(1280, 800), new(810, 500)];

    private static int RunNativeInstallGuidesPreview(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(() => CaptureInstallGuidesAsync(Path.GetFullPath(output)));

    private static async Task<int> CaptureInstallGuidesAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true), cancellationToken: timeout.Token);
        await startup.InvokeAsync(() =>
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), regionalPolicy: new("CN"));
            fixture.Shell.Renderer.ReducedMotion = true;
            fixture.Controller.OpenJavaLauncherStore = _ => throw new InvalidOperationException("Preview must not launch a browser.");
            using var bedrock = new BedrockInstallPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback,
                () => throw new InvalidOperationException("Preview must not launch the Store."),
                _ => throw new InvalidOperationException("Preview must not launch a browser."), supportsStore: true);
            fixture.Controller.BedrockInstallPage = bedrock.Page;
            var window = new Window { Width = 1280, Height = 800, ShowInTaskbar = false };
            window.Show();
            try
            {
                foreach (bool dark in InstallGuidePreviewThemes)
                {
                    fixture.Shell.Renderer.ColorScheme = new(dark);
                    foreach (var size in InstallGuidePreviewSizes)
                    {
                        Capture("java", "ui.install.java", "JavaLauncherStore", size, dark);
                        Capture("bedrock", "ui.install.bedrock", "BedrockInstallStore", size, dark);
                    }
                }
            }
            finally
            {
                window.Content = null;
                window.Close();
                Emit(fixture.Intents, "ui.page.back");
            }

            void Capture(string page, string command, string action, XsrUiSize size, bool dark)
            {
                Emit(fixture.Intents, "ui.page.back");
                Emit(fixture.Intents, "ui.navigation.download");
                Emit(fixture.Intents, command);
                var scene = CaptureNativeReviewScene(window, fixture.Shell, size,
                    Path.Combine(output, $"{page}-{(dark ? "dark" : "light")}-{size.Width}.png"));
                var button = FindByKey(fixture.Shell, scene, action);
                AssertTrue(button.IsEnabled && button.Rect.Height >= 40);
                AssertTrue(button.Rect.X >= 0 && button.Rect.X + button.Rect.Width <= size.Width);
                AssertTrue(button.Rect.Y >= 0 && button.Rect.Y + button.Rect.Height <= size.Height);
                var card = FindByKey(fixture.Shell, scene, page == "java" ? "JavaLauncherGuideCard" : "BedrockInstallCard");
                AssertTrue(button.Rect.X >= card.Rect.X + 20);
                AssertTrue(button.Rect.Y + button.Rect.Height <= card.Rect.Y + card.Rect.Height - 20);
            }
        }, timeout.Token);
        Console.WriteLine("PASS: actual Java and Bedrock NXML pages, light/dark and 1280x800/810x500 native previews; no external effects.");
        return 0;
    }

    private static XsrUiScene CaptureNativeReviewScene(Window window, XsrUiShell shell, XsrUiSize size, string path)
    {
        window.Width = size.Width; window.Height = size.Height;
        using var surface = new AvaloniaUiSceneSurface(shell) { Width = size.Width, Height = size.Height };
        window.Content = surface;
        surface.Measure(new Size(size.Width, size.Height));
        surface.Arrange(new Rect(0, 0, size.Width, size.Height));
        surface.CommitScene();
        window.UpdateLayout(); surface.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        bitmap.Render(surface);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        window.Content = null;
        return surface.Scene!;
    }
}
