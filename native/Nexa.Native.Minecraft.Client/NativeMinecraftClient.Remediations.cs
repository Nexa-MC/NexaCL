using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Launch;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient : IMinecraftRemediationPort
{
    public async Task<RemediationResult> RemediateAsync(RemediationRequest request, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.Remediation, writer => writer.RemediationRequest(request), cancellationToken).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.RemediationResult() ?? throw new InvalidDataException("Missing Minecraft remediation result."); reader.End(); return result;
    }
}
