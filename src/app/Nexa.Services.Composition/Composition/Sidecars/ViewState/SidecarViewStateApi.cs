using Nexa.Sidecar.Host.ViewState.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General presentation operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarViewStateApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarViewStateApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(1023, true, SidecarWireCodecs.GeneratedDto,
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
        if (domain != "viewstate" || Resolve(caller) is not { } session) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostViewEmpty, HostViewRevision>(SidecarViewStateRoutes.Revision, (_, _) =>
            ValueTask.FromResult(Resolve(caller) == session
                ? XsrResult.Success(new HostViewRevision(session.PresentationRevision))
                : XsrResult.Failure<HostViewRevision>(XsrRuntimeErrors.RouteNotFound())));
        return routes.Build(_observer);
    }

    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "viewstate" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostViewReplace>(SidecarViewStateRoutes.Replace, async (input, token) =>
        {
            if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
            try
            {
                if (input.Documents is null || input.Documents.Length > 32 || input.Documents.Any(bytes => bytes is null)
                    || input.Documents.Sum(bytes => (long)bytes.Length) > 4 * 1024 * 1024)
                    throw new ArgumentException("Invalid document bundle budget.");
                var documents = input.Documents.Select(bytes => SidecarNxmlDocument.Decode(bytes)).ToArray();
                var changes = ReadModels(input.Models);
                var models = Enumerable.Range(0, documents.Length).Select(_ => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()).ToArray();
                foreach (var change in changes)
                {
                    if ((uint)change.Key >= (uint)models.Length) throw new ArgumentException("Invalid document index.");
                    models[change.Key] = change.Value;
                }
                await session.ReplacePresentationWithModelsAsync(input.ExpectedRevision, documents, models, token).ConfigureAwait(false);
                return XsrResult.Success();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
            { return Rejected(); }
        });
        routes.Register<HostViewModelsUpdate>(SidecarViewStateRoutes.Models, async (input, token) =>
        {
            if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
            try
            {
                await session.UpdatePresentationModelsAsync(input.ExpectedRevision, ReadModels(input.Models), token).ConfigureAwait(false);
                return XsrResult.Success();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
            { return Rejected(); }
        });
        return routes.Build(_observer);
    }

    private static XsrResult Rejected() => XsrResult.Failure(new(XsrErrorKind.Rejected,
        XsrSemanticId.Parse("xsr.rejected"), "View-state candidate was rejected."));

    private static Dictionary<int, IReadOnlyDictionary<string, string>> ReadModels(HostViewModel[] models)
    {
        if (models is null || models.Length > 32) throw new ArgumentException("Invalid model update count.");
        var result = new Dictionary<int, IReadOnlyDictionary<string, string>>();
        var utf8 = new System.Text.UTF8Encoding(false, true);
        long bytes = 0;
        foreach (var model in models)
        {
            if (model is null || model.Document < 0 || model.Values is null || model.Values.Length > 4096)
                throw new ArgumentException("Invalid document model.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in model.Values)
            {
                if (field is null || string.IsNullOrWhiteSpace(field.Key) || field.Key.Length > 256 || field.Key.Any(char.IsControl)
                    || field.Utf8Text is null || field.Utf8Text.Length > 512 * 1024)
                    throw new ArgumentException("Invalid model field.");
                bytes += utf8.GetByteCount(field.Key) + field.Utf8Text.Length;
                if (bytes > 4 * 1024 * 1024) throw new ArgumentException("Model text budget exceeded.");
                if (!values.TryAdd(field.Key, utf8.GetString(field.Utf8Text))) throw new ArgumentException("Duplicate model key.");
            }
            if (!result.TryAdd(model.Document, values)) throw new ArgumentException("Duplicate document model.");
        }
        return result;
    }

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
