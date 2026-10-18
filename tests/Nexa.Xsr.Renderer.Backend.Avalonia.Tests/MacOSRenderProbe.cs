using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static readonly string[] MacProbeStages = ["initial", "resized", "restored"];
    private static int RunMacOSRenderProbe(string mode, string directory)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Cocoa probe requires macOS.");
        return AvaloniaUiStartupSession.RunApplicationOnMainThread(async () =>
        {
            Directory.CreateDirectory(directory);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
                new(mode == "dark" ? XsrUiThemeMode.Dark : XsrUiThemeMode.Light, ReducedMotion: true), cancellationToken: timeout.Token);
            XsrUiShell? shell = null;
            AvaloniaUiShellWindow? window = null;
            Task<int>? lifetime = null;
            var actions = new AvaloniaUiPlatformActions();
            actions.SetDesktopPolicy(new(TrayEnabled: false));
            try
            {
                await startup.InvokeAsync(() =>
                {
                    var builder = new XsrStateStoreBuilder();
                    XsrUiShellWindowState.Declare(builder);
                    shell = XsrUiShellComposer.Compose(builder.Build());
                    shell.Renderer.ColorScheme = new(mode == "dark");
                    shell.Renderer.ReducedMotion = true;
                }, timeout.Token);
                await startup.PrepareShellAsync(shell!, actions, timeout.Token);
                await startup.WarmUpShellAsync(shell!, timeout.Token);
                lifetime = Task.Run(() => AvaloniaUiShellHost.Run(startup, shell!, actions));
                while (window is null)
                {
                    await startup.InvokeAsync(() => window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                        ?.MainWindow as AvaloniaUiShellWindow, timeout.Token);
                    if (window is null) await Task.Delay(20, timeout.Token);
                }
                foreach (string stage in MacProbeStages)
                {
                    await startup.InvokeAsync(() =>
                    {
                        if (stage == "resized") { window.Width = 1040; window.Height = 680; }
                        if (stage == "restored") { window.Hide(); window.Show(); }
                    }, timeout.Token);
                    await Task.Delay(250, timeout.Token);
                    await startup.InvokeAsync(() =>
                    {
                        window.UpdateLayout(); window.Surface.CommitScene();
                        double scale = window.RenderScaling;
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.ClientSize.Width * scale),
                            (int)Math.Ceiling(window.ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
                        bitmap.Render(window);
                        string name = Path.Combine(directory, $"macos-{mode}-{stage}");
                        bitmap.Save(name + ".png", PngBitmapEncoderOptions.Default);
                        var origin = window.Surface.TranslatePoint(default, window) ?? default;
                        var title = window.Surface.Scene!.Nodes.First(node => node.Role == XsrUiSemanticRole.TitleBar);
                        JsonObject facts = new()
                        {
                            ["mode"] = mode,
                            ["stage"] = stage,
                            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                            ["clientWidth"] = window.ClientSize.Width,
                            ["clientHeight"] = window.ClientSize.Height,
                            ["windowWidth"] = window.Bounds.Width,
                            ["windowHeight"] = window.Bounds.Height,
                            ["surfaceX"] = origin.X,
                            ["surfaceY"] = origin.Y,
                            ["surfaceWidth"] = window.Surface.Bounds.Width,
                            ["surfaceHeight"] = window.Surface.Bounds.Height,
                            ["scale"] = scale,
                            ["extended"] = window.IsExtendedIntoWindowDecorations,
                            ["titleX"] = title.Rect.X,
                            ["titleY"] = title.Rect.Y,
                            ["titleWidth"] = title.Rect.Width,
                            ["titleHeight"] = title.Rect.Height,
                            ["titleColor"] = title.VisualStyle.Background.ToString(),
                            ["trafficLightPadding"] = AvaloniaMacWindow.ConfigureAndMeasure(window),
                            ["native"] = MacProbeNativeFacts(window),
                            ["renderer"] = "RenderTargetBitmap of actual Cocoa window; excludes OS compositor buttons"
                        };
                        File.WriteAllText(name + ".json", facts.ToJsonString());
                        Console.WriteLine("PASS capture " + name);
                    }, timeout.Token);
                }
            }
            finally
            {
                if (!startup.Completion.IsCompleted) await startup.InvokeAsync(() => window?.Close());
                if (lifetime is not null) AssertEqual(0, await lifetime.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            return 0;
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacProbeRect { public double X, Y, Width, Height; }
    private const string MacProbeObjC = "/usr/lib/libobjc.A.dylib";
    [LibraryImport(MacProbeObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint MacProbeSelector(string name);
    [DllImport(MacProbeObjC, EntryPoint = "objc_msgSend")]
    private static extern nint MacProbeSend(nint target, nint selector);
    [DllImport(MacProbeObjC, EntryPoint = "objc_msgSend")]
    private static extern nint MacProbeButton(nint target, nint selector, nint kind);
    [DllImport(MacProbeObjC, EntryPoint = "objc_msgSend")]
    private static extern MacProbeRect MacProbeGetRect(nint target, nint selector);
    [DllImport(MacProbeObjC, EntryPoint = "objc_msgSend_stret")]
    private static extern void MacProbeGetRectStret(out MacProbeRect result, nint target, nint selector);
    private static MacProbeRect MacProbeFrame(nint target)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return MacProbeGetRect(target, MacProbeSelector("frame"));
        MacProbeGetRectStret(out var result, target, MacProbeSelector("frame")); return result;
    }
    private static JsonObject MacProbeRectFacts(MacProbeRect value) => new()
    { ["x"] = value.X, ["y"] = value.Y, ["width"] = value.Width, ["height"] = value.Height };
    private static JsonObject MacProbeNativeFacts(Window window)
    {
        var handle = window.TryGetPlatformHandle()!;
        nint native = handle.HandleDescriptor == "NSWindow" ? handle.Handle
            : handle.HandleDescriptor == "NSView" ? MacProbeSend(handle.Handle, MacProbeSelector("window")) : 0;
        JsonArray buttons = [];
        if (native != 0)
            for (nint i = 0; i < 3; i++)
            {
                nint button = MacProbeButton(native, MacProbeSelector("standardWindowButton:"), i);
                if (button != 0) buttons.Add((JsonNode)new JsonObject
                {
                    ["kind"] = (int)i,
                    ["frame"] = MacProbeRectFacts(MacProbeFrame(button)),
                    ["superviewFrame"] = MacProbeRectFacts(MacProbeFrame(MacProbeSend(button, MacProbeSelector("superview"))))
                });
            }
        return new()
        {
            ["descriptor"] = handle.HandleDescriptor,
            ["hasWindow"] = native != 0,
            ["buttons"] = buttons,
            ["frame"] = native == 0 ? null : MacProbeRectFacts(MacProbeFrame(native))
        };
    }
}
