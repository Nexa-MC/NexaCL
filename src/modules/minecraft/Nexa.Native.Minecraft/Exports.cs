using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Nexa.Native.Minecraft;
using Nexa.Native.Minecraft.Assets;
using Nexa.Native.Minecraft.Libraries;

namespace Nexa.Native.Minecraft;

internal static unsafe class Exports
{
    private static readonly NativeBufferResults Results = new(MinecraftProtocol.OutputLimit);

    [UnmanagedCallersOnly(EntryPoint = "nexa_minecraft_execute_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static long Execute(byte* input, int length, int* outputLength)
    {
        if (outputLength == null) return 0;
        *outputLength = 0;
        try
        {
            if (length < 4 || length > MinecraftProtocol.InputLimit || input == null) return 0;
            using var source = new BinaryReader(new UnmanagedMemoryStream(input, length));
            using var output = new MinecraftWireBuffer(MinecraftProtocol.OutputLimit);
            using var writer = new BinaryWriter(output);
            try
            {
                writer.Write(0);
                Dispatch(source.ReadInt32(), source, writer);
                MinecraftProtocol.End(source);
                if (output.Length > MinecraftProtocol.OutputLimit) throw new InvalidDataException("Minecraft result exceeds the wire budget.");
            }
            catch (Exception failure)
            {
                output.SetLength(0); output.Position = 0;
                writer.Write(failure switch
                {
                    ArgumentNullException => 2,
                    ArgumentException => 3,
                    FormatException => 4,
                    OverflowException => 5,
                    NotSupportedException => 6,
                    InvalidOperationException => 7,
                    _ => 1,
                });
                MinecraftProtocol.Text(writer, failure.Message);
                MinecraftProtocol.Text(writer, (failure as ArgumentException)?.ParamName);
            }
            return Results.Store(output.GetBuffer().AsMemory(0, checked((int)output.Length)), outputLength);
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_minecraft_copy_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Copy(long id, byte* output, int capacity) => Results.Copy(id, output, capacity);

    [UnmanagedCallersOnly(EntryPoint = "nexa_minecraft_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(long id) => Results.Release(id);

    private static void Dispatch(int operation, BinaryReader reader, BinaryWriter writer)
    {
        switch (operation)
        {
            case 1:
                {
                    var json = MinecraftProtocol.Json(reader) as JsonObject ?? throw new InvalidDataException("Missing version manifest.");
                    string root = MinecraftProtocol.RequiredText(reader);
                    string? instance = MinecraftProtocol.Text(reader);
                    var context = MinecraftProtocol.Context(reader);
                    bool glfw = reader.ReadBoolean();
                    MinecraftProtocol.Libraries(writer, MinecraftLibraryResolver.Resolve(new()
                    {
                        VersionJson = json,
                        MinecraftRootDirectory = root,
                        TargetInstanceDirectory = instance,
                        OperatingSystem = context.OperatingSystem,
                        OperatingSystemVersion = context.OperatingSystemVersion,
                        Is64BitArchitecture = context.Is64BitArchitecture,
                        IsArm64Architecture = context.IsArm64Architecture,
                        Features = context.Features,
                        UseSystemGlfw = glfw,
                    }));
                    break;
                }
            case 2:
                MinecraftProtocol.Text(writer, MinecraftLibraryResolver.GetCoordinatePath(
                    MinecraftProtocol.Text(reader)!, MinecraftProtocol.Text(reader)!, reader.ReadBoolean()));
                break;
            case 3:
                MinecraftProtocol.Text(writer, MinecraftLibraryResolver.GetNativeCoordinatePath(
                    MinecraftProtocol.Text(reader)!, MinecraftProtocol.Text(reader)!, MinecraftProtocol.Text(reader)!));
                break;
            case 4:
                MinecraftProtocol.Strings(writer, MinecraftClasspathPlanner.CreatePlan(new()
                {
                    Libraries = MinecraftProtocol.Libraries(reader),
                    ClasspathHeadEntries = MinecraftProtocol.Strings(reader),
                    BundledClasspathEntries = MinecraftProtocol.Strings(reader),
                    HasCleanroom = reader.ReadBoolean(),
                }).Entries);
                break;
            case 5:
            case 6:
                {
                    var json = MinecraftProtocol.Json(reader);
                    var context = MinecraftProtocol.Context(reader);
                    writer.Write(operation == 5 ? MinecraftRuleEvaluator.IsAllowed(json, context)
                        : MinecraftRuleEvaluator.Matches(json as JsonObject ?? throw new InvalidDataException("Missing rule."), context));
                    break;
                }
            case 7:
                MinecraftProtocol.Assets(writer, MinecraftAssetListResolver.GetAssetList(new()
                {
                    IndexJson = MinecraftProtocol.Json(reader) as JsonObject ?? throw new InvalidDataException("Missing asset index."),
                    MinecraftRootDirectory = MinecraftProtocol.RequiredText(reader),
                    InstanceDirectory = MinecraftProtocol.RequiredText(reader),
                }));
                break;
            default: throw new InvalidDataException("Unknown Minecraft operation.");
        }
    }
}
