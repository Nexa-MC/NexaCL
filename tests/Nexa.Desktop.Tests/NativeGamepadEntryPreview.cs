using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly bool[] GamepadPreviewThemes = [false, true];
    private static int RunNativeGamepadEntryPreview(string output) =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(() => CaptureGamepadEntryAsync(Path.GetFullPath(output)));

    private static async Task<int> CaptureGamepadEntryAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(Nexa.Xsr.Renderer.XsrUiThemeMode.Dark, ReducedMotion: true), cancellationToken: timeout.Token);
        await startup.InvokeAsync(async () =>
        {
            foreach (bool dark in GamepadPreviewThemes)
                foreach (var layout in Enum.GetValues<GamepadLayout>())
                {
                    using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
                    fixture.Shell.Renderer.ColorScheme = new(dark);
                    fixture.Shell.Renderer.ReducedMotion = false;
                    var window = new AvaloniaUiShellWindow(fixture.Shell) { Width = 1100, Height = 750, ShowInTaskbar = false, Position = new(120, 70) };
                    var platform = new AvaloniaUiPlatformActions();
                    platform.Attach(window); window.Show();
                    using var entry = new GamepadEntrySession(fixture.Shell, fixture.Intents, fixture.Store, platform, runTimer: false);
                    try
                    {
                        var pad = new GamepadSample("preview", layout);
                        entry.Step([pad], 0); entry.UpdatePresentation(1200);
                        await Task.Delay(400);
                        Save($"prompt-{layout}-{(dark ? "dark" : "light")}");
                        AssertTrue(window.GetVisualDescendants().OfType<AvaloniaUiSceneNodeControl>()
                            .Any(control => control.Node.ImageSource == "gamepad/drawing" && control.HasDecodedRaster));
                        if (!dark || layout != GamepadLayout.OffsetLeftHigh) continue;
                        JsonArray frames = [];
                        long clock = 1200;
                        for (int index = 0; index < 136; index++)
                        {
                            if (index >= 20) entry.Step([pad with { LeftX = 1, RightX = -1 }], clock);
                            else entry.UpdatePresentation(clock);
                            Save($"frame-{index:D3}");
                            if (index == 64) AssertTrue(window.GetVisualDescendants().OfType<AvaloniaUiSceneNodeControl>()
                                .Any(control => control.Node.ImageSource == "gamepad/horizon" && control.HasDecodedRaster));
                            if (index == 56) AssertTrue(window.GetVisualDescendants().OfType<AvaloniaUiSceneNodeControl>()
                                .Any(control => control.Node.ImageSource == "gamepad/flash" && control.HasDecodedRaster));
                            string? phase = index switch
                            {
                                40 => "entry-black",
                                56 => "entry-flash",
                                64 => "entry-corner",
                                72 => "entry-pullback",
                                82 => "entry-logo",
                                96 => "entry-name",
                                112 => "entry-lockup",
                                _ => null
                            };
                            if (phase is not null) Save(phase);
                            frames.Add((JsonNode)new JsonObject
                            {
                                ["file"] = $"frame-{index:D3}.png",
                                ["x"] = window.Position.X,
                                ["y"] = window.Position.Y,
                                ["width"] = window.Bounds.Width,
                                ["height"] = window.Bounds.Height
                            });
                            await Task.Delay(50); clock += 50;
                        }
                        AssertTrue(entry.IsEntered); AssertTrue(entry.IsIntroComplete);
                        AssertEqual(global::Avalonia.Controls.WindowState.FullScreen, window.WindowState);
                        File.WriteAllText(Path.Combine(output, "frames.json"), frames.ToJsonString());
                        entry.Step([pad with { Cancel = true }], clock);
                        await Task.Delay(150);
                        AssertEqual(global::Avalonia.Controls.WindowState.Normal, window.WindowState);
                        AssertTrue(Math.Abs(window.Width - 1100) < 1); AssertTrue(Math.Abs(window.Height - 750) < 1);
                        AssertTrue(Math.Abs(window.Bounds.Width - 1100) < 1); AssertTrue(Math.Abs(window.Bounds.Height - 750) < 1);
                        window.WindowState = global::Avalonia.Controls.WindowState.Maximized;
                        await Task.Delay(200);
                        Console.WriteLine($"Before maximized entry: requested={window.Width}x{window.Height} actual={window.Bounds.Size} state={window.WindowState}");
                        window.SetImmersivePresentation(true); await Task.Delay(800);
                        window.SetImmersivePresentation(false); await Task.Delay(200);
                        Console.WriteLine($"After maximized entry: requested={window.Width}x{window.Height} actual={window.Bounds.Size} state={window.WindowState}");
                        AssertEqual(global::Avalonia.Controls.WindowState.Maximized, window.WindowState);
                        window.WindowState = global::Avalonia.Controls.WindowState.Normal; await Task.Delay(200);
                        AssertEqual(new Size(1100, 750), window.Bounds.Size);
                        window.WindowState = global::Avalonia.Controls.WindowState.FullScreen; await Task.Delay(200);
                        window.SetImmersivePresentation(true); window.SetImmersivePresentation(false);
                        AssertEqual(global::Avalonia.Controls.WindowState.FullScreen, window.WindowState);
                    }
                    finally { fixture.Shell.Renderer.ReducedMotion = true; entry.Close(); window.Close(); }

                    void Save(string name)
                    {
                        window.UpdateLayout();
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height)), new Vector(96, 96));
                        bitmap.Render(window); bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
                        if (name is "entry-black" or "entry-corner" or "entry-flash")
                        {
                            // Decoding alone does not prove a recipe was admitted and painted.
                            // The plate arc must paint, and its interior must occlude the sun.
                            IntPtr pixel = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                            try
                            {
                                bitmap.CopyPixels(new PixelRect(bitmap.PixelSize.Width / 2, bitmap.PixelSize.Height / 2, 1, 1), pixel, 4, 4);
                                int color = System.Runtime.InteropServices.Marshal.ReadInt32(pixel) & 0x00ffffff;
                                if (name == "entry-black") AssertEqual(0, color);
                                else AssertTrue(color != 0);
                                if (name == "entry-corner")
                                {
                                    bitmap.CopyPixels(new PixelRect(bitmap.PixelSize.Width / 2 + 60, bitmap.PixelSize.Height / 2 + 60, 1, 1), pixel, 4, 4);
                                    int night = System.Runtime.InteropServices.Marshal.ReadInt32(pixel);
                                    AssertTrue((night & 255) < 24 && ((night >> 8) & 255) < 24 && ((night >> 16) & 255) < 24);
                                    bitmap.CopyPixels(new PixelRect(bitmap.PixelSize.Width / 5, bitmap.PixelSize.Height / 5, 1, 1), pixel, 4, 4);
                                    int space = System.Runtime.InteropServices.Marshal.ReadInt32(pixel);
                                    AssertTrue((space & 255) < 8 && ((space >> 8) & 255) < 8 && ((space >> 16) & 255) < 8);
                                }
                            }
                            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pixel); }
                        }
                    }
                }
        }, timeout.Token);
        Console.WriteLine("PASS: native gamepad prompts for all layouts/themes, actual fullscreen window expansion, entry logo and restoration.");
        return 0;
    }
}
