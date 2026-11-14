using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime;

/// <summary>Retain an existing source index, or supply a new source with index -1, with a complete model.</summary>
public sealed record XsrNxmlSourceCandidate(int PreviousDocument, SidecarNxmlDocument? Source, IReadOnlyDictionary<string, string> Model);

public sealed partial class SidecarHostSession
{
    /// <summary>Commits an ordered source set while preserving explicitly retained document identities and action lifetimes.</summary>
    public async ValueTask ReconcilePresentationAsync(long expectedRevision, IReadOnlyList<XsrNxmlSourceCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (candidates.Count > 32) throw new SidecarProtocolException("Session NXML source budget exceeded.");
        var owned = candidates.ToArray();
        if (owned.Length > 32 || owned.Any(candidate => candidate is null || candidate.PreviousDocument < -1
            || (candidate.PreviousDocument == -1) != (candidate.Source is not null)))
            throw new SidecarProtocolException("Each candidate must retain an index or provide one new source.");
        // Own caller data before waiting for another lifecycle operation.
        var indexes = owned.Select(candidate => candidate.PreviousDocument).ToArray();
        var sources = new SidecarNxmlDocument?[owned.Length];
        long newSourceBytes = 0;
        for (int index = 0; index < owned.Length; index++)
        {
            if (owned[index].Source is not { } source) continue;
            byte[] encoded = SidecarNxmlDocument.Encode(source);
            newSourceBytes += encoded.Length;
            if (newSourceBytes > 4 * 1024 * 1024) throw new SidecarProtocolException("Session NXML source budget exceeded.");
            sources[index] = SidecarNxmlDocument.Decode(encoded);
        }
        var models = OwnPresentationModels(owned.Length, owned.Select(candidate => candidate.Model).ToArray());
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        XsrNxmlModuleRuntime.Lease? prior = null, next = null;
        try
        {
            SidecarNxmlDocument[] previous;
            lock (_gate)
            {
                ValidatePresentationRevision(expectedRevision);
                if (!_negotiatedFeatures.HasFlag(SidecarFeatures.RetainedDocuments))
                    throw new InvalidOperationException("Document retention was not negotiated.");
                previous = _nxmlDocuments;
            }
            HashSet<int> retained = [];
            for (int index = 0; index < indexes.Length; index++)
            {
                int old = indexes[index];
                if (old == -1) continue;
                if ((uint)old >= (uint)previous.Length || !retained.Add(old))
                    throw new SidecarProtocolException("A retained index must identify one unique source in this session.");
                sources[index] = previous[old];
            }
            var declarations = new SidecarRegistrationItem[sources.Length];
            long sourceBytes = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                byte[] encoded = SidecarNxmlDocument.Encode(sources[index]!);
                sourceBytes += encoded.Length;
                if (sourceBytes > 4 * 1024 * 1024) throw new SidecarProtocolException("Session NXML source budget exceeded.");
                declarations[index] = new(SidecarRegistrationKind.UiModule,
                    "sidecar.reconciled." + index.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 0, encoded);
            }
            var documents = XsrNxmlModuleRuntime.Prepare(declarations, NegotiatedFeatures);
            // Validation round-trips the complete set; retain authoritative source objects for compiler caches.
            for (int index = 0; index < indexes.Length; index++)
                if (indexes[index] >= 0) documents[index] = previous[indexes[index]];
            var compiled = NxmlModules!.Compile(documents, this, models);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ValidatePresentationRevision(expectedRevision);
                prior = _nxmlLease;
                next = NxmlModules.Replace(prior, documents, compiled, this, models, indexes);
                _nxmlLease = next; _nxmlDocuments = documents; _nxmlCompiled = compiled; _nxmlModels = models;
                _presentationRevision++;
            }
        }
        finally
        {
            if (next is not null)
            {
                prior?.FinishRetirement(next.DocumentLifetimes);
                next.RequestRefresh();
            }
            _lifecycleGate.Release();
            if (next is not null) NxmlModules!.Publish();
        }
    }
}
