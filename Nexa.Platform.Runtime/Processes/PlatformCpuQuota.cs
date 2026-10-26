using System.Diagnostics;
using System.Globalization;

namespace Nexa.Platform;

/// <summary>Places a waiting JVM Host in an independently collected user scope before bootstrap.</summary>
public sealed class PlatformCpuQuota : IPlatformCpuQuota
{
    public ValueTask<PlatformProcessControlResult> ApplyAsync(Process process, int percent, CancellationToken cancellationToken = default)
        => ApplyAsync(process, new PlatformProcessQuota(percent), cancellationToken);

    public async ValueTask<PlatformProcessControlResult> ApplyAsync(Process process, PlatformProcessQuota quota, CancellationToken cancellationToken = default)
    {
        int percent = quota.CpuPercent;
        if (quota.MemoryLimitBytes < 0 || quota.ReadBytesPerSecond < 0 || quota.WriteBytesPerSecond < 0
            || ((quota.ReadBytesPerSecond > 0 || quota.WriteBytesPerSecond > 0) && (quota.IoPath is null || !Path.IsPathFullyQualified(quota.IoPath) || !Directory.Exists(quota.IoPath))))
            return new(false, "invalid_quota", "资源配额或工作目录无效。");
        if (percent is < 0 or > 102400) return new(false, "invalid_quota", "资源配额必须为 0 至 102400，100 表示一个 CPU。");
        if (percent == 0 && quota.MemoryLimitBytes == 0 && quota.ReadBytesPerSecond == 0 && quota.WriteBytesPerSecond == 0) return new(true, "ok", "");
        if (!OperatingSystem.IsLinux()) return new(false, "platform_unsupported", "资源配额需要 Linux systemd 用户会话与 cgroup v2。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        string unit = "nexacl-game-" + Guid.NewGuid().ToString("N") + ".scope";
        try
        {
            if (!File.Exists("/sys/fs/cgroup/cgroup.controllers"))
                return new(false, "dependency_missing", "资源配额需要 cgroup v2。");
            ProcessStartInfo info = new("/usr/bin/busctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in StartArguments(unit, process.Id, quota)) info.ArgumentList.Add(argument);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.HasExited) return new(false, "process_exited", "JVM Host 在应用 资源配额前已退出。");
            using var command = Process.Start(info) ?? throw new IOException("Unable to start busctl.");
            // Drain without retaining manager output, which can contain environment-specific data.
            Task output = command.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            Task error = command.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            try { await command.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            finally
            {
                if (!command.HasExited) command.Kill(entireProcessTree: true);
                await command.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            if (command.ExitCode != 0) return new(false, "quota_refused", "无法创建 资源配额：请检查 systemd 用户会话、资源控制器委派及权限。");
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (process.HasExited) return new(false, "process_exited", "JVM Host 在应用 资源配额前已退出。");
                string? group = File.ReadLines($"/proc/{process.Id}/cgroup").FirstOrDefault(line => line.StartsWith("0::", StringComparison.Ordinal))?[3..];
                if (group is not null && group.StartsWith('/') && !group.Split('/').Contains("..") && group.EndsWith("/" + unit, StringComparison.Ordinal))
                {
                    string directory = Path.Combine("/sys/fs/cgroup", group.TrimStart('/'));
                    if ((percent == 0 || MatchesQuota(File.ReadAllText(Path.Combine(directory, "cpu.max")), percent))
                        && (quota.MemoryLimitBytes == 0 || MatchesMemoryLimit(File.ReadAllText(Path.Combine(directory, "memory.max")), quota.MemoryLimitBytes))
                        && (quota.ReadBytesPerSecond == 0 && quota.WriteBytesPerSecond == 0
                            || MatchesIoLimit(File.ReadAllText(Path.Combine(directory, "io.max")), quota.ReadBytesPerSecond, quota.WriteBytesPerSecond))) return new(true, "ok", "资源配额已应用。");
                }
                await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, "quota_timeout", "资源配额未在时限内生效。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return new(false, "quota_unavailable", "资源配额不可用：需要 busctl、systemd 用户会话及可用的 资源控制器。"); }
    }

    internal static string[] StartArguments(string unit, int pid, int percent) => StartArguments(unit, pid, new PlatformProcessQuota(percent));

    internal static string[] StartArguments(string unit, int pid, PlatformProcessQuota quota)
    {
        int properties = 2 + (quota.CpuPercent > 0 ? 1 : 0) + (quota.MemoryLimitBytes > 0 ? 1 : 0)
            + (quota.ReadBytesPerSecond > 0 ? 1 : 0) + (quota.WriteBytesPerSecond > 0 ? 1 : 0);
        List<string> args = ["--user", "--timeout=4", "call", "org.freedesktop.systemd1", "/org/freedesktop/systemd1",
            "org.freedesktop.systemd1.Manager", "StartTransientUnit", "ssa(sv)a(sa(sv))", unit, "fail", Number(properties),
            "PIDs", "au", "1", Number(pid), "CollectMode", "s", "inactive-or-failed"];
        if (quota.CpuPercent > 0) args.AddRange(["CPUQuotaPerSecUSec", "t", Number((long)quota.CpuPercent * 10000)]);
        if (quota.MemoryLimitBytes > 0) args.AddRange(["MemoryMax", "t", Number(quota.MemoryLimitBytes)]);
        if (quota.ReadBytesPerSecond > 0) args.AddRange(["IOReadBandwidthMax", "a(st)", "1", quota.IoPath!, Number(quota.ReadBytesPerSecond)]);
        if (quota.WriteBytesPerSecond > 0) args.AddRange(["IOWriteBandwidthMax", "a(st)", "1", quota.IoPath!, Number(quota.WriteBytesPerSecond)]);
        args.Add("0");
        return args.ToArray();
        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    }

    internal static bool MatchesMemoryLimit(string value, long limit) => limit > 0
        && long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long actual) && actual > 0 && actual <= limit;

    internal static bool MatchesIoLimit(string value, long readLimit, long writeLimit)
    {
        string[] rows = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length != 1 || readLimit < 0 || writeLimit < 0 || readLimit == 0 && writeLimit == 0) return false;
        string[] fields = rows[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] device = fields[0].Split(':');
        if (device.Length != 2 || !uint.TryParse(device[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !uint.TryParse(device[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
        return Matches("rbps", readLimit) && Matches("wbps", writeLimit);
        bool Matches(string key, long limit)
        {
            if (limit == 0) return true;
            string[] candidates = fields.Skip(1).Where(field => field.StartsWith(key + "=", StringComparison.Ordinal)).ToArray();
            return candidates.Length == 1 && MatchesMemoryLimit(candidates[0][(key.Length + 1)..], limit);
        }
    }

    internal static bool MatchesQuota(string value, int percent)
    {
        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return percent is > 0 and <= 102400 && parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long quota) && quota > 0
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long period) && period is >= 1000 and <= 1000000
            && (decimal)quota * 100 <= (decimal)percent * period;
    }
}
