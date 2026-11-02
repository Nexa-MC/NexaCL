using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static int AutomaticBenchmark(string corePath)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-patch-benchmark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            corePath = Path.GetFullPath(corePath);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("AutomaticBenchmark", [CSharpSyntaxTree.ParseText(BenchmarkSource)], references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));
            string assembly = Path.Combine(directory, "AutomaticBenchmark.dll");
            var result = compilation.Emit(assembly);
            if (!result.Success) throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
            File.Copy(corePath, Path.Combine(directory, "Nexa.Core.dll"));
            File.WriteAllText(Path.Combine(directory, "AutomaticBenchmark.runtimeconfig.json"), """{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
            var baseline = MeasureAutomatic(assembly);
            AssemblyInstrumenter.Run(assembly, corePath, Path.Combine(directory, "AutomaticBenchmark.nfp"));
            var instrumented = MeasureAutomatic(assembly);
            if (baseline.Allocations != 0 || instrumented.Allocations != 0 || baseline.Checksum != instrumented.Checksum)
                throw new InvalidDataException("Automatic no-program path changed results or allocated memory.");
            double scale = 1_000_000_000d / Stopwatch.Frequency / 10_000_000;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Automatic no-program benchmark (10M non-inlined calls, managed): baseline={baseline.Ticks * scale:F2} ns/call; instrumented={instrumented.Ticks * scale:F2} ns/call; allocations=0 bytes. Timing is informational; correctness/allocation are enforced."));
            return 0;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (long Ticks, long Allocations, long Checksum) MeasureAutomatic(string assembly)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly); start.Environment["DOTNET_TieredCompilation"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Benchmark process did not start.");
        string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidDataException("Automatic benchmark failed: " + error);
        string[] values = output.Trim().Split(',');
        return (long.Parse(values[0], CultureInfo.InvariantCulture), long.Parse(values[1], CultureInfo.InvariantCulture), long.Parse(values[2], CultureInfo.InvariantCulture));
    }

    private const string BenchmarkSource = """
        using System;
        using System.Diagnostics;
        using System.Runtime.CompilerServices;
        static class Benchmark {
            static long Checksum;
            [MethodImpl(MethodImplOptions.NoInlining)] static int Sum(int a,int b)=>a+b;
            static void Main() {
                for(int i=0;i<100000;i++) Checksum+=Sum(i,2);
                GC.Collect(); long before=GC.GetAllocatedBytesForCurrentThread();
                long start=Stopwatch.GetTimestamp(), total=0;
                for(int i=0;i<10000000;i++) total+=Sum(i,2);
                long elapsed=Stopwatch.GetTimestamp()-start, allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Checksum=total; Console.WriteLine(elapsed+","+allocated+","+Checksum);
            }
        }
        """;
}
