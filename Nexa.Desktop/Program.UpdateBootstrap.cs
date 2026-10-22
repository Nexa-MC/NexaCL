using System.Diagnostics;
using Nexa.Platform.Updates;
using Nexa.Services.Updates;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static bool RedirectUpdatedLauncher(string[] args)
    {
        bool system = new AutomaticUpdateHost().IsSystemInstallation;
        string? portable = system ? null : PortableUpdateInstallation.Root;
        string bootstrap = system ? UpdateInstallation.Bootstrap : portable is null ? "" : PortableUpdateInstallation.Executable(portable);
        if (!string.Equals(Environment.ProcessPath, bootstrap,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        try
        {
            using IUpdateDirectory root = system ? ProtectedUpdateDirectory.Open(UpdateInstallation.Root)
                : PortableUpdateDirectory.Open(portable!);
            return RedirectSelectedLauncher(root, ResolveInformationalVersion(), args, start =>
            {
                using Process process = Process.Start(start) ?? throw new IOException("无法启动更新版本。");
                return true;
            });
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not AccessViolationException)
        {
            // Stable bootstrap remains runnable if its selected update cannot be admitted.
            return false;
        }
    }

    internal static bool RedirectSelectedLauncher(IUpdateDirectory root, string runningVersion, string[] args,
        Func<ProcessStartInfo, bool> startProcess)
    {
        using FileStream journal = root.OpenRead(AutomaticUpdateTransaction.ActivationName);
        string[]? active = UpdateTransactionJournal.Read(journal);
        if (active is not { Length: 4 } || active[1].Length == 0) return false;
        if (!UpdateVersion.TryParse(active[0], out var target) || !UpdateVersion.TryParse(runningVersion, out var baseline)
            || target <= baseline || !PortableUpdateInstallation.IsSlot(active[1]))
            return false;
        using IUpdateDirectory slot = root.OpenDirectory(active[1]);
        IUpdateDirectory? app = null, contents = null, binaries = null;
        try
        {
            if (OperatingSystem.IsMacOS())
            { app = slot.OpenDirectory("Nexa.app"); contents = app.OpenDirectory("Contents"); binaries = contents.OpenDirectory("MacOS"); }
            IUpdateDirectory payload = binaries ?? slot;
            string name = "Nexa.Desktop" + (OperatingSystem.IsWindows() ? ".exe" : "");
            using FileStream image = payload.OpenRead(name);
            var start = new ProcessStartInfo(Path.Combine(payload.Path, name)) { UseShellExecute = false, WorkingDirectory = payload.Path };
            foreach (string argument in args) start.ArgumentList.Add(argument);
            return startProcess(start);
        }
        finally { binaries?.Dispose(); contents?.Dispose(); app?.Dispose(); }
    }
}
