using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexa.Services.Memory;

namespace Nexa.Platform;

/// <summary>Independent implementation using Windows process and working-set APIs.</summary>
public sealed class WindowsProcessMemoryPort : IProcessMemoryPort
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public SystemMemorySample Sample()
    {
        if (!IsSupported) return default;
        MemoryStatus status = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) && status.TotalPhysical <= long.MaxValue
            && status.AvailablePhysical <= status.TotalPhysical
            ? new((long)status.TotalPhysical, (long)status.AvailablePhysical) : default;
    }

    public IReadOnlyList<MemoryProcess> CaptureProcesses()
    {
        if (!IsSupported) return [];
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out uint session))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        List<MemoryProcess> result = [];
        ProcessEntry entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Name = "" };
        if (!Process32FirstW(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        do
        {
            if (result.Count >= 4096) throw new IOException("Process snapshot exceeds its cleanup budget.");
            using var process = OpenProcess(0x1000, false, entry.Id);
            long creation = 0;
            bool protect = entry.Id <= 4 || entry.Id == Environment.ProcessId || entry.Id == foreground
                || !ProcessIdToSessionId(entry.Id, out uint ownerSession) || ownerSession != session
                || process.IsInvalid || !TryCreationTime(process, out creation)
                || !IsProcessCritical(process, out bool critical) || critical;
            result.Add(new((int)entry.Id, (int)entry.ParentId, entry.Name, creation, protect));
            entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
        } while (Process32NextW(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    public bool TryTrim(MemoryProcess process, out long workingSetReductionBytes)
    {
        workingSetReductionBytes = 0;
        if (!IsSupported || process.Id <= 4 || process.IsProtected || MemoryOptimizationService.IsGameName(process.Name)) return false;
        using var handle = OpenProcess(0x1000 | 0x100, false, (uint)process.Id);
        if (handle.IsInvalid || !TryCreationTime(handle, out long created) || created != process.CreationTime
            || !IsProcessCritical(handle, out bool critical) || critical) return false;
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
        if (process.Id == foreground || process.Id == Environment.ProcessId) return false;
        char[] path = new char[32768];
        uint length = (uint)path.Length;
        if (!QueryFullProcessImageNameW(handle, 0, path, ref length)
            || !string.Equals(Path.GetFileName(new string(path, 0, (int)length)), process.Name, StringComparison.OrdinalIgnoreCase)
            || MemoryOptimizationService.IsGameName(new string(path, 0, (int)length))) return false;
        if (!TryWorkingSet(handle, out long before) || !EmptyWorkingSet(handle)) return false;
        if (TryWorkingSet(handle, out long after)) workingSetReductionBytes = Math.Max(0, before - after);
        return true;
    }

    private static bool TryCreationTime(SafeProcessHandle process, out long created)
    {
        bool result = GetProcessTimes(process, out long creation, out _, out _, out _);
        created = creation; return result;
    }

    private static bool TryWorkingSet(SafeProcessHandle process, out long bytes)
    {
        ProcessMemoryCounters counters = new() { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
        bool success = GetProcessMemoryInfo(process, ref counters, counters.Size);
        bytes = success ? (long)Math.Min((ulong)counters.WorkingSetSize, long.MaxValue) : 0;
        return success;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public nuint DefaultHeap;
        public uint ModuleId, Threads, ParentId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint session);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessCritical(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] name, ref uint size);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(SafeProcessHandle process, ref ProcessMemoryCounters counters, uint size);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(SafeProcessHandle process);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
