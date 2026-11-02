using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly bool[] TaskLayoutPreviewThemes = [false, true];
    private static readonly int[] TaskLayoutPreviewWidths = [1280, 810];
    private static Action<LaunchPageFixture, string>? _captureTaskLayout;

    private static int RunNativeTaskLayoutPreview(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(() => CaptureTaskLayoutsAsync(Path.GetFullPath(output)));

    private static async Task<int> CaptureTaskLayoutsAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true), cancellationToken: timeout.Token);
        await startup.InvokeAsync(async () =>
        {
            var splash = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow!;
            foreach (bool dark in TaskLayoutPreviewThemes)
            {
                string theme = dark ? "dark" : "light";
                startup.SetAppearance(startup.Appearance with { ThemeMode = dark ? XsrUiThemeMode.Dark : XsrUiThemeMode.Light });
                startup.ReportStage("正在初始化本地版本、账户与在线信息缓存，请稍候…");
                await WaitForStage("正在初始化本地版本、账户与在线信息缓存，请稍候…");
                Save(splash, "splash-" + theme + "-loading");
                const string failure = "初始化未完成。请检查网络连接，或点击重试继续启动。诊断信息会在此处换行显示，较长的内容可以滚动查看。";
                Task<bool> retry = startup.WaitForRetryAsync(failure);
                await WaitForStage(failure);
                Save(splash, "splash-" + theme + "-failed");
                splash.GetVisualDescendants().OfType<Button>().Single(button => button.Content is "重试")
                    .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                AssertTrue(await retry.WaitAsync(timeout.Token));
            }
            var window = new Window { Width = 1280, Height = 800, ShowInTaskbar = false };
            window.Show();
            _captureTaskLayout = (fixture, stage) =>
            {
                foreach (bool dark in TaskLayoutPreviewThemes)
                    foreach (int width in TaskLayoutPreviewWidths)
                    {
                        fixture.Shell.Renderer.ColorScheme = new(dark);
                        fixture.Shell.Renderer.ReducedMotion = true;
                        window.Width = width;
                        using var surface = new AvaloniaUiSceneSurface(fixture.Shell) { Width = width, Height = 800 };
                        window.Content = surface;
                        surface.Measure(new Size(width, 800)); surface.Arrange(new Rect(0, 0, width, 800));
                        surface.CommitScene();
                        window.UpdateLayout(); surface.UpdateLayout();
                        Save(surface, $"task-{(dark ? "dark" : "light")}-{stage}-{width}");
                        window.Content = null;
                    }
            };
            try { await TaskCenterRetryButtonRoutesAndKeepsCardIdentity(); }
            finally { _captureTaskLayout = null; window.Close(); }

            async Task WaitForStage(string expected)
            {
                var status = splash.GetVisualDescendants().OfType<ScrollViewer>()
                    .Select(viewer => viewer.Content).OfType<TextBlock>().Single();
                while (status.Text != expected) await Task.Delay(10, timeout.Token);
                await Task.Delay(100, timeout.Token);
                splash.UpdateLayout();
            }
            void Save(Control control, string name)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width),
                    (int)Math.Ceiling(control.Bounds.Height)), new Vector(96, 96));
                bitmap.Render(control);
                bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
            }
        }, timeout.Token);
        Console.WriteLine("PASS: native horizontal Splash and task cards in both themes, running/failure/completed states, and wide/narrow task layouts.");
        return 0;
    }
}
