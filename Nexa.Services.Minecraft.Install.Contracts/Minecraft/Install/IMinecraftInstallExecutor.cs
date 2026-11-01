using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public interface IMinecraftInstallExecutor
{
    Task<XsrResult<MinecraftInstallResult>> InstallAsync(MinecraftInstallCommand command, CancellationToken cancellationToken = default);
}
