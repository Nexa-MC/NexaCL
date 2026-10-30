#if NEXA_EXTERNAL_MODULES
using System.Text.Json.Nodes;
using Nexa.Native.Minecraft;

namespace Nexa.Services.Minecraft
{
    public static class MinecraftRuleEvaluator
    {
        public static bool IsAllowed(JsonNode? rulesNode, MinecraftRuleContext context) =>
            Evaluate(5, rulesNode, context);
        public static bool Matches(JsonObject rule, MinecraftRuleContext context)
        {
            ArgumentNullException.ThrowIfNull(rule);
            return Evaluate(6, rule, context);
        }
        private static bool Evaluate(int operation, JsonNode? node, MinecraftRuleContext context) =>
            NativeMinecraftClient.Call(operation, writer =>
            {
                MinecraftProtocol.Json(writer, node); MinecraftProtocol.Context(writer, context, node, operation == 6 ? 1 : 0);
            }, reader => reader.ReadBoolean());
    }
}
namespace Nexa.Services.Minecraft.Libraries
{
    public static class MinecraftLibraryResolver
    {
        public static IReadOnlyList<MinecraftLibraryToken> Resolve(MinecraftLibraryResolutionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.MinecraftRootDirectory);
            return NativeMinecraftClient.Call(1, writer =>
            {
                MinecraftProtocol.Json(writer, request.VersionJson);
                MinecraftProtocol.Text(writer, request.MinecraftRootDirectory); MinecraftProtocol.Text(writer, request.TargetInstanceDirectory);
                MinecraftProtocol.Context(writer, new(request.OperatingSystem, request.OperatingSystemVersion,
                    request.Is64BitArchitecture, request.IsArm64Architecture, request.Features), request.VersionJson, 2);
                writer.Write(request.UseSystemGlfw);
            }, MinecraftProtocol.Libraries);
        }
        public static string GetCoordinatePath(string coordinate, string minecraftRootDirectory, bool includeMinecraftRoot = true) =>
            NativeMinecraftClient.Call(2, writer =>
            {
                MinecraftProtocol.Text(writer, coordinate); MinecraftProtocol.Text(writer, minecraftRootDirectory); writer.Write(includeMinecraftRoot);
            }, MinecraftProtocol.RequiredText);
        public static string GetNativeCoordinatePath(string coordinate, string minecraftRootDirectory, string classifier) =>
            NativeMinecraftClient.Call(3, writer =>
            {
                MinecraftProtocol.Text(writer, coordinate); MinecraftProtocol.Text(writer, minecraftRootDirectory); MinecraftProtocol.Text(writer, classifier);
            }, MinecraftProtocol.RequiredText);
    }
    public static class MinecraftClasspathPlanner
    {
        public static MinecraftClasspathPlan CreatePlan(MinecraftClasspathPlanRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return new(NativeMinecraftClient.Call(4, writer =>
            {
                MinecraftProtocol.Libraries(writer, request.Libraries); MinecraftProtocol.Strings(writer, request.ClasspathHeadEntries);
                MinecraftProtocol.Strings(writer, request.BundledClasspathEntries); writer.Write(request.HasCleanroom);
            }, MinecraftProtocol.Strings));
        }
    }
}
#endif
