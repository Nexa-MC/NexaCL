#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Crash;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

internal static class NativeMinecraftReadRouters
{
    internal static void Register(XsrQueryRouterBuilder queries, NativeMinecraftClient minecraft)
    {
        queries.Register<MinecraftVersionsQuery, IReadOnlyList<MinecraftVersionDescriptor>>(MinecraftRouteIds.VersionsRead,
            async (query, token) =>
            {
                try { return XsrResult.Success(await minecraft.VersionsAsync(query.MinecraftRootDirectory, token).ConfigureAwait(false)); }
                catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
                { return XsrResult.Failure<IReadOnlyList<MinecraftVersionDescriptor>>(MinecraftErrors.InvalidRequest(error.Message)); }
            });
        queries.Register<MinecraftInstancesQuery, IReadOnlyList<MinecraftInstanceDescriptor>>(MinecraftRouteIds.InstancesRead,
            async (query, token) =>
            {
                try { return XsrResult.Success(await minecraft.InstancesAsync(query.MinecraftRootDirectory, token: token).ConfigureAwait(false)); }
                catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
                { return XsrResult.Failure<IReadOnlyList<MinecraftInstanceDescriptor>>(MinecraftErrors.InvalidRequest(error.Message)); }
            });
        queries.Register<MinecraftCrashAnalyzeQuery, MinecraftLaunchFaultReport>(MinecraftRouteIds.CrashAnalyze,
            async (query, token) => XsrResult.Success(await minecraft.AnalyzeCrashAsync(query.Evidence, query.Stage, query.LastClassName, token).ConfigureAwait(false)));
    }
}
#endif
