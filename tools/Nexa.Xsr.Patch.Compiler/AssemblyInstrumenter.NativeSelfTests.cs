using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void NativeEngineBoundarySelfTest(string directory, IEnumerable<PortableExecutableReference> references)
    {
        string fixtures = Path.Combine(directory, "native-boundary");
        Directory.CreateDirectory(fixtures);
        const string source = """
            using System.Reflection;
            using System.Runtime.InteropServices;
            [assembly: AssemblyMetadata("Nexa.NativeEngineAbi", "1")]
            public static class Exports
            {
                [UnmanagedCallersOnly(EntryPoint = "nexa_network_test_v1")]
                public static int Test() => 1;
            }
            """;
        string path = Path.Combine(fixtures, "Nexa.Native.Network.dll");
        var emit = CSharpCompilation.Create("Nexa.Native.Network", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(path);
        if (!emit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, emit.Diagnostics));
        if (!AssemblyInstrumenter.IsStandaloneNativeEngine(path)) throw new InvalidDataException("Native ABI marker was not recognized.");
        bool rejected = false;
        try { AssemblyInstrumenter.VerifyDirectory(fixtures); } catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Native ABI marker must not exempt an uninstrumented body.");
        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true }))
        {
            module.AssemblyReferences.Add(new AssemblyNameReference("Nexa.Core", new Version(1, 0, 0, 0)));
            module.Write(path);
        }
        rejected = false;
        try { AssemblyInstrumenter.IsStandaloneNativeEngine(path); } catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("A standalone engine must not acquire a Host assembly dependency.");
    }
}
