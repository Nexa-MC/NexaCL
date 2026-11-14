using System.Text;
using Nexa.Sidecar.Host.Context.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Read-only launcher facts for authenticated native Sidecars, independent of guest SDKs.</summary>
public sealed partial class SidecarContextApi : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly string _version;
    private readonly byte[] _root;
    private HostStartupPhase _phase;
    private Func<bool>? _nativeReady;
    private bool _disposed;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarContextApi(Func<SidecarHostCaller, SidecarHostSession?> resolve,
        IXsrDispatchObserver observer, string version, string dataRoot)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        if (string.IsNullOrWhiteSpace(version) || version.Length > 256 || version.Any(char.IsControl))
            throw new ArgumentException("Invalid launcher version.", nameof(version));
        if (string.IsNullOrEmpty(dataRoot) || !Path.IsPathFullyQualified(dataRoot) || dataRoot.Contains('\0'))
            throw new ArgumentException("An absolute data root is required.", nameof(dataRoot));
        _root = new UTF8Encoding(false, true).GetBytes(dataRoot);
        if (_root.Length > 131072) throw new ArgumentException("Data root exceeds the wire limit.", nameof(dataRoot));
        _version = version;
        _authorize = (caller, semantic) => Resolve(caller) is { } session
            && (semantic == "host.context.catalog" || session.NegotiatedFeatures.HasFlag(SidecarFeatures.LargeGeneratedDtos));
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(3327, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "host.context.catalog"),
            (caller, input, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (input.Length != 0) throw new InvalidDataException("Catalog request must be empty.");
                using var writer = new ServiceWireWriter();
                writer.UInt32(1); writer.String(ServiceWireSchema.Fingerprint + ":" + CatalogFingerprint);
                var contracts = Available(caller); writer.Count(contracts.Count);
                foreach (uint id in contracts) writer.UInt32(id);
                return ValueTask.FromResult(XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, writer.Finish())));
            })).ToArray());
    }

    /// <summary>Called once after normal startup preparation, never during Sidecar activation.</summary>
    public void MarkReady(Func<bool> nativeWindowReady)
    {
        ArgumentNullException.ThrowIfNull(nativeWindowReady);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_phase != HostStartupPhase.Composing) throw new InvalidOperationException("Startup has already advanced.");
            _nativeReady = nativeWindowReady;
            _phase = HostStartupPhase.Ready;
        }
    }

    /// <summary>Terminal state, published before the composition root disposes its controllers.</summary>
    public void MarkStopping()
    {
        lock (_gate) { _phase = HostStartupPhase.Stopping; _nativeReady = null; }
    }

    private SidecarHostSession? Resolve(SidecarHostCaller caller)
    {
        lock (_gate) if (_disposed) return null;
        var session = _resolve(caller);
        if (session is null || session.PluginName != caller.PackageName || session.SessionId != caller.SessionId) return null;
        try { session.CaptureActivationLifetime().ThrowIfCancellationRequested(); return session; }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { return null; }
    }

    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "context" || Resolve(caller) is not { } session) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostContextQuery, HostContextSnapshot>(SidecarContextRoutes.Snapshot, (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            CancellationToken activation;
            try { activation = session.CaptureActivationLifetime(); }
            catch (InvalidOperationException) { return ValueTask.FromResult(XsrResult.Failure<HostContextSnapshot>(XsrRuntimeErrors.Cancelled())); }
            Func<bool>? native;
            lock (_gate) native = _nativeReady;
            // Native facts are read outside the lock; readiness never waits for a UI callback.
            bool ready = native?.Invoke() == true;
            if (activation.IsCancellationRequested || Resolve(caller) != session) return ValueTask.FromResult(XsrResult.Failure<HostContextSnapshot>(XsrRuntimeErrors.Cancelled()));
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed) return ValueTask.FromResult(XsrResult.Failure<HostContextSnapshot>(XsrRuntimeErrors.Cancelled()));
                return ValueTask.FromResult(XsrResult.Success(new HostContextSnapshot(_version, (byte[])_root.Clone(),
                    _phase, _phase == HostStartupPhase.Ready && ready)));
            }
        });
        return routes.Build(_observer);
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _phase = HostStartupPhase.Stopping; _nativeReady = null; }
    }

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
