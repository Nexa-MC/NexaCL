using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Nexa.Platform.Updates;

/// <summary>Portable layout discovery and restart. Never authorizes an elevated helper.</summary>
public static partial class PortableUpdateInstallation
{
    private static readonly SearchValues<char> SlotHex = SearchValues.Create("0123456789abcdef");
    public static bool IsElevated => OperatingSystem.IsWindows() ? IsWindowsAdministrator() : GetEffectiveUid() == 0;

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string? Root => IsElevated ? null : ResolveRoot(Environment.ProcessPath,
        Environment.GetEnvironmentVariable("APPIMAGE"));

    internal static string? ResolveRoot(string? executable, string? appImage = null)
    {
        if (string.IsNullOrEmpty(executable) || !string.IsNullOrEmpty(appImage)) return null;
        string full = System.IO.Path.GetFullPath(executable);
        string suffix = OperatingSystem.IsWindows() ? "Nexa.Desktop.exe" : "Nexa.Desktop";
        if (!string.Equals(System.IO.Path.GetFileName(full), suffix, StringComparison.Ordinal)) return null;
        string root = System.IO.Path.GetDirectoryName(full)!;
        if (OperatingSystem.IsMacOS())
        {
            string bundle = System.IO.Path.Combine("Nexa.app", "Contents", "MacOS");
            if (!root.EndsWith(System.IO.Path.DirectorySeparatorChar + bundle, StringComparison.Ordinal)) return null;
            root = root[..^(bundle.Length + 1)];
        }
        string leaf = System.IO.Path.GetFileName(root);
        if (leaf.StartsWith(".nexa-slot-", StringComparison.Ordinal))
        {
            if (!IsSlot(leaf)) return null;
            root = System.IO.Path.GetDirectoryName(root)!;
        }
        return IsSystemPath(root) ? null : root;
    }

    internal static bool IsSystemPath(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string system = UpdateInstallation.Root;
        return path.Equals(system, comparison) || path.StartsWith(system + System.IO.Path.DirectorySeparatorChar, comparison);
    }

    public static bool IsSlot(string name) => name.Length == 43 && name.StartsWith(".nexa-slot-", StringComparison.Ordinal)
        && name.AsSpan(11).IndexOfAnyExcept(SlotHex) < 0;

    public static string Executable(string root) => OperatingSystem.IsMacOS()
        ? System.IO.Path.Combine(root, "Nexa.app/Contents/MacOS/Nexa.Desktop")
        : System.IO.Path.Combine(root, "Nexa.Desktop" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    public static void Restart(string root)
    {
        using var directory = PortableUpdateDirectory.Open(root);
        using Process process = Process.Start(new ProcessStartInfo(Executable(directory.Path))
        { UseShellExecute = false, WorkingDirectory = directory.Path }) ?? throw new IOException("无法重新启动 NexaCL。");
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
}
