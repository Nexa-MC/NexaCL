using System.Diagnostics;
using System.Globalization;

namespace Nexa.Platform;

/// <summary>Places a waiting JVM Host in an independently collected user scope before bootstrap.</summary>
public sealed class PlatformCpuQuota : IPlatformCpuQuota
{
    public async ValueTask<PlatformProcessControlResult> ApplyAsync(Process process, int percent, CancellationToken cancellationToken = default)
    {
        if (percent is < 0 or > 102400) return new(false, "invalid_quota", "CPU 配额必须为 0 至 102400，100 表示一个 CPU。");
        if (percent == 0) return new(true, "ok", "");
        if (!OperatingSystem.IsLinux()) return new(false, "platform_unsupported", "CPU 配额需要 Linux systemd 用户会话与 cgroup v2。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        string unit = "nexacl-game-" + Guid.NewGuid().ToString("N") + ".scope";
        try
        {
            if (!File.Exists("/sys/fs/cgroup/cgroup.controllers"))
                return new(false, "dependency_missing", "CPU 配额需要 cgroup v2。");
            ProcessStartInfo info = new("/usr/bin/busctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in StartArguments(unit, process.Id, percent)) info.ArgumentList.Add(argument);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.HasExited) return new(false, "process_exited", "JVM Host 在应用 CPU 配额前已退出。");
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
            if (command.ExitCode != 0) return new(false, "quota_refused", "无法创建 CPU 配额：请检查 systemd 用户会话、CPU 控制器委派及权限。");
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (process.HasExited) return new(false, "process_exited", "JVM Host 在应用 CPU 配额前已退出。");
                string? group = File.ReadLines($"/proc/{process.Id}/cgroup").FirstOrDefault(line => line.StartsWith("0::", StringComparison.Ordinal))?[3..];
                if (group is not null && group.StartsWith('/') && !group.Split('/').Contains("..") && group.EndsWith("/" + unit, StringComparison.Ordinal))
                {
                    string limit = File.ReadAllText(Path.Combine("/sys/fs/cgroup", group.TrimStart('/'), "cpu.max"));
                    if (MatchesQuota(limit, percent)) return new(true, "ok", "CPU 配额已应用。");
                }
                await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, "quota_timeout", "CPU 配额未在时限内生效。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return new(false, "quota_unavailable", "CPU 配额不可用：需要 busctl、systemd 用户会话及可用的 CPU 控制器。"); }
    }

    internal static string[] StartArguments(string unit, int pid, int percent) =>
    ["--user", "--timeout=4", "call", "org.freedesktop.systemd1", "/org/freedesktop/systemd1",
        "org.freedesktop.systemd1.Manager", "StartTransientUnit", "ssa(sv)a(sa(sv))", unit, "fail", "3",
        "PIDs", "au", "1", pid.ToString(CultureInfo.InvariantCulture),
        "CPUQuotaPerSecUSec", "t", ((long)percent * 10000).ToString(CultureInfo.InvariantCulture),
        "CollectMode", "s", "inactive-or-failed", "0"];

    internal static bool MatchesQuota(string value, int percent)
    {
        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return percent is > 0 and <= 102400 && parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long quota) && quota > 0
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long period) && period is >= 1000 and <= 1000000
            && (decimal)quota * 100 <= (decimal)percent * period;
    }
}
