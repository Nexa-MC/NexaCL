using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void ExternalPluginSdkBoundarySelfTest(string directory, string corePath, IEnumerable<MetadataReference> references)
    {
        string fixtures = Path.Combine(directory, "plugin-boundary");
        Directory.CreateDirectory(fixtures);
        string owned = Path.Combine(fixtures, "Nexa.OwnedFixture.dll");
        Emit("Nexa.OwnedFixture", owned);
        AssemblyInstrumenter.Run(owned, corePath, Path.ChangeExtension(owned, ".nfp"));
        File.Delete(owned + ".unpatched.dll");
        string sdk = Path.Combine(fixtures, "Nexa.Plugin.Hosting.dll");
        Emit("Nexa.Plugin.Hosting", sdk);
        string manifest = Path.Combine(fixtures, "Fixture.deps.json");
        if (AssemblyInstrumenter.IsExternalPluginSdk(sdk)) throw new InvalidDataException("A name alone cannot exempt an assembly.");
        File.WriteAllText(manifest, """{"libraries":{"Nexa.Plugin.Hosting/0.2.0":{"type":"project"}}}""");
        if (AssemblyInstrumenter.IsExternalPluginSdk(sdk)) throw new InvalidDataException("Owned projects must retain coverage.");
        ExpectRejected();
        File.WriteAllText(manifest, """{"libraries":{"Nexa.Plugin.Hosting/0.2.0":{"type":"package"}}}""");
        if (!AssemblyInstrumenter.IsExternalPluginSdk(sdk)) throw new InvalidDataException("Pinned SDK package identity was not recognized.");
        AssemblyInstrumenter.VerifyDirectory(fixtures);
        File.Delete(owned); File.Delete(Path.ChangeExtension(owned, ".nfp")); Emit("Nexa.OwnedFixture", owned);
        ExpectRejected();
        string unknown = Path.Combine(fixtures, "Nexa.Plugin.Unknown.dll");
        Emit("Nexa.Plugin.Unknown", unknown);
        if (AssemblyInstrumenter.IsExternalPluginSdk(unknown)) throw new InvalidDataException("The SDK prefix must not exempt unknown libraries.");
        File.Delete(sdk); File.Copy(unknown, sdk);
        if (AssemblyInstrumenter.IsExternalPluginSdk(sdk)) throw new InvalidDataException("Renamed assemblies must not acquire package identity.");

        void Emit(string name, string path)
        {
            var result = CSharpCompilation.Create(name,
                [CSharpSyntaxTree.ParseText("public static class Fixture { public static int Value() => 1; }")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(path);
            if (!result.Success) throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
        }
        void ExpectRejected()
        {
            try { AssemblyInstrumenter.VerifyDirectory(fixtures); }
            catch (InvalidDataException) { return; }
            throw new InvalidDataException("External SDK admission exempted an uninstrumented owned assembly.");
        }
    }
}
