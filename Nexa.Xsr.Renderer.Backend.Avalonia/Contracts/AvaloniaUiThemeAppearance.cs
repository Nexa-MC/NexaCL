using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Already authorized resource bytes. No path or URI is opened by native theme preparation.</summary>
public sealed record AvaloniaUiThemeResource(string ResourceId, string MediaType, ReadOnlyMemory<byte> Content);
public enum AvaloniaUiThemeResizePolicy { Auto, Immediate, Deferred, Animated, Instant }
public enum AvaloniaUiThemeSplashMotion { Default, None, Fade, Breathe, Draw }
public enum AvaloniaUiThemeWindowMaterial { Default, Transparent, Acrylic, Mica, Solid }
public sealed record AvaloniaUiThemeMaterialPresentation(AvaloniaUiThemeWindowMaterial Requested, string Actual, bool UsesFallback);
public sealed record AvaloniaUiThemeStartupColors(XsrUiColor Background, XsrUiColor Foreground, XsrUiColor Accent);

/// <summary>One bounded, finite appearance candidate; composition owns permissions and generation selection.</summary>
public sealed record AvaloniaUiThemeAppearance(long Generation,
    AvaloniaUiThemeResource? BrandLight = null, AvaloniaUiThemeResource? BrandDark = null,
    IReadOnlyList<AvaloniaUiThemeResource>? Resources = null,
    AvaloniaUiThemeStartupColors? StartupColors = null,
    AvaloniaUiThemeWindowMaterial WindowMaterial = AvaloniaUiThemeWindowMaterial.Default,
    bool UseNativeTextLayout = true,
    AvaloniaUiThemeResource? WindowIconLight = null, AvaloniaUiThemeResource? WindowIconDark = null,
    AvaloniaUiThemeResource? TrayIconLight = null, AvaloniaUiThemeResource? TrayIconDark = null,
    AvaloniaUiThemeResource? SplashIconLight = null, AvaloniaUiThemeResource? SplashIconDark = null,
    AvaloniaUiThemeResizePolicy ResizePolicy = AvaloniaUiThemeResizePolicy.Auto,
    AvaloniaUiThemeSplashMotion SplashMotion = AvaloniaUiThemeSplashMotion.Default)
{
    internal AvaloniaUiThemeAppearance Snapshot()
    {
        if (Generation < 0) throw new ArgumentOutOfRangeException(nameof(Generation));
        if (!Enum.IsDefined(ResizePolicy) || !Enum.IsDefined(SplashMotion)) throw new ArgumentOutOfRangeException(nameof(ResizePolicy));
        if (!Enum.IsDefined(WindowMaterial)) throw new ArgumentOutOfRangeException(nameof(WindowMaterial));
        AvaloniaUiThemeResource[] resources = (Resources ?? []).ToArray();
        if (resources.Length > 256) throw new InvalidDataException("Native theme resource count exceeds its budget.");
        long total = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        AvaloniaUiThemeResource Copy(AvaloniaUiThemeResource r)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(r.ResourceId);
            if (r.ResourceId.Length > 4096 || r.ResourceId.Any(char.IsControl)) throw new InvalidDataException("Invalid native resource identity.");
            int maximum = r.MediaType switch
            {
                "image/png" or "image/jpeg" or "image/webp" or "image/gif" or "image/bmp" or "image/x-icon" => 16 * 1048576,
                "image/svg+xml" => 1048576,
                "font/ttf" or "font/otf" => 16 * 1048576,
                _ => throw new InvalidDataException("Unsupported native theme resource type: " + r.MediaType),
            };
            if (r.Content.Length is < 1 || r.Content.Length > maximum) throw new InvalidDataException("Native theme resource exceeds its byte budget.");
            total += r.Content.Length;
            if (total > 64 * 1048576) throw new InvalidDataException("Native theme resources exceed their combined budget.");
            return r with { Content = r.Content.ToArray() };
        }
        for (int i = 0; i < resources.Length; i++)
        {
            if (!ids.Add(resources[i].ResourceId)) throw new InvalidDataException("Duplicate native theme resource.");
            resources[i] = Copy(resources[i]);
        }
        AvaloniaUiThemeResource? light = BrandLight is null ? null : Copy(BrandLight);
        AvaloniaUiThemeResource? dark = BrandDark is null ? null : Copy(BrandDark);
        if ((light is null) != (dark is null)) throw new InvalidDataException("Brand appearance requires both light and dark resources.");
        if (light?.MediaType.StartsWith("image/", StringComparison.Ordinal) == false || dark?.MediaType.StartsWith("image/", StringComparison.Ordinal) == false)
            throw new InvalidDataException("Brand resources must be images.");
        (AvaloniaUiThemeResource? Light, AvaloniaUiThemeResource? Dark) Pair(AvaloniaUiThemeResource? a, AvaloniaUiThemeResource? b)
        {
            if ((a is null) != (b is null)) throw new InvalidDataException("Native icon slots require both variants.");
            if (a?.MediaType.StartsWith("image/", StringComparison.Ordinal) == false || b?.MediaType.StartsWith("image/", StringComparison.Ordinal) == false)
                throw new InvalidDataException("Native icons must be images.");
            return (a is null ? null : Copy(a), b is null ? null : Copy(b));
        }
        var window = Pair(WindowIconLight, WindowIconDark); var tray = Pair(TrayIconLight, TrayIconDark); var splash = Pair(SplashIconLight, SplashIconDark);
        return this with
        {
            BrandLight = light,
            BrandDark = dark,
            Resources = resources,
            WindowIconLight = window.Light,
            WindowIconDark = window.Dark,
            TrayIconLight = tray.Light,
            TrayIconDark = tray.Dark,
            SplashIconLight = splash.Light,
            SplashIconDark = splash.Dark
        };

    }
}

public static class AvaloniaUiThemeCapabilities
{
    public static IReadOnlySet<string> Supported { get; } = Build();
    private static HashSet<string> Build()
    {
        var result = new HashSet<string>(StringComparer.Ordinal)
        {
            "images", "svg", "fonts", "opacity", "gradients", "shadows", "transforms", "animations", "translucency", "runtime-icons", "startup-appearance",
        };
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) result.Add("window-acrylic");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) result.Add("window-mica");
        return result;
    }
}
