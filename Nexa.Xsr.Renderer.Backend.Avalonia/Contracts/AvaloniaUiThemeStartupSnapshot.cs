using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Finite last-known-good splash facts. No package, NSS, font, directory or media loading is allowed here.</summary>
public sealed record AvaloniaUiThemeStartupSnapshot(long Generation,
    ReadOnlyMemory<byte> BrandLightPng = default, ReadOnlyMemory<byte> BrandDarkPng = default,
    AvaloniaUiThemeStartupColors? Colors = null,
    AvaloniaUiThemeSplashMotion Motion = AvaloniaUiThemeSplashMotion.Default)
{
    internal AvaloniaUiThemeStartupSnapshot Snapshot()
    {
        if (!Enum.IsDefined(Motion)) throw new ArgumentOutOfRangeException(nameof(Motion));
        if (Generation < 0) throw new ArgumentOutOfRangeException(nameof(Generation));
        if (BrandLightPng.IsEmpty != BrandDarkPng.IsEmpty) throw new InvalidDataException("Startup brand requires both variants.");
        foreach (ReadOnlyMemory<byte> bytes in new[] { BrandLightPng, BrandDarkPng })
            if (!bytes.IsEmpty && (bytes.Length > 262144 || PngImage.TryCreate(bytes.Span) is not { Width: <= 512, Height: <= 512 }))
                throw new InvalidDataException("Startup brand exceeds its finite PNG budget.");
        return this with { BrandLightPng = BrandLightPng.ToArray(), BrandDarkPng = BrandDarkPng.ToArray() };
    }
}
