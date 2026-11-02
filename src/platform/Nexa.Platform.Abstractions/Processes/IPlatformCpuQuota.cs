using System.Diagnostics;
namespace Nexa.Platform;

public sealed record PlatformProcessQuota(int CpuPercent, long MemoryLimitBytes = 0,
    long ReadBytesPerSecond = 0, long WriteBytesPerSecond = 0, string? IoPath = null);

public interface IPlatformCpuQuota
{
    ValueTask<PlatformProcessControlResult> ApplyAsync(Process process, int percent, CancellationToken cancellationToken = default);
    ValueTask<PlatformProcessControlResult> ApplyAsync(Process process, PlatformProcessQuota quota, CancellationToken cancellationToken = default)
        => quota.MemoryLimitBytes == 0 && quota.ReadBytesPerSecond == 0 && quota.WriteBytesPerSecond == 0
            ? ApplyAsync(process, quota.CpuPercent, cancellationToken)
            : ValueTask.FromResult(new PlatformProcessControlResult(false, "platform_unsupported", "此适配器不支持内存或磁盘配额。"));
}
