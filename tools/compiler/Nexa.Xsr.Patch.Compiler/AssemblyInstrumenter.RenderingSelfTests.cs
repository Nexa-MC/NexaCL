using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void RenderingBoundarySelfTest(string directory, string corePath, IEnumerable<MetadataReference> references)
    {
        string fixtures = Path.Combine(directory, "rendering-boundary");
        Directory.CreateDirectory(fixtures);
        string owned = Path.Combine(fixtures, "Nexa.OwnedFixture.dll");
        Emit("Nexa.OwnedFixture", owned);
        AssemblyInstrumenter.Run(owned, corePath, Path.ChangeExtension(owned, ".nfp"));
        File.Delete(owned + ".unpatched.dll");
        string rendering = Path.Combine(fixtures, "Nexa.Xsr.Renderer.Fixture.dll");
        Emit("Nexa.Xsr.Renderer.Fixture", rendering);
        if (!AssemblyInstrumenter.IsRendering(rendering)) throw new InvalidDataException("A rendering assembly without entries was not recognized.");
        AssemblyInstrumenter.VerifyDirectory(fixtures);

        // The name selects the rule; it can neither hide entries nor exempt another assembly.
        AssemblyInstrumenter.Run(rendering, corePath, Path.ChangeExtension(rendering, ".nfp"));
        File.Delete(rendering + ".unpatched.dll");
        ExpectRejected("Rendering assemblies must not carry automatic entries.");
        File.Delete(rendering); File.Delete(Path.ChangeExtension(rendering, ".nfp"));
        Emit("Nexa.Services.Fixture", rendering);
        if (AssemblyInstrumenter.IsRendering(rendering)) throw new InvalidDataException("A file name alone cannot exempt an assembly.");
        ExpectRejected("A renamed owned assembly must retain coverage.");

        void Emit(string name, string path)
        {
            var result = CSharpCompilation.Create(name,
                [CSharpSyntaxTree.ParseText("public static class Fixture { public static int Value() => 1; }")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(path);
            if (!result.Success) throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
        }
        void ExpectRejected(string requirement)
        {
            try { AssemblyInstrumenter.VerifyDirectory(fixtures); }
            catch (InvalidDataException) { return; }
            throw new InvalidDataException(requirement);
        }
    }
}
