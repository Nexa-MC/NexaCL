using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostViewEmpty;
public sealed record HostViewRevision(long Revision);
public sealed record HostViewValue(string Key, byte[] Utf8Text);
public sealed record HostViewModel(int Document, HostViewValue[] Values);
public sealed record HostViewReplace(long ExpectedRevision, byte[][] Documents, HostViewModel[] Models);
public sealed record HostViewModelsUpdate(long ExpectedRevision, HostViewModel[] Models);
public static class SidecarViewStateRoutes
{
    public static readonly XsrSemanticId Revision = XsrSemanticId.Parse("presentation.viewstate.revision");
    public static readonly XsrSemanticId Replace = XsrSemanticId.Parse("presentation.viewstate.replace");
    public static readonly XsrSemanticId Models = XsrSemanticId.Parse("presentation.viewstate.models.update");
}
