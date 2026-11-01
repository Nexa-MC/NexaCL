#if NEXA_MINECRAFT_HTTP_BRIDGE
namespace Nexa.Native.Minecraft.Http;
#else
namespace Nexa.Native.Network.Services;
#endif

internal static class DownloadProtocol
{
    internal const int Download = 2000, State = 2001, Progress = 2002, Log = 2003;
    internal const int AcquireWork = 3000, ClaimWork = 3001, ReleaseWork = 3002, ObserveDiskWrite = 3003;
    internal const int AcquireQuiet = 3010, ClaimQuiet = 3011, ReleaseQuiet = 3012, GraceQuiet = 3013;
}
