namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Finite startup presentation preferences supplied by the product composition root.</summary>
public sealed record AvaloniaUiStartupAppearance(XsrUiThemeMode ThemeMode = XsrUiThemeMode.System,
    bool ReducedMotion = false, string ProductVersion = "2.0.0.alpha.8",
    AvaloniaUiThemeStartupSnapshot? Theme = null)
{
    public AvaloniaUiStartupAppearance(XsrUiThemeMode themeMode, bool reducedMotion, string productVersion)
        : this(themeMode, reducedMotion, productVersion, null) { }
    public void Deconstruct(out XsrUiThemeMode themeMode, out bool reducedMotion, out string productVersion)
    { themeMode = ThemeMode; reducedMotion = ReducedMotion; productVersion = ProductVersion; }

    internal bool SuppressMotion => ReducedMotion;
    internal bool SuppressSplashMotion => ReducedMotion || Theme?.Motion == AvaloniaUiThemeSplashMotion.None;

    internal AvaloniaUiStartupAppearance Snapshot() { Validate(); return this with { Theme = Theme?.Snapshot() }; }

    internal void Validate()
    {
        Theme?.Snapshot();
        if (ThemeMode is not (XsrUiThemeMode.System or XsrUiThemeMode.Light or XsrUiThemeMode.Dark))
            throw new ArgumentOutOfRangeException(nameof(ThemeMode));
        ArgumentException.ThrowIfNullOrWhiteSpace(ProductVersion);
        if (ProductVersion.Length > 80 || ProductVersion.Any(char.IsControl))
            throw new ArgumentException("The startup product version must be a bounded display value.", nameof(ProductVersion));
    }
}
