using Nexa.Xsr;
namespace Nexa.Services.Composition.Sidecars;

public sealed record HostPeerBind(long Id, string PackageName, string Service);
public sealed record HostPeerRef(long Id);
public sealed record HostPeerBinding(long Id, string TargetSession);
public sealed record HostPeerCall(long Id, byte[] Payload);
public sealed record HostPeerReply(byte[] Payload);
public sealed record HostPeerEnvelope(string CallerPackage, string CallerSession, byte[] Payload);
public static class SidecarPeerRoutes
{
    public static readonly XsrSemanticId Bind = XsrSemanticId.Parse("sidecar.peer.bind");
    public static readonly XsrSemanticId Invoke = XsrSemanticId.Parse("sidecar.peer.invoke");
    public static readonly XsrSemanticId Release = XsrSemanticId.Parse("sidecar.peer.release");
}
