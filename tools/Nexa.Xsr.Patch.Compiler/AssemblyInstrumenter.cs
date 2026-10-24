using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Nexa.Xsr.Patch.Compiler;

internal sealed partial class AssemblyInstrumenter
{
    private const string Marker = "Nexa.FunctionPatches.Catalog.v1";
    private const string AutomaticAbi = "Nexa.AutomaticFunctionPatches";
    private readonly ModuleDefinition _module;
    private readonly ModuleDefinition _core;
    private readonly TypeReference _assemblyType, _frameType;
    private readonly MethodReference _register, _enter, _reference, _default, _originalReturn;
    private readonly MethodReference _argument, _arguments, _argumentSource, _returnSource, _result, _complete, _shouldSkip;
    private readonly FieldReference _enabled, _evaluating;
    private FieldDefinition _sites = null!;
    private sealed record Descriptor(MethodDefinition Method, string Id, string Signature, byte Status, int Slot);

    private AssemblyInstrumenter(ModuleDefinition module, ModuleDefinition core)
    {
        _module = module; _core = core;
        _assemblyType = ImportType("PatchAssembly"); _frameType = ImportType("PatchFrame");
        _register = ImportMethod("Patch", "Register"); _enter = ImportMethod("Patch", "Enter");
        _reference = ImportMethod("Patch", "Reference"); _default = ImportMethod("Patch", "get_DefaultValue");
        _originalReturn = ImportMethod("Patch", "get_OriginalReturn");
        _argument = ImportMethod("PatchFrame", "Argument"); _argumentSource = ImportMethod("PatchFrame", "ArgumentSource");
        _arguments = ImportMethod("PatchFrame", "get_Arguments");
        _returnSource = ImportMethod("PatchFrame", "ReturnSource"); _result = ImportMethod("PatchFrame", "get_Result");
        _complete = ImportMethod("PatchFrame", "Complete"); _shouldSkip = ImportMethod("PatchFrame", "ShouldSkip");
        _enabled = module.ImportReference(CoreType("PatchAssembly").Fields.Single(field => field.Name == "Enabled"));
        _evaluating = module.ImportReference(CoreType("Patch").Fields.Single(field => field.Name == "Evaluating"));
    }

    internal static int Run(string assemblyPath, string corePath, string catalogPath, string? references = null)
    {
        assemblyPath = Path.GetFullPath(assemblyPath); corePath = Path.GetFullPath(corePath);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)); resolver.AddSearchDirectory(Path.GetDirectoryName(corePath));
        resolver.AddSearchDirectory(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        if (references is not null)
            foreach (var directory in File.ReadLines(references).Where(File.Exists).Select(path => Path.GetDirectoryName(Path.GetFullPath(path))!).Distinct(StringComparer.Ordinal))
                resolver.AddSearchDirectory(directory);
        string pdb = Path.ChangeExtension(assemblyPath, ".pdb");
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(AssemblyInstrumenter).Assembly.Location)));
        string sourceAssembly = assemblyPath;
        using (var inspect = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { InMemory = true, AssemblyResolver = resolver }))
        {
            if (inspect.Resources.OfType<EmbeddedResource>().FirstOrDefault(resource => resource.Name == Marker + ".Compiler") is { } previous)
            {
                if (Encoding.UTF8.GetString(previous.GetResourceData()) == fingerprint)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!);
                    File.WriteAllBytes(catalogPath, inspect.Resources.OfType<EmbeddedResource>().Single(resource => resource.Name == Marker).GetResourceData());
                    return 0;
                }
                sourceAssembly = assemblyPath + ".unpatched.dll";
                if (!File.Exists(sourceAssembly)) throw new InvalidDataException("NXRP: clean the intermediate assembly before upgrading its instrumentation compiler.");
            }
            else
            {
                File.Copy(assemblyPath, assemblyPath + ".unpatched.dll", overwrite: true);
                if (File.Exists(pdb)) File.Copy(pdb, Path.ChangeExtension(assemblyPath + ".unpatched.dll", ".pdb"), overwrite: true);
            }
        }
        using var module = ModuleDefinition.ReadModule(sourceAssembly, new ReaderParameters
        { InMemory = true, AssemblyResolver = resolver, ReadSymbols = File.Exists(Path.ChangeExtension(sourceAssembly, ".pdb")), SymbolReaderProvider = new DefaultSymbolReaderProvider(false) });
        using var core = assemblyPath == corePath ? null : ModuleDefinition.ReadModule(corePath, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var instrumenter = new AssemblyInstrumenter(module, core ?? module);
        byte[] catalog = instrumenter.Instrument();
        module.Resources.Add(new EmbeddedResource(Marker + ".Compiler", ManifestResourceAttributes.Private, Encoding.UTF8.GetBytes(fingerprint)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!);
        File.WriteAllBytes(catalogPath, catalog);
        string temporary = Path.Combine(Path.GetDirectoryName(assemblyPath)!, ".nexa-weave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string staged = Path.Combine(temporary, Path.GetFileName(assemblyPath));
        try
        {
            module.Write(staged, new WriterParameters { WriteSymbols = module.HasSymbols, DeterministicMvid = true });
            module.Dispose();
            File.Move(staged, assemblyPath, overwrite: true);
            if (File.Exists(Path.ChangeExtension(staged, ".pdb"))) File.Move(Path.ChangeExtension(staged, ".pdb"), pdb, overwrite: true);
        }
        finally { Directory.Delete(temporary, recursive: true); }
        return 0;
    }

    private TypeDefinition CoreType(string name) => _core.GetType("Nexa.Core.Patching." + name);
    private TypeReference ImportType(string name) => _module.ImportReference(CoreType(name));
    private MethodReference ImportMethod(string type, string name) => _module.ImportReference(CoreType(type).Methods.Single(method => method.Name == name));

    private byte[] Instrument()
    {
        var methods = _module.GetTypes().SelectMany(type => type.Methods).ToArray();
        foreach (var method in methods.Where(method => method.IsPInvokeImpl)) CreateNativeFacade(method);
        int slot = 0;
        var descriptors = methods.Select(method =>
        {
            string signature = Signature(method);
            byte status = Infrastructure(method.DeclaringType) ? (byte)3
                : method.IsAbstract ? (byte)1 : method.HasBody ? (byte)0 : (byte)2;
            string id = Identity(_module.Assembly.Name.Name, signature);
            return new Descriptor(method, id, signature, status, -1);
        }).OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => item with { Slot = item.Status == 0 ? slot++ : -1 }).ToArray();
        if (descriptors.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != descriptors.Length)
            throw new InvalidDataException("Automatic patch identity collision.");
        byte[] catalog = Catalog(descriptors);
        CreateSites(catalog);
        foreach (var descriptor in descriptors.Where(item => item.Status == 0)) Weave(descriptor.Method, descriptor.Slot);
        _module.Resources.Add(new EmbeddedResource(Marker, ManifestResourceAttributes.Private, catalog));
        Console.WriteLine($"NXRP: {_module.Assembly.Name.Name}: {slot} executable bodies instrumented; {descriptors.Length - slot} declarations inventoried.");
        return catalog;
    }

    private static string Signature(MethodDefinition method) => (method.IsStatic ? "static " : "instance ")
        + Key(method.DeclaringType) + "::" + method.Name + "``" + method.GenericParameters.Count
        + "(" + string.Join(',', method.Parameters.Select(parameter => Key(parameter.ParameterType))) + ")->" + Key(method.ReturnType);
    private static string Identity(string assembly, string signature) => "function.auto."
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(assembly + "|" + signature)).AsSpan(0, 16));
    private static string Key(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        ByReferenceType reference => Key(reference.ElementType) + "&",
        PointerType pointer => Key(pointer.ElementType) + "*",
        FunctionPointerType pointer => "fnptr[" + (int)pointer.CallingConvention + "," + pointer.HasThis + "," + pointer.ExplicitThis
            + "](" + string.Join(',', pointer.Parameters.Select(parameter => Key(parameter.ParameterType))) + ")->" + Key(pointer.ReturnType),
        ArrayType array => Key(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]",
        GenericInstanceType generic => generic.ElementType.FullName + "<" + string.Join(',', generic.GenericArguments.Select(Key)) + ">",
        RequiredModifierType modifier => "modreq(" + Key(modifier.ModifierType) + ")" + Key(modifier.ElementType),
        OptionalModifierType modifier => "modopt(" + Key(modifier.ModifierType) + ")" + Key(modifier.ElementType),
        _ => type.FullName,
    };

    private static TypeReference Unmodified(TypeReference type) => type switch
    {
        RequiredModifierType required => Unmodified(required.ElementType),
        OptionalModifierType optional => Unmodified(optional.ElementType),
        _ => type,
    };
    private static bool ByReference(TypeReference type) => Unmodified(type) is ByReferenceType;
    private static TypeReference ValueType(TypeReference type) => Unmodified(type) is ByReferenceType reference ? Unmodified(reference.ElementType) : Unmodified(type);
    private static string ShapeKey(TypeReference type) => Unmodified(type) is ByReferenceType reference ? ShapeKey(reference.ElementType) + "&" : Key(Unmodified(type));
    private static bool Infrastructure(TypeDefinition type)
    {
        if (type.Module.Assembly.Name.Name != "Nexa.Core") return false;
        for (var current = type; current is not null; current = current.DeclaringType)
            if (current.Namespace == "Nexa.Core.Patching" || current.Namespace.StartsWith("Nexa.Core.Patching.", StringComparison.Ordinal)) return true;
        return false;
    }
    private static bool StackOnly(TypeReference type)
    {
        type = ValueType(type);
        if (type is PointerType or FunctionPointerType || type.MetadataType == MetadataType.TypedByReference) return true;
        if (type is GenericParameter parameter) return ((int)parameter.Attributes & 0x20) != 0;
        try { return type.Resolve()?.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") == true; }
        catch (AssemblyResolutionException error) { throw new InvalidDataException("Cannot prove patch argument shape: " + type.FullName, error); }
    }
    private static byte Traits(TypeReference type, bool output = false) => (byte)((ValueType(type).IsValueType ? 1 : 0)
        | (ByReference(type) ? 2 : 0) | (StackOnly(type) ? 4 : 0) | (output ? 8 : 0) | (ValueType(type) is PointerType or FunctionPointerType ? 16 : 0));

    private static byte[] Catalog(Descriptor[] descriptors)
    {
        using var output = new MemoryStream();
        using (var compression = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new BinaryWriter(compression, Encoding.UTF8))
        {
            writer.Write(0x3143504Eu); writer.Write(descriptors.Length);
            foreach (var descriptor in descriptors)
            {
                var method = descriptor.Method;
                writer.Write(descriptor.Id); writer.Write(descriptor.Signature); writer.Write(ShapeKey(method.ReturnType));
                writer.Write(Traits(method.ReturnType)); writer.Write(descriptor.Status); writer.Write(descriptor.Slot);
                writer.Write(checked((ushort)method.Parameters.Count));
                foreach (var parameter in method.Parameters)
                { writer.Write(parameter.Name ?? ""); writer.Write(ShapeKey(parameter.ParameterType)); writer.Write(Traits(parameter.ParameterType, parameter.IsOut)); }
            }
        }
        return output.ToArray();
    }

    private void CreateSites(byte[] catalog)
    {
        if (!AutomaticAssembly(_module.Assembly))
        {
            var attributeType = new TypeReference("System.Reflection", "AssemblyMetadataAttribute", _module, _module.TypeSystem.Object.Scope);
            var constructor = new MethodReference(".ctor", _module.TypeSystem.Void, attributeType) { HasThis = true };
            constructor.Parameters.Add(new(_module.TypeSystem.String)); constructor.Parameters.Add(new(_module.TypeSystem.String));
            var attribute = new CustomAttribute(constructor);
            attribute.ConstructorArguments.Add(new(_module.TypeSystem.String, AutomaticAbi));
            attribute.ConstructorArguments.Add(new(_module.TypeSystem.String, "1")); _module.Assembly.CustomAttributes.Add(attribute);
        }
        var type = new TypeDefinition("", "__NexaPatchSites", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, _module.TypeSystem.Object);
        _module.Types.Add(type);
        _sites = new("Assembly", FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly, _assemblyType);
        type.Fields.Add(_sites);
        var initialize = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, _module.TypeSystem.Void);
        type.Methods.Add(initialize);
        var il = initialize.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, _module.Assembly.Name.Name); il.Emit(OpCodes.Ldstr, Convert.ToBase64String(catalog));
        il.Emit(OpCodes.Call, _register); il.Emit(OpCodes.Stsfld, _sites); il.Emit(OpCodes.Ret);
        // Direct static edges register referenced modules before READY, without reflection.
        var ensure = new MethodDefinition("EnsureRegistered", MethodAttributes.Public | MethodAttributes.Static, _module.TypeSystem.Void);
        type.Methods.Add(ensure);
        var ensureIl = ensure.Body.GetILProcessor(); ensureIl.Emit(OpCodes.Ldsfld, _sites); ensureIl.Emit(OpCodes.Pop); ensureIl.Emit(OpCodes.Ret);
        var moduleType = _module.Types.Single(item => item.Name == "<Module>");
        var moduleInitializer = moduleType.Methods.FirstOrDefault(method => method.Name == ".cctor");
        if (moduleInitializer is null)
        {
            moduleInitializer = new(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, _module.TypeSystem.Void);
            moduleType.Methods.Add(moduleInitializer); moduleInitializer.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        var processor = moduleInitializer.Body.GetILProcessor(); var first = moduleInitializer.Body.Instructions[0];
        foreach (var reference in _module.AssemblyReferences.Where(reference => AutomaticAssembly(
            reference.Name == _core.Assembly.Name.Name ? _core.Assembly : _module.AssemblyResolver.Resolve(reference))).OrderBy(reference => reference.Name, StringComparer.Ordinal))
        {
            var dependency = new TypeReference("", "__NexaPatchSites", _module, reference);
            processor.InsertBefore(first, Instruction.Create(OpCodes.Call, new MethodReference("EnsureRegistered", _module.TypeSystem.Void, dependency)));
        }
        processor.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, _sites)); processor.InsertBefore(first, Instruction.Create(OpCodes.Pop));
    }

    private static bool AutomaticAssembly(AssemblyDefinition assembly) => assembly.CustomAttributes.Any(attribute =>
        attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && attribute.ConstructorArguments.Count == 2
        && attribute.ConstructorArguments[0].Value is AutomaticAbi && attribute.ConstructorArguments[1].Value is "1");

    private static void CreateNativeFacade(MethodDefinition method)
    {
        var native = new MethodDefinition("__NexaNative_" + method.MetadataToken.ToInt32(),
            (method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Private, method.ReturnType)
        { PInvokeInfo = method.PInvokeInfo, ImplAttributes = method.ImplAttributes };
        foreach (var parameter in method.Parameters)
        {
            var copy = new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType) { MarshalInfo = parameter.MarshalInfo };
            native.Parameters.Add(copy);
        }
        native.MethodReturnType.MarshalInfo = method.MethodReturnType.MarshalInfo;
        foreach (var attribute in method.CustomAttributes.Where(attribute => attribute.AttributeType.FullName is
            "System.Runtime.InteropServices.SuppressGCTransitionAttribute" or "System.Runtime.InteropServices.UnmanagedCallConvAttribute"
            or "System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute"))
            native.CustomAttributes.Add(attribute);
        method.DeclaringType.Methods.Add(native);
        method.PInvokeInfo = null; method.IsPInvokeImpl = false;
        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        method.Body = new MethodBody(method);
        var il = method.Body.GetILProcessor();
        foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, native); il.Emit(OpCodes.Ret);
    }
}
