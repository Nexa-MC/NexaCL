using Nexa.Sidecar.Host.ThemeActivation.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General theme activation with session-owned preview compensation.</summary>
public sealed partial class SidecarThemeActivationApi : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly Action<XsrError>? _cleanupFailed;
    private readonly HashSet<Operation> _operations = [];
    private readonly Dictionary<(SidecarHostSession, CancellationToken), Owner> _owners = [];
    private Binding? _binding;
    private bool _disposed;
    private Task? _disposeTask;
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarThemeActivationApi(Func<SidecarHostCaller, SidecarHostSession?> resolve,
        IXsrDispatchObserver observer, Action<XsrError>? cleanupFailed = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _cleanupFailed = cleanupFailed;
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(2303, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "themes.activation.catalog"),
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
    public IDisposable Bind(SidecarThemeActivationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Activate);
        ArgumentNullException.ThrowIfNull(actions.EndPreview);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("Theme activation is already bound.");
            return _binding = new(this, actions);
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
    private Owner? GetOwner(SidecarHostCaller caller)
    {
        var session = Resolve(caller);
        if (session is null) return null;
        CancellationToken lifetime;
        try { lifetime = session.CaptureActivationLifetime(); }
        catch (InvalidOperationException) { return null; }
        Owner owner;
        lock (_gate)
        {
            if (_disposed || lifetime.IsCancellationRequested) return null;
            if (!_owners.TryGetValue((session, lifetime), out owner!))
            {
                if (_owners.Count >= 128) return null;
                _owners.Add((session, lifetime), owner = new(this, session, lifetime));
            }
        }
        owner.Observe();
        return owner;
    }
    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "themeactivation" || Resolve(caller) is null) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostThemeActivation>(SidecarThemeActivationRoutes.Activate, (input, token) => Activate(caller, input, 0, token));
        routes.Register<HostThemePreview>(SidecarThemeActivationRoutes.Preview, (input, token) => input.LeaseId <= 0
            ? ValueTask.FromResult(XsrResult.Failure(Rejected("A positive preview lease ID is required.")))
            : Activate(caller, input.Theme, input.LeaseId, token));
        routes.Register<HostThemePreviewEnd>(SidecarThemeActivationRoutes.EndPreview, (input, token) => End(caller, input.LeaseId, token));
        return routes.Build(_observer);
    }
    private async ValueTask<XsrResult> Activate(SidecarHostCaller caller, HostThemeActivation input, long leaseId, CancellationToken token)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.SourceIdentity) || input.SourceIdentity.Length > 4096 || input.SourceIdentity.Contains('\0')
            || string.IsNullOrWhiteSpace(input.SourceRevision) || input.SourceRevision.Length > 4096 || input.SourceRevision.Contains('\0') || input.ExpectedRevision < 0)
            return XsrResult.Failure(Rejected("Invalid theme source or revision."));
        var owner = GetOwner(caller);
        if (owner is null) return XsrResult.Failure(Unavailable());
        Operation operation;
        lock (_gate)
        {
            if (!owner.IsActive) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
            if (_binding is not { } binding) return XsrResult.Failure(Unavailable());
            if (leaseId > 0 && leaseId <= owner.LastId) return XsrResult.Failure(Rejected("Preview lease IDs must increase and cannot be reused."));
            if (_operations.Count >= 64) return XsrResult.Failure(new(XsrErrorKind.Backpressure, XsrSemanticId.Parse("xsr.backpressure"), "Theme operation capacity is exhausted."));
            if (leaseId > 0) owner.LastId = leaseId;
            operation = new(owner, binding, input, leaseId, token);
            owner.Operations.Add(operation); _operations.Add(operation);
        }
        operation.Start();
        using var cancelled = token.Register(operation.Retire);
        bool retained = false;
        try
        {
            var result = await operation.Activated.Task.WaitAsync(operation.Lifetime).ConfigureAwait(false);
            operation.Lifetime.ThrowIfCancellationRequested();
            if (!result.IsSuccess) return XsrResult.Failure(result.Error!);
            bool retired;
            lock (_gate) retired = !owner.IsActive || !ReferenceEquals(_binding, operation.Binding) || leaseId > 0 && operation.Retired;
            if (retired) { operation.Retire(); return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            retained = leaseId > 0;
            return XsrResult.Success();
        }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        finally { if (!retained) operation.Retire(); }
    }
    private async ValueTask<XsrResult> End(SidecarHostCaller caller, long leaseId, CancellationToken token)
    {
        if (leaseId <= 0) return XsrResult.Failure(Rejected("A positive preview lease ID is required."));
        token.ThrowIfCancellationRequested();
        var owner = GetOwner(caller);
        if (owner is null) return XsrResult.Failure(Unavailable());
        Operation? operation;
        lock (_gate)
        {
            if (!owner.IsActive) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
            // A release arriving before its delayed activation must fence that activation.
            owner.LastId = Math.Max(owner.LastId, leaseId);
            operation = owner.Operations.FirstOrDefault(value => value.LeaseId == leaseId);
        }
        if (operation is null) return XsrResult.Success();
        operation.Retire();
        try { return await operation.Ended.Task.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            _disposed = true;
            var binding = _binding; _binding = null;
            return new(_disposeTask = DrainAsync(binding, _owners.Values.ToArray(), _operations.ToArray()));
        }
    }
    private async Task DrainAsync(Binding? binding, Owner[] owners, Operation[] operations)
    {
        await Task.Yield();
        binding?.Dispose();
        foreach (var owner in owners) owner.Retire();
        foreach (var operation in operations) operation.Retire();
        try { await Task.WhenAll(operations.Select(value => value.Drained.Task)).WaitAsync(CleanupBudget).ConfigureAwait(false); }
        catch (TimeoutException) { Report(new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "Theme cleanup did not drain before shutdown.")); }
    }
    private void Report(XsrError error)
    {
        try { _cleanupFailed?.Invoke(error); }
        catch (Exception failure) when (failure is not OutOfMemoryException and not AccessViolationException) { }
    }
    private static XsrError Rejected(string message) => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), message);
    private static XsrError Unavailable() => new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "Theme activation UI is unavailable.");
    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }

    private sealed class Binding : IDisposable
    {
        internal readonly SidecarThemeActivationApi Api;
        internal readonly SidecarThemeActivationActions Actions;
        private readonly CancellationTokenSource _stop = new();
        internal CancellationToken Lifetime { get; }
        private bool _retired;
        internal Binding(SidecarThemeActivationApi api, SidecarThemeActivationActions actions)
        { Api = api; Actions = actions; Lifetime = _stop.Token; }
        public void Dispose()
        {
            Operation[] operations;
            lock (Api._gate)
            {
                if (_retired) return; _retired = true;
                if (ReferenceEquals(Api._binding, this)) Api._binding = null;
                operations = Api._operations.Where(value => ReferenceEquals(value.Binding, this)).ToArray();
            }
            try { _stop.Cancel(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Api.Report(Rejected("Theme binding cancellation failed.")); }
            finally
            {
                foreach (var operation in operations) operation.Retire();
                _stop.Dispose();
            }
        }
    }
    private sealed class Owner(SidecarThemeActivationApi api, SidecarHostSession session, CancellationToken lifetime)
    {
        internal SidecarThemeActivationApi Api { get; } = api;
        internal CancellationToken Lifetime { get; } = lifetime;
        internal readonly HashSet<Operation> Operations = [];
        internal long LastId;
        private bool _retired;
        private int _observed;
        private CancellationTokenRegistration _registration;
        internal bool IsActive => !_retired && !Lifetime.IsCancellationRequested && !Api._disposed;
        internal void Observe()
        {
            if (Interlocked.Exchange(ref _observed, 1) != 0) return;
            var registration = Lifetime.Register(Retire);
            bool retired;
            lock (Api._gate) { retired = _retired; if (!retired) _registration = registration; }
            if (retired) registration.Unregister();
        }
        internal void Retire()
        {
            Operation[] operations; CancellationTokenRegistration registration;
            lock (Api._gate)
            {
                if (_retired) return; _retired = true;
                Api._owners.Remove((session, Lifetime));
                registration = _registration; _registration = default;
                operations = Operations.ToArray();
            }
            registration.Unregister();
            foreach (var operation in operations) operation.Retire();
        }
    }
    private sealed class Operation
    {
        private readonly Owner _owner;
        internal Binding Binding { get; }
        private readonly HostThemeActivation _input;
        private readonly CancellationTokenSource _stop;
        private int _retired;
        internal long LeaseId { get; }
        internal CancellationToken Lifetime { get; }
        internal bool Retired => Volatile.Read(ref _retired) != 0;
        internal TaskCompletionSource<XsrResult<long>> Activated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<XsrResult> Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Operation(Owner owner, Binding binding, HostThemeActivation input, long leaseId, CancellationToken token)
        { _owner = owner; Binding = binding; _input = input; LeaseId = leaseId; _stop = CancellationTokenSource.CreateLinkedTokenSource(token, owner.Lifetime, binding.Lifetime); Lifetime = _stop.Token; }
        internal void Start() => _ = ActivateAsync();
        private async Task ActivateAsync()
        {
            await Task.Yield();
            XsrResult<long> result;
            try
            {
                _stop.Token.ThrowIfCancellationRequested();
                result = await Binding.Actions.Activate(_input, LeaseId > 0, _stop.Token).ConfigureAwait(false);
                if (result.IsSuccess && LeaseId > 0 && result.Value <= 0) result = XsrResult.Failure<long>(Rejected("Invalid native preview identity."));
            }
            catch (OperationCanceledException) { result = XsrResult.Failure<long>(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { result = XsrResult.Failure<long>(Rejected("Theme activation failed.")); }
            Activated.TrySetResult(result);
        }
        internal void Retire()
        {
            if (Interlocked.Exchange(ref _retired, 1) != 0) return;
            try { _stop.Cancel(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { _owner.Api.Report(Rejected("Theme operation cancellation failed.")); }
            finally { _ = CleanupAsync(); }
        }
        private async Task CleanupAsync()
        {
            await Task.Yield();
            var result = XsrResult.Success();
            Task<XsrResult>? pending = null;
            try
            {
                var activated = await Activated.Task.ConfigureAwait(false);
                if (LeaseId > 0 && activated.IsSuccess)
                {
                    using var deadline = new CancellationTokenSource(CleanupBudget);
                    pending = Binding.Actions.EndPreview(activated.Value, deadline.Token);
                    result = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { result = XsrResult.Failure(new(XsrErrorKind.TimedOut, XsrSemanticId.Parse("xsr.timed_out"), "Theme preview cleanup timed out.")); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { result = XsrResult.Failure(Rejected("Theme preview cleanup failed.")); }
            if (!result.IsSuccess) _owner.Api.Report(result.Error!);
            Ended.TrySetResult(result);
            // A noncooperative native cleanup still owns its bounded admission slot.
            if (pending is not null)
                try { await pending.ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            _stop.Dispose();
            lock (_owner.Api._gate) { _owner.Operations.Remove(this); _owner.Api._operations.Remove(this); }
            Drained.TrySetResult();
        }
    }
}
