using Nexa.Sidecar.Host.Confirmation.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General presentation operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarConfirmationApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly Func<SidecarHostCaller, string, string, CancellationToken, ValueTask<bool>> _ask;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarConfirmationApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer,
        Func<SidecarHostCaller, string, string, CancellationToken, ValueTask<bool>> ask)
    {
        _ask = ask ?? throw new ArgumentNullException(nameof(ask));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(1535, true, SidecarWireCodecs.GeneratedDto,
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
            && session.State == SidecarSessionState.Active ? session : null;
    }

    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "confirmation" || Resolve(caller) is not { } session) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostConfirmationRequest, HostConfirmationDecision>(SidecarConfirmationRoutes.Ask, async (input, token) =>
        {
            try
            {
                if (Resolve(caller) != session) return XsrResult.Failure<HostConfirmationDecision>(XsrRuntimeErrors.RouteNotFound());
                if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 256 || input.Title.Any(char.IsControl)
                    || input.Utf8Details is null || input.Utf8Details.Length is 0 or > 256 * 1024)
                    throw new ArgumentException("Invalid confirmation text.");
                string details = new System.Text.UTF8Encoding(false, true).GetString(input.Utf8Details);
                if (string.IsNullOrWhiteSpace(details) || details.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
                    throw new ArgumentException("Invalid confirmation details.");
                bool accepted = await _ask(caller, input.Title, details, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (Resolve(caller) != session) return XsrResult.Failure<HostConfirmationDecision>(XsrRuntimeErrors.RouteNotFound());
                return XsrResult.Success(new HostConfirmationDecision(accepted));
            }
            catch (OperationCanceledException) { return XsrResult.Failure<HostConfirmationDecision>(XsrRuntimeErrors.Cancelled()); }
            catch (TimeoutException) { return XsrResult.Failure<HostConfirmationDecision>(new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "Confirmation expired.")); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { return XsrResult.Failure<HostConfirmationDecision>(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Confirmation was rejected.")); }
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
