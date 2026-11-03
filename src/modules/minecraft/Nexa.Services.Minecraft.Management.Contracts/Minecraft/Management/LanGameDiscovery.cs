using Nexa.Xsr;
namespace Nexa.Services.Minecraft.Management;

public sealed record LanGameDiscoveryQuery(int ListenMilliseconds = 3000);
public sealed record LanGame(string Name, string Address);
public sealed record LanGameDiscoverySnapshot(IReadOnlyList<LanGame> Games);
public static class LanGameDiscoveryContract
{
    public static readonly XsrSemanticId Discover = XsrSemanticId.Parse("minecraft.lan.discover");
}
