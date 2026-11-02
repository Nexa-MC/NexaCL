using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static void NativeEngineBoundarySelfTest(string directory, string corePath, IEnumerable<PortableExecutableReference> references)
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
        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true }))
        {
            var marker = new CustomAttribute(module.Assembly.CustomAttributes.Single(attribute =>
                attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute").Constructor);
            marker.ConstructorArguments.Add(new(module.TypeSystem.String, "Nexa.NativeEngineKind"));
            marker.ConstructorArguments.Add(new(module.TypeSystem.String, "Service"));
            module.Assembly.CustomAttributes.Add(marker); module.Write(path);
        }
        if (AssemblyInstrumenter.IsStandaloneNativeEngine(path)) throw new InvalidDataException("A shared Core service is not a standalone engine.");
        rejected = false;
        try { AssemblyInstrumenter.VerifyDirectory(fixtures); } catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Service ownership must not exempt an uninstrumented body.");
        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true }))
        { module.AssemblyReferences.Add(new AssemblyNameReference("Nexa.Desktop", new Version(1, 0, 0, 0))); module.Write(path); }
        rejected = false;
        try { AssemblyInstrumenter.IsStandaloneNativeEngine(path); } catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("A native service owner must not acquire presentation dependencies.");

        const string sharedSource = """
            namespace Nexa.Native.Instrumentation
            { public static class Probe { public static void Tick() { } } }
            public static class Api { public static int Call(int value) => value + 1; }
            """;
        string shared = Path.Combine(fixtures, "Nexa.Native.SharedRuntimeFixture.dll");
        var sharedEmit = CSharpCompilation.Create("Nexa.Native.SharedRuntimeFixture", [CSharpSyntaxTree.ParseText(sharedSource)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(shared);
        if (!sharedEmit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, sharedEmit.Diagnostics));
        AssemblyInstrumenter.Run(shared, corePath, Path.ChangeExtension(shared, ".nfp"));
        if (AssemblyInstrumenter.Verify(shared) != 1)
            throw new InvalidDataException("Shared native services must verify Core entries while preserving native probe infrastructure.");
    }
}
