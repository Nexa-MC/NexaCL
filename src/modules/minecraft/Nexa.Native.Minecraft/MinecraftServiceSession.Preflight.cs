using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private async ValueTask<XsrResult> BeforeProcessStartAsync(CancellationToken token)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.BeforeProcessStart);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    private async Task<CapabilityPreflightReport> EvaluatePreflightAsync(string root, string instance, MinecraftLaunchPlan plan, CancellationToken token)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.PreflightEvaluate);
        request.String(root); request.String(instance); request.MinecraftLaunchPlan(plan);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var report = reader.CapabilityPreflightReport() ?? throw new InvalidDataException("Missing preflight report.");
        reader.End(); return report;
    }
}
