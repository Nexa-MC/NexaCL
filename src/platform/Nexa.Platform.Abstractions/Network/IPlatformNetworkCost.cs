namespace Nexa.Platform;

public enum PlatformNetworkCost { Unknown, Unmetered, Metered, Roaming }
public interface IPlatformNetworkCost
{
    ValueTask<PlatformNetworkCost> ReadAsync(CancellationToken cancellationToken = default);
}
