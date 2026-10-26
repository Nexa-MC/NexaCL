using System.Diagnostics;

namespace Nexa.Platform;

public interface IPlatformGameMode
{
    ValueTask<IAsyncDisposable> StartAsync(Process process, CancellationToken cancellationToken = default);
}
