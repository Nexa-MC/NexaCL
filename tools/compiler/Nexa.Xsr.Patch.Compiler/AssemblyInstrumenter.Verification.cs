using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Nexa.Xsr.Patch.Compiler;

internal sealed partial class AssemblyInstrumenter
{
    internal static int VerifyDirectory(string directory)
    {
        int assemblies = 0, methods = 0, engines = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "Nexa.*.dll").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) == "Nexa.Xsr.Patch.Compiler.dll")
                throw new InvalidDataException("The build compiler must not ship in the product runtime closure.");
            if (IsExternalPluginSdk(path)) continue;
            if (IsStandaloneNativeEngine(path)) engines++;
            methods += Verify(path); assemblies++;
        }
        if (assemblies == 0) throw new InvalidDataException("No owned product assemblies were found for coverage verification.");
        Console.WriteLine($"Automatic coverage verified: {assemblies} assemblies; {methods} executable bodies; {engines} standalone native engines verified.");
        return 0;
    }

    internal static bool IsExternalPluginSdk(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name is not ("Nexa.Plugin.Bootstrap.V1" or "Nexa.Plugin.Sdk.Abi100" or "Nexa.Plugin.Hosting" or "Nexa.Plugin.MarketDelivery")) return false;
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        if (module.Assembly.Name.Name != name) return false;
        bool package = false;
        foreach (string manifest in Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(path))!, "*.deps.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries)) continue;
            foreach (var library in libraries.EnumerateObject())
            {
                if (!library.Name.StartsWith(name + "/", StringComparison.Ordinal)) continue;
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package") return false;
                package = true;
            }
        }
        return package;
    }

    internal static bool IsStandaloneNativeEngine(string path)
    {
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        bool marked = module.Assembly.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute"
            && attribute.ConstructorArguments.Count == 2
            && attribute.ConstructorArguments[0].Value is "Nexa.NativeEngineAbi"
            && attribute.ConstructorArguments[1].Value is "1");
        if (!marked) return false;
        string? kind = module.Assembly.CustomAttributes.Where(attribute =>
            attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute"
            && attribute.ConstructorArguments.Count == 2
            && attribute.ConstructorArguments[0].Value is "Nexa.NativeEngineKind")
            .Select(attribute => attribute.ConstructorArguments[1].Value as string).SingleOrDefault();
        if (kind == "Service")
        {
            string? servicePrefix = module.Assembly.Name.Name switch
            { "Nexa.Native.Network" => "nexa_network_", "Nexa.Native.Accounts" => "nexa_accounts_", "Nexa.Native.Minecraft" => "nexa_minecraft_", "Nexa.Native.Updates" => "nexa_updates_", _ => null };
            if (servicePrefix is null || !module.AssemblyReferences.Any(reference => reference.Name == "Nexa.Core")
                || module.GetType("Nexa.Native.Instrumentation.Patch") is not null
                || module.GetType("Nexa.Core.Patching.Patch") is not null
                || module.AssemblyReferences.Any(reference => reference.Name is "Nexa.Desktop" or "Nexa.Services" or "Nexa.Services.Composition"
                    || reference.Name.StartsWith("Avalonia", StringComparison.Ordinal)
                    || reference.Name.StartsWith("Nexa.Xsr.Renderer", StringComparison.Ordinal))
                || !HasNativeExport(module, servicePrefix))
                throw new InvalidDataException("Invalid native service owner boundary: " + path);
            return false;
        }
        if (kind is not (null or "Standalone")) throw new InvalidDataException("Unknown native engine kind: " + path);
        string? prefix = module.Assembly.Name.Name switch
        {
            "Nexa.Native.Pac" => "nexa_pac_",
            "Nexa.Native.Crypto" => "nexa_crypto_",
            "Nexa.Native.Network" => "nexa_network_",
            "Nexa.Native.Updates" => "nexa_updates_",
            _ => null,
        };
        if (prefix is null
            || module.AssemblyReferences.Any(reference => reference.Name.StartsWith("Nexa.", StringComparison.Ordinal))
            || !HasNativeExport(module, prefix))
            throw new InvalidDataException("Invalid standalone native engine boundary: " + path);
        return true;
    }

    private static bool HasNativeExport(ModuleDefinition module, string prefix) =>
        module.GetTypes().SelectMany(type => type.Methods).Any(method => method.IsStatic
                && method.CustomAttributes.Any(attribute =>
                    attribute.AttributeType.FullName == "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"
                    && attribute.Fields.Any(field => field.Name == "EntryPoint" && field.Argument.Value is string entry
                        && entry.StartsWith(prefix, StringComparison.Ordinal) && entry.EndsWith("_v1", StringComparison.Ordinal))));

    internal static int Verify(string path)
    {
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        var resource = module.Resources.OfType<EmbeddedResource>().SingleOrDefault(item => item.Name == Marker)
            ?? throw new InvalidDataException("Uninstrumented product assembly: " + path);
        byte[] catalog = resource.GetResourceData();
        if (!File.ReadAllBytes(Path.ChangeExtension(path, ".nfp")).AsSpan().SequenceEqual(catalog))
            throw new InvalidDataException("Published function catalog does not match its assembly: " + path);
        int minimumBody = module.Resources.OfType<EmbeddedResource>().SingleOrDefault(item => item.Name == ScopeMarker) is { } scope
            ? ParseMinimumBody(Encoding.UTF8.GetString(scope.GetResourceData())) : 0;
        if (minimumBody != 0 && !RenderingAssembly(module.Assembly.Name.Name))
            throw new InvalidDataException("Only rendering assemblies may narrow their automatic coverage: " + path);
        using var source = new MemoryStream(catalog, writable: false);
        using var compression = new BrotliStream(source, CompressionMode.Decompress);
        using var reader = new BinaryReader(compression, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x3143504E) throw new InvalidDataException("Invalid compiled catalog.");
        int count = reader.ReadInt32();
        Dictionary<string, (string Id, byte Status, int Slot)> entries = new(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            string id = reader.ReadString(), signature = reader.ReadString();
            _ = reader.ReadString(); _ = reader.ReadByte();
            byte status = reader.ReadByte(); int slot = reader.ReadInt32();
            entries.Add(signature, (id, status, slot));
            int parameters = reader.ReadUInt16();
            for (int parameter = 0; parameter < parameters; parameter++)
            { _ = reader.ReadString(); _ = reader.ReadString(); _ = reader.ReadByte(); }
        }
        int bodies = 0;
        foreach (var method in module.GetTypes().SelectMany(type => type.Methods))
        {
            if (method.DeclaringType.Name == "__NexaPatchSites" || method.Name.StartsWith("__NexaNative_", StringComparison.Ordinal)) continue;
            string signature = Signature(method);
            if (method.DeclaringType.Name == "<Module>" && method.IsConstructor && !entries.ContainsKey(signature)) continue;
            if (!entries.Remove(signature, out var entry))
                throw new InvalidDataException("Function missing from automatic inventory: " + signature);
            if (entry.Id != Identity(module.Assembly.Name.Name, signature))
                throw new InvalidDataException("Unstable automatic function identity: " + signature);
            if (Infrastructure(method.DeclaringType))
            {
                if (entry.Status != 3) throw new InvalidDataException("Patch infrastructure must be explicitly inventoried.");
                continue;
            }
            if (RuntimeAccessor(method) || !method.HasBody)
            {
                if (entry.Status != (method.IsAbstract ? 1 : 2) || entry.Slot != -1)
                    throw new InvalidDataException("Invalid no-body function inventory: " + signature);
                continue;
            }
            bool readsSites = method.Body.Instructions.Any(item => item.OpCode == OpCodes.Ldfld && item.Operand is FieldReference field
                && field.DeclaringType.FullName == RuntimeNamespace(module) + ".PatchAssembly" && field.Name == "Enabled");
            bool enters = method.Body.Instructions.Any(item => item.OpCode == OpCodes.Call && item.Operand is MethodReference called
                && called.DeclaringType.FullName == RuntimeNamespace(module) + ".Patch" && called.Name == "Enter");
            if (entry.Status == InternalStatus)
            {
                // Recomputed from the shipped body: the scope cannot hide a major body or a half-woven one.
                if (minimumBody == 0 || entry.Slot != -1 || readsSites || enters || Major(method, minimumBody))
                    throw new InvalidDataException("Function is left without an automatic entry outside its assembly's scope: " + signature);
                continue;
            }
            if (entry.Status != 0 || entry.Slot < 0 || !readsSites || !enters)
                throw new InvalidDataException("Function is cataloged without its automatic entry: " + signature);
            if (minimumBody != 0 && !Major(method, 1))
                throw new InvalidDataException("Function carries an automatic entry its assembly's scope excludes: " + signature);
            bodies++;
        }
        if (entries.Count != 0) throw new InvalidDataException("Compiled catalog contains missing functions: " + path);
        return bodies;
    }

    private static bool RenderingAssembly(string name) => name is "Nexa.Xsr.Renderer" or "Nexa.Native.Window"
        || name.StartsWith("Nexa.Xsr.Renderer.", StringComparison.Ordinal);
}
