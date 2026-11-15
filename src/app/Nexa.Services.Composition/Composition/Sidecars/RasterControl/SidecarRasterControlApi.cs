using Nexa.Sidecar.Host.RasterControl.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General presentation operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarRasterControlApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly HostRasterActions _actions;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarRasterControlApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer,
        HostRasterActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(4351, true, SidecarWireCodecs.GeneratedDto,
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
            && session.NegotiatedFeatures.HasFlag(SidecarFeatures.Nxml | SidecarFeatures.BinaryPayloads | SidecarFeatures.UiForms) ? session : null;
    }

    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "rasters" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        Register(SidecarRasterControlRoutes.Begin, _actions.Begin);
        Register(SidecarRasterControlRoutes.Append, _actions.Append);
        Register(SidecarRasterControlRoutes.Seal, _actions.Seal);
        Register(SidecarRasterControlRoutes.Replace, _actions.Replace);
        Register(SidecarRasterControlRoutes.Release, _actions.Release);
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

    private static bool IsExpected(Exception error) => error is InvalidDataException or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException;
    private static XsrError Failure(Exception error) => error switch
    {
        OperationCanceledException => XsrRuntimeErrors.Cancelled(),
        TimeoutException => new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "Raster control expired."),
        _ => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Raster control was rejected.")
    };

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
