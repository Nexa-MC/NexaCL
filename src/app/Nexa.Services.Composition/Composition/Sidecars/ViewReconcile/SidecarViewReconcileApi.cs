using Nexa.Sidecar.Host.ViewReconcile.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Atomically reconciles only the invoking Sidecar's sources while retaining selected documents.</summary>
public sealed partial class SidecarViewReconcileApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarViewReconcileApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(3839, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "presentation.catalog"),
            (caller, input, _) =>
            {
                if (input.Length != 0) throw new InvalidDataException("Catalog request must be empty.");
                using var writer = new ServiceWireWriter();
                writer.UInt32(1); writer.String(ServiceWireSchema.Fingerprint + ":" + CatalogFingerprint);
                var contracts = Available(caller); writer.Count(contracts.Count);
                foreach (uint id in contracts) writer.UInt32(id);
                return ValueTask.FromResult(XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, writer.Finish())));
            })).ToArray());
    }

    private SidecarHostSession? Resolve(SidecarHostCaller caller)
    {
        var session = _resolve(caller);
        return session is not null && session.PluginName == caller.PackageName && session.SessionId == caller.SessionId
            && session.State == SidecarSessionState.Active && session.NxmlModules is not null
            && session.NegotiatedFeatures.HasFlag(SidecarFeatures.Nxml | SidecarFeatures.RetainedDocuments) ? session : null;
    }

    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "viewreconcile" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostViewReconcileRequest>(SidecarViewReconcileRoutes.Reconcile, async (input, token) =>
        {
            if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
            try
            {
                if (input.Documents is null || input.Documents.Length > 32) throw new ArgumentException("Invalid document count.");
                var candidates = new XsrNxmlSourceCandidate[input.Documents.Length];
                var utf8 = new System.Text.UTF8Encoding(false, true);
                long sourceBytes = 0, modelBytes = 0;
                for (int index = 0; index < input.Documents.Length; index++)
                {
                    var document = input.Documents[index];
                    if (document is null || document.PreviousDocument < -1 || document.Source is null
                        || document.PreviousDocument >= 0 && document.Source.Length != 0
                        || document.PreviousDocument == -1 && document.Source.Length == 0
                        || document.Model is null || document.Model.Length > 4096)
                        throw new ArgumentException("Invalid document candidate.");
                    sourceBytes += document.Source.Length;
                    if (sourceBytes > 4 * 1024 * 1024) throw new ArgumentException("Source budget exceeded.");
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var field in document.Model)
                    {
                        if (field is null || string.IsNullOrWhiteSpace(field.Key) || field.Key.Length > 256 || field.Key.Any(char.IsControl)
                            || field.Utf8Text is null || field.Utf8Text.Length > 512 * 1024)
                            throw new ArgumentException("Invalid model field.");
                        modelBytes += utf8.GetByteCount(field.Key) + field.Utf8Text.Length;
                        if (modelBytes > 4 * 1024 * 1024) throw new ArgumentException("Model budget exceeded.");
                        if (!values.TryAdd(field.Key, utf8.GetString(field.Utf8Text))) throw new ArgumentException("Duplicate model key.");
                    }
                    candidates[index] = new(document.PreviousDocument,
                        document.PreviousDocument == -1 ? SidecarNxmlDocument.Decode(document.Source) : null, values);
                }
                await session.ReconcilePresentationAsync(input.ExpectedRevision, candidates, token).ConfigureAwait(false);
                return XsrResult.Success();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
            { return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Document reconciliation was rejected.")); }
        });
        return routes.Build(_observer);
    }

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
