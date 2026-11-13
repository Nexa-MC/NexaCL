using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime;

public sealed partial class SidecarHostSession
{
    private long _presentationRevision;

    /// <summary>Revision of the current session's complete, committed NXML document set.</summary>
    public long PresentationRevision { get { lock (_gate) return _presentationRevision; } }

    /// <summary>Replaces only this active session's presentation. Failed compilation preserves its predecessor.</summary>
    public async ValueTask ReplacePresentationAsync(long expectedRevision, IReadOnlyList<SidecarNxmlDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (documents.Count > 32) throw new SidecarProtocolException("Session NXML source budget exceeded.");
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        XsrNxmlModuleRuntime.Lease? prior = null, next = null;
        try
        {
            lock (_gate) Validate();
            // Round-trip through the same admission rules as initial registration.
            var declarations = documents.Select((document, index) => new SidecarRegistrationItem(
                SidecarRegistrationKind.UiModule, "sidecar.dynamic." + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                0, 0, SidecarNxmlDocument.Encode(document))).ToArray();
            var candidate = XsrNxmlModuleRuntime.Prepare(declarations, NegotiatedFeatures);
            var compiled = NxmlModules!.Compile(candidate, this);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Validate();
                prior = _nxmlLease;
                next = NxmlModules.Replace(prior, candidate, compiled, this);
                _nxmlLease = next; _nxmlDocuments = candidate; _nxmlCompiled = compiled;
                _presentationRevision++;
            }
        }
        finally
        {
            // Cancellation callbacks and publication run outside both ownership locks.
            if (next is not null)
            {
                prior?.FinishRetirement();
                next.RequestRefresh();
            }
            _lifecycleGate.Release();
            if (next is not null) NxmlModules!.Publish();
        }

        void Validate()
        {
            if (_stopped || _admissionClosed || _state != SidecarSessionState.Active
                || NxmlModules is null || !_negotiatedFeatures.HasFlag(SidecarFeatures.Nxml))
                throw new InvalidOperationException("Presentation requires an active NXML session.");
            if (_presentationRevision != expectedRevision)
                throw new InvalidOperationException("Presentation revision changed.");
        }
    }
}
