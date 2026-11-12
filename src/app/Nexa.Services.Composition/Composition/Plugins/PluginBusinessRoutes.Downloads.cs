using System.Security.Cryptography;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Downloads;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private const long MaximumPluginDownloadBytes = 8L * 1024 * 1024 * 1024;
    private DownloadService? _downloadService;
    private readonly Dictionary<EntityId, PluginDownload> _pluginDownloads = [];
    private readonly HashSet<TaskCompletionSource> _downloadWorkers = [];
    private bool _downloadsStopped;
    private sealed class PluginDownload(PluginIdentity owner, DownloadPlan plan, string path)
    {
        internal readonly PluginIdentity Owner = owner;
        internal readonly DownloadPlan Plan = plan;
        internal readonly string Path = path;
        internal OperationRef? Operation;
        internal OperationState State = OperationState.Pending;
        internal long Bytes, Revision;
        internal long? Total = plan.Size;
        internal bool Paused;
        internal string? Digest, Error;
        internal CancellationTokenSource? Transfer;
        internal TaskCompletionSource<bool> Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void BindDownloads(DownloadService downloads) => _downloadService = downloads;
    private DownloadPlan ResolveDownload(HostInvocation c, DownloadPlan request)
    {
        if (request.Sources.Length is < 1 or > 8 || request.Size is < 0 or > MaximumPluginDownloadBytes || request.Sha256 is { } hash && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        foreach (var source in request.Sources)
        {
            if (!Uri.TryCreate(source.Uri.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if (source.Credential is not null || source.Provider is { Value: not "nexa:http" }) throw new AbiHostException(AbiErrors.PROVIDER_UNAVAILABLE);
        }
        string path = Application.Files.Resolve(c.Identity, request.Target, true);
        if (Path.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path))) throw new AbiHostException(AbiErrors.CONFLICT);
        return request with { Sha256 = request.Sha256?.ToUpperInvariant() };
    }
    private PluginDownload Download(HostInvocation c, EntityId id)
    {
        lock (_planGate)
            return _pluginDownloads.TryGetValue(id, out var item) && item.Owner == c.Identity ? item : throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private DownloadState ReadDownload(HostInvocation c, EntityId id)
    {
        var item = Download(c, id);
        lock (_planGate) return new(id, item.Paused && item.State == OperationState.Running ? OperationState.Pending : item.State, item.Bytes, item.Total,
            item.Error is { } error ? [new(error, new(error, []), c.Options.CorrelationId, false, [])] : [], new(Token(c, "download:" + id.Value + ":" + item.Revision)));
    }
    private async ValueTask<AbiReply<AbiValue>> RunDownload(HostInvocation c, PluginDownload item, CancellationToken token)
    {
        lock (_planGate)
        {
            if (_downloadsStopped) throw new OperationCanceledException();
            _downloadWorkers.Add(item.Finished);
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _resourceStop.Token);
        token = lifetime.Token;
        string stage = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.Path)!, ".nexa-plugin-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Confirm(c, "downloads/start", "Download to " + item.Path, DownloadPlan.Write(item.Plan), token).ConfigureAwait(false);
            while (true)
            {
                Task? wait;
                lock (_planGate) wait = item.Paused ? item.Resume.Task : null;
                if (wait is not null) { await wait.WaitAsync(token).ConfigureAwait(false); continue; }
                using var transfer = CancellationTokenSource.CreateLinkedTokenSource(token, _resourceStop.Token);
                lock (_planGate)
                {
                    if (item.Paused) continue;
                    item.Transfer = transfer; item.State = OperationState.Running; item.Revision++;
                }
                _changes.Writer.TryWrite(true);
                try
                {
                    Application.Files.Resolve(c.Identity, item.Plan.Target, true);
                    if (Path.Exists(item.Path)) throw new AbiHostException(AbiErrors.CONFLICT);
                    var service = _downloadService ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                    var result = await service.DownloadAsync(new DownloadRequest
                    {
                        DestinationPath = stage,
                        Sources = item.Plan.Sources.Select(s => s.Uri.Value).ToArray(),
                        AllowResume = item.Plan.Sha256 is not null,
                        CacheIdentity = item.Plan.Sha256 is { } digest ? new(digest, item.Plan.Size) : null,
                        ConnectionFactory = url => new PluginDownloadConnection(_pluginHttp, url, item.Plan.Size)
                    }, progress =>
                    {
                        lock (_planGate) { item.Bytes = progress.DownloadedBytes; item.Total = progress.TotalBytes >= 0 ? progress.TotalBytes : item.Plan.Size; item.Revision++; }
                        _changes.Writer.TryWrite(true);
                    }, transfer.Token).ConfigureAwait(false);
                    transfer.Token.ThrowIfCancellationRequested();
                    if (!result.Success) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                    var info = new FileInfo(stage);
                    if (info.Length > MaximumPluginDownloadBytes || item.Plan.Size is { } size && size != info.Length) throw new AbiHostException(AbiErrors.CONFLICT);
                    await using (var file = File.OpenRead(stage)) item.Digest = Convert.ToHexString(await SHA256.HashDataAsync(file, transfer.Token).ConfigureAwait(false));
                    if (item.Plan.Sha256 is { } expected && item.Digest != expected) throw new AbiHostException(AbiErrors.CONFLICT);
                    await Application.Files.MutationGate.WaitAsync(transfer.Token).ConfigureAwait(false);
                    try
                    {
                        Application.Files.Resolve(c.Identity, item.Plan.Target, true);
                        transfer.Token.ThrowIfCancellationRequested();
                        if (!Application.Runtime.Active.Contains(c.Identity) || Path.Exists(item.Path)) throw new AbiHostException(AbiErrors.CONFLICT);
                        lock (_planGate)
                        {
                            transfer.Token.ThrowIfCancellationRequested();
                            File.Move(stage, item.Path, false);
                            item.State = OperationState.Completed; item.Bytes = info.Length; item.Total = info.Length; item.Revision++;
                        }
                    }
                    finally { Application.Files.MutationGate.Release(); }
                    return AbiReply<AbiValue>.Ok(FileRef.Write(item.Plan.Target));
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && !_resourceStop.IsCancellationRequested)
                { lock (_planGate) { item.State = OperationState.Pending; item.Revision++; } }
                finally { lock (_planGate) if (ReferenceEquals(item.Transfer, transfer)) item.Transfer = null; }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            lock (_planGate)
            {
                item.State = error is OperationCanceledException ? OperationState.Cancelled : OperationState.Failed;
                item.Error = error is AbiHostException known ? known.Code : error is OperationCanceledException ? AbiErrors.CANCELLED : AbiErrors.CAPABILITY_UNAVAILABLE;
                item.Revision++;
            }
            throw;
        }
        finally
        {
            // DownloadService's writer owns this exact suffix; no directory traversal or wildcard cleanup.
            foreach (string file in new[] { stage, stage + ".PCLDownloading" })
                try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _changes.Writer.TryWrite(true);
            lock (_planGate) { _downloadWorkers.Remove(item.Finished); item.Finished.TrySetResult(); }
        }
    }
    private IEnumerable<HostCapabilityRoute> DownloadRoutes => [
        HostRoutes.Typed<DownloadPlan, DownloadPlan>("Downloads", "Resolve", (c, v) => ValueTask.FromResult(ResolveDownload(c, v))),
        HostRoutes.Typed<DownloadPlan, OperationRef>("Downloads", "Start", (c, v) =>
        {
            if (_downloadService is null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var plan = ResolveDownload(c, v); var item = new PluginDownload(c.Identity, plan, Application.Files.Resolve(c.Identity, plan.Target, true));
            lock (_planGate)
            {
                if (_pluginDownloads.Count >= 64) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                if (_downloadsStopped) throw new OperationCanceledException();
                var operation = Services.StartHostOperation(c, "nexa.downloads.write", Abi100Schemas.FileRef, token => RunDownload(c, item, token));
                item.Operation = operation; _pluginDownloads.Add(operation.Id, item); return ValueTask.FromResult(operation);
            }
        }),
        HostRoutes.Typed<EntityRequest, DownloadState>("Downloads", "Read", (c, v) => ValueTask.FromResult(ReadDownload(c, v.Id))),
        HostRoutes.Typed<WatchRequest, SubscriptionRef>("Downloads", "Watch", (c, v) => ValueTask.FromResult(Watch(c, v,
            "nexa.downloads/100", "nexa.downloads.read", Abi100Schemas.DownloadState, () => DownloadState.Write(ReadDownload(c, v.Target))))),
        HostRoutes.Typed<OperationRequest, OperationRef>("Downloads", "Pause", (c, v) =>
        {
            var item = Download(c, v.Operation);
            lock (_planGate)
            {
                if (item.State is not (OperationState.Pending or OperationState.Running)) throw new AbiHostException(AbiErrors.CONFLICT);
                item.Paused = true; item.Transfer?.Cancel(); item.Revision++;
            }
            _changes.Writer.TryWrite(true); return ValueTask.FromResult(item.Operation!);
        }),
        HostRoutes.Typed<OperationRequest, OperationRef>("Downloads", "Resume", (c, v) =>
        {
            var item = Download(c, v.Operation);
            lock (_planGate)
            {
                if (!item.Paused || item.State is not (OperationState.Pending or OperationState.Running)) throw new AbiHostException(AbiErrors.CONFLICT);
                item.Paused = false; item.Resume.TrySetResult(true); item.Resume = new(TaskCreationOptions.RunContinuationsAsynchronously); item.Revision++;
            }
            _changes.Writer.TryWrite(true); return ValueTask.FromResult(item.Operation!);
        }),
        HostRoutes.Typed<OperationRequest, OperationRef>("Downloads", "Cancel", async (c, v) =>
        {
            Download(c, v.Operation);
            var route = Services.Routes.Single(r => r.Descriptor.Group == "Tasks" && r.Descriptor.Name == "Cancel");
            var reply = await route.Handler(c, OperationRequest.Write(v)).ConfigureAwait(false);
            if (!reply.Success) throw new AbiHostException(reply.Error!.Code); return OperationRef.Read(reply.Value);
        }),
        HostRoutes.Typed<EntityRequest, IntegrityReport>("Downloads", "Verify", async (c, v) =>
        {
            var item = Download(c, v.Id); string? expected;
            lock (_planGate) { if (item.State != OperationState.Completed) throw new AbiHostException(AbiErrors.CONFLICT); expected = item.Digest; }
            string path = Application.Files.Resolve(c.Identity, item.Plan.Target);
            await using var input = File.OpenRead(path); if (input.Length > MaximumPluginDownloadBytes) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            string digest = Convert.ToHexString(await SHA256.HashDataAsync(input, c.Options.Cancellation).ConfigureAwait(false));
            bool valid = digest == expected;
            return new(v.Id, valid, valid ? [] : [Issue("DOWNLOAD_CHANGED", "The downloaded file changed.", v.Id.Value)], new(digest));
        })
    ];
}
