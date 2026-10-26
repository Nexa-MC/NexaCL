namespace Nexa.Platform;

public enum PlatformPowerPressure { Unknown, Normal, Constrained }
public interface IPlatformPowerPressure
{
    ValueTask<PlatformPowerPressure> ReadAsync(CancellationToken cancellationToken = default);
}
