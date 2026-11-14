using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public enum HostStartupPhase { Composing = 0, Ready = 1, Stopping = 2 }
public sealed record HostContextQuery;
public sealed record HostContextSnapshot(string Version, byte[] Utf8DataRoot, HostStartupPhase Phase, bool NativeWindowReady);
public static class SidecarContextRoutes
{
    public static readonly XsrSemanticId Snapshot = XsrSemanticId.Parse("host.context.snapshot");
}
