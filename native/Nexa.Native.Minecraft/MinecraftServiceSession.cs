using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Caching;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Crash;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft;

internal sealed class MinecraftServiceSession : IAsyncDisposable
{
    private readonly ServiceEndpoint _endpoint;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly SharedStateCache _cache = new();
    private readonly MinecraftVersionDiscovery _versions = new();
    private readonly XsrStateStore _store;
    private readonly ServiceStatePublisher _publications;
    private MinecraftInstanceDiscovery? _instances;
    private MinecraftProcessService? _processes;
    private JavaRuntimeInventoryService? _java;

    internal MinecraftServiceSession(NativeUiCallTable peer)
    {
        _endpoint = new(ExecuteAsync, _ => throw new InvalidDataException("Unexpected Minecraft publication."),
            error => Console.Error.WriteLine("Native Minecraft operation failed: " + error.GetType().Name), Rejected);
        _endpoint.Connect(peer);
        XsrStateStoreBuilder builder = new(); MinecraftProcessStateComposition.DeclareState(builder); _store = builder.Build();
        _publications = new(_endpoint); _store.Changed += Changed;
    }
    internal NativeUiCallTable Table => _endpoint.Table;
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
        finally { await _publications.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
        return writer.Finish();
    }
    private async Task<byte[]> ExecuteCoreAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
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
                if (_instances is not null) throw new InvalidOperationException("Minecraft service is already initialized.");
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
                _instances = new(_cache, cache, versionDiscovery: _versions);
                _processes = new(hostStore: _store, jvmHostExecutable: jvmHost)
                { ArchitectureValidator = new Nexa.Platform.PlatformJvmArchitectureValidator() };
                _java = new(new LocalJavaRuntimeLocator(java));
            }
            finally { _initialization.Release(); }
            return [];
        }
        if (_instances is null) throw new InvalidOperationException("Minecraft service has not initialized.");
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
                if (!inventory.IsSuccess) throw new InvalidOperationException("Java inventory is unavailable.");
                writer.JavaRuntimeInventorySnapshot(inventory.Value); break;
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
        bool processes = change.SemanticId == MinecraftProcessStateComposition.SessionsKey;
        if (!processes && change.SemanticId != MinecraftProcessStateComposition.FailuresKey) return;
        var entry = _store.CaptureSnapshot().Entries.Single(item => item.SemanticId == change.SemanticId);
        _publications.Post(processes ? 0 : 1, entry.Revision, () =>
        {
            using var writer = MinecraftServiceProtocol.Request(processes ? MinecraftServiceProtocol.ProcessState : MinecraftServiceProtocol.FailureState);
            writer.Int64(entry.Revision); writer.Int32((int)entry.Availability);
            if (processes)
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
        await _endpoint.StopAsync().ConfigureAwait(false);
        if (_processes is not null) await _processes.DisposeAsync().ConfigureAwait(false);
        _store.Changed -= Changed; await _publications.DisposeAsync().ConfigureAwait(false);
        _cache.Dispose(); await _endpoint.DisposeAsync().ConfigureAwait(false); _initialization.Dispose();
    }
}
