using System.Collections.Concurrent;
using System.Net;
using Nexa.Native.Network.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Downloads;
using Nexa.Services.Logging;
using Nexa.Services.Scheduling;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Network.Client;

public sealed partial class NativeNetworkClient
{
    private HostWorkLeaseServer? _hostWork;
    private XsrStateStore? _downloadState;
    private readonly ConcurrentDictionary<long, DownloadProgressSink> _downloadProgress = [];
    private readonly TaskCompletionSource _downloadsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextDownload;
    private int _activeDownloads;
    private NativeLogPipe? _downloadLogs;

    public static void DeclareDownloadState(XsrStateStoreBuilder builder)
    {
        builder.Collection<DownloadTransferView, string>(DownloadStateContract.TransfersKey,
            "Nexa.Services.Downloads", static value => value.DestinationPath);
        builder.Replica(DownloadStateContract.TransfersKey);
    }
    public void AttachDownloads(IWorkScheduler scheduler, XsrStateStore? replicas = null, Func<bool>? backgroundEnabled = null,
        ILogWriter? logging = null, Action<string, TimeSpan, bool>? operation = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_hostWork is not null) throw new InvalidOperationException("Native downloads are already attached.");
            if (replicas is not null) _ = replicas.Resolve(DownloadStateContract.TransfersKey);
            _downloadState = replicas; _hostWork = new(scheduler, backgroundEnabled, Report);
            if (logging is not null || operation is not null) _downloadLogs = new(message =>
            {
                if (message.Operation) operation?.Invoke(message.Module, message.Duration, message.Succeeded);
                else logging?.Write(message.Level, message.Module, message.Message, message.Exception);
                return Task.CompletedTask;
            });
        }
    }
    private Task<byte[]> ExecuteReverseAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) =>
        (Volatile.Read(ref _hostWork) ?? throw new InvalidOperationException("Native download work admission is not attached.")).ExecuteAsync(bytes, token);
    private void RejectReverse(long id, ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> result) =>
        Volatile.Read(ref _hostWork)?.Reject(id, request, result);
    private byte[] ApplyPublication(ReadOnlyMemory<byte> bytes)
    {
        using var reader = NetworkProtocol.Read(bytes, out int operation);
        if (operation == DownloadProtocol.State)
        {
            long revision = reader.Int64(); var availability = (XsrStateAvailability)reader.Int32(); int count = reader.Count();
            if (count < 0 || revision <= 0 || availability is not (XsrStateAvailability.Unavailable or XsrStateAvailability.Available or XsrStateAvailability.Stale))
                throw new InvalidDataException("Invalid transfer state.");
            DownloadTransferView[] items = new DownloadTransferView[count];
            for (int index = 0; index < count; index++) items[index] = reader.DownloadTransferView();
            reader.End();
            if (_downloadState is { } state)
                state.ReplicateCollection<DownloadTransferView, string>(state.Resolve(DownloadStateContract.TransfersKey), revision, availability, items);
        }
        else if (operation == DownloadProtocol.Progress)
        {
            long call = reader.Int64(); var progress = reader.DownloadProgress(); reader.End();
            if (_downloadProgress.TryGetValue(call, out var sink)) sink.Post(progress);
        }
        else if (operation == DownloadProtocol.Log)
        {
            int level = reader.Int32(); string module = reader.String() ?? "Download", message = reader.String() ?? ""; string? exception = reader.String();
            bool observation = reader.Boolean(); TimeSpan duration = reader.TimeSpan(); bool succeeded = reader.Boolean(); reader.End();
            if (level is < 0 or > 4 || duration < TimeSpan.Zero) throw new InvalidDataException("Invalid native download log.");
            _downloadLogs?.Post(new((LogLevel)level, module, message, exception, observation, duration, succeeded));
        }
        else throw new InvalidDataException("Unknown network publication.");
        return [];
    }
    public Task<DownloadTransferResult> DownloadAsync(NativeDownloadRequest request, Action<DownloadProgress>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_hostWork is null) throw new InvalidOperationException("Native downloads require the canonical host scheduler.");
            if (_activeDownloads >= 128) throw new InvalidOperationException("Native download caller budget exceeded.");
            _activeDownloads++;
        }
        return DownloadCoreAsync(request, progress, token);
    }
    private async Task<DownloadTransferResult> DownloadCoreAsync(NativeDownloadRequest request, Action<DownloadProgress>? progress, CancellationToken token)
    {
        long call = Interlocked.Increment(ref _nextDownload); DownloadProgressSink? sink = null;
        try
        {
            if (progress is not null) { sink = new(progress); if (!_downloadProgress.TryAdd(call, sink)) throw new InvalidOperationException("Duplicate download call."); }
            using var writer = NetworkProtocol.Request(DownloadProtocol.Download); writer.Int64(call); writer.NativeDownloadRequest(request);
            byte[] bytes = await HttpAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes);
            var result = reader.NativeDownloadOutcome() ?? throw new InvalidDataException("Missing native download outcome."); reader.End();
            return new(result.Success, result.DestinationPath, result.SuccessfulSource, result.TotalBytes, result.Duration,
                result.Errors.Select(error => new DownloadAttemptError(error.Source, error.Message, Failure(error))).ToArray());
        }
        finally
        {
            _downloadProgress.TryRemove(call, out _);
            try { if (sink is not null) await sink.DisposeAsync().ConfigureAwait(false); }
            finally { lock (_gate) { _activeDownloads--; if (_closed && _activeDownloads == 0) _downloadsDrained.TrySetResult(); } }
        }
    }
    private static Exception Failure(NativeDownloadFailure error) => error.Kind switch
    {
        NativeDownloadFailureKind.Http => new HttpRequestException(error.Message, null, error.HttpStatus is { } status ? (HttpStatusCode)status : null),
        NativeDownloadFailureKind.Unauthorized => new UnauthorizedAccessException(error.Message),
        NativeDownloadFailureKind.InvalidData => new InvalidDataException(error.Message),
        NativeDownloadFailureKind.InvalidOperation => new InvalidOperationException(error.Message),
        NativeDownloadFailureKind.Argument => new ArgumentException(error.Message),
        NativeDownloadFailureKind.NotSupported => new NotSupportedException(error.Message),
        _ => new IOException(error.Message)
    };
    private async Task DrainDownloadsAsync()
    {
        lock (_gate) { if (_activeDownloads == 0) _downloadsDrained.TrySetResult(); }
        await _downloadsDrained.Task.ConfigureAwait(false);
        if (_hostWork is not null) await _hostWork.DisposeAsync().ConfigureAwait(false);
        if (_downloadLogs is not null) await _downloadLogs.DisposeAsync().ConfigureAwait(false);
    }
}
