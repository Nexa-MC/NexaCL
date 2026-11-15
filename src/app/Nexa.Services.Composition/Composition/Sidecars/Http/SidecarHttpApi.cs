using Nexa.Sidecar.Host.Http.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General HTTP operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarHttpApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly SidecarHttpPool _pool;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarHttpApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer,
        SidecarHttpPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(4607, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "network.http.catalog"),
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
            && session.State == SidecarSessionState.Active
            && session.NegotiatedFeatures.HasFlag(SidecarFeatures.HostRequests | SidecarFeatures.BinaryPayloads) ? session : null;
    }

    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "http" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        Register<HostHttpBegin>(SidecarHttpRoutes.Begin, (current, input, token) =>
        { token.ThrowIfCancellationRequested(); _pool.Begin(current.SessionId, input, () => Resolve(caller) == current); return ValueTask.CompletedTask; });
        Register<HostHttpAppend>(SidecarHttpRoutes.Append, (current, input, token) => _pool.AppendAsync(current.SessionId, input, token));
        Register<HostHttpRef>(SidecarHttpRoutes.Release, (current, input, token) => _pool.ReleaseAsync(current.SessionId, input, token));
        void Register<T>(XsrSemanticId route, Func<SidecarHostSession, T, CancellationToken, ValueTask> action) where T : notnull
            => routes.Register<T>(route, async (input, token) =>
            {
                try
                {
                    if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
                    await action(session, input, token).ConfigureAwait(false);
                    return XsrResult.Success();
                }
                catch (Exception error) when (IsExpected(error)) { return XsrResult.Failure(Failure(error)); }
            });
        return routes.Build(_observer);
    }

    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "http" || Resolve(caller) is not { } session) return null;
        XsrQueryRouterBuilder routes = new();
        Register<HostHttpRef, HostHttpResponse>(SidecarHttpRoutes.Send, _pool.SendAsync);
        Register<HostHttpRead, HostHttpChunk>(SidecarHttpRoutes.Read, _pool.ReadAsync);
        void Register<T, U>(XsrSemanticId route, Func<Guid, T, CancellationToken, ValueTask<U>> action) where T : notnull
            => routes.Register<T, U>(route, async (input, token) =>
            {
                try
                {
                    if (Resolve(caller) != session) return XsrResult.Failure<U>(XsrRuntimeErrors.RouteNotFound());
                    return XsrResult.Success(await action(session.SessionId, input, token).ConfigureAwait(false));
                }
                catch (Exception error) when (IsExpected(error)) { return XsrResult.Failure<U>(Failure(error)); }
            });
        return routes.Build(_observer);
    }

    private static bool IsExpected(Exception error) => error is IOException or HttpRequestException or InvalidDataException or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException;
    private static XsrError Failure(Exception error) => error switch
    {
        OperationCanceledException => XsrRuntimeErrors.Cancelled(),
        TimeoutException => new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "HTTP exchange expired."),
        IOException or HttpRequestException => new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "HTTP transport unavailable."),
        _ => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "HTTP exchange was rejected.")
    };

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
