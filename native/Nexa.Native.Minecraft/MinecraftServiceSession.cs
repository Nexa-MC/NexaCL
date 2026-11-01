using Nexa.Native.Minecraft.Http;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Network.Client;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Caching;
using Nexa.Services.Downloads;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Crash;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession : IAsyncDisposable
{
    private readonly ServiceEndpoint _endpoint;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _closing;
    private readonly SharedStateCache _cache = new();
    private readonly RemoteWorkScheduler _work;
    private readonly MinecraftVersionDiscovery _versions = new();
    private readonly XsrStateStore _store;
    private readonly ServiceStatePublisher _publications;
    private readonly TaskCenterService _tasks;
    private readonly NetworkHttpStreamHandler _hostHttp;
    private readonly HttpClient _http;
    private readonly HttpClient _metadataHttp;
    private readonly RemoteMinecraftDownloads _downloads;
    private readonly DownloadService _sessionDownloads;
    private MinecraftInstanceDiscovery? _instances;
    private MinecraftProcessService? _processes;
    private JavaRuntimeInventoryService? _java;
    private JavaRuntimeInstaller? _javaInstaller;
    private string? _catalogCacheDirectory;
    private long _javaProgressRevision;
    private readonly SemaphoreSlim _javaAdmission = new(32, 32);
    private readonly System.Collections.Concurrent.ConcurrentQueue<int> _javaSlots = new(Enumerable.Range(3, 32));

    internal MinecraftServiceSession(NativeUiCallTable peer)
    {
        _endpoint = new(ExecuteAsync, ReceiveHostPublication,
            error => Console.Error.WriteLine("Native Minecraft operation failed: " + error.GetType().Name), Rejected);
        _endpoint.Connect(peer);
        _work = new(_endpoint);
        _downloads = new(_endpoint);
        _hostHttp = new((request, token) => _endpoint.InvokeAsync(request, token), _ => []);
        _strictHostHttp = new((request, token) => _endpoint.InvokeAsync(request, token), _ =>
        { using var configuration = new ServiceWireWriter(); configuration.Boolean(false); return configuration.Finish(); });
        _settings = new(_endpoint);
#if NEXA_NATIVE_SERVICES
        _http = NativeHttpClients.CreateDescribed(_hostHttp, _downloads.DescribeAsync);
#else
        _http = new(_hostHttp);
#endif
        _http.Timeout = TimeSpan.FromMinutes(10);
        _metadataHttp = new(_hostHttp, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
        _metadataHttp.DefaultRequestHeaders.UserAgent.ParseAdd("NexaCL/2.0");
        XsrStateStoreBuilder builder = new(); MinecraftProcessStateComposition.DeclareState(builder); TaskCenterStateContract.DeclareState(builder); DownloadService.DeclareState(builder); MinecraftLaunchProgressState.DeclareState(builder);
        Nexa.Services.Minecraft.MinecraftLibraryContract.DeclareState(builder);
        Nexa.Services.Minecraft.Install.InstallCatalogStateContract.DeclareState(builder); Nexa.Services.Minecraft.Install.MinecraftInstallDraftContract.DeclareState(builder); _store = builder.Build();
        _sessionDownloads = new(_store);
#if NEXA_NATIVE_SERVICES
        _sessionDownloads.AttachNative(_downloads);
#endif
        _tasks = new(_store); _publications = new(_endpoint); _store.Changed += Changed;
        _installEventPump = Task.Run(PumpInstallEventsAsync);
    }
    internal NativeUiCallTable Table => _endpoint.Table;
    private byte[] ReceiveHostPublication(ReadOnlyMemory<byte> bytes)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out int operation);
        if (operation != MinecraftHostOperations.DownloadProgress) throw new InvalidDataException("Unexpected Minecraft host publication.");
        _downloads.ReceiveProgress(reader); return [];
    }
    private async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        try
        {
            byte[] result = await ExecuteCoreAsync(bytes, token).ConfigureAwait(false);
            writer.Int32(0); writer.Buffer(result);
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or UnauthorizedAccessException
            or IOException or InvalidOperationException or NotSupportedException)
        {
            writer.Int32(error switch
            {
                ArgumentNullException => 1,
                ArgumentOutOfRangeException => 2,
                ArgumentException => 3,
                InvalidDataException => 4,
                UnauthorizedAccessException => 5,
                IOException => 6,
                InvalidOperationException => 7,
                NotSupportedException => 8,
                _ => throw new InvalidOperationException()
            });
            writer.String(error.Message); writer.String((error as ArgumentException)?.ParamName);
        }
        finally
        {
            await _publications.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await FlushInstallEventsAsync().ConfigureAwait(false);
        }
        return writer.Finish();
    }
    private async Task<byte[]> ExecuteCoreAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
        using var reader = MinecraftServiceProtocol.Read(bytes, out int operation);
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftServiceProtocol.Initialize)
        {
            string directory = reader.String() ?? "", java = reader.String() ?? "";
            string? cache = reader.String(), jvmHost = reader.String(); reader.End();
            if (!Path.IsPathFullyQualified(directory) || !Path.IsPathFullyQualified(java)
                || cache is not null && !Path.IsPathFullyQualified(cache)
                || jvmHost is not null && !Path.IsPathFullyQualified(jvmHost))
                throw new InvalidDataException("Minecraft service paths must be absolute.");
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
                if (_instances is not null) throw new InvalidOperationException("Minecraft service is already initialized.");
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
                _instances = new(_cache, cache, versionDiscovery: _versions);
                _catalogCacheDirectory = cache is null ? null : Path.Combine(cache, "install-catalogs");
                _processes = new(hostStore: _store, jvmHostExecutable: jvmHost)
                { ArchitectureValidator = new Nexa.Platform.PlatformJvmArchitectureValidator() };
                _javaRuntimeRoot = java; _javaLocator = new(java); _java = new(_javaLocator);
                _javaInstaller = new(new JavaRuntimeDownloadPlanService(new HttpJavaRuntimeMetadataProvider(_metadataHttp)), _http, tasks: _tasks)
                { SessionDownloads = _sessionDownloads };
            }
            finally { _initialization.Release(); }
            return [];
        }
        if (_instances is null) throw new InvalidOperationException("Minecraft service has not initialized.");
        if (operation is >= MinecraftServiceProtocol.ManageInstanceExportOptionsSave and <= MinecraftServiceProtocol.ManageInstanceWorldSnapshotVerifyQuery)
            return await ExecuteManagementAsync(operation, reader, token).ConfigureAwait(false);
        if (operation == MinecraftServiceProtocol.ResourceConfigure) return await ConfigureResourcesAsync(reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.ResourceResourceNetworkPolicyQuery and <= MinecraftServiceProtocol.ResourceResourceModpackUpdateCommand)
            return await ExecuteResourceOperationAsync(operation, reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.LibraryConfigure and <= MinecraftServiceProtocol.LibraryRememberRenamed) return await ExecuteLibraryAsync(operation, reader, token).ConfigureAwait(false);
        if (operation == MinecraftServiceProtocol.EnvironmentFacts) return await ReadEnvironmentFactsAsync(reader, token).ConfigureAwait(false);
        if (operation == MinecraftServiceProtocol.LocalDocuments) return await ReadLocalDocumentsAsync(reader, token).ConfigureAwait(false);
        if (operation is MinecraftServiceProtocol.MigrationReceiptAuthorize or MinecraftServiceProtocol.MigrationReceiptVerify)
            return await MigrationReceiptAsync(operation, reader, token).ConfigureAwait(false);
        if (operation == MinecraftServiceProtocol.Remediation) return await ExecuteRemediationAsync(reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.CatalogConfigure and <= MinecraftServiceProtocol.CatalogDraft or MinecraftServiceProtocol.CatalogDraftReplace)
            return await ExecuteCatalogAsync(operation, reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.ModpackUpdatePreview and <= MinecraftServiceProtocol.FolderImport)
            return await ExecuteImportAsync(operation, reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.JavaConfigure and <= MinecraftServiceProtocol.JavaModules)
            return await ExecuteJavaManagementAsync(operation, reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.InstallConfigure and <= MinecraftServiceProtocol.InstallEditRead)
            return await ExecuteInstallationAsync(operation, reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.RecoveryRead and <= MinecraftServiceProtocol.RecoveryResume)
            return await ExecuteRecoveryAsync(operation, reader, token).ConfigureAwait(false);
        if (operation is >= MinecraftServiceProtocol.LaunchConfigure and <= MinecraftServiceProtocol.LaunchFinalize
            or MinecraftServiceProtocol.LaunchSafeRead or MinecraftServiceProtocol.LaunchDiagnostics)
            return await ExecuteLaunchAsync(operation, reader, token).ConfigureAwait(false);
        switch (operation)
        {
            case MinecraftServiceProtocol.Versions:
                string root = reader.String() ?? throw new InvalidDataException("Missing Minecraft directory."); reader.End();
                var versions = _versions.Discover(root, token); writer.Count(versions.Count);
                foreach (var version in versions) writer.MinecraftVersionDescriptor(version);
                break;
            case MinecraftServiceProtocol.Instances:
            case MinecraftServiceProtocol.RefreshInstances:
                string instancesRoot = reader.String() ?? throw new InvalidDataException("Missing Minecraft directory."); reader.End();
                var instances = operation == MinecraftServiceProtocol.RefreshInstances
                    ? await _instances.RefreshAsync(instancesRoot, token).ConfigureAwait(false)
                    : await _instances.DiscoverAsync(instancesRoot, token).ConfigureAwait(false);
                writer.Count(instances.Count); foreach (var instance in instances) writer.MinecraftInstanceDescriptor(instance);
                break;
            case MinecraftServiceProtocol.Processes:
                reader.End(); var processes = _processes!.ListSessions(); writer.Count(processes.Count);
                foreach (var process in processes) writer.MinecraftProcessSnapshot(process);
                break;
            case MinecraftServiceProtocol.StartProcess:
                var plan = reader.MinecraftLaunchPlan() ?? throw new InvalidDataException("Missing launch plan.");
                string instanceId = reader.String() ?? throw new InvalidDataException("Missing instance identity."); reader.End();
                var started = await _processes!.StartAsync(plan, instanceId, token).ConfigureAwait(false);
                writer.MinecraftProcessSnapshot(started.Snapshot); break;
            case MinecraftServiceProtocol.ProcessOutput:
                var sessionId = reader.Guid(); reader.End();
                var output = await _processes!.ReadOutputAsync(sessionId, token).ConfigureAwait(false);
                writer.XsrError(output.Error); if (output.IsSuccess) writer.MinecraftProcessOutputSnapshot(output.Value);
                break;
            case MinecraftServiceProtocol.CancelProcess:
                var canceledId = reader.Guid(); reader.End();
                writer.XsrError(_processes!.TryCancel(canceledId) ? null : MinecraftErrors.ProcessNotFound(canceledId)); break;
            case MinecraftServiceProtocol.CrashAnalyze:
                int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing crash evidence.");
                string[] evidence = new string[count];
                for (int index = 0; index < count; index++) evidence[index] = reader.String() ?? throw new InvalidDataException("Missing crash line.");
                string? stage = reader.String(), lastClass = reader.String(); reader.End();
                writer.MinecraftLaunchFaultReport(MinecraftLaunchFaultAnalyzer.AnalyzeText(evidence, stage, lastClass)); break;
            case MinecraftServiceProtocol.JavaInventory:
                bool refresh = reader.Boolean(); reader.End();
                var inventory = await _java!.ReadAsync(new(refresh), token).ConfigureAwait(false);
                writer.XsrError(inventory.Error); if (inventory.IsSuccess) writer.JavaRuntimeInventorySnapshot(inventory.Value); break;
            case MinecraftServiceProtocol.JavaInstall:
                long call = reader.Int64(); string component = reader.String() ?? throw new InvalidDataException("Missing Java component.");
                string runtimeRoot = reader.String() ?? throw new InvalidDataException("Missing runtime root."); string? fingerprint = reader.String(); reader.End();
                if (call <= 0) throw new InvalidDataException("Invalid Java install call.");
                await _javaAdmission.WaitAsync(token).ConfigureAwait(false);
                if (!_javaSlots.TryDequeue(out int javaSlot)) { _javaAdmission.Release(); throw new InvalidOperationException("Java progress slot accounting failed."); }
                try
                {
                    var progress = new JavaProgress(value => _publications.Post(javaSlot, Interlocked.Increment(ref _javaProgressRevision), () =>
                    {
                        using var publication = MinecraftServiceProtocol.Request(MinecraftServiceProtocol.JavaProgress);
                        publication.Int64(call); publication.JavaRuntimeInstallProgress(value); return publication.Finish();
                    }));
                    string installed = fingerprint is null
                        ? await _javaInstaller!.InstallAsync(component, runtimeRoot, progress, token).ConfigureAwait(false)
                        : await _javaInstaller!.InstallConfirmedAsync(component, runtimeRoot, fingerprint, progress, token).ConfigureAwait(false);
                    writer.String(installed);
                }
                finally
                {
                    try { await _publications.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
                    finally { _javaSlots.Enqueue(javaSlot); _javaAdmission.Release(); }
                }
                break;
            case MinecraftServiceProtocol.JavaStop:
                bool pause = reader.Boolean(); reader.End(); writer.XsrError((await _javaInstaller!.StopAsync(new(pause), token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.JavaRecover:
                string recoveryRoot = reader.String() ?? throw new InvalidDataException("Missing Java recovery root."); reader.End();
                writer.XsrError((await _javaInstaller!.RecoverAsync(recoveryRoot, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.TaskRetry:
                string retry = reader.String() ?? throw new InvalidDataException("Missing task identity."); reader.End();
                if (_resourceSource is not null) await RefreshResourceSettingsAsync(token).ConfigureAwait(false);
                writer.Boolean(_tasks.RequestRetry(retry)); break;
            case MinecraftServiceProtocol.TaskCancel:
                string cancel = reader.String() ?? throw new InvalidDataException("Missing task identity."); reader.End();
                writer.Int32((int)_tasks.RequestCancel(cancel)); break;
            case MinecraftServiceProtocol.TaskDismiss:
                string dismiss = reader.String() ?? throw new InvalidDataException("Missing task identity."); reader.End();
                writer.Boolean(await _tasks.DismissAsync(dismiss).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.TaskClear:
                reader.End(); writer.Int32(await _tasks.ClearFinishedAsync().ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.TaskDismissFinished:
                int expectedCount = reader.Count(); if (expectedCount < 0) throw new InvalidDataException("Missing task history.");
                TaskCenterEntry[] expected = new TaskCenterEntry[expectedCount];
                for (int index = 0; index < expectedCount; index++) expected[index] = reader.TaskCenterEntry() ?? throw new InvalidDataException("Missing task history entry.");
                reader.End(); writer.Boolean(_tasks.DismissFinished(expected)); break;
            default: throw new InvalidDataException("Unknown Minecraft service operation.");
        }
        return writer.Finish();
    }
    private void Rejected(long id, ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> result)
    {
        using var reader = MinecraftServiceProtocol.Read(request, out int operation);
        if (operation != MinecraftServiceProtocol.StartProcess) return;
        using var envelope = new ServiceWireReader(result);
        if (envelope.Int32() != 0) return;
        byte[] payload = envelope.Buffer(); envelope.End();
        using var response = new ServiceWireReader(payload);
        var session = response.MinecraftProcessSnapshot() ?? throw new InvalidDataException("Missing rejected process."); response.End();
        _processes!.TryCancel(session.SessionId);
    }
    private void Changed(XsrStateChange change)
    {
        if (PublishLibraryState(change)) return;
        if (PublishLaunchState(change)) return;
        if (PublishObservationState(change)) return;
        if (PublishCatalogState(change)) return;
        bool processes = change.SemanticId == MinecraftProcessStateComposition.SessionsKey;
        bool tasks = change.SemanticId == TaskCenterStateContract.EntriesKey;
        if (!processes && !tasks && change.SemanticId != MinecraftProcessStateComposition.FailuresKey) return;
        var entry = _store.CaptureSnapshot().Entries.Single(item => item.SemanticId == change.SemanticId);
        _publications.Post(tasks ? 2 : processes ? 0 : 1, entry.Revision, () =>
        {
            using var writer = MinecraftServiceProtocol.Request(tasks ? MinecraftServiceProtocol.TaskState : processes ? MinecraftServiceProtocol.ProcessState : MinecraftServiceProtocol.FailureState);
            writer.Int64(entry.Revision); writer.Int32((int)entry.Availability);
            if (tasks)
            {
                var items = (IReadOnlyList<TaskCenterEntry>)entry.Value!; writer.Count(items.Count);
                foreach (var item in items) writer.TaskCenterEntry(item);
            }
            else if (processes)
            {
                var items = (IReadOnlyList<MinecraftProcessSnapshot>)entry.Value!; writer.Count(items.Count);
                foreach (var item in items) writer.MinecraftProcessSnapshot(item);
            }
            else
            {
                var items = (IReadOnlyList<MinecraftProcessFailure>)entry.Value!; writer.Count(items.Count);
                foreach (var item in items) writer.MinecraftProcessFailure(item);
            }
            return writer.Finish();
        });
    }
    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _closing, true);
        await _initialization.WaitAsync().ConfigureAwait(false);
        _initialization.Release();
        _launch?.CancelActiveLaunch();
        _catalog?.Dispose();
        await RetireInstallationAsync().ConfigureAwait(false);
        await _endpoint.StopAsync().ConfigureAwait(false);
        _manualJava?.Dispose();
        if (_javaInstaller is not null) await _javaInstaller.StopAsync(new(Pause: true)).ConfigureAwait(false);
        await _tasks.DisposeAsync().ConfigureAwait(false);
        _installEvents.Writer.TryComplete();
        if (_installEventPump is { } events) await events.ConfigureAwait(false);
        if (_library is not null) await _library.DisposeAsync().ConfigureAwait(false);
        _installation?.Dispose(); _installationHttp?.Dispose();
        if (_processes is not null) await _processes.DisposeAsync().ConfigureAwait(false);
        if (_launchExecutor is not null) await _launchExecutor.WaitForFinalizationAsync().ConfigureAwait(false);
        if (_jvmHost is not null) await _jvmHost.WaitForFinalizationAsync().ConfigureAwait(false);
        _completion?.Dispose(); _authlib?.Dispose(); _launchHttp?.Dispose();
        await _cache.DisposeAsync().ConfigureAwait(false);
        _resourceIcons?.Dispose(); _resourceInformation?.Dispose(); _resourceHttp?.Dispose();
        _metadataHttp.Dispose(); _http.Dispose(); await _hostHttp.DisposeAsync().ConfigureAwait(false);
        await _strictHostHttp.DisposeAsync().ConfigureAwait(false);
        _sessionDownloads.Dispose();
        _javaInstaller?.Dispose();
        _store.Changed -= Changed; await _publications.DisposeAsync().ConfigureAwait(false);
        await _work.DisposeAsync().ConfigureAwait(false);
        await _endpoint.DisposeAsync().ConfigureAwait(false); _initialization.Dispose();
        _javaAdmission.Dispose();
    }
    private sealed class JavaProgress(Action<JavaRuntimeInstallProgress> report) : IProgress<JavaRuntimeInstallProgress>
    { public void Report(JavaRuntimeInstallProgress value) => report(value); }
}
