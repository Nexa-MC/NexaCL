using Avalonia.Threading;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiSceneSurface
{
    private AvaloniaUiPreparedThemeAppearance? _themeResources;
    private bool _themeResourceCommitPending;
    private readonly Dictionary<long, AvaloniaUiPreparedThemeAppearance> _themeResourceHistory = [];
    private readonly HashSet<long> _revokedThemeGenerations = [];
    private DispatcherTimer? _themeAnimationTimer;
    internal bool ThemeAnimationClockActive => _themeAnimationTimer?.IsEnabled == true;
    private void DriveThemeAnimationClock()
    {
        if (!_shell.Renderer.HasThemeAnimations || !_rasterPresentationEnabled || _disposed)
        { _themeAnimationTimer?.Stop(); return; }
        _themeAnimationTimer ??= new DispatcherTimer(AvaloniaUiMotion.FrameInterval, DispatcherPriority.Render, (_, _) =>
        {
            if (_disposed || !_rasterPresentationEnabled) { _themeAnimationTimer?.Stop(); return; }
            _shell.Renderer.AdvanceThemeAnimations();
            CommitScene();
            if (!_shell.Renderer.HasThemeAnimations) _themeAnimationTimer?.Stop();
        });
        _themeAnimationTimer.Interval = AvaloniaUiMotion.FrameInterval;
        _themeAnimationTimer.Start();
    }
    internal void FlushRetiredThemeResources(long generation)
    {
        _revokedThemeGenerations.Add(generation);
        foreach (AvaloniaUiSceneNodeControl control in _controls.Values.Concat(_outgoingControls))
            if (control.PresentedThemeGeneration == generation) control.SetThemeResources(null);
        if (_themeResourceHistory.Remove(generation, out AvaloniaUiPreparedThemeAppearance? resources)) resources.ReleaseBorrow();
        RequestCommit();
    }

    private AvaloniaUiPreparedThemeAppearance? ResourcesFor(XsrUiSceneNode node) => _revokedThemeGenerations.Contains(node.ThemeActivationGeneration) ? null
        : node.ThemeActivationGeneration == 0 ? _themeResources : _themeResourceHistory.GetValueOrDefault(node.ThemeActivationGeneration);

    private void RetireUnusedThemeResources(XsrUiScene scene)
    {
        HashSet<long> used = [_themeResources?.Appearance.Generation ?? 0];
        foreach (XsrUiSceneNode node in scene.Nodes.Concat(scene.Outgoing.SelectMany(layer => layer.Nodes))) used.Add(node.ThemeActivationGeneration);
        _revokedThemeGenerations.RemoveWhere(generation => !used.Contains(generation));
        foreach (long generation in _themeResourceHistory.Keys.Where(key => !used.Contains(key)).ToArray())
        {
            AvaloniaUiPreparedThemeAppearance resources = _themeResourceHistory[generation];
            _themeResourceHistory.Remove(generation); resources.ReleaseBorrow();
        }
    }

    internal void ReleaseThemeResourceHistory()
    {
        foreach (AvaloniaUiPreparedThemeAppearance resources in _themeResourceHistory.Values) resources.ReleaseBorrow();
        _themeResourceHistory.Clear(); _revokedThemeGenerations.Clear(); _themeResources = null;
    }

    internal void ClearThemeResources()
    {
        _themeAnimationTimer?.Stop();
        _themeResizePolicy = AvaloniaUiThemeResizePolicy.Auto; RetireThemeResize();
        foreach (long generation in _themeResourceHistory.Keys)
            if (generation > 0) _revokedThemeGenerations.Add(generation);
        foreach (AvaloniaUiSceneNodeControl control in _controls.Values) control.SetThemeResources(null);
        foreach (AvaloniaUiSceneNodeControl control in _outgoingControls)
        {
            if (control.Node.ThemeActivationGeneration > 0) _revokedThemeGenerations.Add(control.Node.ThemeActivationGeneration);
            Children.Remove(control); control.ReleasePresentation();
        }
        _outgoingControls.Clear();
        foreach (AvaloniaUiPreparedThemeAppearance resources in _themeResourceHistory.Values) resources.ReleaseBorrow();
        _themeResourceHistory.Clear(); _themeResources = null; _themeResourceCommitPending = true;
        _shell.Renderer.TextLayoutProvider = null;
        RequestCommit();
    }

    internal void ApplyThemeResources(AvaloniaUiPreparedThemeAppearance resources)
    {
        if (_themeResourceHistory.TryAdd(resources.Appearance.Generation, resources)) resources.Borrow();
        _themeResources = resources; _themeResourceCommitPending = true;
        ApplyThemeResizePolicy(resources.Appearance.ResizePolicy);
        _shell.Renderer.TextLayoutProvider = resources.Appearance.UseNativeTextLayout ? resources.TextLayout : null;

        RequestCommit();
    }
}
