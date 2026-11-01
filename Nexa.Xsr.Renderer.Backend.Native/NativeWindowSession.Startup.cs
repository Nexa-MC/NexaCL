using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed record NativeWindowStartupSnapshot(AvaloniaUiStartupAppearance Appearance,
    bool HardwareAccelerationDisabled, string Stage, TimeSpan? FirstRenderElapsed, TimeSpan? ShellReadyElapsed);

public sealed record NativeWindowPlatformSnapshot(bool IsWindowActive, bool? IsSystemDark,
    TimeSpan DoubleClickInterval, long ThemeAppearanceGeneration, AvaloniaUiThemeMaterialPresentation? Material);

public sealed partial class NativeWindowSession
{
    public async Task<NativeWindowStartupSnapshot> ReadStartupAsync(CancellationToken cancellationToken = default)
    {
        var facts = WindowStartupFacts.Decode(await InvokeOperation(WindowCommand.StartupFacts, cancellationToken).ConfigureAwait(false));
        return new(facts.Appearance, facts.HardwareAccelerationDisabled, facts.Stage,
            facts.FirstRenderTicks is { } first ? TimeSpan.FromTicks(first) : null,
            facts.ShellReadyTicks is { } ready ? TimeSpan.FromTicks(ready) : null);
    }

    public async Task SetAppearanceAsync(AvaloniaUiStartupAppearance appearance, CancellationToken cancellationToken = default)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Appearance);
        writer.Buffer(WindowValueChannel.EncodeStartup(appearance));
        await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
    }

    public async Task RelocalizeAsync(Func<string, string> localize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localize); Localize = localize;
        await InvokeOperation(WindowCommand.Relocalize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> WaitForRetryAsync(string message, CancellationToken cancellationToken = default)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.WaitForRetry); writer.String(message);
        byte[] result = await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
        if (result.Length != 1 || result[0] > 1) throw new InvalidDataException("Invalid startup retry response.");
        return result[0] != 0;
    }

    public Task ShutdownAsync() => InvokeOperation(WindowCommand.QuiesceUi);

    public async Task<NativeWindowPlatformSnapshot> ReadPlatformAsync(CancellationToken cancellationToken = default)
    {
        using var result = PlatformResult(await PlatformAsync(WindowPlatformCommand.Facts, null, cancellationToken).ConfigureAwait(false), WindowPlatformCommand.Facts);
        bool active = result.Boolean();
        bool? dark = result.Byte() switch { 0 => false, 1 => true, 2 => null, _ => throw new InvalidDataException("Invalid system appearance.") };
        long doubleClickTicks = result.Int64(), generation = result.Int64();
        var material = result.Boolean() ? WindowValueChannel.DecodeMaterial(result.Buffer()) : null;
        result.End();
        if (doubleClickTicks <= 0 || generation < -1) throw new InvalidDataException("Invalid native platform facts.");
        return new(active, dark, TimeSpan.FromTicks(doubleClickTicks), generation, material);
    }
}
