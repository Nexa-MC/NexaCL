using Avalonia.Controls;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiShellWindow
{
    internal AvaloniaUiThemeWindowMaterial PresentedThemeMaterial { get; private set; }
    internal void ClearThemeAppearance(AvaloniaUiPreparedThemeAppearance? previous)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AvaloniaUiBrandImages? retired = ReferenceEquals(_brandImages, _builtinBrandImages) ? null : _brandImages;
        AvaloniaUiBrandImages? previousWindowIcons = _nativeWindowIcons, previousTrayIcons = _nativeTrayIcons;
        _brandImages = _builtinBrandImages;
        _nativeWindowIcons = null; _nativeTrayIcons = null;
        _presentedBrandDark = null;
        ApplyBrandTheme(_committedBrandDark ?? false);
        if (previous is not null)
        {
            previous.ReturnBrand(retired);
            previous.ReturnNativeIcons(previousWindowIcons, previousTrayIcons);
        }
        else { retired?.Dispose(); previousWindowIcons?.Dispose(); previousTrayIcons?.Dispose(); }
        _surface.ClearThemeResources();
        PresentedThemeMaterial = AvaloniaUiThemeWindowMaterial.Default;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
        UpdateChromeForState(WindowState is WindowState.Maximized or WindowState.FullScreen);
        _ = AvaloniaWindowsFrame.SuppressBorder(this);
    }

    internal void ApplyPreparedThemeAppearance(AvaloniaUiPreparedThemeAppearance prepared, AvaloniaUiPreparedThemeAppearance? previous = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!prepared.IsNativePrepared) throw new InvalidOperationException("Theme native resources are not prepared.");
        // All fallible resource creation precedes the native presentation commit.
        var nativeIcons = prepared.TakeNativeIcons();
        AvaloniaUiBrandImages? previousWindowIcons = _nativeWindowIcons, previousTrayIcons = _nativeTrayIcons;
        _nativeWindowIcons = nativeIcons.Window; _nativeTrayIcons = nativeIcons.Tray;
        if (previous is not null) previous.ReturnNativeIcons(previousWindowIcons, previousTrayIcons);
        else { previousWindowIcons?.Dispose(); previousTrayIcons?.Dispose(); }
        AvaloniaUiBrandImages? brand = prepared.TakeBrand();
        if (brand is not null)
        {
            AvaloniaUiBrandImages? retired = SwapOwnedBrandImages(brand);
            if (previous is not null) previous.ReturnBrand(retired);
            else retired?.Dispose();
        }
        _presentedBrandDark = null;
        ApplyBrandTheme(_committedBrandDark ?? false);
        _surface.ApplyThemeResources(prepared);
        PresentedThemeMaterial = prepared.Appearance.WindowMaterial;
        // Window decoration/layer ownership remains unchanged. Never recreate the window to change material.
        TransparencyLevelHint = PresentedThemeMaterial switch
        {
            AvaloniaUiThemeWindowMaterial.Acrylic => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None],
            AvaloniaUiThemeWindowMaterial.Mica => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None],
            _ => [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None],
        };
        UpdateChromeForState(WindowState is WindowState.Maximized or WindowState.FullScreen);
        _ = AvaloniaWindowsFrame.SuppressBorder(this);
    }
}
