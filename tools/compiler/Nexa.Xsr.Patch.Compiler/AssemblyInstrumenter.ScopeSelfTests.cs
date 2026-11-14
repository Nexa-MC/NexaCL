using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void ScopedCoverageSelfTest(string directory, string corePath, IEnumerable<PortableExecutableReference> references)
    {
        const int minimumBody = 256;
        string fixtures = Path.Combine(directory, "scoped-coverage");
        Directory.CreateDirectory(fixtures);
        File.Copy(corePath, Path.Combine(fixtures, "Nexa.Core.dll"));
        string source = ScopedFixture.Replace("/*MAJOR*/", string.Concat(Enumerable.Repeat("value = value * 31 + 7; value ^= value >> 3; ", 24)), StringComparison.Ordinal);
        string assembly = EmitScopedFixture(fixtures, "Nexa.Xsr.Renderer.ScopeFixture", source, references);
        string catalog = Path.ChangeExtension(assembly, ".nfp");
        AssemblyInstrumenter.Run(assembly, corePath, catalog, minimumBody: minimumBody);
        int major = AssemblyInstrumenter.Verify(assembly);
        byte[] woven = File.ReadAllBytes(assembly);
        AssemblyInstrumenter.Run(assembly, corePath, catalog, minimumBody: minimumBody);
        if (!woven.AsSpan().SequenceEqual(File.ReadAllBytes(assembly)))
            throw new InvalidDataException("Incremental scoped weaving must preserve IL.");

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = fixtures };
        start.ArgumentList.Add(assembly);
        using (var process = Process.Start(start) ?? throw new InvalidOperationException("Scoped fixture process did not start."))
        {
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidDataException("Scoped instrumentation fixture failed.");
        }

        // The scope is part of the weaving input: dropping it must weave the clean assembly again.
        AssemblyInstrumenter.Run(assembly, corePath, catalog);
        if (AssemblyInstrumenter.Verify(assembly) <= major) throw new InvalidDataException("A scope must narrow coverage, not replace it.");
        AssemblyInstrumenter.Run(assembly, corePath, catalog, minimumBody: minimumBody);
        if (AssemblyInstrumenter.Verify(assembly) != major) throw new InvalidDataException("Restoring a scope must restore its coverage.");

        using (var altered = ModuleDefinition.ReadModule(assembly, new ReaderParameters { InMemory = true }))
        {
            var declared = altered.Resources.OfType<EmbeddedResource>().Single(resource => resource.Name == AssemblyInstrumenter.ScopeMarker);
            altered.Resources.Remove(declared);
            altered.Resources.Add(new EmbeddedResource(AssemblyInstrumenter.ScopeMarker, ManifestResourceAttributes.Private, Encoding.UTF8.GetBytes("1")));
            string invalid = Path.Combine(fixtures, "Understated.dll"); altered.Write(invalid);
            File.Copy(catalog, Path.ChangeExtension(invalid, ".nfp"));
            bool rejected = false;
            try { AssemblyInstrumenter.Verify(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("A declared scope must not hide a body it does not exclude.");
        }

        string service = EmitScopedFixture(fixtures, "Nexa.Services.ScopeFixture", source, references);
        AssemblyInstrumenter.Run(service, corePath, Path.ChangeExtension(service, ".nfp"), minimumBody: minimumBody);
        bool outside = false;
        try { AssemblyInstrumenter.Verify(service); } catch (InvalidDataException) { outside = true; }
        if (!outside) throw new InvalidDataException("Only rendering assemblies may narrow their automatic coverage.");
    }

    private static string EmitScopedFixture(string directory, string name, string source, IEnumerable<PortableExecutableReference> references)
    {
        string assembly = Path.Combine(directory, name + ".dll");
        var emit = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release)).Emit(assembly);
        if (!emit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, emit.Diagnostics));
        File.WriteAllText(Path.Combine(directory, name + ".runtimeconfig.json"), """{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        return assembly;
    }

    // /*MAJOR*/ expands to straight-line statements that put a body well above the minimum.
    private const string ScopedFixture = """
        using System;
        using System.Linq;
        using System.Threading.Tasks;
        using Nexa.Core.Patching;

        readonly record struct ScopePoint(int X, int Y);
        struct ScopeSize { public int Width; public ScopeSize(int value) { /*MAJOR*/ Width = value; } }
        class ScopeSurface {
            public int Width { get; set; }
            public int Minor(int value) => value + Width;
            public int Major(int value) { /*MAJOR*/ return value + Width; }
            public static async Task<int> MajorAsync(int value) { await Task.Yield(); /*MAJOR*/ return value; }
            public static int Closure(int seed) { Func<int, int> body = value => { /*MAJOR*/ return value; }; return body(seed); }
        }
        static class ScopeFixture {
            static int Assertions;
            static void Check(bool value) { if (!value) throw new Exception("Assertion " + Assertions); Assertions++; }
            static PatchTarget Target(string method) => Patch.Catalog.Single(t => t.Signature.Contains(method, StringComparison.Ordinal));
            static bool Rejected(PatchTarget target) {
                try { Patch.Install(target, PatchPhase.Head, f => { }).Dispose(); return false; }
                catch (ArgumentException) { return true; }
            }
            static void Main() {
                var surface = new ScopeSurface { Width = 2 };
                var minor = Target("ScopeSurface::Minor``0");
                Check(minor.Status == PatchTargetStatus.Internal && minor.Slot == -1 && Rejected(minor));
                Check(Target("ScopeSurface::get_Width``0").Status == PatchTargetStatus.Internal);
                Check(Target("ScopeSize::.ctor``0").Status == PatchTargetStatus.Internal);
                Check(Target("ScopePoint::GetHashCode``0").Status == PatchTargetStatus.Internal);
                // Lambdas and state machines are nested synthesized types; other assemblies keep their own scope.
                var synthesized = Patch.Catalog.Where(t => t.Signature.Contains("ScopeSurface/", StringComparison.Ordinal)
                    || t.Signature.Contains("ScopeFixture/", StringComparison.Ordinal)).ToArray();
                Check(synthesized.Length != 0 && synthesized.All(t => t.Status == PatchTargetStatus.Internal));
                Check(surface.Minor(1) == 3 && new ScopeSize(3).Width == new ScopeSize(3).Width
                    && new ScopePoint(1, 2).GetHashCode() == new ScopePoint(1, 2).GetHashCode());
                var major = Target("ScopeSurface::Major``0");
                Check(major.Status == PatchTargetStatus.Instrumented && major.Slot >= 0);
                int original = surface.Major(5);
                using (Patch.Install(major, PatchPhase.Replace, f => { f.Result = 77; f.SkipOriginal = true; })) Check(surface.Major(5) == 77);
                Check(surface.Major(5) == original);
                var majorAsync = Target("ScopeSurface::MajorAsync``0");
                Check(majorAsync.Status == PatchTargetStatus.Instrumented);
                int awaited = ScopeSurface.MajorAsync(5).GetAwaiter().GetResult();
                using (Patch.Install(majorAsync, PatchPhase.Replace, f => { f.Result = Task.FromResult(12); f.SkipOriginal = true; }))
                    Check(ScopeSurface.MajorAsync(5).GetAwaiter().GetResult() == 12);
                Check(ScopeSurface.MajorAsync(5).GetAwaiter().GetResult() == awaited && ScopeSurface.Closure(5) == awaited);
                Console.WriteLine("Scoped instrumentation fixture passed: " + Assertions + " assertions.");
            }
        }
        """;
}
