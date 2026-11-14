using Nexa.Services.Themes;
using Nexa.Sidecar.Host.ThemeOptions.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Theme configuration for authenticated Sidecars, independent of any guest SDK.</summary>
public sealed partial class SidecarThemeOptionsApi : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private Binding? _binding;
    private bool _disposed;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarThemeOptionsApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(2047, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "themes.options.catalog"),
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

    public IDisposable Bind(SidecarThemeOptionsActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Save);
        ArgumentNullException.ThrowIfNull(actions.Reset);
        ArgumentNullException.ThrowIfNull(actions.Import);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("Theme option actions are already bound.");
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
        if (domain != "themeoptions" || Resolve(caller) is null) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<ThemeOptionsSaveCommand>(SidecarThemeOptionsRoutes.Save, (input, token) => Execute(caller, (actions, cancellation) =>
        {
            ValidateSource(input.SourceIdentity, input.ExpectedRevision);
            return actions.Save(input with { Values = ValidateOptions(input.Values) }, cancellation);
        }, token));
        routes.Register<ThemeOptionsResetCommand>(SidecarThemeOptionsRoutes.Reset, (input, token) => Execute(caller, (actions, cancellation) =>
        {
            ValidateSource(input.SourceIdentity, input.ExpectedRevision);
            return actions.Reset(input, cancellation);
        }, token));
        routes.Register<HostThemeOptionsImport>(SidecarThemeOptionsRoutes.Import, (input, token) => Execute(caller, (actions, cancellation) =>
        {
            ValidateSource(input.SourceIdentity, input.ExpectedRevision);
            if (string.IsNullOrWhiteSpace(input.ThemeId) || input.ThemeId.Length > 4096 || input.ThemeId.Contains('\0'))
                throw new ArgumentException("Invalid imported theme identity.");
            return actions.Import(input with { Values = ValidateOptions(input.Values) }, cancellation);
        }, token));
        return routes.Build(_observer);
    }
    private static void ValidateSource(string identity, long revision)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 4096 || identity.Contains('\0') || revision < 0)
            throw new ArgumentException("Invalid theme source or revision.");
    }
    private static Dictionary<string, ThemeOptionValue> ValidateOptions(IReadOnlyDictionary<string, ThemeOptionValue> values)
    {
        if (values is null || values.Count > 256) throw new ArgumentException("Invalid option count.");
        Dictionary<string, ThemeOptionValue> owned = new(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (key.Length is < 1 or > 128 || value is null || !Enum.IsDefined(value.Kind)
                || value.Value is null || value.Value.Length > 4096 || value.Value.Contains('\0')
                || value.Kind != ThemeOptionKind.Text && value.Value.Any(char.IsControl)
                || value.Kind != ThemeOptionKind.Files && value.Values is not null
                || value.Kind == ThemeOptionKind.Files && (value.Value.Length != 0 || value.Values is null)
                || value.Values is { Count: > 256 } || value.Values?.Any(item => item is null || item.Length > 4096 || item.Any(char.IsControl)) == true)
                throw new ArgumentException("Invalid theme option value.");
            owned.Add(key, value with { Values = value.Values?.ToArray() });
        }
        return owned;
    }
    private async ValueTask<XsrResult> Execute(SidecarHostCaller caller,
        Func<SidecarThemeOptionsActions, CancellationToken, Task<XsrResult>> action, CancellationToken token)
    {
        var session = Resolve(caller);
        if (session is null) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
        Binding? binding;
        lock (_gate) binding = _binding;
        if (binding is null) return XsrResult.Failure(new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "Theme configuration UI is unavailable."));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, binding.Lifetime);
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            var result = await action(binding.Actions, stop.Token).WaitAsync(stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            if (Resolve(caller) != session) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
            lock (_gate) if (!ReferenceEquals(_binding, binding)) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
            return result;
        }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        { return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Theme configuration operation was rejected.")); }
    }
    public void Dispose()
    {
        Binding? binding;
        lock (_gate) { if (_disposed) return; _disposed = true; binding = _binding; _binding = null; }
        binding?.Dispose();
    }
    private sealed class Binding : IDisposable
    {
        private readonly SidecarThemeOptionsApi _owner;
        private readonly CancellationTokenSource _stop = new();
        private int _disposed;
        internal SidecarThemeOptionsActions Actions { get; }
        internal CancellationToken Lifetime { get; }
        internal Binding(SidecarThemeOptionsApi owner, SidecarThemeOptionsActions actions)
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
