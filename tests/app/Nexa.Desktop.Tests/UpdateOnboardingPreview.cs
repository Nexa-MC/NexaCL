using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Nexa.Desktop.Ui;
using Nexa.Services.Updates;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly string[] UpdatePreviewPhases = ["downloading", "failed", "restart"];
    private static readonly string[] OnboardingPreviewPages = ["SidecarOnboardingStart", "SidecarOnboardingEnd"];
    private static async Task UpdateOnboardingPreview(string output)
    {
        Directory.CreateDirectory(output);
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true));
        await startup.InvokeAsync(async () =>
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
            var shell = fixture.Shell; shell.Renderer.ReducedMotion = true;
            using var settings = new SettingsPageController(shell, fixture.Intents, fixture.Foundation.Queries,
                fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
            fixture.Controller.SettingsPage = settings.Page;
            settings.ConfigureUpdates(fixture.Foundation.Queries, new("2.0.0.alpha.16", "linux-x64", "alpha"), _ => { });
            var updates = new PreviewSidecarUpdates();
            settings.ConfigureSidecarUpdates(updates, () => { });
            Emit(fixture.Intents, "ui.navigation.settings");
            var scene = shell.Render(new(1040, 760));
            Emit(fixture.Intents, "ui.settings.section", FindByKey(shell, scene, "SettingsNav.advanced").Entity);
            var window = new Window { Width = 1040, Height = 760, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Capture("update-center");
                foreach (string phase in UpdatePreviewPhases)
                {
                    updates.Snapshot = new("checked", [new("Nexa.Cloud.Market.Sidecar", "1.0.0", "1.1.0", phase, phase == "failed", .42)]);
                    await Capture("sidecar-" + phase);
                }
                foreach (string name in OnboardingPreviewPages)
                {
                    var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load(name + ".nxml"), shell.Tree, fixture.Store);
                    shell.Stage.Navigation.Replace(page); await Capture(name);
                }
            }
            finally { window.Close(); }
            async Task Capture(string name)
            {
                foreach (int width in SidecarPreviewWidths)
                    foreach (bool dark in WardrobePreviewThemes)
                    {
                        shell.Renderer.ColorScheme = new(dark);
                        using var surface = new AvaloniaUiSceneSurface(shell) { Width = width, Height = 700 };
                        window.Content = surface;
                        surface.Measure(new Size(width, 700)); surface.Arrange(new Rect(0, 0, width, 700));
                        for (int frame = 0; frame < 4; frame++) surface.CommitScene();
                        window.UpdateLayout(); surface.UpdateLayout(); await Task.Delay(100);
                        using var bitmap = new RenderTargetBitmap(new PixelSize(width, 700), new Vector(96, 96));
                        bitmap.Render(surface);
                        string stem = $"{name}-{width}-{(dark ? "dark" : "light")}";
                        bitmap.Save(Path.Combine(output, stem + ".png"), PngBitmapEncoderOptions.Default);
                        File.WriteAllLines(Path.Combine(output, stem + ".txt"), surface.Scene!.Nodes.Select(n => $"{shell.Tree.Name(n.Entity)} | {n.Rect} | {n.Text}"));
                    }
            }
        });
    }
    private sealed class PreviewSidecarUpdates : ISidecarUpdateControl
    {
        public SidecarUpdateSnapshot Snapshot { get; set; } = new("checked", [new("Nexa.Cloud.Market.Sidecar", "1.0.0", "1.1.0", "available", true)]);
        public Task CheckAsync(string channel, CancellationToken token) => Task.CompletedTask;
        public Task InstallAsync(string id, CancellationToken token) => Task.CompletedTask;
    }
}
