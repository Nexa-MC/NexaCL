using System.Diagnostics;
using System.Globalization;

namespace Nexa.Platform;

public sealed class PlatformGameMode : IPlatformGameMode
{
    public async ValueTask<IAsyncDisposable> StartAsync(Process process, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("GameMode 仅支持 Linux。");
        if (!File.Exists("/usr/bin/busctl")) throw new InvalidOperationException("GameMode 需要 systemd 的 busctl 和已安装的 GameMode 服务。");
        int pid = process.Id;
        try
        {
            if (!await RequestAsync("RegisterGameByPID", pid, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("GameMode 服务拒绝了游戏请求，请检查服务安装和权限。");
            return new Session(pid);
        }
        catch
        {
            // A timeout may have happened after registration; remove our exact registration.
            await RequestAsync("UnregisterGameByPID", pid, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    internal static string[] Arguments(string method, int gamePid, int requesterPid) =>
        ["--user", "--timeout=4s", "call", "com.feralinteractive.GameMode", "/com/feralinteractive/GameMode",
         "com.feralinteractive.GameMode", method, "ii", gamePid.ToString(CultureInfo.InvariantCulture), requesterPid.ToString(CultureInfo.InvariantCulture)];

    private static async Task<bool> RequestAsync(string method, int pid, CancellationToken token)
    {
        try
        {
            string output = await PlatformNetworkCostProbe.RunAsync("/usr/bin/busctl", Arguments(method, pid, Environment.ProcessId), token).ConfigureAwait(false);
            return output.Trim() == "i 0";
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return false; }
    }

    private sealed class Session(int pid) : IAsyncDisposable
    {
        private int _disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await RequestAsync("UnregisterGameByPID", pid, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
