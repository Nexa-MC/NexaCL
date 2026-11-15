using Nexa.Xsr;
namespace Nexa.Services.Composition.Sidecars;

public sealed record HostHttpHeader(string Name, string[] Values);
public sealed record HostHttpBegin(long Id, string Method, string Uri, HostHttpHeader[] Headers, int BodyLength);
public sealed record HostHttpAppend(long Id, int Offset, byte[] Bytes);
public sealed record HostHttpRef(long Id);
public sealed record HostHttpRead(long Id, long Offset, int Count);
public sealed record HostHttpResponse(int StatusCode, HostHttpHeader[] Headers);
public sealed record HostHttpChunk(long Offset, byte[] Bytes);
public static class SidecarHttpRoutes
{
    public static readonly XsrSemanticId Begin = XsrSemanticId.Parse("network.http.begin");
    public static readonly XsrSemanticId Append = XsrSemanticId.Parse("network.http.append");
    public static readonly XsrSemanticId Send = XsrSemanticId.Parse("network.http.send");
    public static readonly XsrSemanticId Read = XsrSemanticId.Parse("network.http.read");
    public static readonly XsrSemanticId Release = XsrSemanticId.Parse("network.http.release");
}
