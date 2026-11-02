using Nexa.Services.Capabilities;

namespace Nexa.Services.Minecraft.Launch;

public interface IMinecraftRemediationPort
{
    Task<RemediationResult> RemediateAsync(RemediationRequest request, CancellationToken cancellationToken = default);
}
