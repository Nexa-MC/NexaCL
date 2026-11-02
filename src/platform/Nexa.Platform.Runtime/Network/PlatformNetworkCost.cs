using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Nexa.Platform;

public sealed partial class PlatformNetworkCostProbe : IPlatformNetworkCost
{
    public async ValueTask<PlatformNetworkCost> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (OperatingSystem.IsWindows()) return await Task.Run(ReadWindows, cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsMacOS()) return Parse(await RunAsync(Path.Combine(AppContext.BaseDirectory, "Nexa.Platform.Helper"), ["network-cost"], cancellationToken).ConfigureAwait(false));
            if (OperatingSystem.IsLinux())
            {
                string[] interfaces = File.ReadLines("/proc/net/route").Take(256).Skip(1)
                    .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(row => row.Length >= 8 && row[1] == "00000000" && row[7] == "00000000")
                    .Select(row => row[0]).Distinct(StringComparer.Ordinal).Take(8).ToArray();
                // Include IPv6-only default routes. Kernel-provided interface names are argv, never shell text.
                if (File.Exists("/proc/net/ipv6_route"))
                    interfaces = interfaces.Concat(File.ReadLines("/proc/net/ipv6_route").Take(256)
                        .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        .Where(row => row.Length >= 10 && row[0] == new string('0', 32) && row[1] == "00" && row[^1] != "lo")
                        .Select(row => row[^1])).Distinct(StringComparer.Ordinal).Take(8).ToArray();
                if (interfaces.Length == 0) return PlatformNetworkCost.Unknown;
                bool unknown = false;
                foreach (string name in interfaces)
                {
                    PlatformNetworkCost cost = Parse(await RunAsync("/usr/bin/nmcli", ["-g", "GENERAL.METERED", "device", "show", name], cancellationToken).ConfigureAwait(false));
                    if (cost is PlatformNetworkCost.Metered or PlatformNetworkCost.Roaming) return cost;
                    unknown |= cost == PlatformNetworkCost.Unknown;
                }
                return unknown ? PlatformNetworkCost.Unknown : PlatformNetworkCost.Unmetered;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        { }
        return PlatformNetworkCost.Unknown;
    }

    internal static PlatformNetworkCost Parse(string text) => text.Trim().ToLowerInvariant() switch
    {
        "yes" or "yes (guessed)" or "metered" => PlatformNetworkCost.Metered,
        "no" or "no (guessed)" or "unmetered" => PlatformNetworkCost.Unmetered,
        "roaming" => PlatformNetworkCost.Roaming,
        _ => PlatformNetworkCost.Unknown
    };

    internal static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken token)
    {
        if (!File.Exists(executable)) return "unknown";
        ProcessStartInfo info = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["LC_ALL"] = "C";
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Native cost probe did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        var text = new char[256];
        try
        {
            int size = await process.StandardOutput.ReadBlockAsync(text.AsMemory(), deadline.Token).ConfigureAwait(false);
            if (size == text.Length) return "unknown";
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? new string(text, 0, size) : "unknown";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return "unknown"; }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await error.ConfigureAwait(false);
        }
    }

    internal static PlatformNetworkCost FromWindowsCost(uint cost) => (cost & 0x40000) != 0 ? PlatformNetworkCost.Roaming
        : (cost & (2 | 4 | 0x10000 | 0x80000)) != 0 ? PlatformNetworkCost.Metered
        : (cost & 1) != 0 ? PlatformNetworkCost.Unmetered : PlatformNetworkCost.Unknown;

    private static unsafe PlatformNetworkCost ReadWindows()
    {
        int initialized = CoInitializeEx(0, 0);
        nint instance = 0;
        try
        {
            Guid clsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
            Guid iid = new("DCB00008-570F-4A9B-8D69-199FDBA5723B");
            if (CoCreateInstance(in clsid, 0, 1, in iid, out instance) < 0 || instance == 0) return PlatformNetworkCost.Unknown;
            uint cost = 0;
            var vtable = *(nint**)instance;
            int result = ((delegate* unmanaged[Stdcall]<nint, uint*, nint, int>)vtable[3])(instance, &cost, 0);
            return result >= 0 ? FromWindowsCost(cost) : PlatformNetworkCost.Unknown;
        }
        finally
        {
            if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
            if (initialized >= 0) CoUninitialize();
        }
    }

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint apartment);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
