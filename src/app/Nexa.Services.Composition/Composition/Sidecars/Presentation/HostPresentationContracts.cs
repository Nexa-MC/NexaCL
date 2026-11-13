using Nexa.Sidecar.Protocol;
using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostPresentationEmpty;
public sealed record HostPresentationRevision(long Revision);
public sealed record HostPresentationFile(string Path, string Content);
public sealed record HostPresentationDocument(string Target, SidecarNxmlOperation Operation, string EntryPoint,
    HostPresentationFile[] Files, string OnboardingRevision);
public sealed record HostPresentationReplace(long ExpectedRevision, HostPresentationDocument[] Documents);
public static class SidecarPresentationRoutes
{
    public static readonly XsrSemanticId Revision = XsrSemanticId.Parse("presentation.nxml.revision");
    public static readonly XsrSemanticId Replace = XsrSemanticId.Parse("presentation.nxml.replace");
}
