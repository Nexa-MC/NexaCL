using Nexa.Sidecar.Host.Shell.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Native shell effects for authenticated Sidecars, independent of any guest SDK.</summary>
public sealed partial class SidecarShellApi : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private Binding? _binding;
    private bool _disposed;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarShellApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(1791, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "shell.catalog"),
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

    public IDisposable Bind(SidecarShellActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.OpenHttps);
        ArgumentNullException.ThrowIfNull(actions.RevealDirectory);
        ArgumentNullException.ThrowIfNull(actions.Displays);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("Shell actions are already bound.");
            return _binding = new(this, actions);
        }
    }
    private SidecarHostSession? Resolve(SidecarHostCaller caller)
    {
        lock (_gate) if (_disposed) return null;
        var session = _resolve(caller);
        return session is not null && session.PluginName == caller.PackageName && session.SessionId == caller.SessionId
            && session.State == SidecarSessionState.Active ? session : null;
    }
    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "shell" || Resolve(caller) is null) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostHttpsRequest>(SidecarShellRoutes.OpenHttps, (input, token) => Effect(caller, async (actions, cancellation) =>
        {
            if (string.IsNullOrWhiteSpace(input.Address) || input.Address.Length > 16384 || input.Address.Any(char.IsControl)
                || !Uri.TryCreate(input.Address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
                throw new ArgumentException("Invalid HTTPS address.");
            cancellation.ThrowIfCancellationRequested();
            await actions.OpenHttps(uri.AbsoluteUri, cancellation).ConfigureAwait(false);
        }, token));
        routes.Register<HostDirectoryRequest>(SidecarShellRoutes.RevealDirectory, (input, token) => Effect(caller, async (actions, cancellation) =>
        {
            if (string.IsNullOrWhiteSpace(input.Path) || input.Path.Length > 32768 || input.Path.Contains('\0')
                || !Path.IsPathFullyQualified(input.Path) || !Directory.Exists(input.Path))
                throw new ArgumentException("An existing absolute directory is required.");
            string path = Path.GetFullPath(input.Path);
            cancellation.ThrowIfCancellationRequested();
            await actions.RevealDirectory(path, cancellation).ConfigureAwait(false);
        }, token));
        return routes.Build(_observer);
    }
    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "shell" || Resolve(caller) is null) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostShellEmpty, HostDisplays>(SidecarShellRoutes.Displays, (_, token) => Execute(caller, async (actions, cancellation) =>
        {
            var displays = await actions.Displays(cancellation).ConfigureAwait(false);
            if (displays is null || displays.Length > 32 || displays.Any(display => display is null
                || string.IsNullOrWhiteSpace(display.Id) || display.Id.Length > 256 || display.Id.Any(char.IsControl)
                || display.Width <= 0 || display.Height <= 0 || !double.IsFinite(display.Scale) || display.Scale <= 0)
                || displays.Select(display => display.Id).Distinct(StringComparer.Ordinal).Count() != displays.Length)
                throw new ArgumentException("Invalid display result.");
            return new HostDisplays(displays.ToArray());
        }, token));
        return routes.Build(_observer);
    }
    private async ValueTask<XsrResult> Effect(SidecarHostCaller caller, Func<SidecarShellActions, CancellationToken, Task> action, CancellationToken token)
    {
        var result = await Execute(caller, async (actions, cancellation) =>
        { await action(actions, cancellation).ConfigureAwait(false); return true; }, token).ConfigureAwait(false);
        return result.IsSuccess ? XsrResult.Success() : XsrResult.Failure(result.Error!);
    }
    private async ValueTask<XsrResult<T>> Execute<T>(SidecarHostCaller caller, Func<SidecarShellActions, CancellationToken, Task<T>> action, CancellationToken token)
    {
        var session = Resolve(caller);
        if (session is null) return XsrResult.Failure<T>(XsrRuntimeErrors.RouteNotFound());
        Binding? binding;
        lock (_gate) binding = _binding;
        if (binding is null) return XsrResult.Failure<T>(new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "Shell UI is unavailable."));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, binding.Lifetime);
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            T value = await action(binding.Actions, stop.Token).WaitAsync(stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            if (Resolve(caller) != session) return XsrResult.Failure<T>(XsrRuntimeErrors.RouteNotFound());
            lock (_gate) if (!ReferenceEquals(_binding, binding)) return XsrResult.Failure<T>(XsrRuntimeErrors.Cancelled());
            return XsrResult.Success(value);
        }
        catch (OperationCanceledException) { return XsrResult.Failure<T>(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        { return XsrResult.Failure<T>(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Shell operation was rejected.")); }
    }
    public void Dispose()
    {
        Binding? binding;
        lock (_gate) { if (_disposed) return; _disposed = true; binding = _binding; _binding = null; }
        binding?.Dispose();
    }
    private sealed class Binding : IDisposable
    {
        private readonly SidecarShellApi _owner;
        private readonly CancellationTokenSource _stop = new();
        private int _disposed;
        internal SidecarShellActions Actions { get; }
        internal CancellationToken Lifetime { get; }
        internal Binding(SidecarShellApi owner, SidecarShellActions actions)
        { _owner = owner; Actions = actions; Lifetime = _stop.Token; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_owner._gate) if (ReferenceEquals(_owner._binding, this)) _owner._binding = null;
            _stop.Cancel(); _stop.Dispose();
        }
    }
    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
}
