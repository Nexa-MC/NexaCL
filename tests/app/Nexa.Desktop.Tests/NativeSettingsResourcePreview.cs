using Avalonia.Controls;
using Nexa.Desktop.Ui;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static int RunNativeSettingsResourcePreview(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(() => CaptureSettingsResourceAsync(Path.GetFullPath(output)));

    private static async Task<int> CaptureSettingsResourceAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true), cancellationToken: timeout.Token);
        await startup.InvokeAsync(() =>
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
            using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
                fixture.Foundation.Commands, fixture.Store, fixture.Feedback, regionalPolicy: new("CN"));
            fixture.Controller.SettingsPage = settings.Page;
            fixture.Shell.Renderer.ReducedMotion = true;
            var window = new Window { Width = 1280, Height = 800, ShowInTaskbar = false };
            window.Show();
            try
            {
                Emit(fixture.Intents, "ui.navigation.settings");
                AssertTrue(SpinWait.SpinUntil(() =>
                {
                    fixture.Shell.Render(new(1280, 800));
                    return fixture.Shell.Tree.LiveEntities.Any(node => fixture.Shell.Tree.Name(node) == "SettingsNav.general");
                }, TimeSpan.FromSeconds(5)));
                foreach (bool dark in InstallGuidePreviewThemes)
                {
                    fixture.Shell.Renderer.ColorScheme = new(dark);
                    foreach (var page in SettingsCatalog.GlobalPages)
                    {
                        var navigation = fixture.Shell.Tree.LiveEntities.Single(node => fixture.Shell.Tree.Name(node) == "SettingsNav." + page.Id);
                        Emit(fixture.Intents, "ui.settings.section", navigation);
                        fixture.Shell.Render(new(1280, 800));
                        CaptureNativeReviewScene(window, fixture.Shell, new(1280, 800),
                            Path.Combine(output, $"settings-{page.Id}-{(dark ? "dark" : "light")}.png"));
                    }
                }
                Emit(fixture.Intents, "ui.navigation.launch");
                using var resources = new NativeResourcePreviewHarness();
                resources.Fixture.Shell.Renderer.ReducedMotion = true;
                foreach (bool dark in InstallGuidePreviewThemes)
                {
                    resources.Fixture.Shell.Renderer.ColorScheme = new(dark);
                    foreach (var size in InstallGuidePreviewSizes)
                    {
                        var scene = CaptureNativeReviewScene(window, resources.Fixture.Shell, size,
                            Path.Combine(output, $"resources-loading-{(dark ? "dark" : "light")}-{size.Width}.png"));
                        var filters = FindByKey(resources.Fixture.Shell, scene, "ResourceFilters");
                        var loading = FindByKey(resources.Fixture.Shell, scene, "ResourceInitialLoading");
                        AssertTrue(loading.Rect.Y >= filters.Rect.Y + filters.Rect.Height);
                        AssertTrue(FindByKey(resources.Fixture.Shell, scene, "ResourceProvider").IsClickable);
                        AssertTrue(FindByKey(resources.Fixture.Shell, scene, "ResourceSearch").IsEnabled);
                    }
                }
            }
            finally { window.Content = null; window.Close(); }
        }, timeout.Token);
        Console.WriteLine("PASS: ten actual global settings pages and resource loading with retained filters, both themes; controlled fixtures only.");
        return 0;
    }
}
