using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexa.Jvm.Host;

/// <summary>Owns the auxiliary bridge and children independently of process snapshots.</summary>
internal sealed partial class JvmChildWindowsJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    internal JvmChildWindowsJob()
    {
        nint handle = CreateJobObject(0, 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _handle = new(handle, ownsHandle: true);
        try
        {
            SetKillOnClose(true);
            if (!AssignProcessToJobObject(_handle, GetCurrentProcess()))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        catch { _handle.Dispose(); throw; }
    }

    internal void Complete() => SetKillOnClose(false);

    internal void Terminate()
    {
        if (!TerminateJobObject(_handle, 1)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private unsafe void SetKillOnClose(bool enabled)
    {
        ExtendedLimits limits = new() { Basic = new() { LimitFlags = enabled ? 0x2000u : 0u } };
        if (!SetInformationJobObject(_handle, 9, &limits, (uint)sizeof(ExtendedLimits)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessUserTime, JobUserTime;
        public uint LimitFlags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial nint CreateJobObject(nint security, nint name);
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, ExtendedLimits* information, uint length);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
