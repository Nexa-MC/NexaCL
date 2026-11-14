using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostDialogQuery(long ExpectedRevision);
public sealed record HostDialogChange(long ExpectedRevision, int Document);
public sealed record HostDialogEntry(int Document, string Target, bool Open);
public sealed record HostDialogState(long Revision, HostDialogEntry[] Dialogs);
public static class SidecarDialogControlRoutes
{
    public static readonly XsrSemanticId State = XsrSemanticId.Parse("presentation.dialogs.state");
    public static readonly XsrSemanticId Open = XsrSemanticId.Parse("presentation.dialogs.open");
    public static readonly XsrSemanticId Close = XsrSemanticId.Parse("presentation.dialogs.close");
}
