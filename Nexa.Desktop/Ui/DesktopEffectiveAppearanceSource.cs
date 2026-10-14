using Nexa.Core.Media;
using Nexa.Services.Settings;

namespace Nexa.Desktop.Ui;

/// <summary>One committed generation shared by appearance consumers; no consumer performs theme IO.</summary>
internal sealed record DesktopEffectiveAppearanceSnapshot(long Generation, SettingsEffectiveSnapshot Settings,
    IReadOnlyList<string> AudioPlaylist, bool ThemeResources = false,
    PngImage? Logo = null, PngImage? Background = null, bool HasPreparedImages = false,
    string? LogoResourceId = null, string? BackgroundResourceId = null);

internal sealed class DesktopEffectiveAppearanceSource
{
    private DesktopEffectiveAppearanceSnapshot? _current;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal event Action? Changed;
    internal DesktopEffectiveAppearanceSnapshot? Current => Volatile.Read(ref _current);
    internal bool IsCurrent(long generation) => Current?.Generation == generation;

    internal void Publish(DesktopEffectiveAppearanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Current is { } previous && snapshot.Generation < previous.Generation)
            throw new InvalidOperationException("An effective appearance generation cannot move backwards.");
        Volatile.Write(ref _current, snapshot);
        _ready.TrySetResult();
        Changed?.Invoke();
    }

    internal async ValueTask<SettingsEffectiveSnapshot?> ReadAsync(CancellationToken token)
    {
        await _ready.Task.WaitAsync(token).ConfigureAwait(false);
        return Current?.Settings;
    }

    internal void FailInitial(Exception error) => _ready.TrySetException(error);
}
