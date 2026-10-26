using System.Globalization;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Telemetry;

/// <summary>Allowlisted lifecycle evidence. Unknown measurements are never converted to zero.</summary>
internal static class RunLifecycleTelemetry
{
    internal static readonly string[] LaunchFields = ["java", "classpath", "libraries", "natives", "heap_mib",
        "cpu_sets", "cpu_policy", "quality", "cpu_quota", "memory_limit_mib", "read_limit_mib", "write_limit_mib",
        "gamemode", "wrapped", "inherited"];
    internal static readonly string[] SummaryFields = ["elapsed_ms", "launch_ms", "exit", "exit_observed", "core_observed", "cpu_observed", "io_observed",
        "working_peak", "private_peak", "threads_peak", "cpu_ms", "cpu_peak", "physical_p95", "cpu_p95",
        "heap_peak", "native_peak", "commit_peak", "gpu_local_peak", "gpu_shared_peak", "gpu_combined_peak",
        "heap_p95", "commit_p95", "gpu_p95", "tree_working_peak", "tree_process_peak", "io_read", "io_write",
        "launch_window_ms", "launch_heap_peak", "launch_native_peak", "launch_physical_peak", "launch_commit_peak",
        "launch_gpu_local_peak", "launch_gpu_shared_peak", "launch_cpu_peak", "launch_io_read"];

    public static IReadOnlyDictionary<string, string> Launch(Guid run, JvmRunLaunchProfile p) => Fields(run, LaunchFields,
        [p.JavaMajor, p.ClasspathCount, p.LibraryCount, p.NativeLibraryCount, p.HeapLimitMiB, p.CpuSetCount,
            p.CpuPolicy, p.ProcessQuality, p.CpuQuotaPercent, p.ProcessMemoryLimitMiB, p.IoReadLimitMiB,
            p.IoWriteLimitMiB, p.GameMode ? 1 : 0, p.Wrapped ? 1 : 0, p.InheritedClientJar ? 1 : 0]);

    public static IReadOnlyDictionary<string, string> Summary(JvmHostObservation o) => Fields(o.SessionId, SummaryFields,
        [o.EndedAt is { } end ? Math.Max(0, (long)(end - o.StartedAt).TotalMilliseconds) : -1,
            o.LaunchDurationMilliseconds, o.ExitCode ?? -1, o.ExitCode.HasValue ? 1 : 0, o.CoreMetricsObserved ? 1 : 0,
            o.CpuPercentObserved ? 1 : 0, o.IoObserved ? 1 : 0,
            o.CoreMetricsObserved ? o.PeakWorkingSetBytes : -1, o.CoreMetricsObserved ? o.PeakPrivateBytes : -1,
            o.CoreMetricsObserved ? o.PeakThreadCount : -1, o.CoreMetricsObserved ? o.TotalProcessorMilliseconds : -1,
            o.CpuPercentObserved ? o.CpuPeakPercent : -1, o.CoreMetricsObserved ? o.RuntimePhysicalP95Bytes : -1,
            o.CpuPercentObserved ? o.RuntimeCpuP95Percent : -1,
            o.MeasuredHeapPeakBytes, o.MeasuredNativePeakBytes, o.MeasuredCommitPeakBytes,
            o.MeasuredGpuLocalPeakBytes, o.MeasuredGpuSharedPeakBytes, o.MeasuredCombinedGpuPeakBytes,
            o.MeasuredRuntimeHeapP95Bytes, o.MeasuredRuntimeCommitP95Bytes, o.MeasuredRuntimeGpuP95Bytes,
            o.PeakTreeWorkingSetBytes, o.PeakTreeProcessCount, o.IoObserved ? o.IoReadBytes : -1,
            o.IoObserved ? o.IoWriteBytes : -1, o.LaunchWindowMilliseconds,
            o.MeasuredLaunchHeapPeakBytes, o.MeasuredLaunchNativePeakBytes, o.MeasuredLaunchPhysicalPeakBytes,
            o.MeasuredLaunchCommitPeakBytes, o.MeasuredLaunchGpuLocalPeakBytes, o.MeasuredLaunchGpuSharedPeakBytes,
            o.MeasuredLaunchCpuPeakPercent, o.MeasuredLaunchIoReadBytes]);

    private static Dictionary<string, string> Fields(Guid run, string[] keys, long?[] values)
    {
        var result = new Dictionary<string, string> { ["run"] = run.ToString("N"), ["schema"] = "1" };
        for (int i = 0; i < keys.Length; i++) result[keys[i]] = (values[i] ?? -1).ToString(CultureInfo.InvariantCulture);
        return result;
    }

    public static bool Valid(string name, IReadOnlyDictionary<string, string> p)
    {
        var keys = name == "diagnostic.launch" ? LaunchFields : SummaryFields;
        return p.Count == keys.Length + 6 && p.GetValueOrDefault("schema") == "1"
            && p.TryGetValue("run", out string? run) && run.Length == 32 && Guid.TryParseExact(run, "N", out _)
            && keys.All(key => p.TryGetValue(key, out string? value)
                && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)
                && n >= (key == "exit" ? int.MinValue : -1) && n <= (key == "exit" ? int.MaxValue : 1_000_000_000_000_000L));
    }
}
