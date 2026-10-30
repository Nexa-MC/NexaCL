using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiShellWindow
{
    private AvaloniaUiBrandImages? _brandImages;
    // Retained independently of theme leases so recovery never decodes or opens artwork.
    private AvaloniaUiBrandImages? _builtinBrandImages;
    private AvaloniaUiBrandImages? _nativeWindowIcons, _nativeTrayIcons;
    internal WindowIcon? ThemeTrayIcon => _nativeTrayIcons?.ResolveIcon(_presentedBrandDark ?? _committedBrandDark ?? false) ?? Icon;
    internal event EventHandler? ThemeTrayIconChanged;
    private Bitmap? _fallbackBrandImage;
    private WindowIcon? _fallbackBrandIcon;
    private bool? _committedBrandDark;
    private bool? _presentedBrandDark;

    private void InitializeBranding(Stream? fallback)
    {
        PropertyChanged += OnBrandThemeChanged;
        if (AvaloniaUiBrandImages.TryLoadProduct() is { } images)
            SetOwnedBuiltinBrandImages(images);
        else if (fallback is not null)
        {
            _fallbackBrandImage = new Bitmap(fallback);
            _closeIcon = _fallbackBrandImage;
            Icon = _fallbackBrandIcon = new WindowIcon(_fallbackBrandImage);
        }
    }

    /// <summary>Replaces the owned built-in artwork; third-party themes use separate prepared leases.</summary>
    internal void SetOwnedBuiltinBrandImages(AvaloniaUiBrandImages images)
    {
        ArgumentNullException.ThrowIfNull(images);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_brandImages, images)) return;
        // Validate both variants before replacing the retained fallback.
        _ = images.ResolveIcon(false); _ = images.ResolveIcon(true);
        AvaloniaUiBrandImages? previousBuiltin = _builtinBrandImages;
        AvaloniaUiBrandImages? previousBrand = _brandImages;
        _builtinBrandImages = images;
        SwapOwnedBrandImages(images)?.Dispose();
        if (!ReferenceEquals(previousBuiltin, previousBrand)) previousBuiltin?.Dispose();
    }

    private AvaloniaUiBrandImages? SwapOwnedBrandImages(AvaloniaUiBrandImages images)
    {
        AvaloniaUiBrandImages? previous = _brandImages;
        _brandImages = images;
        _presentedBrandDark = null;
        ApplyBrandTheme(_committedBrandDark ?? ActualThemeVariant == ThemeVariant.Dark);
        return ReferenceEquals(previous, _builtinBrandImages) ? null : previous;
    }

    private void OnBrandThemeChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        // Before the first immutable scene, use the actual native preference. Once the
        // scene exists it is authoritative, including explicit Light on a dark OS.
        if (!_disposed && args.Property == ActualThemeVariantProperty && _committedBrandDark is null)
            ApplyBrandTheme(ActualThemeVariant == ThemeVariant.Dark);
    }

    private void ApplyCommittedBranding(XsrUiScene scene)
    {
        if (_disposed) return;
        _committedBrandDark = scene.Nodes.FirstOrDefault(node => node.Entity == _shell.Root).ColorScheme.IsDark;
        ApplyBrandTheme(_committedBrandDark.Value);
    }

    private void ApplyBrandTheme(bool isDark)
    {
        if (_presentedBrandDark == isDark) return;
        Bitmap? previous = _closeIcon;
        _closeIcon = _brandImages?.Resolve(isDark) ?? _fallbackBrandImage;
        _presentedBrandDark = isDark;
        Icon = _nativeWindowIcons?.ResolveIcon(isDark) ?? _brandImages?.ResolveIcon(isDark) ?? _fallbackBrandIcon;
        ThemeTrayIconChanged?.Invoke(this, EventArgs.Empty);
        // An already running close decoration borrows the current image too. Scene raster
        // controls and user-provided logos are separate and are never touched here.
        if (_root is not null && previous is not null)
            foreach (Image image in _root.Children.OfType<Image>())
                if (ReferenceEquals(image.Source, previous)) image.Source = _closeIcon;
    }

    private void DisposeBranding()
    {
        PropertyChanged -= OnBrandThemeChanged;
        if (_root is not null)
            foreach (Image image in _root.Children.OfType<Image>())
                if (ReferenceEquals(image.Source, _closeIcon)) image.Source = null;
        _closeIcon = null;
        if (!ReferenceEquals(_brandImages, _builtinBrandImages)) _brandImages?.Dispose();
        _brandImages = null;
        _builtinBrandImages?.Dispose(); _builtinBrandImages = null;
        _nativeWindowIcons?.Dispose(); _nativeWindowIcons = null;
        _nativeTrayIcons?.Dispose(); _nativeTrayIcons = null;
        _fallbackBrandImage?.Dispose(); _fallbackBrandImage = null;
        _fallbackBrandIcon = null;
    }
}
