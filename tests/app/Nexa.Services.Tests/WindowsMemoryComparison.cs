using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Nexa.Platform;
using Nexa.Services.Memory;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async Task CompareWindowsMemoryCleanup(string baselineExecutable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Memory comparison requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Use an elevated, disposable Windows test host.");
        if (!Path.IsPathFullyQualified(baselineExecutable) || !File.Exists(baselineExecutable))
            throw new FileNotFoundException("The pinned Mem Reduct comparison executable is required.");
        string baselineDirectory = Path.Combine(Path.GetTempPath(), "nexa-memory-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baselineDirectory);
        string copiedBaseline = Path.Combine(baselineDirectory, "memreduct.exe");
        File.Copy(baselineExecutable, copiedBaseline);
        try
        {
            List<double> nexa = [], baseline = [];
            List<long> nexaAvailable = [], baselineAvailable = [];
            // Alternate first/second order and recreate the same touched 64 MiB process for
            // every sample. Never compare two cleaners against the same already-cleaned process.
            for (int round = 0; round < 6; round++)
            {
                foreach (bool useNexa in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    var start = new ProcessStartInfo(Environment.ProcessPath!)
                    { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true };
                    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Nexa.Services.Tests.dll"));
                    start.ArgumentList.Add("--memory-cleanup-child");
                    using var child = Process.Start(start)!;
                    try
                    {
                        AssertEqual("memory-ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
                        child.Refresh(); long workingBefore = child.WorkingSet64;
                        AssertTrue(workingBefore >= 48L * 1024 * 1024);
                        var port = new WindowsMemoryElevationPort(); var before = port.Sample();
                        long started = Stopwatch.GetTimestamp();
                        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                        if (useNexa)
                        {
                            var result = await port.CleanElevatedAsync(new HashSet<int>(), deadline.Token);
                            AssertEqual(8, result.SystemOperations.Count);
                            AssertTrue(result.SystemOperations.Any(step => step.Operation == SystemMemoryCleanupOperation.WorkingSets && step.Outcome == SystemMemoryCleanupOutcome.Succeeded));
                            AssertFalse(result.SystemOperations.Any(step => step.Outcome == SystemMemoryCleanupOutcome.GameProtected));
                        }
                        else
                        {
                            var baselineStart = new ProcessStartInfo(copiedBaseline) { UseShellExecute = false, CreateNoWindow = true };
                            baselineStart.ArgumentList.Add("-clean:full");
                            baselineStart.ArgumentList.Add("-portable");
                            using var cleaner = Process.Start(baselineStart)!;
                            try
                            {
                                // The pinned CLI displays an owned result dialog even with balloon
                                // notifications disabled. Dismiss only that process's result window.
                                var exit = cleaner.WaitForExitAsync(deadline.Token);
                                while (!exit.IsCompleted)
                                {
                                    deadline.Token.ThrowIfCancellationRequested();
                                    _ = EnumWindows((window, context) =>
                                    {
                                        _ = GetWindowThreadProcessId(window, out uint owner);
                                        if (owner == cleaner.Id)
                                        {
                                            char[] name = new char[64]; int length = GetClassNameW(window, name, name.Length);
                                            if (new string(name, 0, Math.Max(0, length)) == "#32770") _ = PostMessageW(window, 0x111, 1, 0);
                                        }
                                        return context == 0;
                                    }, 0);
                                    await Task.WhenAny(exit, Task.Delay(50, deadline.Token));
                                }
                                await exit;
                                // This release returns ERROR_APP_INIT_FAILURE after handling CLI
                                // commands too. The controlled workload proves cleanup actually ran.
                                AssertTrue(cleaner.ExitCode is 0 or 575);
                            }
                            finally
                            {
                                if (!cleaner.HasExited) { cleaner.Kill(entireProcessTree: true); await cleaner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                            }
                        }
                        var after = port.Sample(); child.Refresh(); long workingAfter = child.WorkingSet64;
                        double fraction = Math.Clamp((workingBefore - workingAfter) / (double)workingBefore, 0, 1);
                        (useNexa ? nexa : baseline).Add(fraction);
                        (useNexa ? nexaAvailable : baselineAvailable).Add(after.AvailableBytes - before.AvailableBytes);
                        Console.WriteLine($"MEMORY_COMPARISON\t{round}\t{(useNexa ? "NexaCL" : "MemReduct-3.5.2")}\tworking-before={workingBefore}\tworking-after={workingAfter}\ttrim-fraction={fraction:F6}\tavailable-change={after.AvailableBytes - before.AvailableBytes}\telapsed-ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}");
                        AssertFalse(child.HasExited);
                        await child.StandardInput.WriteLineAsync("exit");
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
                }
            }
            static double Median(IEnumerable<double> values) { double[] sorted = values.Order().ToArray(); return (sorted[2] + sorted[3]) / 2; }
            double nexaMedian = Median(nexa), baselineMedian = Median(baseline);
            Console.WriteLine($"MEMORY_COMPARISON_MEDIAN\tNexaCL={nexaMedian:F6}\tMemReduct-3.5.2={baselineMedian:F6}\tNexaAvailable={Median(nexaAvailable.Select(x => (double)x)):F0}\tMemReductAvailable={Median(baselineAvailable.Select(x => (double)x)):F0}");
            AssertTrue(baselineMedian >= 0.75, "Baseline did not trim the controlled workload; comparison is invalid.");
            AssertTrue(nexaMedian + 0.05 >= baselineMedian, "NexaCL trails the baseline's controlled working-set reduction by more than five percentage points.");
        }
        finally { Directory.Delete(baselineDirectory, recursive: true); }
    }
    [return: MarshalAs(UnmanagedType.Bool)] private delegate bool MemoryComparisonWindowCallback(nint window, nint context);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(MemoryComparisonWindowCallback callback, nint context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, [Out] char[] name, int length);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(nint window, uint message, nuint parameter, nint extra);

}
