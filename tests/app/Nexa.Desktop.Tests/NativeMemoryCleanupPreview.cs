using Avalonia.Controls;
using Nexa.Desktop.Ui;
using Nexa.Services.Memory;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly bool[] MemoryPreviewThemes = [false, true];
    private static readonly XsrUiSize[] MemoryPreviewSizes = [new(850, 500), new(1100, 700)];
    private static int RunNativeMemoryCleanupPreview(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(async () =>
        {
            Directory.CreateDirectory(output);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
                new(ReducedMotion: true), cancellationToken: timeout.Token);
            await startup.InvokeAsync(async () =>
            {
                await Task.CompletedTask;
                var window = new Window { Width = 1100, Height = 700, ShowInTaskbar = false };
                window.Show();
                try
                {
                    using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), memoryPort: new CardMemoryPort());
                    fixture.Shell.Renderer.ReducedMotion = true;
                    fixture.Store.Publish(fixture.Store.Resolve(MemoryOptimizationContract.Snapshot),
                        new MemoryOptimizationSnapshot(true, new(16L << 30, 3L << 30), false, "complete", new(18, 7, 2, 192L << 20, 128L << 20)));
                    Emit(fixture.Intents, "ui.launch.widget.memory");
                    foreach (bool dark in MemoryPreviewThemes)
                    {
                        fixture.Shell.Renderer.ColorScheme = new(dark);
                        foreach (var size in MemoryPreviewSizes)
                        {
                            var scene = CaptureNativeReviewScene(window, fixture.Shell, size,
                                Path.Combine(output, $"memory-{(dark ? "dark" : "light")}-{size.Width}.png"));
                            var pager = FindByKey(fixture.Shell, scene, "LaunchWidgetPager").Rect;
                            var deep = FindByKey(fixture.Shell, scene, "MemoryDeep").Rect;
                            AssertTrue(deep.Y >= pager.Y && deep.Y + deep.Height <= pager.Y + pager.Height);
                            AssertTrue(FindByKey(fixture.Shell, scene, "MemoryClean").IsClickable);
                        }
                    }
                }
                finally { window.Content = null; window.Close(); }
            }, timeout.Token);
            Console.WriteLine("PASS: native memory card, light/dark and narrow/wide captures; controlled memory facts only.");
            return 0;
        });
}
