using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostViewCandidateValue(string Key, byte[] Utf8Text);
public sealed record HostViewCandidate(int PreviousDocument, byte[] Source, HostViewCandidateValue[] Model);
public sealed record HostViewReconcileRequest(long ExpectedRevision, HostViewCandidate[] Documents);
public static class SidecarViewReconcileRoutes
{
    public static readonly XsrSemanticId Reconcile = XsrSemanticId.Parse("presentation.viewstate.reconcile");
}
