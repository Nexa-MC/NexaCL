using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Assets;
using Nexa.Services.Minecraft.Libraries;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static unsafe void NativeMinecraftWorks()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexa-native-minecraft-fixture");
        var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["DEMO"] = true };
        var context = MinecraftRuleContext.From(MinecraftLibraryOperatingSystem.Linux, "6.1", true, false, features);
        var rules = JsonNode.Parse("""[{"action":"allow","os":{"name":"linux"},"features":{"demo":true}}]""");
        AssertEqual(true, MinecraftRuleEvaluator.IsAllowed(rules, context));
        AssertEqual(false, MinecraftRuleEvaluator.IsAllowed(rules, context with { Features = new Dictionary<string, bool>() }));
        AssertEqual(true, MinecraftRuleEvaluator.IsAllowed(null, context));
        AssertEqual(false, MinecraftRuleEvaluator.Matches(JsonNode.Parse("""{"os":{"version":"["}}""")!.AsObject(), context));
        var version = JsonNode.Parse("""{"libraries":[{"name":"example:lib:1","rules":[{"action":"allow","features":{"demo":true}}]},{"name":"../bad:escape:1"}]}""")!.AsObject();
        var libraries = MinecraftLibraryResolver.Resolve(new()
        {
            VersionJson = version, MinecraftRootDirectory = root, OperatingSystem = context.OperatingSystem,
            Features = features, Is64BitArchitecture = true,
        });
        AssertEqual(1, libraries.Count);
        AssertEqual(Path.Combine(root, "libraries", "example", "lib", "1", "lib-1.jar"), libraries[0].LocalPath);
        AssertEqual(Path.Combine("example", "lib", "1", "lib-1.jar"), MinecraftLibraryResolver.GetCoordinatePath("example:lib:1", "", false));
        AssertEqual(Path.Combine(root, "libraries", "example", "lib", "1", "lib-1-natives-linux.jar"),
            MinecraftLibraryResolver.GetNativeCoordinatePath("example:lib:1", root, "natives-linux"));
        var plan = MinecraftClasspathPlanner.CreatePlan(new()
        {
            Libraries = [libraries[0], libraries[0]], ClasspathHeadEntries = [libraries[0].LocalPath],
        });
        AssertEqual(1, plan.Entries.Count);
        Reject(() => MinecraftLibraryResolver.GetCoordinatePath("../escape:lib:1", root));
        var index = JsonNode.Parse("""{"objects":{"sounds/test.ogg":{"hash":"abcdef","size":12}}}""")!.AsObject();
        var assets = MinecraftAssetListResolver.GetAssetList(new() { IndexJson = index, MinecraftRootDirectory = root, InstanceDirectory = root });
        AssertEqual(Path.Combine(root, "assets", "objects", "ab", "abcdef"), assets[0].LocalPath);
        AssertEqual(12L, assets[0].Size);
        index["objects"] = JsonNode.Parse("""{"../escape":{"hash":"abcdef","size":1}}""");
        Reject(() => MinecraftAssetListResolver.GetAssetList(new() { IndexJson = index, MinecraftRootDirectory = root, InstanceDirectory = root }));
        try
        {
            MinecraftAssetListResolver.GetAssetList(new() { IndexJson = new(), MinecraftRootDirectory = root, InstanceDirectory = root });
            throw new InvalidOperationException("Missing asset objects accepted.");
        }
        catch (FormatException) { }
        Parallel.For(0, 32, _ => AssertEqual(true, MinecraftRuleEvaluator.IsAllowed(rules, context)));
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-minecraft" + suffix));
        var execute = (delegate* unmanaged[Cdecl]<byte*, int, int*, long>)NativeLibrary.GetExport(library, "nexa_minecraft_execute_v1");
        var copy = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_minecraft_copy_v1");
        var release = (delegate* unmanaged[Cdecl]<long, void>)NativeLibrary.GetExport(library, "nexa_minecraft_release_v1");
        int size = 7;
        AssertEqual(0L, execute(null, 4, &size)); AssertEqual(0, size);
        byte* request = stackalloc byte[4]; new Span<byte>(request, 4).Fill(0xff);
        long id = execute(request, 4, &size);
        if (id <= 0 || size < 4) throw new InvalidOperationException("Missing error envelope.");
        try
        {
            byte[] output = new byte[size];
            fixed (byte* buffer = output)
            {
                AssertEqual(-1, copy(id, buffer, size - 1)); AssertEqual(0, copy(id, buffer, size));
                if (System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(output) == 0)
                    throw new InvalidOperationException("Unknown operation succeeded.");
            }
        }
        finally { release(id); release(id); }
        AssertEqual(-1, copy(id, request, 4));
        Console.WriteLine("Native Minecraft caller: rules/comparer semantics, libraries, classpath, assets, errors and result retirement passed.");
    }
}
