using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Network.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Native.Updates.Services;
using Nexa.Platform.Updates;
using Nexa.Services.Rollouts;
using Nexa.Services.Scheduling;
using Nexa.Services.Updates;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Updates.Client;

public sealed partial class NativeUpdatesClient : IAsyncDisposable, IUpdateTelemetrySource
{
    private readonly object _gate = new();
    private readonly ServiceEndpoint _endpoint;
    private readonly NetworkHttpStreamServer _http;
    private HostWorkLeaseServer? _work;
    private readonly nint _close;
    private readonly long _context;
    private readonly Task _initialization;
    private readonly Action<Exception>? _failure;
    private Task? _retirement;
    private bool _closed;
    private SidecarUpdateSnapshot _sidecars = new("initial", []);
    private NativeRolloutProjection _rollout = new(new(0, false, false), DateTimeOffset.MinValue);
    private long _sidecarRevision, _rolloutRevision, _telemetrySequence;
    private XsrStateStore? _replicas;
    public Action<string, string>? Record { get; set; }
    public SidecarUpdateSnapshot SidecarSnapshot => Volatile.Read(ref _sidecars);
    public bool CompactTelemetryBatches { get { var current = Volatile.Read(ref _rollout); return current.Snapshot.CompactTelemetryBatches && current.EffectiveUntil > DateTimeOffset.UtcNow; } }
    public NativeUpdatesClient(NativeUpdatesInitialization configuration, HttpClient http, IWorkScheduler? work = null, Action<Exception>? failure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(http); _failure = failure;
        _http = new((request, _, token) => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token));
        _endpoint = new(ExecuteReverseAsync, ApplyPublication, Report, RejectReverse);
        if (work is not null) _work = new(work, null, Report);
        try { (_context, _close) = Open(_endpoint); } catch { _ = _endpoint.DisposeAsync().AsTask(); throw; }
        _initialization = InitializeAsync(configuration);
        _ = _initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public Task Initialization => _initialization;
    public Task ConfigureCommunityAsync(string statePath, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.ConfigureCommunity, writer => writer.String(statePath), token);
    public Task AcknowledgeCommunityAsync(Nexa.Services.Community.LauncherAnnouncement announcement, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.AcknowledgeCommunity, writer => writer.LauncherAnnouncement(announcement), token);
    public async Task<Nexa.Services.Community.LauncherCommunitySnapshot> StartCommunityAsync(CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(UpdatesServiceProtocol.StartCommunity, _ => { }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.LauncherCommunitySnapshot() ?? throw new InvalidDataException("Missing community snapshot."); reader.End(); return result;
    }
    public void AttachWork(IWorkScheduler work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); if (_work is not null) throw new InvalidOperationException("Updates work is already attached."); _work = new(work, null, Report); }
    }
    public void AttachRolloutReplicas(XsrStateStore replicas)
    {
        ArgumentNullException.ThrowIfNull(replicas);
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); if (_replicas is not null) throw new InvalidOperationException("Updates rollout replicas are already attached."); _replicas = replicas; }
    }
    private void Report(Exception error) { try { _failure?.Invoke(error); } catch { } }
    private async Task InitializeAsync(NativeUpdatesInitialization configuration)
    {
        using var writer = UpdatesServiceProtocol.Request(UpdatesServiceProtocol.Initialize); writer.NativeUpdatesInitialization(configuration);
        byte[] bytes = await InvokeFrameAsync(writer.Finish(), CancellationToken.None).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); reader.End();
    }
    private async Task<byte[]> InvokeAsync(int operation, Action<ServiceWireWriter> write, CancellationToken token)
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        await _initialization.WaitAsync(token).ConfigureAwait(false);
        using var writer = UpdatesServiceProtocol.Request(operation); write(writer); return await InvokeFrameAsync(writer.Finish(), token).ConfigureAwait(false);
    }
    private async Task<byte[]> InvokeFrameAsync(byte[] request, CancellationToken token)
    {
        byte[] bytes = await _endpoint.InvokeAsync(request, token).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); int kind = reader.Int32();
        if (kind == 0) { byte[] result = reader.Buffer(); reader.End(); return result; }
        string message = reader.String() ?? "Updates request failed."; string? parameter = reader.String(); reader.End();
        throw kind switch { 1 => new ArgumentNullException(parameter, message), 2 => new ArgumentOutOfRangeException(parameter, message), 3 => new ArgumentException(message, parameter), 4 => new InvalidDataException(message), 5 => new UnauthorizedAccessException(message), 6 => new IOException(message), 7 => new InvalidOperationException(message), 8 => new NotSupportedException(message), _ => new InvalidDataException("Invalid Updates error envelope.") };
    }
    private async Task EmptyAsync(int operation, Action<ServiceWireWriter> write, CancellationToken token)
    { byte[] bytes = await InvokeAsync(operation, write, token).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); reader.End(); }
    public Task ConfigureSidecarsAsync(NativeSidecarUpdateConfiguration configuration, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.ConfigureSidecars, writer => writer.NativeSidecarUpdateConfiguration(configuration), token);
    public Task ConfigureDiscoveryAsync(NativeUpdateDiscoveryConfiguration configuration, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.ConfigureDiscovery, writer => writer.NativeUpdateDiscoveryConfiguration(configuration), token);
    public Task ConfigureControlAsync(NativeAutomaticUpdateConfiguration configuration, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.ConfigureControl, writer => writer.NativeAutomaticUpdateConfiguration(configuration), token);
    public Task CheckSidecarsAsync(string channel, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.CheckSidecars, writer => writer.String(channel), token);
    public Task InstallSidecarAsync(string package, CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.InstallSidecar, writer => writer.String(package), token);
    public Task StartRolloutsAsync(CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.StartRollouts, _ => { }, token);
    public Task RefreshRolloutsAsync(CancellationToken token = default) => EmptyAsync(UpdatesServiceProtocol.RefreshRollouts, _ => { }, token);
    public async Task<IReadOnlyDictionary<string, string>> ActivePackagesAsync(CancellationToken token = default)
    { byte[] bytes = await InvokeAsync(UpdatesServiceProtocol.ActiveSidecars, _ => { }, token).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); var result = reader.NativeSidecarPackages() ?? throw new InvalidDataException("Missing active Sidecar packages."); reader.End(); return result.Packages; }
    public async Task<XsrResult<NexaUpdateStatus>> CheckAsync(NexaUpdateQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(UpdatesServiceProtocol.CheckUpdate, writer => writer.NexaUpdateQuery(query), token).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.NexaUpdateStatus() ?? throw new InvalidDataException("Missing update status.")) : XsrResult.Failure<NexaUpdateStatus>(error); reader.End(); return result;
    }
    public Task<AutomaticUpdateStatus> ReadControlAsync(CancellationToken token = default) => ControlAsync(UpdatesServiceProtocol.ReadControl, null, token);
    public Task<AutomaticUpdateStatus> InstallControlAsync(string version, string channel, CancellationToken token = default) => ControlAsync(UpdatesServiceProtocol.InstallControl, new(version, channel), token);
    public Task<AutomaticUpdateStatus> RollbackControlAsync(CancellationToken token = default) => ControlAsync(UpdatesServiceProtocol.RollbackControl, null, token);
    private async Task<AutomaticUpdateStatus> ControlAsync(int operation, NativeUpdateInstallRequest? request, CancellationToken token)
    { byte[] bytes = await InvokeAsync(operation, writer => { if (request is not null) writer.NativeUpdateInstallRequest(request); }, token).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); var result = reader.AutomaticUpdateStatus() ?? throw new InvalidDataException("Missing automatic update status."); reader.End(); return result; }
    private async Task<byte[]> ExecuteReverseAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = UpdatesServiceProtocol.Read(bytes, out int operation);
        if (operation == UpdatesServiceProtocol.Telemetry)
        {
            long sequence = reader.Int64(); string name = reader.String() ?? "", outcome = reader.String() ?? ""; reader.End();
            lock (_gate) { if (sequence != _telemetrySequence + 1) throw new InvalidDataException("Invalid update telemetry sequence."); _telemetrySequence = sequence; }
            try { Record?.Invoke(name, outcome); } catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Report(error); }
            return [];
        }
        if (operation is >= DownloadProtocol.AcquireWork and <= DownloadProtocol.ObserveDiskWrite or >= DownloadProtocol.AcquireQuiet and <= DownloadProtocol.GraceQuiet)
            return await (Volatile.Read(ref _work) ?? throw new InvalidOperationException("Updates work is not attached.")).ExecuteAsync(bytes, token).ConfigureAwait(false);
        if (operation is >= NetworkHttpWire.Create and <= NetworkHttpWire.Release) return await _http.ExecuteAsync(operation, reader, token).ConfigureAwait(false);
        throw new InvalidDataException("Unknown Updates reverse operation.");
    }
    private byte[] ApplyPublication(ReadOnlyMemory<byte> bytes)
    {
        using var reader = UpdatesServiceProtocol.Read(bytes, out int operation); long revision = reader.Int64();
        if (revision <= 0) throw new InvalidDataException("Invalid update state revision.");
        lock (_gate)
        {
            if (operation == UpdatesServiceProtocol.SidecarState)
            { var value = reader.SidecarUpdateSnapshot() ?? throw new InvalidDataException("Missing Sidecar state."); reader.End(); if (revision <= _sidecarRevision) throw new InvalidDataException("Stale Sidecar state."); _sidecarRevision = revision; Volatile.Write(ref _sidecars, value); }
            else if (operation == UpdatesServiceProtocol.RolloutState)
            {
                var value = reader.NativeRolloutProjection() ?? throw new InvalidDataException("Missing rollout state."); reader.End(); if (revision <= _rolloutRevision) throw new InvalidDataException("Stale rollout state."); _rolloutRevision = revision; Volatile.Write(ref _rollout, value);
                if (_replicas is { } store) store.Replicate(store.Resolve(RolloutStateContract.StateKey), revision, XsrStateAvailability.Available, true, value.Snapshot);
            }
            else throw new InvalidDataException("Unknown update publication.");
        }
        return [];
    }
    private void RejectReverse(long id, ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> result)
    {
        _work?.Reject(id, request, result);
        using var reader = UpdatesServiceProtocol.Read(request, out int operation); if (operation != NetworkHttpWire.Create) return;
        using var response = new ServiceWireReader(result); long handle = response.Int64(); response.End(); _ = RejectHttpAsync(handle);
    }
    private async Task RejectHttpAsync(long handle) { try { await _http.RejectAsync(handle).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Report(error); } }
    private static unsafe (long Context, nint Close) Open(ServiceEndpoint endpoint)
    {
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint image = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-updates" + suffix));
        var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, "nexa_updates_service_identity_v1");
        byte[] expected = Encoding.UTF8.GetBytes(UpdatesServiceProtocol.Identity); if (identity(null, 0) != expected.Length) throw new InvalidDataException("Incompatible native Updates schema.");
        byte[] actual = new byte[expected.Length]; fixed (byte* output = actual) if (identity(output, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Incompatible native Updates schema.");
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, int>)NativeLibrary.GetExport(image, "nexa_updates_service_open_v1");
        NativeUiCallTable peer = endpoint.Table, owner = default; if (open(&peer, &owner) != 0) throw new IOException("Unable to open native Updates.");
        endpoint.Connect(owner); return (owner.Context, NativeLibrary.GetExport(image, "nexa_updates_service_close_v1"));
    }
    private unsafe int Close() => ((delegate* unmanaged[Cdecl]<long, int>)_close)(_context);
    public ValueTask DisposeAsync() { lock (_gate) { _closed = true; return new(_retirement ??= RetireAsync()); } }
    private async Task RetireAsync()
    {
        int delay = 1;
        while (true) { int status = Close(); if (status == 1) break; if (status != 0) throw new IOException("Native Updates shutdown failed; callbacks remain retained."); await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
        await _http.DisposeAsync().ConfigureAwait(false); if (_work is { } work) await work.DisposeAsync().ConfigureAwait(false); await _endpoint.DisposeAsync().ConfigureAwait(false);
    }
}
