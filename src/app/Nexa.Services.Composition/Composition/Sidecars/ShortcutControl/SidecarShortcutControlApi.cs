using Nexa.Sidecar.Host.ShortcutControl.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General presentation operations bound to the authenticated invoking session.</summary>
public sealed partial class SidecarShortcutControlApi
{
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly Func<SidecarHostSession, HostShortcutSet, CancellationToken, ValueTask> _replace;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarShortcutControlApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer,
        Func<SidecarHostSession, HostShortcutSet, CancellationToken, ValueTask> replace)
    {
        _replace = replace ?? throw new ArgumentNullException(nameof(replace));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(4095, true, SidecarWireCodecs.GeneratedDto,
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
        if (domain != "shortcuts" || Resolve(caller) is not { } session) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostShortcutSet>(SidecarShortcutControlRoutes.Replace, async (input, token) =>
        {
            try
            {
                if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
                await _replace(session, input, token).ConfigureAwait(false);
                return XsrResult.Success();
            }
            catch (Exception error) when (IsExpected(error)) { return XsrResult.Failure(Failure(error)); }
        });
        return routes.Build(_observer);
    }

    private static bool IsExpected(Exception error) => error is ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException;
    private static XsrError Failure(Exception error) => error switch
    {
        OperationCanceledException => XsrRuntimeErrors.Cancelled(),
        TimeoutException => new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "Shortcut control expired."),
        _ => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Shortcut control was rejected.")
    };

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
