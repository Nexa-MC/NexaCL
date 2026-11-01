namespace Nexa.Native.Network.Services;

internal static class DownloadProtocol
{
    internal const int Download = 2000, State = 2001, Progress = 2002, Log = 2003;
    internal const int AcquireWork = 3000, ClaimWork = 3001, ReleaseWork = 3002, ObserveDiskWrite = 3003;
}
