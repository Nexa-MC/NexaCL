using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>
/// The synchronous host's compact, passive startup card. Product startup uses the interactive
/// readiness window; both paths share the same theme-aware artwork and quiet presentation.
/// </summary>
public sealed class AvaloniaSplashWindow : Window
{
    private readonly AvaloniaUiStartupCard _card;
    private AvaloniaUiStartupAppearance _appearance = new();

    public AvaloniaSplashWindow(Stream iconStream)
    {
        ArgumentNullException.ThrowIfNull(iconStream);
        Width = AvaloniaUiStartupCard.WindowWidth;
        Height = AvaloniaUiStartupCard.PassiveHeight;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
        _card = new AvaloniaUiStartupCard(static text => text, fallback: iconStream);
        _card.Header.PointerPressed += OnHeaderPointerPressed;
        Content = _card;
        PropertyChanged += OnThemeChanged;
        UpdateBrand();
    }

    internal void SetAppearance(AvaloniaUiStartupAppearance appearance)
    {
        _appearance = appearance;
        RequestedThemeVariant = appearance.ThemeMode switch
        { XsrUiThemeMode.Light => ThemeVariant.Light, XsrUiThemeMode.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
        UpdateBrand();
    }

    private void OnThemeChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == ActualThemeVariantProperty) UpdateBrand(); }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!AvaloniaUiPointerPolicy.IsPrimaryPress(args, this)) return;
        BeginMoveDrag(args);
        args.Handled = true;
    }

    private void UpdateBrand()
    {
        bool dark = AvaloniaUiBrandImages.ResolveDark(_appearance.ThemeMode, ActualThemeVariant);
        _card.ApplyAppearance(dark, _appearance.ProductVersion);
        _card.ApplySplashMotion(AvaloniaUiThemeSplashMotion.Default, _appearance.ReducedMotion);
        Icon = _card.ProductIcon;
        TransparencyBackgroundFallback = new SolidColorBrush(dark ? Color.FromRgb(13, 26, 36) : Color.FromRgb(248, 252, 255));
    }

    protected override void OnClosed(EventArgs e)
    {
        PropertyChanged -= OnThemeChanged;
        _card.Header.PointerPressed -= OnHeaderPointerPressed;
        _card.Dispose();
        base.OnClosed(e);
    }
}
