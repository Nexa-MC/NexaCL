using System.Runtime.InteropServices;

namespace Nexa.Services.Capabilities;

public static partial class GpuProbes
{
    /// <summary>Minimum local-segment headroom among adapters allocating for this process.</summary>
    public static long? ReadActiveGpuHeadroom()
    {
        if (!OperatingSystem.IsWindows()) return null;
        nint factory = 0;
        try
        {
            Guid factoryId = new("50C83A1C-E072-4C48-87B0-3630FA36A6D0");
            if (CreateDXGIFactory1(ref factoryId, out factory) != 0 || factory == 0) return null;
            long? minimum = null;
            for (uint index = 0; index <= 32; index++)
            {
                nint adapter = 0, adapter3 = 0;
                try
                {
                    int hr = CallComSlot(factory, 7, index, out adapter);
                    if (hr == unchecked((int)0x887A0002)) return minimum; // DXGI_ERROR_NOT_FOUND
                    if (hr != 0 || adapter == 0 || index == 32) return null;
                    Guid adapterId = new("645967A4-1392-4310-A798-8053CE3E93FD");
                    if (QI(adapter, ref adapterId, out adapter3) != 0 || adapter3 == 0) continue;
                    DXGI_QUERY_VIDEO_MEMORY_INFO info = default;
                    if (QueryVideoMemoryInfo(adapter3, 0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, ref info) != 0) continue;
                    if (info.CurrentUsage == 0 || info.Budget == 0) continue;
                    ulong available = info.Budget > info.CurrentUsage ? info.Budget - info.CurrentUsage : 0;
                    long bounded = (long)Math.Min(available, (ulong)long.MaxValue);
                    minimum = Math.Min(minimum ?? long.MaxValue, bounded);
                }
                finally
                {
                    if (adapter3 != 0) Marshal.Release(adapter3);
                    if (adapter != 0) Marshal.Release(adapter);
                }
            }
            return null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or COMException) { return null; }
        finally { if (factory != 0) Marshal.Release(factory); }
    }
}
