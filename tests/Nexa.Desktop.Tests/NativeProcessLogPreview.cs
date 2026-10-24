using Avalonia.Controls;
using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly int[] ProcessLogPreviewCounts = [2, 24, 0];
    private static readonly bool[] ProcessLogPreviewThemes = [false, true];

    private static int RunNativeProcessLogPreview(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(async () =>
        {
            Directory.CreateDirectory(output);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
                new(ReducedMotion: true), cancellationToken: timeout.Token);
            await startup.InvokeAsync(async () =>
            {
                var window = new Window { Width = 1100, Height = 700, ShowInTaskbar = false };
                window.Show();
                try
                {
                    foreach (int count in ProcessLogPreviewCounts)
                    {
                        Guid session = Guid.NewGuid();
                        var entries = Enumerable.Range(1, count).Select(index => new MinecraftProcessOutputEntry(index,
                            index < 3 || index % 6 == 0 ? MinecraftProcessOutputChannel.Stderr : MinecraftProcessOutputChannel.Stdout,
                            index == 1 ? "Nexa JVM Host: bootstrap received"
                            : index == 2 ? "Nexa JVM Host Error: JVM library missing or unloadable (jvm.dll/libjvm.so/libjvm.dylib). (FileNotFoundException)"
                            : index == 6 ? string.Join(" ", Enumerable.Repeat("at net.minecraft.client.main.Main.main(Main.java:251)", 5))
                            : $"[Render thread/INFO]: Preparing world resources, completed stage {index}. 测试日志正常显示。")).ToArray();
                        using var runtime = ProcessLogRuntime((_, _) => ValueTask.FromResult(XsrResult.Success(
                            new MinecraftProcessOutputSnapshot(session, "GTQT", MinecraftProcessState.Failed, 1, 0, entries))));
                        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), runtime, ownsMinecraftRuntime: false);
                        fixture.Shell.Renderer.ReducedMotion = true;
                        PublishLogSessions(fixture, new MinecraftProcessSnapshot(session, "GTQT", 11, MinecraftProcessState.Failed, 1,
                            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                        fixture.Shell.Render(new(1100, 700));
                        Emit(fixture.Intents, "ui.process.logs." + session.ToString("N"));
                        await fixture.Controller.WaitForProcessLogsAsync();
                        foreach (bool dark in ProcessLogPreviewThemes)
                        {
                            fixture.Shell.Renderer.ColorScheme = new(dark);
                            foreach (XsrUiSize size in new XsrUiSize[] { new(850, 500), new(1100, 700) })
                            {
                                var scene = CaptureNativeReviewScene(window, fixture.Shell, size,
                                    Path.Combine(output, $"process-log-{count}-{(dark ? "dark" : "light")}-{size.Width}.png"));
                                var footer = FindByKey(fixture.Shell, scene, "ProcessLogFooter").Rect;
                                AssertTrue(footer.Y + footer.Height <= size.Height);
                                AssertEqual(count == 0, HasKey(fixture.Shell, scene, "ProcessLogEmpty"));
                            }
                        }
                        if (count > 0)
                        {
                            var scene = fixture.Shell.Render(new(1100, 700));
                            Emit(fixture.Intents, "ui.process.log.select", FindByKey(fixture.Shell, scene, "ProcessLogEntry:1").Entity);
                            fixture.Shell.Renderer.ColorScheme = new(true);
                            CaptureNativeReviewScene(window, fixture.Shell, new(1100, 700), Path.Combine(output, $"process-log-{count}-selected.png"));
                            var language = new UiLocalizationCatalog();
                            language.SetLanguage("en");
                            fixture.Shell.Renderer.TextLocalizer = language.Translate;
                            CaptureNativeReviewScene(window, fixture.Shell, new(850, 500), Path.Combine(output, $"process-log-{count}-english.png"));
                        }
                    }
                }
                finally { window.Content = null; window.Close(); }
            }, timeout.Token);
            Console.WriteLine("PASS: native process-log layout, short/full/empty output, light/dark and narrow/wide captures; controlled redacted fixtures only.");
            return 0;
        });
}
