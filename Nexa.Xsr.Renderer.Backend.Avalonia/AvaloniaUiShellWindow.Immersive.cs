using Avalonia;
using Avalonia.Controls;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiShellWindow
{
    private (PixelPoint Position, Size Size, WindowState State, bool ControlsVisible)? _immersiveOrigin;
    private (PixelPoint Position, Size Size)? _normalPlacement;

    private void RememberNormalPlacement()
    {
        if (_immersiveOrigin is null && WindowState == WindowState.Normal && Bounds.Width > 0 && Bounds.Height > 0)
            _normalPlacement = (Position, Bounds.Size);
    }

    internal void SetClientScrimOpacity(double opacity)
    {
        _windowActions.Opacity = 1 - Math.Clamp(opacity, 0, 1);
        _windowActions.IsHitTestVisible = opacity <= .01;
        AvaloniaMacWindow.SetControlsScrimOpacity(this, Math.Clamp(opacity, 0, 1));
    }

    internal void SetImmersivePresentation(bool enabled)
    {
        if (_disposed) return;
        AvaloniaUiMotion.Cancel(this, "immersive-bounds");
        if (!enabled)
        {
            if (_immersiveOrigin is not { } original) return;
            _immersiveOrigin = null;
            _windowActions.IsVisible = original.ControlsVisible;
            if (original.State == WindowState.FullScreen) return;
            WindowState = WindowState.Normal;
            Position = original.Position; Width = original.Size.Width; Height = original.Size.Height;
            // Submit normal geometry before re-maximizing; deferred layout would otherwise
            // let the native window manager retain the animation's final fullscreen size.
            InvalidateMeasure(); UpdateLayout();
            WindowState = original.State;
            return;
        }
        if (_immersiveOrigin is not null) return;
        RememberNormalPlacement();
        var restore = WindowState != WindowState.Normal && _normalPlacement is { } normal ? normal : (Position, Bounds.Size);
        _immersiveOrigin = (restore.Position, restore.Size, WindowState, _windowActions.IsVisible);
        if (WindowState == WindowState.FullScreen) { _windowActions.IsVisible = false; return; }
        PixelRect target = Screens.ScreenFromWindow(this)?.Bounds ?? new(Position, PixelSize.FromSize(Bounds.Size, RenderScaling));
        double scaling = Screens.ScreenFromWindow(this)?.Scaling ?? RenderScaling;
        PixelPoint start = Position; Size size = Bounds.Size;
        WindowState = WindowState.Normal;
        Width = size.Width; Height = size.Height; Position = start;
        double progress = 0;
        AvaloniaUiMotion.Animate(this, "immersive-bounds", () => progress, value =>
        {
            progress = value;
            Width = size.Width + (target.Width / scaling - size.Width) * value;
            Height = size.Height + (target.Height / scaling - size.Height) * value;
            Position = new((int)Math.Round(start.X + (target.X - start.X) * value),
                (int)Math.Round(start.Y + (target.Y - start.Y) * value));
        }, 1, _shell.Renderer.EffectiveReducedMotion ? 0 : 600,
            completed: () => { if (_immersiveOrigin is not null && !_disposed) { WindowState = WindowState.FullScreen; _windowActions.IsVisible = false; } },
            reducedMotion: () => _shell.Renderer.EffectiveReducedMotion);
    }
}
