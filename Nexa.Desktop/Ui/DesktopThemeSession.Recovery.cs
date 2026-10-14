using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class DesktopThemeSession
{
    private static readonly HashSet<string> HostProtectedAppearancePolicies = new(StringComparer.Ordinal)
    {
        "appearance.theme-mode", "appearance.theme-pack-id", "appearance.launcher-icon-mode", "appearance.launcher-icon-path",
        "appearance.reduced-motion", "appearance.animations-disabled", "appearance.hardware-acceleration-disabled",
    };

    private void OnThemeStyleFailed(object? sender, XsrUiThemeStyleFailureEventArgs failure)
    {
        if (_disposed || failure.ActivationGeneration != _shell.Renderer.ThemeActivationGeneration) return;
        CancellationTokenSource previousRequest;
        lock (_requestGate)
        {
            if (_disposed) return;
            previousRequest = _requestStop; _requestStop = new();
            Interlocked.Increment(ref _request); _themeRecoveryPending = true;
        }
        previousRequest.Cancel(); previousRequest.Dispose();
        long generation = NextGeneration();
        var retired = _platform.ClearThemeAppearance(generation);
        try
        {
            _shell.Renderer.ClearTheme(generation); _shell.Renderer.TextLayoutProvider = null;
            StartupSnapshot = null; _appliedDark = null;
            _appearance.Publish(ProductAppearance(generation));
            _log("Theme style failed; product appearance restored: " + failure.Exception.Message);
            SetSnapshot(error: failure.Exception.Message);
        }
        finally
        {
            retired?.Dispose();
            // The render retry uses only the safe presentation; resource preparation stays on the actor.
            _updates.Writer.TryWrite(true);
        }
    }

    private DesktopEffectiveAppearanceSnapshot ProductAppearance(long generation)
    {
        var basis = _baseSettings ?? _appearance.Current?.Settings
            ?? throw new InvalidOperationException("Theme recovery requires a committed settings snapshot.");
        var values = basis.Values.ToDictionary(value => value.Key, StringComparer.Ordinal);
        foreach (var definition in SettingsPolicySchema.Definitions)
            if ((definition.Key.StartsWith("appearance.", StringComparison.Ordinal) || definition.Key.StartsWith("music.", StringComparison.Ordinal))
                && !HostProtectedAppearancePolicies.Contains(definition.Key))
                values[definition.Key] = new(definition.Key, new(SettingsOverrideMode.Custom, definition.DefaultValue), SettingsLayer.Temporary, definition.Timing, null);
        foreach (string key in new[] { "appearance.logo-path", "appearance.background-path", "appearance.video-path", "music.path", "appearance.custom-theme" })
            if (values.TryGetValue(key, out var value)) values[key] = value with { Value = new(SettingsOverrideMode.Custom, "") };
        return new(generation, basis with { Values = Array.AsReadOnly(values.Values.ToArray()) }, [], ThemeResources: true, HasPreparedImages: true);
    }
}
