using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static int AutomaticSelfTest(string corePath)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-patch-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            corePath = Path.GetFullPath(corePath);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)).Append(MetadataReference.CreateFromFile(corePath));
            NativeEngineBoundarySelfTest(directory, references);
            DeterministicSymbolsSelfTest(directory, corePath, references);
            string dependency = Path.Combine(directory, "Nexa.AutomaticDependency.dll");
            var dependencyCompilation = CSharpCompilation.Create("Nexa.AutomaticDependency",
                [CSharpSyntaxTree.ParseText("public static class Unloaded { public static int Value() => 7; }")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            var dependencyEmit = dependencyCompilation.Emit(dependency);
            if (!dependencyEmit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, dependencyEmit.Diagnostics));
            AssemblyInstrumenter.Run(dependency, corePath, Path.Combine(directory, "Nexa.AutomaticDependency.nfp"));
            string vendor = Path.Combine(directory, "Nexa.UninstrumentedVendor.dll");
            var vendorEmit = CSharpCompilation.Create("Nexa.UninstrumentedVendor",
                [CSharpSyntaxTree.ParseText("public static class Vendor { public static int Value() => 14; }")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(vendor);
            if (!vendorEmit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, vendorEmit.Diagnostics));
            var compilation = CSharpCompilation.Create("AutomaticFixture", [CSharpSyntaxTree.ParseText(AutomaticFixture)],
                references.Append(MetadataReference.CreateFromFile(dependency)).Append(MetadataReference.CreateFromFile(vendor)),
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));
            string assembly = Path.Combine(directory, "AutomaticFixture.dll");
            var emit = compilation.Emit(assembly);
            if (!emit.Success) throw new InvalidDataException(string.Join(Environment.NewLine, emit.Diagnostics));
            File.Copy(corePath, Path.Combine(directory, "Nexa.Core.dll"));
            File.WriteAllText(Path.Combine(directory, "AutomaticFixture.runtimeconfig.json"), """{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
            AssemblyInstrumenter.Run(assembly, corePath, Path.Combine(directory, "AutomaticFixture.nfp"));
            int coverage = AssemblyInstrumenter.Verify(assembly);
            byte[] woven = File.ReadAllBytes(assembly);
            File.Delete(Path.Combine(directory, "AutomaticFixture.nfp"));
            AssemblyInstrumenter.Run(assembly, corePath, Path.Combine(directory, "AutomaticFixture.nfp"));
            if (!woven.AsSpan().SequenceEqual(File.ReadAllBytes(assembly)) || AssemblyInstrumenter.Verify(assembly) != coverage)
                throw new InvalidDataException("Incremental weaving must preserve IL and restore a missing catalog.");
            using (var altered = Mono.Cecil.ModuleDefinition.ReadModule(assembly, new Mono.Cecil.ReaderParameters { InMemory = true }))
            {
                var missing = new Mono.Cecil.MethodDefinition("Uncovered", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, altered.TypeSystem.Void);
                altered.Types.Single(type => type.Name == "Sample").Methods.Add(missing);
                missing.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
                string invalid = Path.Combine(directory, "Uncovered.dll"); altered.Write(invalid);
                File.Copy(Path.Combine(directory, "AutomaticFixture.nfp"), Path.Combine(directory, "Uncovered.nfp"));
                bool rejected = false;
                try { AssemblyInstrumenter.Verify(invalid); } catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new InvalidDataException("Coverage verification accepted a missing executable body.");
            }
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = directory };
            start.ArgumentList.Add(assembly);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture process did not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private const string AutomaticFixture = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using Nexa.Core.Patching;

        class Parent { public static int Count; public Parent() { Count++; } }
        class Sample : Parent {
            public int Value {get; private set;}
            public Sample(int value) { Value=value; }
            public int Add(int x,int y)=>x+y;
            public static T Echo<T>(T item)=>item;
            public static int References(ref int a,out int b) { a+=2; b=3; return a+b; }
            public static ref int Reference(ref int a)=>ref a;
            public static ref readonly int ReadonlyReference(in int a)=>ref a;
            public static T StackGeneric<T>(T a,T b) where T: allows ref struct => a;
            public static Span<int> Span(Span<int> a,Span<int> b)=>a;
            public static unsafe delegate* unmanaged[Cdecl]<int,int> Pointer(delegate* unmanaged[Cdecl]<int,int> value)=>value;
            public static unsafe delegate* unmanaged[Stdcall]<int,int> OtherPointer(delegate* unmanaged[Stdcall]<int,int> value)=>value;
            public static async Task<int> Async() { await Task.Yield(); return 42; }
            public static IEnumerable<int> Iterate() { yield return 1; yield return 2; }
            public static string Caller([CallerMemberName]string caller="")=>caller;
            public static int Throw() { try { throw new InvalidOperationException("original"); } finally { ValueFinally++; } }
            public static int ValueFinally;
            [DllImport("kernel32.dll", EntryPoint="GetCurrentProcessId")] public static extern uint NativeWindows();
            [DllImport("libc", EntryPoint="getpid")] public static extern int NativeUnix();
        }
        struct Generic<T> { private T _value; public Generic(T value) {_value=value;} public T Get()=>_value; }
        record Initialized { public int Value {get; init;} }
        static class Later { public static int Value=19; static Later() { Value=23; } }
        static class Fixture {
            static int Assertions;
            static void Check(bool value) { if(!value)throw new Exception("Assertion "+Assertions); Assertions++; }
            static PatchTarget Target(string method)=>Patch.Catalog.Single(t=>t.Signature.Contains(method,StringComparison.Ordinal));
            static unsafe void Main() {
                Check(Patch.Catalog.Any(t=>t.Signature.Contains("Unloaded::Value",StringComparison.Ordinal))); Check(Unloaded.Value()==7);
                Check(Vendor.Value()==14); Check(!Patch.Catalog.Any(t=>t.Signature.Contains("Vendor::Value",StringComparison.Ordinal)));
                var sample=new Sample(3); Check(sample.Add(1,2)==3);
                var add=Target("Sample::Add``0"); int head=0,tail=0;
                using(var h=Patch.Install(add,PatchPhase.Head,f=>head++))
                using(var a=Patch.Install(add,PatchPhase.Args,f=>f.Arguments[0]=10))
                using(var t=Patch.Install(add,PatchPhase.Tail,f=>tail++))
                using(var r=Patch.Install(add,PatchPhase.Return,f=>f.Result=(int)f.Result+100)) {Check(sample.Add(1,2)==112);Check(head==1&&tail==1);}
                Check(sample.Add(1,2)==3);
                using(var replace=Patch.Install(add,PatchPhase.Replace,f=>{f.Result=77;f.SkipOriginal=true;}))Check(sample.Add(1,2)==77);
                using(var fault=Patch.Install(add,PatchPhase.Args,f=>{f.Arguments[0]=99;throw new Exception();}))Check(sample.Add(1,2)==3);
                var ctor=Target("Sample::.ctor``0");
                using(var change=Patch.Install(ctor,PatchPhase.Args,f=>f.Arguments[0]=9))Check(new Sample(1).Value==9);
                int before=Parent.Count;
                using(var skip=Patch.Install(ctor,PatchPhase.Replace,f=>f.SkipOriginal=true)) {Check(new Sample(7).Value==0);Check(Parent.Count==before+1);}
                using(var generic=Patch.Install(Target("Sample::Echo``1"),PatchPhase.Return,f=>f.Result=f.Arguments[0])) {Check(Sample.Echo(4)==4);Check(Sample.Echo("ok")=="ok");}
                Check(new Generic<int>(8).Get()==8); Check(new Generic<string>("typed").Get()=="typed");
                using(var valueCtor=Patch.Install(Target("Generic`1::.ctor``0"),PatchPhase.Replace,f=>f.SkipOriginal=true))
                {Check(new Generic<int>(9).Get()==0);Check(new Generic<string>("ignored").Get()==null);}
                Check(new Initialized {Value=21}.Value==21);
                int value=4,output;
                using(var refs=Patch.Install(Target("Sample::References``0"),PatchPhase.Args,f=>f.Arguments[0]=10))
                using(var observe=Patch.Install(Target("Sample::References``0"),PatchPhase.Tail,f=>Check((int)f.Arguments[0]==12&&(int)f.Arguments[1]==3)))
                {Check(Sample.References(ref value,out output)==15);Check(value==12&&output==3);}
                using(var reference=Patch.Install(Target("Sample::Reference``0"),PatchPhase.Replace,f=>{f.Result=Patch.Reference(0);f.SkipOriginal=true;})) {ref int location=ref Sample.Reference(ref value);location=30;Check(value==30);}
                int[] left={1},right={2};
                Check(Sample.ReadonlyReference(in value)==30);
                using(var ro=Patch.Install(Target("Sample::ReadonlyReference``0"),PatchPhase.Replace,f=>{f.Result=Patch.Reference(0);f.SkipOriginal=true;}))Check(Sample.ReadonlyReference(in value)==30);
                using(var stack=Patch.Install(Target("Sample::StackGeneric``1"),PatchPhase.Args,f=>f.Arguments[0]=Patch.Reference(1)))Check(Sample.StackGeneric<Span<int>>(left,right)[0]==2);
                using(var span=Patch.Install(Target("Sample::Span``0"),PatchPhase.Args,f=>f.Arguments[0]=Patch.Reference(1)))Check(Sample.Span(left,right)[0]==2);
                using(var span=Patch.Install(Target("Sample::Span``0"),PatchPhase.Replace,f=>{f.Result=Patch.Reference(1);f.SkipOriginal=true;}))Check(Sample.Span(left,right)[0]==2);
                using(var span=Patch.Install(Target("Sample::Span``0"),PatchPhase.Return,f=>f.Result=Patch.DefaultValue))Check(Sample.Span(left,right).Length==0);
                using(var a=Patch.Install(Target("Sample::Span``0"),PatchPhase.Args,f=>f.Arguments[0]=Patch.Reference(1)))
                using(var r=Patch.Install(Target("Sample::Span``0"),PatchPhase.Return,f=>f.Result=f.Arguments[0]))Check(Sample.Span(left,right)[0]==2);
                using(var a=Patch.Install(Target("Sample::Span``0"),PatchPhase.Args,f=>{f.Arguments[0]=Patch.Reference(1);f.Arguments[1]=Patch.Reference(0);}))
                using(var r=Patch.Install(Target("Sample::Span``0"),PatchPhase.Replace,f=>{f.Result=f.Arguments[0];f.SkipOriginal=true;}))Check(Sample.Span(left,right)[0]==2);
                Check(Sample.Pointer(null)==null);
                using(var p=Patch.Install(Target("Sample::Pointer``0"),PatchPhase.Replace,f=>{f.Result=Patch.DefaultValue;f.SkipOriginal=true;}))Check(Sample.Pointer(null)==null);
                Check(Target("Sample::Pointer``0").Parameters[0].Type!=Target("Sample::OtherPointer``0").Parameters[0].Type);
                Check(Sample.Async().GetAwaiter().GetResult()==42);
                using(var async=Patch.Install(Target("Sample::Async``0"),PatchPhase.Replace,f=>{f.Result=Task.FromResult(12);f.SkipOriginal=true;}))Check(Sample.Async().GetAwaiter().GetResult()==12);
                Check(Sample.Iterate().Sum()==3);Check(Sample.Caller()=="Main");
                using(var later=Patch.Install(Target("Later::.cctor``0"),PatchPhase.Replace,f=>f.SkipOriginal=true))Check(Later.Value==0);
                int tails=0;
                using(var throwing=Patch.Install(Target("Sample::Throw``0"),PatchPhase.Tail,f=>tails++)) {try {Sample.Throw();}catch(InvalidOperationException e){Check(e.Message=="original");}Check(Sample.ValueFinally==1&&tails==0);}
                if(OperatingSystem.IsWindows()) {Check(Sample.NativeWindows()>0);using var native=Patch.Install(Target("Sample::NativeWindows``0"),PatchPhase.Replace,f=>{f.Result=123u;f.SkipOriginal=true;});Check(Sample.NativeWindows()==123);}
                else {Check(Sample.NativeUnix()>0);using var native=Patch.Install(Target("Sample::NativeUnix``0"),PatchPhase.Replace,f=>{f.Result=123;f.SkipOriginal=true;});Check(Sample.NativeUnix()==123);}
                Check(Patch.Catalog.Any(t=>t.Signature.Contains("MoveNext",StringComparison.Ordinal)));
                Check(Patch.Catalog.All(t=>t.Status!=PatchTargetStatus.Instrumented||t.Slot>=0));
                Console.WriteLine("Automatic instrumentation fixture passed: "+Assertions+" assertions.");
            }
        }
        """;
}
