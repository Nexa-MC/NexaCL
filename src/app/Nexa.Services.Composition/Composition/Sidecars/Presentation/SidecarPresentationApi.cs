using Nexa.Sidecar.Host.Presentation.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General presentation operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarPresentationApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarPresentationApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(767, true, SidecarWireCodecs.GeneratedDto,
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
            && session.NegotiatedFeatures.HasFlag(SidecarFeatures.Nxml) ? session : null;
    }

    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "presentation" || Resolve(caller) is not { } session) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostPresentationEmpty, HostPresentationRevision>(SidecarPresentationRoutes.Revision, (_, _) =>
            ValueTask.FromResult(Resolve(caller) == session
                ? XsrResult.Success(new HostPresentationRevision(session.PresentationRevision))
                : XsrResult.Failure<HostPresentationRevision>(XsrRuntimeErrors.RouteNotFound())));
        return routes.Build(_observer);
    }

    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "presentation" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostPresentationReplace>(SidecarPresentationRoutes.Replace, async (input, token) =>
        {
            if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
            try
            {
                if (input.Documents is null || input.Documents.Length > 32) throw new ArgumentException("Invalid document count.");
                var documents = input.Documents.Select(document =>
                {
                    if (document is null || document.Files is null || document.Files.Length > SidecarNxmlDocument.MaximumFiles
                        || document.Files.Any(file => file is null)) throw new ArgumentException("Invalid source files.");
                    return new SidecarNxmlDocument(document.Target, document.Operation, document.EntryPoint,
                        document.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal))
                    { OnboardingRevision = document.OnboardingRevision };
                }).ToArray();
                await session.ReplacePresentationAsync(input.ExpectedRevision, documents, token).ConfigureAwait(false);
                return XsrResult.Success();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or SidecarProtocolException or InvalidDataException)
            { return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Presentation candidate was rejected.")); }
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
