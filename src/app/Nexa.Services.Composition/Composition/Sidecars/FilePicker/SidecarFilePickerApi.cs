using System.Text;
using Nexa.Sidecar.Host.FilePicker.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>One native selection at a time, owned by an authenticated Sidecar activation.</summary>
public sealed partial class SidecarFilePickerApi : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private Binding? _binding;
    private bool _busy, _disposed;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarFilePickerApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _authorize = (caller, _) => Resolve(caller) is not null;
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(2815, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => _authorize(caller, "filepicker.catalog"),
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
    public IDisposable Bind(Func<SidecarHostCaller, HostFilePick, CancellationToken, Task<string?>> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("File selection is already bound.");
            return _binding = new(this, pick);
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
    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "filepicker" || Resolve(caller) is null) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostFilePick, HostFileSelection>(SidecarFilePickerRoutes.Pick, (input, token) => Pick(caller, input, token));
        return routes.Build(_observer);
    }
    private async ValueTask<XsrResult<HostFileSelection>> Pick(SidecarHostCaller caller, HostFilePick input, CancellationToken token)
    {
        if (input is null || !Enum.IsDefined(input.Kind) || input.Title is null || input.Title.Length > 256
            || input.Title.Any(char.IsControl) || input.Extensions is null || input.Extensions.Length > 32
            || input.Extensions.Any(extension => extension is null || extension.Length is < 1 or > 32
                || extension.All(character => character == '.')
                || extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.')))
            return Failure(XsrErrorKind.Rejected, "xsr.rejected", "Invalid file selection request.");
        var session = Resolve(caller);
        if (session is null) return XsrResult.Failure<HostFileSelection>(XsrRuntimeErrors.RouteNotFound());
        CancellationToken lifetime;
        try { lifetime = session.CaptureActivationLifetime(); }
        catch (InvalidOperationException) { return XsrResult.Failure<HostFileSelection>(XsrRuntimeErrors.RouteNotFound()); }
        Binding binding;
        lock (_gate)
        {
            if (_binding is null) return Failure(XsrErrorKind.Unavailable, "xsr.unavailable", "Native file selection is unavailable.");
            if (_busy) return Failure(XsrErrorKind.Backpressure, "xsr.backpressure", "A native selection is still open.");
            binding = _binding; _busy = true;
        }
        // The native operation owns its slot and CTS until it actually returns.
        // Cancelling the request cannot dismiss every OS picker implementation.
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token, binding.Lifetime, lifetime);
        stop.CancelAfter(TimeSpan.FromMinutes(2));
        var cancellation = stop.Token;
        var pending = RunNative();
        try { return await pending.WaitAsync(cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { return XsrResult.Failure<HostFileSelection>(XsrRuntimeErrors.Cancelled()); }

        async Task<XsrResult<HostFileSelection>> RunNative()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                string? path = await binding.Pick(caller, input with { Extensions = input.Extensions.ToArray() }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                lock (_gate) if (!ReferenceEquals(_binding, binding)) return XsrResult.Failure<HostFileSelection>(XsrRuntimeErrors.Cancelled());
                if (path is null) return XsrResult.Success(new HostFileSelection(null));
                if (path.Length > 32768 || path.Contains('\0') || !Path.IsPathFullyQualified(path))
                    return Failure(XsrErrorKind.Rejected, "xsr.rejected", "Native selection returned an invalid path.");
                return XsrResult.Success(new HostFileSelection(Utf8.GetBytes(path)));
            }
            catch (OperationCanceledException) { return XsrResult.Failure<HostFileSelection>(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { return Failure(XsrErrorKind.Unavailable, "xsr.unavailable", "Native file selection failed."); }
            finally
            {
                stop.Dispose();
                lock (_gate) _busy = false;
            }
        }
    }
    private static XsrResult<HostFileSelection> Failure(XsrErrorKind kind, string code, string message) =>
        XsrResult.Failure<HostFileSelection>(new(kind, XsrSemanticId.Parse(code), message));
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
        private readonly SidecarFilePickerApi _owner;
        private readonly CancellationTokenSource _stop = new();
        private int _disposed;
        internal Func<SidecarHostCaller, HostFilePick, CancellationToken, Task<string?>> Pick { get; }
        internal CancellationToken Lifetime { get; }
        internal Binding(SidecarFilePickerApi owner, Func<SidecarHostCaller, HostFilePick, CancellationToken, Task<string?>> pick)
        { _owner = owner; Pick = pick; Lifetime = _stop.Token; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_owner._gate) if (ReferenceEquals(_owner._binding, this)) _owner._binding = null;
            try { _stop.Cancel(); } finally { _stop.Dispose(); }
        }
    }
}
