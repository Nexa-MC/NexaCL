using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Applies only committed preferences at the frame boundary, including while Settings is closed.</summary>
internal sealed class DesktopAppearanceSession : IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly XsrStateStore _state;
    private readonly XsrStateId _mode, _accent;
    private readonly IXsrUiSystemAppearance? _system;
    private readonly DesktopEffectiveAppearanceSource? _effectiveAppearance;
    private readonly XsrUiThemeMode _fallbackMode;
    private readonly XsrUiColorScheme _previousScheme;
    private XsrUiThemeMode? _previousMode;
    private int _pending = 1;
    private bool _disposed;

    internal DesktopAppearanceSession(XsrUiShell shell, XsrStateStore state, IXsrUiSystemAppearance? system = null,
        XsrUiThemeMode fallbackMode = XsrUiThemeMode.System, DesktopEffectiveAppearanceSource? effectiveAppearance = null)
    {
        _shell = shell;
        _state = state;
        _system = system;
        _effectiveAppearance = effectiveAppearance;
        _fallbackMode = fallbackMode;
        _previousScheme = shell.Renderer.ColorScheme;
        state.TryResolve(XsrSemanticId.Parse("UiDarkMode"), out _mode);
        state.TryResolve(XsrSemanticId.Parse("UiAccentColor"), out _accent);
        state.Changed += OnChanged;
        if (_system is not null) _system.SystemAppearanceChanged += OnSystemChanged;
        if (_effectiveAppearance is not null) _effectiveAppearance.Changed += OnEffectiveAppearance;
        shell.Renderer.FramePreparing += OnFrame;
        OnFrame(this, EventArgs.Empty);
    }

    private void OnChanged(XsrStateChange change)
    {
        if (change.Id == _mode || change.Id == _accent) Interlocked.Exchange(ref _pending, 1);
    }
    private void OnSystemChanged() => Interlocked.Exchange(ref _pending, 1);
    private void OnEffectiveAppearance()
    {
        Interlocked.Exchange(ref _pending, 1);
        // Effective appearance is published by the composition layer on the render thread.
        // Apply these scalar facts in the same transaction as its prepared images/styles.
        OnFrame(this, EventArgs.Empty);
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed || Interlocked.Exchange(ref _pending, 0) == 0) return;
        XsrUiThemeMode mode = _mode.IsAssigned ? _state.Read<int>(_mode).Value switch
        { 0 => XsrUiThemeMode.Light, 1 => XsrUiThemeMode.Dark, _ => XsrUiThemeMode.System } : _fallbackMode;
        string? effectiveAccent = _effectiveAppearance?.Current?.Settings.Values
            .FirstOrDefault(item => item.Key == "appearance.accent")?.Value.Value;
        XsrUiAccent accent = (effectiveAccent ?? (_accent.IsAssigned ? _state.Read<string>(_accent).Value : null)) switch
        { "purple" => XsrUiAccent.Purple, "green" => XsrUiAccent.Green, "orange" => XsrUiAccent.Orange, _ => XsrUiAccent.Blue };
        bool dark = mode == XsrUiThemeMode.Dark || mode == XsrUiThemeMode.System && _system?.IsSystemDark == true;
        _shell.Renderer.ColorScheme = _shell.Renderer.ColorScheme with { IsDark = dark, Accent = accent };
        if (_previousMode == mode) return;
        _previousMode = mode;
        _system?.SetThemeMode(mode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Changed -= OnChanged;
        if (_system is not null) _system.SystemAppearanceChanged -= OnSystemChanged;
        if (_effectiveAppearance is not null) _effectiveAppearance.Changed -= OnEffectiveAppearance;
        _shell.Renderer.FramePreparing -= OnFrame;
        _shell.Renderer.ColorScheme = _previousScheme;
    }
}
