using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostRasterBegin(long Id, int Length, byte[] Sha256);
public sealed record HostRasterAppend(long Id, int Offset, byte[] Bytes);
public sealed record HostRasterRef(long Id);
public sealed record HostRasterSet(long ExpectedRevision, HostRasterBinding[] Bindings);
public sealed record HostRasterBinding(int Document, string Slot, long ImageId, double Width, double Height, string Command, string Argument);
public static class SidecarRasterControlRoutes
{
    public static readonly XsrSemanticId Begin = XsrSemanticId.Parse("presentation.rasters.begin");
    public static readonly XsrSemanticId Append = XsrSemanticId.Parse("presentation.rasters.append");
    public static readonly XsrSemanticId Seal = XsrSemanticId.Parse("presentation.rasters.seal");
    public static readonly XsrSemanticId Replace = XsrSemanticId.Parse("presentation.rasters.replace");
    public static readonly XsrSemanticId Release = XsrSemanticId.Parse("presentation.rasters.release");
}
