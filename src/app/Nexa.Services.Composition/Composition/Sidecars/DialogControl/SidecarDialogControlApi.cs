using Nexa.Sidecar.Host.DialogControl.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General presentation operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarDialogControlApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly Func<SidecarHostSession, long, CancellationToken, ValueTask<HostDialogState>> _read;
    private readonly Func<SidecarHostSession, long, int, bool, CancellationToken, ValueTask> _change;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarDialogControlApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer,
        Func<SidecarHostSession, long, CancellationToken, ValueTask<HostDialogState>> read,
        Func<SidecarHostSession, long, int, bool, CancellationToken, ValueTask> change)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _change = change ?? throw new ArgumentNullException(nameof(change));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(3583, true, SidecarWireCodecs.GeneratedDto,
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
            && session.NegotiatedFeatures.HasFlag(SidecarFeatures.Nxml) && session.NegotiatedFeatures.HasFlag(SidecarFeatures.Dialogs) ? session : null;
    }

    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "dialogs" || Resolve(caller) is not { } session) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostDialogQuery, HostDialogState>(SidecarDialogControlRoutes.State, async (input, token) =>
        {
            try
            {
                if (Resolve(caller) != session) return XsrResult.Failure<HostDialogState>(XsrRuntimeErrors.RouteNotFound());
                return XsrResult.Success(await _read(session, input.ExpectedRevision, token).ConfigureAwait(false));
            }
            catch (Exception error) when (IsExpected(error)) { return XsrResult.Failure<HostDialogState>(Failure(error)); }
        });
        return routes.Build(_observer);
    }

    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "dialogs" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostDialogChange>(SidecarDialogControlRoutes.Open, (input, token) => Change(input, true, token));
        routes.Register<HostDialogChange>(SidecarDialogControlRoutes.Close, (input, token) => Change(input, false, token));
        return routes.Build(_observer);

        async ValueTask<XsrResult> Change(HostDialogChange input, bool open, CancellationToken token)
        {
            try
            {
                if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
                await _change(session, input.ExpectedRevision, input.Document, open, token).ConfigureAwait(false);
                return XsrResult.Success();
            }
            catch (Exception error) when (IsExpected(error)) { return XsrResult.Failure(Failure(error)); }
        }
    }

    private static bool IsExpected(Exception error) => error is ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException;
    private static XsrError Failure(Exception error) => error switch
    {
        OperationCanceledException => XsrRuntimeErrors.Cancelled(),
        TimeoutException => new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "Modal control expired."),
        _ => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Modal control was rejected.")
    };

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
