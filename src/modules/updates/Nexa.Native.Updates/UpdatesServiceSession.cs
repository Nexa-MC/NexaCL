using Nexa.Native.Network.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Native.Updates.Services;
using Nexa.Platform.Updates;
using Nexa.Services.Rollouts;
using Nexa.Services.Updates;
using Nexa.Xsr.State;

namespace Nexa.Native.Updates;

internal sealed partial class UpdatesServiceSession : IAsyncDisposable
{
    private readonly ServiceEndpoint _endpoint;
    private readonly ServiceStatePublisher _publications;
    private readonly NetworkHttpStreamHandler _httpHandler;
    private readonly HttpClient _http;
    private readonly RemoteWorkScheduler _work;
    private readonly SemaphoreSlim _configuration = new(1);
    private readonly XsrStateStore _store;
    private UpdateGpgVerifier? _verifier;
    private SidecarUpdateService? _sidecars;
    private RolloutService? _rollouts;
    private NexaUpdateService? _updates;
    private IAutomaticUpdateControl? _control;
    private Nexa.Services.Community.LauncherCommunityService? _community;
    private bool _initialized, _closing;
    private long _sidecarRevision;
    internal UpdatesServiceSession(NativeUiCallTable peer)
    {
        _endpoint = new(ExecuteAsync, _ => [], error => Console.Error.WriteLine("Native Updates operation failed: " + error.GetType().Name)); _endpoint.Connect(peer);
        _publications = new(_endpoint); _work = new(_endpoint);
        _httpHandler = new((request, token) => _endpoint.InvokeAsync(request, token), _ => []);
        _http = new(_httpHandler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(20) };
        XsrStateStoreBuilder builder = new(); RolloutStateContract.DeclareState(builder); _store = builder.Build(); _store.Changed += Changed;
        _telemetryPump = Task.Run(PumpTelemetryAsync);
    }
    internal NativeUiCallTable Table => _endpoint.Table;
    private async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var envelope = new ServiceWireWriter();
        try { byte[] result = await ExecuteCoreAsync(bytes, token).ConfigureAwait(false); envelope.Int32(0); envelope.Buffer(result); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or UnauthorizedAccessException or IOException or InvalidOperationException or NotSupportedException)
        {
            envelope.Int32(error switch { ArgumentNullException => 1, ArgumentOutOfRangeException => 2, ArgumentException => 3, InvalidDataException => 4, UnauthorizedAccessException => 5, IOException => 6, InvalidOperationException => 7, _ => 8 });
            envelope.String(error.Message); envelope.String((error as ArgumentException)?.ParamName);
        }
        finally { await _publications.FlushAsync(CancellationToken.None).ConfigureAwait(false); await FlushTelemetryAsync().ConfigureAwait(false); }
        return envelope.Finish();
    }
    private async Task<byte[]> ExecuteCoreAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
        using var reader = UpdatesServiceProtocol.Read(bytes, out int operation); using var writer = new ServiceWireWriter();
        if (operation == UpdatesServiceProtocol.Initialize)
        {
            var config = reader.NativeUpdatesInitialization() ?? throw new InvalidDataException("Missing Updates initialization."); reader.End();
            if (!Path.IsPathFullyQualified(config.BaseDirectory) || config.PublicKey.Length is 0 or > 65536 || config.Fingerprint.Length != 40 || !config.Fingerprint.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid Updates initialization.");
            await _configuration.WaitAsync(token).ConfigureAwait(false);
            try { ObjectDisposedException.ThrowIf(_closing, this); if (_initialized) throw new InvalidOperationException("Updates is already initialized."); AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", config.BaseDirectory); _verifier = new(config.PublicKey, config.Fingerprint); _initialized = true; }
            finally { _configuration.Release(); }
            return [];
        }
        if (!_initialized) throw new InvalidOperationException("Updates is not initialized.");
        if (operation is UpdatesServiceProtocol.ConfigureSidecars or UpdatesServiceProtocol.ConfigureDiscovery or UpdatesServiceProtocol.ConfigureControl or UpdatesServiceProtocol.ConfigureCommunity)
        {
            await _configuration.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                switch (operation)
                {
                    case UpdatesServiceProtocol.ConfigureCommunity:
                        string statePath = reader.String() ?? ""; reader.End();
                        if (_community is not null || !Path.IsPathFullyQualified(statePath)) throw new InvalidOperationException("Invalid community admission.");
                        _community = new(statePath, _http); break;
                    case UpdatesServiceProtocol.ConfigureSidecars:
                        var sidecar = reader.NativeSidecarUpdateConfiguration() ?? throw new InvalidDataException("Missing Sidecar configuration."); reader.End();
                        if (_sidecars is not null || !Path.IsPathFullyQualified(sidecar.Root) || !Path.IsPathFullyQualified(sidecar.Bundled)) throw new InvalidOperationException("Invalid Sidecar admission.");
                        _sidecars = new(_http, _verifier!, sidecar.Root, sidecar.Bundled, sidecar.RuntimeId, sidecar.HostVersion); _sidecars.SnapshotChanged += PublishSidecars; PublishSidecars(_sidecars.Snapshot); break;
                    case UpdatesServiceProtocol.ConfigureDiscovery:
                        var discovery = reader.NativeUpdateDiscoveryConfiguration() ?? throw new InvalidDataException("Missing discovery configuration."); reader.End();
                        if (_rollouts is not null || !Path.IsPathFullyQualified(discovery.SeedPath)) throw new InvalidOperationException("Invalid discovery admission.");
                        _rollouts = new(_http, _store, discovery.SeedPath, discovery.Channel, discovery.RuntimeId) { WorkScheduler = discovery.UseHostWork ? _work : null, Record = Record };
                        _updates = new(_http, _rollouts) { WorkScheduler = discovery.UseHostWork ? _work : null, Record = Record }; break;
                    case UpdatesServiceProtocol.ConfigureControl:
                        var control = reader.NativeAutomaticUpdateConfiguration() ?? throw new InvalidDataException("Missing update control configuration."); reader.End();
                        if (_control is not null || control.PortableRoot is not null && !Path.IsPathFullyQualified(control.PortableRoot)) throw new InvalidOperationException("Invalid update control admission.");
                        _control = control.PortableRoot is { } root ? new PortableUpdateControl(root, control.Running, _verifier!, new GitHubUpdateReleaseSource(_http)) : new AutomaticUpdateControl(new AutomaticUpdateHost()); break;

                }
            }
            finally { _configuration.Release(); }
            return [];
        }
        switch (operation)
        {
            case UpdatesServiceProtocol.StartCommunity:
                reader.End(); writer.LauncherCommunitySnapshot(await (_community ?? throw new InvalidOperationException("Community is not configured.")).StartAsync(token).ConfigureAwait(false)); break;
            case UpdatesServiceProtocol.AcknowledgeCommunity:
                var announcement = reader.LauncherAnnouncement() ?? throw new InvalidDataException("Missing announcement."); reader.End();
                await (_community ?? throw new InvalidOperationException("Community is not configured.")).AcknowledgeAsync(announcement, token).ConfigureAwait(false); break;
            case UpdatesServiceProtocol.ActiveSidecars:
                reader.End(); writer.NativeSidecarPackages(new(await Sidecars.ActivePackagesAsync(token).ConfigureAwait(false))); break;
            case UpdatesServiceProtocol.CheckSidecars:
                string channel = reader.String() ?? ""; reader.End(); await Sidecars.CheckAsync(channel, token).ConfigureAwait(false); break;
            case UpdatesServiceProtocol.InstallSidecar:
                string package = reader.String() ?? ""; reader.End(); await Sidecars.InstallAsync(package, token).ConfigureAwait(false); break;
            case UpdatesServiceProtocol.CheckUpdate:
                var query = reader.NexaUpdateQuery() ?? throw new InvalidDataException("Missing update query."); reader.End();
                var result = await (_updates ?? throw new InvalidOperationException("Discovery is not configured.")).CheckAsync(query, token).ConfigureAwait(false);
                writer.XsrError(result.Error); if (result.IsSuccess) writer.NexaUpdateStatus(result.Value); break;
            case UpdatesServiceProtocol.StartRollouts:
                reader.End(); (_rollouts ?? throw new InvalidOperationException("Discovery is not configured.")).Start(); break;
            case UpdatesServiceProtocol.RefreshRollouts:
                reader.End(); await (_rollouts ?? throw new InvalidOperationException("Discovery is not configured.")).RefreshAsync(token).ConfigureAwait(false); break;
            case UpdatesServiceProtocol.ReadControl:
                reader.End(); writer.AutomaticUpdateStatus(await Control.ReadAsync(token).ConfigureAwait(false)); break;
            case UpdatesServiceProtocol.InstallControl:
                var install = reader.NativeUpdateInstallRequest() ?? throw new InvalidDataException("Missing update install request."); reader.End(); writer.AutomaticUpdateStatus(await Control.InstallAsync(install.Version, install.Channel, token).ConfigureAwait(false)); break;
            case UpdatesServiceProtocol.RollbackControl:
                reader.End(); writer.AutomaticUpdateStatus(await Control.RollbackAsync(token).ConfigureAwait(false)); break;
            default: throw new InvalidDataException("Unknown Updates operation.");
        }
        return writer.Finish();
    }
    private SidecarUpdateService Sidecars => _sidecars ?? throw new InvalidOperationException("Sidecar updates are not configured.");
    private IAutomaticUpdateControl Control => _control ?? throw new InvalidOperationException("Update control is not configured.");
    private void PublishSidecars(SidecarUpdateSnapshot snapshot)
    {
        long revision = Interlocked.Increment(ref _sidecarRevision);
        _publications.Post(0, revision, () =>
        { using var writer = UpdatesServiceProtocol.Request(UpdatesServiceProtocol.SidecarState); writer.Int64(revision); writer.SidecarUpdateSnapshot(snapshot); return writer.Finish(); });
    }
    private void Changed(XsrStateChange change)
    {
        if (change.SemanticId != RolloutStateContract.StateKey) return;
        var snapshot = _store.Read<RolloutSnapshot>(_store.Resolve(change.SemanticId));
        var projection = new NativeRolloutProjection(snapshot.Value!, _rollouts?.EffectiveUntil ?? DateTimeOffset.MinValue);
        _publications.Post(1, snapshot.Revision, () =>
        { using var writer = UpdatesServiceProtocol.Request(UpdatesServiceProtocol.RolloutState); writer.Int64(snapshot.Revision); writer.NativeRolloutProjection(projection); return writer.Finish(); });
    }
    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _closing, true);
        await _configuration.WaitAsync().ConfigureAwait(false); _configuration.Release();
        if (_rollouts is not null) await _rollouts.DisposeAsync().ConfigureAwait(false);
        await _endpoint.StopAsync().ConfigureAwait(false);
        if (_community is not null) await _community.DisposeAsync().ConfigureAwait(false);
        _telemetry.Writer.TryComplete(); await _telemetryPump.ConfigureAwait(false);
        _sidecars?.Dispose();
        _store.Changed -= Changed; await _publications.DisposeAsync().ConfigureAwait(false);
        _http.Dispose(); await _httpHandler.DisposeAsync().ConfigureAwait(false);
        await _work.DisposeAsync().ConfigureAwait(false); await _endpoint.DisposeAsync().ConfigureAwait(false); _configuration.Dispose();
    }
}
