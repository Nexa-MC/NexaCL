using Avalonia;
using Avalonia.Threading;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiSceneSurface
{
    private AvaloniaUiThemeResizePolicy _themeResizePolicy;
    private XsrUiSize? _themeResizeViewport;
    private XsrUiSize _lastPresentedViewport;
    private DispatcherTimer? _themeResizeDelay;
    internal AvaloniaUiThemeResizePolicy PresentedResizePolicy => _themeResizePolicy;
    internal XsrUiSize PresentedViewport => _lastPresentedViewport;

    private void ApplyThemeResizePolicy(AvaloniaUiThemeResizePolicy policy)
    {
        if (_themeResizePolicy == policy) return;
        _themeResizePolicy = policy; RetireThemeResize(); RequestCommit();
    }

    private void RetireThemeResize()
    {
        _themeResizeDelay?.Stop(); _themeResizeViewport = null;
        AvaloniaUiMotion.Cancel(this, "theme-resize-width"); AvaloniaUiMotion.Cancel(this, "theme-resize-height");
    }

    private void OnNativeViewportChanged()
    {
        if (_disposed) return;
        if (!_rasterPresentationEnabled || _shell.Renderer.EffectiveReducedMotion || _scene is null
            || _themeResizePolicy is AvaloniaUiThemeResizePolicy.Auto or AvaloniaUiThemeResizePolicy.Immediate or AvaloniaUiThemeResizePolicy.Instant)
        { RetireThemeResize(); RequestCommit(); return; }
        if (_themeResizePolicy == AvaloniaUiThemeResizePolicy.Deferred)
        {
            _themeResizeViewport ??= _lastPresentedViewport;
            _themeResizeDelay ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Render,
                (_, _) => { _themeResizeDelay!.Stop(); _themeResizeViewport = null; RequestCommit(); });
            _themeResizeDelay.Stop(); _themeResizeDelay.Start(); return;
        }
        _themeResizeViewport ??= _lastPresentedViewport;
        XsrUiSize target = new(Bounds.Width, Bounds.Height);
        bool widthSettled = false, heightSettled = false;
        void Complete(bool width)
        {
            if (width) widthSettled = true; else heightSettled = true;
            if (widthSettled && heightSettled) { _themeResizeViewport = null; RequestCommit(); }
        }
        AvaloniaUiMotion.Animate(this, "theme-resize-width", () => _themeResizeViewport?.Width ?? Bounds.Width,
            value => { _themeResizeViewport = new(value, _themeResizeViewport?.Height ?? Bounds.Height); RequestCommit(); },
            target.Width, 180, completed: () => Complete(true), reducedMotion: () => _shell.Renderer.EffectiveReducedMotion);
        AvaloniaUiMotion.Animate(this, "theme-resize-height", () => _themeResizeViewport?.Height ?? Bounds.Height,
            value => { _themeResizeViewport = new(_themeResizeViewport?.Width ?? Bounds.Width, value); RequestCommit(); },
            target.Height, 180, completed: () => Complete(false), reducedMotion: () => _shell.Renderer.EffectiveReducedMotion);
    }

    private XsrUiSize ThemeRenderViewport()
    {
        if (_shell.Renderer.EffectiveReducedMotion) RetireThemeResize();
        _lastPresentedViewport = _themeResizeViewport ?? new XsrUiSize(Bounds.Width, Bounds.Height);
        return _lastPresentedViewport;
    }
}
