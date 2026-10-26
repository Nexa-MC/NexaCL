using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Nexa.Platform;

public sealed partial class PlatformCpuTopology : IPlatformCpuTopology
{
    internal sealed record Cpu(int Index, int? Capacity, int? Numa, long? CacheBytes, string? CacheDomain);

    public IReadOnlyList<int> ChooseProcessors(string preference)
    {
        if (preference == "system") return [];
        if (preference is not ("performance" or "cache" or "numa")) throw new ArgumentException("未知处理器拓扑策略。", nameof(preference));
        try
        {
            Cpu[] cpus = OperatingSystem.IsLinux() ? ReadLinux() : OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? ReadWindows() : [];
            return ChooseProcessors(cpus, preference);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or DllNotFoundException or EntryPointNotFoundException)
        { throw new InvalidOperationException("无法读取当前允许使用的处理器拓扑。", error); }
    }

    internal static IReadOnlyList<int> ChooseProcessors(IReadOnlyList<Cpu> cpus, string preference)
    {
        if (cpus.Count is 0 or > 1024 || cpus.Any(c => c.Index is < 0 or >= 1024) || cpus.Select(c => c.Index).Distinct().Count() != cpus.Count)
            throw new InvalidOperationException("处理器拓扑不可用或超过支持范围。");
        IEnumerable<Cpu> selected;
        if (preference == "performance" && cpus.All(c => c.Capacity.HasValue))
            selected = cpus.Where(c => c.Capacity == cpus.Max(c => c.Capacity));
        else if (preference == "cache" && cpus.All(c => c.CacheBytes is > 0 && c.CacheDomain is not null))
            selected = cpus.GroupBy(c => c.CacheDomain!).OrderByDescending(g => g.Max(c => c.CacheBytes))
                .ThenByDescending(g => g.Count()).ThenBy(g => g.Min(c => c.Index)).First();
        else if (preference == "numa" && cpus.All(c => c.Numa is >= 0))
            selected = cpus.GroupBy(c => c.Numa!.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        else throw new InvalidOperationException("系统未提供此策略所需的可靠拓扑信息，请使用系统默认调度。");
        return Array.AsReadOnly(selected.Select(c => c.Index).Order().ToArray());
    }

    private static unsafe Cpu[] ReadLinux()
    {
        byte[] affinity = new byte[128];
        fixed (byte* mask = affinity)
            if (sched_getaffinity(Environment.ProcessId, (nuint)affinity.Length, mask) != 0) throw new IOException("CPU affinity unavailable.");
        List<Cpu> result = [];
        for (int cpu = 0; cpu < 1024; cpu++)
        {
            if ((affinity[cpu / 8] & (1 << (cpu % 8))) == 0) continue;
            string root = $"/sys/devices/system/cpu/cpu{cpu}";
            int? capacity = Number(Path.Combine(root, "cpu_capacity")) ?? Number(Path.Combine(root, "acpi_cppc/highest_perf"));
            if (capacity is <= 0) capacity = null;
            int? numa = Directory.EnumerateDirectories(root, "node*").Take(16)
                .Select(path => int.TryParse(Path.GetFileName(path).AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int node) ? (int?)node : null)
                .FirstOrDefault(n => n.HasValue);
            long? bytes = null; string? domain = null; int level = -1;
            string cacheRoot = Path.Combine(root, "cache");
            if (Directory.Exists(cacheRoot))
                foreach (string cache in Directory.EnumerateDirectories(cacheRoot, "index*").Take(16))
                {
                    int? current = Number(Path.Combine(cache, "level"));
                    if (current is null || current < level || Read(Path.Combine(cache, "type")) is not ("Unified" or "Data")) continue;
                    string? size = Read(Path.Combine(cache, "size"));
                    string? shared = Read(Path.Combine(cache, "shared_cpu_list"));
                    if (size is null || shared is null) continue;
                    long unit = size.EndsWith('K') ? 1024 : size.EndsWith('M') ? 1048576 : 1;
                    string number = unit == 1 ? size : size[..^1];
                    if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long count) || count <= 0 || count > long.MaxValue / unit) continue;
                    level = current.Value; bytes = count * unit; domain = level.ToString(CultureInfo.InvariantCulture) + ":" + shared;
                }
            result.Add(new(cpu, capacity, numa, bytes, domain));
        }
        return result.ToArray();
        static int? Number(string path) => int.TryParse(Read(path), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 0 ? number : null;
        static string? Read(string path)
        {
            if (!File.Exists(path)) return null;
            using var reader = new StreamReader(path);
            char[] buffer = new char[4096];
            int count = reader.ReadBlock(buffer, 0, buffer.Length);
            return count == buffer.Length ? null : new string(buffer, 0, count).Trim();
        }
    }

    private static unsafe Cpu[] ReadWindows()
    {
        if (!Environment.Is64BitProcess) throw new InvalidOperationException("拓扑调度需要 64 位进程。");
        using var process = Process.GetCurrentProcess();
        uint length = 0;
        _ = GetSystemCpuSetInformation(null, 0, out length, process.Handle, 0);
        if (length is 0 or > 262144) throw new IOException("CPU set buffer unavailable.");
        byte[] data = new byte[length];
        fixed (byte* buffer = data)
            if (GetSystemCpuSetInformation(buffer, length, out length, process.Handle, 0) == 0 || length > data.Length) throw new IOException("CPU set read failed.");
        uint cacheLength = 0;
        _ = GetLogicalProcessorInformationEx(2, null, ref cacheLength);
        if (cacheLength > 1048576) throw new IOException("Cache topology exceeds the read budget.");
        byte[] caches = new byte[cacheLength];
        if (cacheLength > 0)
            fixed (byte* buffer = caches)
                if (GetLogicalProcessorInformationEx(2, buffer, ref cacheLength) == 0 || cacheLength > caches.Length) throw new IOException("Cache topology read failed.");
        uint defaultCount = 0;
        int readDefaults = GetProcessDefaultCpuSets(process.Handle, null, 0, out defaultCount);
        if (defaultCount > 1024 || readDefaults == 0 && defaultCount == 0) throw new IOException("Process CPU sets unavailable.");
        uint[] defaults = new uint[defaultCount];
        if (defaultCount > 0)
        {
            fixed (uint* ids = defaults)
                if (GetProcessDefaultCpuSets(process.Handle, ids, defaultCount, out uint actual) == 0 || actual != defaultCount)
                    throw new IOException("Process CPU sets changed during observation.");
        }
        var topology = ParseWindows(data.AsSpan(0, (int)length), caches.AsSpan(0, (int)cacheLength),
            defaults.Length == 0 ? null : defaults.ToHashSet());
        ushort count = 16;
        ushort[] groups = new ushort[count];
        fixed (ushort* values = groups)
            if (GetProcessGroupAffinity(process.Handle, ref count, values) == 0 || count is 0 or > 16) throw new IOException("Process groups unavailable.");
        if (GetProcessAffinityMask(process.Handle, out nuint mask, out _) == 0) throw new IOException("Process affinity unavailable.");
        return topology.Where(cpu => groups.Take(count).Contains((ushort)(cpu.Index / 64))
            && (count != 1 || mask == 0 || ((ulong)mask & (1UL << (cpu.Index % 64))) != 0)).ToArray();
    }

    internal static Cpu[] ParseWindows(ReadOnlySpan<byte> sets, ReadOnlySpan<byte> caches, IReadOnlySet<uint>? allowedIds = null)
    {
        Dictionary<int, (int Level, uint Bytes, string Domain)> sizes = [];
        int offset = 0;
        while (offset < caches.Length)
        {
            ReadOnlySpan<byte> entry = caches[offset..];
            if (entry.Length < 8) throw new InvalidDataException("Truncated cache header.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            if (length < 8 || length > entry.Length) throw new InvalidDataException("Invalid cache record length.");
            entry = entry[..(int)length];
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) == 2)
            {
                if (entry.Length < 56) throw new InvalidDataException("Truncated cache relationship.");
                int groups = Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(entry[38..]));
                if (groups > 16 || 40 + groups * 16 > entry.Length) throw new InvalidDataException("Invalid cache groups.");
                int level = entry[8]; uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
                if (type is 0 or 2)
                    for (int index = 0; index < groups; index++)
                    {
                        ReadOnlySpan<byte> group = entry[(40 + index * 16)..];
                        ulong mask = BinaryPrimitives.ReadUInt64LittleEndian(group);
                        int id = BinaryPrimitives.ReadUInt16LittleEndian(group[8..]);
                        for (int bit = 0; bit < 64; bit++)
                            if ((mask & (1UL << bit)) != 0 && id < 16 && (!sizes.TryGetValue(id * 64 + bit, out var prior) || level > prior.Level))
                                sizes[id * 64 + bit] = (level, bytes, offset.ToString(CultureInfo.InvariantCulture));
                    }
            }
            offset += (int)length;
        }
        List<Cpu> result = []; offset = 0;
        while (offset < sets.Length)
        {
            ReadOnlySpan<byte> entry = sets[offset..];
            if (entry.Length < 8) throw new InvalidDataException("Truncated CPU header.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (length < 8 || length > entry.Length) throw new InvalidDataException("Invalid CPU record length.");
            entry = entry[..(int)length];
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) == 0)
            {
                if (entry.Length < 32) throw new InvalidDataException("Truncated CPU record.");
                int group = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);
                int index = group * 64 + entry[14];
                if ((allowedIds is null || allowedIds.Contains(BinaryPrimitives.ReadUInt32LittleEndian(entry[8..])))
                    && group < 16 && entry[14] < 64 && ((entry[19] & 2) == 0 || (entry[19] & 4) != 0))
                {
                    bool known = sizes.TryGetValue(index, out var cache);
                    result.Add(new(index, entry[18], entry[17], known ? cache.Bytes : null, known ? cache.Domain : null));
                }
            }
            offset += (int)length;
        }
        return result.ToArray();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int GetProcessDefaultCpuSets(nint process, uint* ids, uint count, out uint required);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int GetProcessGroupAffinity(nint process, ref ushort count, ushort* groups);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int GetProcessAffinityMask(nint process, out nuint mask, out nuint systemMask);
    [LibraryImport("libc", SetLastError = true)] private static unsafe partial int sched_getaffinity(int pid, nuint size, byte* mask);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int GetSystemCpuSetInformation(byte* buffer, uint length, out uint required, nint process, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);
}
