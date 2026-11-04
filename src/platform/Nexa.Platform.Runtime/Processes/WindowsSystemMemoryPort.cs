using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexa.Services.Memory;

namespace Nexa.Platform;

/// <summary>Independent implementation of documented NT memory information classes.</summary>
public sealed class WindowsSystemMemoryPort : ISystemMemoryCleanupPort, IDisposable
{
    private readonly WindowsProcessMemoryPort _measure = new();
    private readonly List<(SafeAccessTokenHandle Token, TokenPrivileges Previous)> _privileges = [];
    public bool IsSupported => OperatingSystem.IsWindows();
    public SystemMemorySample Sample() => _measure.Sample();
    public WindowsSystemMemoryPort()
    {
        if (!IsSupported) return;
        Enable("SeDebugPrivilege"); Enable("SeProfileSingleProcessPrivilege"); Enable("SeIncreaseQuotaPrivilege");
    }
    public int Execute(SystemMemoryCleanupOperation operation)
    {
        if (!IsSupported) return unchecked((int)0xC00000BB);
        if (operation == SystemMemoryCleanupOperation.VolumeCache) return FlushVolumes();
        if (operation == SystemMemoryCleanupOperation.FileCache)
        {
            FileCacheInformation cache = new() { MinimumWorkingSet = nuint.MaxValue, MaximumWorkingSet = nuint.MaxValue };
            return SetCache(81, ref cache, (uint)Marshal.SizeOf<FileCacheInformation>());
        }
        if (operation == SystemMemoryCleanupOperation.RegistryCache) return SetEmpty(155, 0, 0);
        if (operation == SystemMemoryCleanupOperation.CombinePages)
        {
            CombineInformation combine = default;
            return SetCombine(130, ref combine, (uint)Marshal.SizeOf<CombineInformation>());
        }
        int command = operation switch
        {
            SystemMemoryCleanupOperation.WorkingSets => 2,
            SystemMemoryCleanupOperation.ModifiedPages => 3,
            SystemMemoryCleanupOperation.StandbyPages => 4,
            SystemMemoryCleanupOperation.LowPriorityStandby => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        return SetList(80, ref command, sizeof(int));
    }
    private static int FlushVolumes()
    {
        // Enumerate volume GUID paths, including volumes without a drive letter. No dismount,
        // lock or disk write is requested: FlushFileBuffers only commits the existing cache.
        char[] name = new char[1024];
        nint enumeration = FindFirstVolumeW(name, (uint)name.Length);
        if (enumeration == -1) return StatusFromError();
        int firstFailure = 0, count = 0;
        try
        {
            do
            {
                if (++count > 256) return unchecked((int)0xC000009A);
                int length = Array.IndexOf(name, '\0');
                if (length <= 1) return unchecked((int)0xC000000D);
                string path = new string(name, 0, length).TrimEnd('\\');
                using var volume = CreateFileW(path, 0x40000000, 0x1 | 0x2, 0, 3, 0, 0);
                int status = volume.IsInvalid || !FlushFileBuffers(volume) ? StatusFromError() : 0;
                if (status < 0 && firstFailure == 0) firstFailure = status;
                Array.Clear(name);
            } while (FindNextVolumeW(enumeration, name, (uint)name.Length));
            int error = Marshal.GetLastWin32Error();
            return error == 18 ? firstFailure : RtlNtStatusFromDosError((uint)error);
        }
        finally { _ = FindVolumeClose(enumeration); }
    }
    private static int StatusFromError() => RtlNtStatusFromDosError((uint)Marshal.GetLastWin32Error());
    private void Enable(string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) return;
        if (!LookupPrivilegeValueW(null, name, out long luid)) { token.Dispose(); return; }
        TokenPrivileges requested = new() { Count = 1, Luid = luid, Attributes = 2 };
        if (!AdjustTokenPrivileges(token, false, ref requested, (uint)Marshal.SizeOf<TokenPrivileges>(), out var previous, out _)
            || Marshal.GetLastWin32Error() != 0) { token.Dispose(); return; }
        _privileges.Add((token, previous));
    }
    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var entry in _privileges)
        {
            var previous = entry.Previous;
            _ = AdjustTokenPrivileges(entry.Token, false, ref previous, (uint)Marshal.SizeOf<TokenPrivileges>(), out _, out _);
            entry.Token.Dispose();
        }
        _privileges.Clear();
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct TokenPrivileges { public uint Count; public long Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileCacheInformation
    {
        public nuint CurrentSize, PeakSize; public uint PageFaultCount; public nuint MinimumWorkingSet, MaximumWorkingSet,
            CurrentSizeIncludingTransitionInPages, PeakSizeIncludingTransitionInPages; public uint TransitionRePurposeCount, Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CombineInformation { public nint EventHandle; public nuint PagesCombined; public uint Flags; }
    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")] private static extern int SetList(int informationClass, ref int value, int size);
    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")] private static extern int SetCache(int informationClass, ref FileCacheInformation value, uint size);
    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")] private static extern int SetCombine(int informationClass, ref CombineInformation value, uint size);
    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")] private static extern int SetEmpty(int informationClass, nint value, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint FindFirstVolumeW([Out] char[] name, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextVolumeW(nint enumeration, [Out] char[] name, uint length);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindVolumeClose(nint enumeration);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool FlushFileBuffers(SafeFileHandle file);
    [DllImport("ntdll.dll")] private static extern int RtlNtStatusFromDosError(uint error);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges requested, uint length, out TokenPrivileges previous, out uint returned);
}
