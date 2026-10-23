using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Nexa.Desktop.Ui;
using Nexa.Services.Capabilities;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static int RunNativeSettingsNavigationStress(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(async () =>
        {
            Directory.CreateDirectory(output);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], false,
                new(XsrUiThemeMode.Dark), cancellationToken: timeout.Token);
            LaunchPageFixture? fixture = null;
            SettingsPageController? settings = null;
            AvaloniaUiShellWindow? window = null;
            Task<int>? lifetime = null;
            var actions = new AvaloniaUiPlatformActions();
            actions.SetDesktopPolicy(new(TrayEnabled: false));
            try
            {
                await startup.InvokeAsync(async () =>
                {
                    fixture = new(new ImmediateInstanceSource([]));
                    settings = new(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
                        fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
                    fixture.Controller.SettingsPage = settings.Page;
                    await settings.PrepareStartupAsync(timeout.Token);
                    fixture.Shell.Renderer.ReducedMotion = false;
                    AssertFalse(fixture.Shell.Renderer.EffectiveReducedMotion);
                    Emit(fixture.Intents, "ui.navigation.settings");
                    fixture.Shell.Render(new(850, 680));
                }, timeout.Token);
                var f = fixture!;
                await startup.PrepareShellAsync(f.Shell, actions, timeout.Token);
                await startup.WarmUpShellAsync(f.Shell, timeout.Token);
                lifetime = Task.Run(() => AvaloniaUiShellHost.Run(startup, f.Shell, actions));
                while (window is null)
                {
                    await startup.InvokeAsync(() => window = (Application.Current?.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime)?.MainWindow as AvaloniaUiShellWindow, timeout.Token);
                    if (window is null) await Task.Delay(20, timeout.Token);
                }
                await startup.InvokeAsync(() =>
                {
                    window.Width = 850; window.Height = 680;
                    window.UpdateLayout(); window.Surface.CommitScene();
                    AssertTrue(Node("SettingsNavigation").Scroll is { MaximumOffsetX: > 0 });
                }, timeout.Token);

                HashSet<string> visited = [];
                // Real product segmented track, pager, scene commits and native compositor.
                // Each sweep reverses while the prior page/selection springs are still active.
                for (int sweep = 0; sweep < 24; sweep++)
                {
                    XsrUiPoint point = default;
                    await startup.InvokeAsync(() =>
                    {
                        window.Surface.CommitScene();
                        var thumb = Node("SettingsThumb");
                        point = new(thumb.Rect.X + thumb.Rect.Width / 2, thumb.Rect.Y + thumb.Rect.Height / 2);
                        AssertTrue(f.Shell.Renderer.PointerPressed(point));
                        window.Surface.CommitScene();
                    }, timeout.Token);
                    for (int step = 0; step <= 16; step++)
                    {
                        await startup.InvokeAsync(() =>
                        {
                            var navigation = Node("SettingsNavigation");
                            double fraction = sweep % 2 == 0 ? step / 16d : 1 - step / 16d;
                            point = new(navigation.Rect.X + 2 + (navigation.Rect.Width - 4) * fraction,
                                navigation.Rect.Y + navigation.Rect.Height / 2);
                            AssertTrue(f.Shell.Renderer.PointerMoved(point));
                            window.Surface.CommitScene();
                            visited.Add(settings!.SelectedSection);
                        }, timeout.Token);
                        await Task.Delay(4, timeout.Token);
                    }
                    await startup.InvokeAsync(() =>
                    {
                        AssertTrue(f.Shell.Renderer.PointerReleased(point));
                        window.Surface.CommitScene();
                    }, timeout.Token);
                }
                if (!visited.Contains("platform")) throw new InvalidOperationException("The gesture must actually enter device capabilities.");
                if (visited.Count < 8) throw new InvalidOperationException("The gesture must traverse settings rather than only scroll a page.");
                // Keep the real capability query alive long enough to finish, then verify the
                // same off-thread native memory path used by the broker without cached facts.
                await Task.Delay(500, timeout.Token);
                if (OperatingSystem.IsMacOS())
                {
                    var values = await Task.Run(() => MemoryCapabilityProvider.CollectMacOs(DateTimeOffset.UtcNow), timeout.Token);
                    AssertEqual(CapabilityAvailability.Available,
                        values.Single(value => value.Id == MachineCapabilityCatalog.PhysicalAvailable.Id).Availability);
                }
                await startup.InvokeAsync(() =>
                {
                    window.Surface.CommitScene();
                    double scale = window.RenderScaling;
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.ClientSize.Width * scale),
                        (int)Math.Ceiling(window.ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(output, "settings-after-rapid-drag.png"), PngBitmapEncoderOptions.Default);
                    Console.WriteLine($"PASS: 24 rapid native settings sweeps, 408 moves; visited={string.Join(',', visited.Order())}; motion enabled.");
                }, timeout.Token);

                XsrUiSceneNode Node(string key) => FindByKey(f.Shell, window.Surface.Scene!, key);
            }
            finally
            {
                if (!startup.Completion.IsCompleted)
                    await startup.InvokeAsync(() => window?.Close());
                if (lifetime is not null) AssertEqual(0, await lifetime.WaitAsync(TimeSpan.FromSeconds(10)));
                settings?.Dispose(); fixture?.Dispose();
            }
            return 0;
        });
}
