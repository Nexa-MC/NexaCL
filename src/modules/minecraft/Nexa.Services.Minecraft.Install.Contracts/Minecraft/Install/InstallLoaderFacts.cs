namespace Nexa.Services.Minecraft.Install;

public static class InstallLoaderFacts
{
    public static bool IsAddon(InstallLoader loader) => loader is InstallLoader.FabricApi or InstallLoader.Qsl or InstallLoader.OptiFabric;
}
