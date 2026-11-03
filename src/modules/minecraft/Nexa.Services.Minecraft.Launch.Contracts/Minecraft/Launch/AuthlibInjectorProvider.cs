




namespace Nexa.Services.Minecraft.Launch;

public interface IAuthlibInjectorProvider
{
    Task<string> EnsureNide8Async(string minecraftRoot, string serverId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nide8 agent preparation is unavailable.");
    Task<string> EnsureAsync(string minecraftRoot, CancellationToken cancellationToken = default);
}
