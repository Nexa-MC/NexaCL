using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    private AvaloniaUiPreparedThemeAppearance? _themeAppearance;
    private long _themeAppearanceGeneration = -1;
    public AvaloniaUiThemeMaterialPresentation? ThemeMaterialPresentation => _owner is AvaloniaUiShellWindow window
        ? new(window.PresentedThemeMaterial, window.ActualTransparencyLevel.ToString(),
            window.PresentedThemeMaterial switch
            {
                AvaloniaUiThemeWindowMaterial.Acrylic => window.ActualTransparencyLevel != WindowTransparencyLevel.AcrylicBlur,
                AvaloniaUiThemeWindowMaterial.Mica => window.ActualTransparencyLevel != WindowTransparencyLevel.Mica,
                _ => false,
            }) : null;
    public IXsrUiTextLayoutProvider? PreparedTextLayoutProvider => _themeAppearance?.TextLayout;
    public long ThemeAppearanceGeneration => Interlocked.Read(ref _themeAppearanceGeneration);

    public Task<AvaloniaUiPreparedThemeAppearance> PrepareThemeAppearanceAsync(AvaloniaUiThemeAppearance appearance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        AvaloniaUiThemeAppearance snapshot = appearance.Snapshot();
        return Application.Current is null || Dispatcher.UIThread.CheckAccess() ? Task.FromResult(Prepare()) : Dispatcher.UIThread.InvokeAsync(Prepare,
            DispatcherPriority.Normal, cancellationToken).GetTask();
        AvaloniaUiPreparedThemeAppearance Prepare()
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateThemeMaterial(snapshot.WindowMaterial);
            return new(snapshot, deferNative: Application.Current is null);
        }
    }

    /// <summary>Commits prepared native facts synchronously. The caller retires the returned previous lease after its style transaction.</summary>
    public AvaloniaUiPreparedThemeAppearance? CommitThemeAppearance(AvaloniaUiPreparedThemeAppearance prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (Application.Current is not null) Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(prepared.IsDisposed, prepared);
        if (prepared.IsTransferred) throw new InvalidOperationException("Theme candidate ownership has already been transferred.");
        if (prepared.Appearance.Generation <= ThemeAppearanceGeneration) throw new InvalidOperationException("Stale native theme generation.");
        ValidateThemeMaterial(prepared.Appearance.WindowMaterial);
        if (Application.Current is not null && !prepared.IsNativePrepared)
            throw new InvalidOperationException("Native theme resources must finish preparation before the frame commit.");
        AvaloniaUiPreparedThemeAppearance? previous = _themeAppearance;
        if (_owner is AvaloniaUiShellWindow window) window.ApplyPreparedThemeAppearance(prepared, previous);
        _themeAppearance = prepared; prepared.IsTransferred = true;
        Interlocked.Exchange(ref _themeAppearanceGeneration, prepared.Appearance.Generation);
        return previous;
    }

    /// <summary>
    /// Clears native theme facts at a frame boundary without loading resources. The caller
    /// retires the returned owner lease after clearing renderer styles and publishing defaults.
    /// Product artwork is restored from the window's prepared cache; a window without product
    /// artwork restores a null icon. Recovery also releases every retained outgoing native borrow.
    /// </summary>
    public AvaloniaUiPreparedThemeAppearance? ClearThemeAppearance(long generation)
    {
        if (Application.Current is not null) Dispatcher.UIThread.VerifyAccess();
        if (generation <= ThemeAppearanceGeneration) throw new InvalidOperationException("Stale native theme generation.");
        AvaloniaUiPreparedThemeAppearance? previous = _themeAppearance;
        if (_owner is AvaloniaUiShellWindow window) window.ClearThemeAppearance(previous);
        _themeAppearance = null;
        Interlocked.Exchange(ref _themeAppearanceGeneration, generation);
        return previous;
    }

    public async Task ApplyThemeAppearanceAsync(AvaloniaUiPreparedThemeAppearance prepared, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        void Apply() { cancellationToken.ThrowIfCancellationRequested(); CommitThemeAppearance(prepared)?.Dispose(); }
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply, DispatcherPriority.Normal, cancellationToken);
    }

    public async Task ApplyThemeAppearanceAsync(AvaloniaUiThemeAppearance appearance, CancellationToken cancellationToken = default)
    {
        AvaloniaUiPreparedThemeAppearance prepared = await PrepareThemeAppearanceAsync(appearance, cancellationToken).ConfigureAwait(false);
        try { await ApplyThemeAppearanceAsync(prepared, cancellationToken).ConfigureAwait(false); }
        catch { prepared.Dispose(); throw; }
    }

    /// <summary>Revokes all remaining native borrows after a replacement generation has committed.</summary>
    public void FlushRetiredThemeAppearanceResources(long generation)
    {
        if (Application.Current is not null) Dispatcher.UIThread.VerifyAccess();
        if (generation >= ThemeAppearanceGeneration) throw new InvalidOperationException("Commit a replacement before revoking current theme resources.");
        if (_owner is AvaloniaUiShellWindow window) window.Surface.FlushRetiredThemeResources(generation);
    }

    private void AttachThemeAppearance(TopLevel owner)
    {
        if (owner is not AvaloniaUiShellWindow window) return;
        if (_themeAppearance is { } prepared)
        {
            prepared.PrepareNative();
            window.ApplyPreparedThemeAppearance(prepared);
        }
        window.Closed += (_, _) => { _themeAppearance?.Dispose(); _themeAppearance = null; };
    }

    private static void ValidateThemeMaterial(AvaloniaUiThemeWindowMaterial material)
    {
        string? required = material switch
        {
            AvaloniaUiThemeWindowMaterial.Acrylic => "window-acrylic",
            AvaloniaUiThemeWindowMaterial.Mica => "window-mica",
            _ => null,
        };
        if (required is not null && !AvaloniaUiThemeCapabilities.Supported.Contains(required))
            throw new PlatformNotSupportedException("Unsupported native theme material: " + material);
    }
}
