using System.Diagnostics;
namespace Nexa.Platform;

public interface IPlatformCpuQuota
{
    ValueTask<PlatformProcessControlResult> ApplyAsync(Process process, int percent, CancellationToken cancellationToken = default);
}
