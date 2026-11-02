using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void DeterministicSymbolsSelfTest(string directory, string corePath, IEnumerable<PortableExecutableReference> references)
    {
        var source = CSharpSyntaxTree.ParseText(SourceText.From("public static class Sample { public static int Value() => 42; }", Encoding.UTF8), path: "/_/Fixture.cs");
        var compilation = CSharpCompilation.Create("SymbolFixture", [source], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        using var assembly = new MemoryStream();
        using var symbols = new MemoryStream();
        var emit = compilation.Emit(assembly, symbols, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: "SymbolFixture.pdb"));
        if (!emit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, emit.Diagnostics));
        byte[]? firstAssembly = null, firstSymbols = null;
        for (int index = 0; index < 2; index++)
        {
            string location = Path.Combine(directory, "symbols-" + index);
            Directory.CreateDirectory(location);
            string path = Path.Combine(location, "SymbolFixture.dll");
            File.WriteAllBytes(path, assembly.ToArray());
            File.WriteAllBytes(Path.ChangeExtension(path, ".pdb"), symbols.ToArray());
            AssemblyInstrumenter.Run(path, corePath, Path.ChangeExtension(path, ".nfp"));
            _ = AssemblyInstrumenter.Verify(path);
            byte[] actualAssembly = File.ReadAllBytes(path), actualSymbols = File.ReadAllBytes(Path.ChangeExtension(path, ".pdb"));
            if (index == 0) { firstAssembly = actualAssembly; firstSymbols = actualSymbols; }
            else if (!actualAssembly.AsSpan().SequenceEqual(firstAssembly) || !actualSymbols.AsSpan().SequenceEqual(firstSymbols))
                throw new InvalidDataException("Staging directories changed instrumented assembly or symbol identity.");
            if (actualAssembly.AsSpan().IndexOf(".nexa-weave-"u8) >= 0)
                throw new InvalidDataException("The instrumented debug identity leaked a random staging path.");
        }
    }
}
