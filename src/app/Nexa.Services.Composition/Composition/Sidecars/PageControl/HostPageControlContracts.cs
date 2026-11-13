using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostPageQuery(long ExpectedRevision);
public sealed record HostPageChange(long ExpectedRevision, int Document);
public sealed record HostPageEntry(int Document, string Target, bool Visible);
public sealed record HostPageState(long Revision, HostPageEntry[] Pages);
public static class SidecarPageControlRoutes
{
    public static readonly XsrSemanticId State = XsrSemanticId.Parse("presentation.pages.state");
    public static readonly XsrSemanticId Open = XsrSemanticId.Parse("presentation.pages.open");
    public static readonly XsrSemanticId Close = XsrSemanticId.Parse("presentation.pages.close");
}
