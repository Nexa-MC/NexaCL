using Nexa.Sidecar.Host.Notifications.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Authenticated, literal notifications admitted by the Host's feedback owner.</summary>
public sealed partial class SidecarNotificationsApi : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private Binding? _binding;
    private bool _disposed;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarNotificationsApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(2559, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "notifications.catalog"),
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
    /// <summary>The local callback only submits to thread-safe feedback; it must not block on UI or IPC.</summary>
    public IDisposable Bind(Action<SidecarHostCaller, HostNotification, CancellationToken> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("Notifications are already bound.");
            return _binding = new(this, publish);
        }
    }
    private SidecarHostSession? Resolve(SidecarHostCaller caller)
    {
        lock (_gate) if (_disposed) return null;
        var session = _resolve(caller);
        if (session is null || session.PluginName != caller.PackageName || session.SessionId != caller.SessionId) return null;
        try { session.CaptureActivationLifetime().ThrowIfCancellationRequested(); return session; }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { return null; }
    }
    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "notifications" || Resolve(caller) is null) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostNotification>(SidecarNotificationsRoutes.Publish,
            (input, token) => ValueTask.FromResult(Publish(caller, input, token)));
        return routes.Build(_observer);
    }
    private XsrResult Publish(SidecarHostCaller caller, HostNotification input, CancellationToken token)
    {
        if (input is null || !Enum.IsDefined(input.Level) || !Valid(input.Title, 4096) || !Valid(input.Body, 8192))
            return XsrResult.Failure(Rejected("Invalid notification text or level."));
        var session = Resolve(caller);
        if (session is null) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
        Binding? binding;
        lock (_gate) binding = _binding;
        if (binding is null) return XsrResult.Failure(new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "Notification UI is unavailable."));
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, binding.Lifetime, session.CaptureActivationLifetime());
            stop.Token.ThrowIfCancellationRequested();
            binding.Publish(caller, input, stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            lock (_gate) if (!ReferenceEquals(_binding, binding)) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
            return XsrResult.Success();
        }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        { return XsrResult.Failure(Rejected("Notification was not acknowledged.")); }
    }
    private static bool Valid(string? text, int limit) => text is not null && text.Length <= limit
        && !text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t');
    private static XsrError Rejected(string message) => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), message);
    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
    public void Dispose()
    {
        Binding? binding;
        lock (_gate) { if (_disposed) return; _disposed = true; binding = _binding; _binding = null; }
        binding?.Dispose();
    }
    private sealed class Binding : IDisposable
    {
        private readonly SidecarNotificationsApi _owner;
        private readonly CancellationTokenSource _stop = new();
        private int _disposed;
        internal Action<SidecarHostCaller, HostNotification, CancellationToken> Publish { get; }
        internal CancellationToken Lifetime { get; }
        internal Binding(SidecarNotificationsApi owner, Action<SidecarHostCaller, HostNotification, CancellationToken> publish)
        { _owner = owner; Publish = publish; Lifetime = _stop.Token; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_owner._gate) if (ReferenceEquals(_owner._binding, this)) _owner._binding = null;
            try { _stop.Cancel(); } finally { _stop.Dispose(); }
        }
    }
}
