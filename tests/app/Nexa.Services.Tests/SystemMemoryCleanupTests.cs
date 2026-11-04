using System.Diagnostics;
using System.Security.Principal;
using Nexa.Platform;
using Nexa.Services.Memory;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask CompletionSystemMemoryCleanup()
    {
        var port = new CompletionMemoryPort();
        var results = await SystemMemoryCleanupService.RunAsync(port, () => false);
        AssertEqual(8, results.Count); AssertEqual(8, port.Calls);
        AssertEqual(SystemMemoryCleanupOutcome.AccessDenied, results[1].Outcome);
        AssertEqual(7, results.Count(r => r.Outcome == SystemMemoryCleanupOutcome.Succeeded));
        port.Calls = 0;
        results = await SystemMemoryCleanupService.RunAsync(port, () => port.Calls >= 1);
        AssertEqual(1, port.Calls); AssertEqual(7, results.Count(r => r.Outcome == SystemMemoryCleanupOutcome.GameProtected));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await TrustRejectsAsync<OperationCanceledException>(() => SystemMemoryCleanupService.RunAsync(port, () => false, stop.Token));
    }
    private sealed class CompletionMemoryPort : ISystemMemoryCleanupPort
    {
        internal int Calls;
        public bool IsSupported => true;
        public SystemMemorySample Sample() => new(10000, 1000 + Calls * 100);
        public int Execute(SystemMemoryCleanupOperation operation)
        { Calls++; return operation == SystemMemoryCleanupOperation.FileCache ? unchecked((int)0xC0000061) : 0; }
    }

    private static async ValueTask WindowsSystemMemoryCleanupReportsNativeOutcomes()
    {
        using var port = new WindowsSystemMemoryPort();
        if (!OperatingSystem.IsWindows())
        {
            AssertFalse(port.IsSupported);
            var unsupported = await SystemMemoryCleanupService.RunAsync(port, () => false);
            AssertEqual(8, unsupported.Count);
            AssertTrue(unsupported.All(step => step.Outcome == SystemMemoryCleanupOutcome.Unsupported));
            return;
        }
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.WriteLine("System-operation measurement requires an elevated, disposable Windows test host.");
            return;
        }
        // This explicitly selected integration case runs in a disposable CI VM. Populate a
        // separate process, then use the real API adapter rather than mocking NT results.
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
            var before = port.Sample(); long started = Stopwatch.GetTimestamp();
            var results = await SystemMemoryCleanupService.RunAsync(port, () => false);
            var after = port.Sample(); child.Refresh(); long workingAfter = child.WorkingSet64;
            AssertEqual(8, results.Count);
            foreach (var step in results)
            {
                Console.WriteLine($"MEMORY_OPERATION\t{step.Operation}\t{step.Outcome}\t0x{step.NativeStatus:X8}\t{step.AvailableChangeBytes}\t{step.ElapsedMilliseconds}");
                AssertFalse(step.NativeStatus == unchecked((int)0xC0000004)); // ABI length mismatch
                if (step.Operation is SystemMemoryCleanupOperation.WorkingSets or SystemMemoryCleanupOperation.FileCache
                    or SystemMemoryCleanupOperation.ModifiedPages or SystemMemoryCleanupOperation.StandbyPages or SystemMemoryCleanupOperation.LowPriorityStandby)
                    AssertEqual(SystemMemoryCleanupOutcome.Succeeded, step.Outcome);
            }
            Console.WriteLine($"MEMORY_MEASUREMENT\tavailable-before={before.AvailableBytes}\tavailable-after={after.AvailableBytes}\tchild-working-before={workingBefore}\tchild-working-after={workingAfter}\telapsed-ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}");
            AssertFalse(child.HasExited);
            await child.StandardInput.WriteLineAsync("exit");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            AssertEqual(0, child.ExitCode);
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
    }

}
