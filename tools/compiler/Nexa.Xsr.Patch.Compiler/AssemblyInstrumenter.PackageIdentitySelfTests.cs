using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void ExternalPackageIdentitySelfTest(string directory, string corePath, IEnumerable<MetadataReference> references)
    {
        string fixtures = Path.Combine(directory, "package-identity"); Directory.CreateDirectory(fixtures);
        string owned = Path.Combine(fixtures, "Nexa.OwnedFixture.dll"); Emit("Nexa.OwnedFixture", owned);
        AssemblyInstrumenter.Run(owned, corePath, Path.ChangeExtension(owned, ".nfp")); File.Delete(owned + ".unpatched.dll");
        AssemblyInstrumenter.VerifyDirectory(fixtures);
        string external = Path.Combine(fixtures, "Nexa.ExternalFixture.dll"); Emit("Nexa.ExternalFixture", external);
        foreach (string kind in new[] { "project", "package" })
        {
            File.WriteAllText(Path.Combine(fixtures, "Fixture.deps.json"), "{\"libraries\":{\"Nexa.ExternalFixture/1.0.0\":{\"type\":\"" + kind + "\"}}}");
            bool rejected = false;
            try { AssemblyInstrumenter.VerifyDirectory(fixtures); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("A dependency manifest cannot exempt an uninstrumented product identity.");
        }
        File.Delete(external); AssemblyInstrumenter.VerifyDirectory(fixtures);
        void Emit(string name, string path)
        {
            var result = CSharpCompilation.Create(name,
                [CSharpSyntaxTree.ParseText("public static class Fixture { public static int Value() => 1; }")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(path);
            if (!result.Success) throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
        }
    }
}
