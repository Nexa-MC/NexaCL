using System.Buffers.Binary;
using Nexa.Core.Media;
using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Ui.Tests;

internal static partial class Program
{
    private static void WindowValues(NativeUiCallClient peer)
    {
        var facts = new WindowStartupFacts(new(), true, "ready", null, 42);
        byte[] encodedFacts = WindowStartupFacts.Encode(facts);
        Equal(facts, WindowStartupFacts.Decode(encodedFacts));
        Throws<InvalidDataException>(() => WindowStartupFacts.Encode(facts with { FirstRenderTicks = -1 }));
        Throws<IOException>(() => WindowStartupFacts.Decode(encodedFacts.AsMemory(0, encodedFacts.Length - 1)));
        Throws<InvalidDataException>(() => WindowStartupFacts.Decode((byte[])[.. encodedFacts, 0]));

        byte[] Exchange(byte kind, byte[] value)
        {
            byte[] request = new byte[value.Length + 2]; request[0] = 10; request[1] = kind;
            value.CopyTo(request, 2); return peer.Exchange(request);
        }
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var colors = new AvaloniaUiThemeStartupColors(new(1, 2, 3, 4), new(5, 6, 7, 8), new(9, 10, 11, 12));
        var startup = new AvaloniaUiStartupAppearance(XsrUiThemeMode.Dark, true, "2.0.0.alpha.99",
            new(42, png, png, colors, AvaloniaUiThemeSplashMotion.Breathe));
        byte[] startupBytes = WindowValueChannel.EncodeStartup(startup);
        var receivedStartup = WindowValueChannel.DecodeStartup(Exchange(0, startupBytes));
        Equal(startup.ThemeMode, receivedStartup.ThemeMode); Equal(startup.ProductVersion, receivedStartup.ProductVersion);
        True(receivedStartup.ReducedMotion);
        Equal(colors, receivedStartup.Theme!.Colors); Equal(42L, receivedStartup.Theme.Generation);
        True(receivedStartup.Theme.BrandLightPng.Span.SequenceEqual(png));
        Equal(AvaloniaUiThemeSplashMotion.Breathe, receivedStartup.Theme.Motion);

        var brand = new AvaloniaUiThemeResource("brand", "image/png", png);
        var theme = new AvaloniaUiThemeAppearance(43, brand, brand, [brand], colors,
            AvaloniaUiThemeWindowMaterial.Acrylic, false, brand, brand, brand, brand, brand, brand,
            AvaloniaUiThemeResizePolicy.Animated, AvaloniaUiThemeSplashMotion.Draw);
        var received = WindowValueChannel.DecodeTheme(Exchange(1, WindowValueChannel.EncodeTheme(theme)));
        Equal(theme.Generation, received.Generation); Equal(theme.StartupColors, received.StartupColors);
        Equal(theme.ResizePolicy, received.ResizePolicy); Equal(theme.SplashMotion, received.SplashMotion);
        Equal(theme.WindowMaterial, received.WindowMaterial); True(!received.UseNativeTextLayout);
        foreach (var resource in new[] { received.BrandLight, received.BrandDark, received.WindowIconLight, received.WindowIconDark,
            received.TrayIconLight, received.TrayIconDark, received.SplashIconLight, received.SplashIconDark, received.Resources![0] })
        { Equal(brand.ResourceId, resource!.ResourceId); Equal(brand.MediaType, resource.MediaType); True(resource.Content.Span.SequenceEqual(png)); }
        var policy = new AvaloniaUiDesktopPolicy(false, true, true);
        Equal(policy, WindowValueChannel.DecodeDesktopPolicy(Exchange(2, WindowValueChannel.EncodeDesktopPolicy(policy))));
        var media = new AvaloniaUiMediaState(AvaloniaUiMediaStatus.Paused, "本地媒体", 44);
        Equal(media, WindowValueChannel.DecodeMediaState(Exchange(3, WindowValueChannel.EncodeMediaState(media))));
        var frame = new AvaloniaUiMediaFrame(45, "/local/frame.png", PngImage.TryCreate(png)!);
        var receivedFrame = WindowValueChannel.DecodeMediaFrame(Exchange(4, WindowValueChannel.EncodeMediaFrame(frame)));
        Equal(frame.Generation, receivedFrame.Generation); Equal(frame.Path, receivedFrame.Path); Equal(frame.Image.Key, receivedFrame.Image.Key);
        var capture = new AvaloniaUiRuntimeCapture(1, 2, 3, true, 60, 59.5, 4, 5, 6, 7, 8, 9,
            new(AvaloniaUiMemoryPressure.Elevated, 10, null, 11, "native"));
        Equal(capture, WindowValueChannel.DecodeRuntimeCapture(Exchange(5, WindowValueChannel.EncodeRuntimeCapture(capture))));
        Equal(default(AvaloniaUiRuntimeCapture), WindowValueChannel.DecodeRuntimeCapture(WindowValueChannel.EncodeRuntimeCapture(default)));
        var material = new AvaloniaUiThemeMaterialPresentation(AvaloniaUiThemeWindowMaterial.Mica, "solid", true);
        Equal(material, WindowValueChannel.DecodeMaterial(Exchange(6, WindowValueChannel.EncodeMaterial(material))));
        Equal(AvaloniaUiInputKind.Touch, WindowValueChannel.DecodeInputKind(Exchange(7, WindowValueChannel.EncodeInputKind(AvaloniaUiInputKind.Touch))));
        Equal(AvaloniaUiThemePickerKind.Font, WindowValueChannel.DecodePickerKind(Exchange(8, WindowValueChannel.EncodePickerKind(AvaloniaUiThemePickerKind.Font))));

        Throws<InvalidDataException>(() => WindowValueChannel.DecodeStartup([.. startupBytes, 0]));
        Throws<EndOfStreamException>(() => WindowValueChannel.DecodeStartup(startupBytes[..^1]));
        byte[] wrongSchema = (byte[])startupBytes.Clone(); wrongSchema[12] ^= 1;
        Throws<InvalidDataException>(() => WindowValueChannel.DecodeStartup(wrongSchema));
        Throws<InvalidDataException>(() => WindowValueChannel.DecodeTheme(startupBytes));
        byte[] badEnum = WindowValueChannel.EncodePickerKind(AvaloniaUiThemePickerKind.Font);
        BinaryPrimitives.WriteInt32LittleEndian(badEnum.AsSpan(badEnum.Length - 4), 999);
        Throws<InvalidDataException>(() => WindowValueChannel.DecodePickerKind(badEnum));
        Throws<ArgumentException>(() => WindowValueChannel.EncodeStartup(startup with { ProductVersion = new string('x', 81) }));
        Throws<InvalidDataException>(() => WindowValueChannel.EncodeTheme(theme with { Resources = [brand, brand] }));

        // The existing 64 MiB resource allowance must survive control-envelope overhead.
        byte[] font = new byte[16 * 1024 * 1024]; font[0] = 91;
        var maximum = new AvaloniaUiThemeAppearance(46, Resources: Enumerable.Range(0, 4)
            .Select(index => new AvaloniaUiThemeResource("font-" + index, "font/ttf", font)).ToArray());
        byte[] maximumBytes = WindowValueChannel.EncodeTheme(maximum);
        True(maximumBytes.Length > 64 * 1024 * 1024);
        var maximumResult = WindowValueChannel.DecodeTheme(Exchange(1, maximumBytes));
        Equal(4, maximumResult.Resources!.Count);
        foreach (var resource in maximumResult.Resources)
        { Equal(font.Length, resource.Content.Length); Equal((byte)91, resource.Content.Span[0]); }
        Throws<InvalidDataException>(() => WindowValueChannel.EncodeTheme(maximum with { Resources = [.. maximum.Resources!, brand] }));
        Console.WriteLine("PASS: window values cross independent runtimes without Avalonia, preserving startup, theme slots, diagnostics and 64 MiB theme admission");
    }
}
