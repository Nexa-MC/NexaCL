using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Nexa.Services.Logging;
using Nexa.Services.Scheduling;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Downloads;

/// <summary>
/// Coordinates failover downloads while sharing one transfer per destination. Each caller
/// keeps an independent cancellation token and progress callback; the published
/// `download.transfers` collection mirrors every active transfer for local state readers.
/// Sources fail over in order with resume support; a 416 against a resumed destination
/// discards the partial file and retries the source list from scratch semantics.
/// </summary>
public sealed partial class DownloadService : IDisposable
{
    public IWorkScheduler? WorkScheduler { get; init; }
    public DownloadBandwidthLimiter? BandwidthLimiter { get; internal set; }
    /// <summary>Committed policy gates new optional background transfers; interactive transfers remain usable.</summary>
    public Func<bool>? BackgroundDownloadsEnabled { get; set; }
    internal TimeProvider ProgressClock { get; init; } = TimeProvider.System;

    public const string OwnerName = "Nexa.Services.Downloads";
    public const string LogModuleName = "Download";

    /// <summary>
    /// The ordered collection state key: items are <see cref="DownloadTransferView"/>, keyed
    /// by destination path. Only active transfers appear; terminal entries are removed.
    /// </summary>
    public static readonly XsrSemanticId TransfersKey = DownloadStateContract.TransfersKey;

    private const int DefaultBufferSize = 128 * 1024;
    private const long DefaultMinimumSegmentBytes = 8 * 1024 * 1024;
    private const int MinBytesBetweenSegmentProgress = 1024 * 1024;
    private static readonly TimeSpan MinimumSingleStreamProgressInterval = TimeSpan.FromMilliseconds(100);
    private const int MaxStateConflicts = 8;

    private readonly ConcurrentDictionary<string, Lazy<DownloadOperation>> _active = new(GetPathComparer());
    private readonly int _bufferSize;
    private readonly long _minimumSegmentBytes;
    private readonly XsrStateStore _store;
    private readonly XsrStateId _transfersId;
    private readonly object _stateGate = new();
    private readonly LogService? _log;

    /// <summary>
    /// Two-phase composition, declaration phase: registers the active-transfers collection
    /// into the shared host builder.
    /// </summary>
    public static void DeclareState(XsrStateStoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Collection<DownloadTransferView, string>(
            TransfersKey,
            OwnerName,
            static view => view.DestinationPath);
    }

    public DownloadService(
        XsrStateStore store,
        int bufferSize = DefaultBufferSize,
        LogService? log = null,
        long minimumSegmentBytes = DefaultMinimumSegmentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSegmentBytes);

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _bufferSize = bufferSize;
        _minimumSegmentBytes = minimumSegmentBytes;
        _log = log;
        _transfersId = _store.Resolve(TransfersKey);
    }

    public XsrStateStore StateStore => _store;

    public Task<DownloadTransferResult> DownloadAsync(
        DownloadRequest request,
        Action<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (request.CacheIdentity is not null)
            return DownloadCachedAsync(request with { DestinationPath = Path.GetFullPath(request.DestinationPath) }, progress, cancellationToken);
        if (WorkScheduler?.CurrentPriority is WorkPriority.Background or WorkPriority.Idle && BackgroundDownloadsEnabled?.Invoke() == false)
            throw new InvalidOperationException("Background downloads are disabled by the committed policy.");
        request = request with { BandwidthBudget = BandwidthLimiter?.Capture() };
        string destinationPath = Path.GetFullPath(request.DestinationPath);
        _log?.Info(LogModuleName, $"Download requested destination={destinationPath} sources={request.Sources.Count}");
        Lazy<DownloadOperation> lazyOperation = _active.GetOrAdd(
            destinationPath,
            static (path, state) => new Lazy<DownloadOperation>(
                () => new DownloadOperation(
                    state.Request with { DestinationPath = path },
                    operation => state.Service.ExecuteAndCompleteAsync(path, operation)),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Service: this, Request: request));
        DownloadOperation operation = lazyOperation.Value;
        return operation.WaitAsync(progress, cancellationToken);
    }

    private IDownloadConnection? CreateConnection(DownloadRequest request, string source, bool probe = false)
    {
        IDownloadConnection? connection = request.ConnectionFactory(source);
        if (connection is null || WorkScheduler is null) return connection;
        WorkResource resources = probe ? WorkResource.Http : WorkResource.Http | WorkResource.Disk;
        WorkPriority priority = WorkScheduler.CurrentPriority;
        return connection is ISegmentedDownloadConnection segmented
            ? new ScheduledSegmentedDownloadConnection(segmented, WorkScheduler, priority, resources)
            : new ScheduledDownloadConnection(connection, WorkScheduler, priority, resources);
    }

    private async Task ExecuteAndCompleteAsync(string destinationPath, DownloadOperation operation)
    {
        using LogOperation? trace = _log?.BeginOperation(LogModuleName, "Transfer", $"destination={destinationPath}");
        int lastStage = -1;
        Action? complete = null;
        try
        {
            void Report(DownloadProgress progress)
            {
                if (Interlocked.Exchange(ref lastStage, (int)progress.Stage) != (int)progress.Stage)
                    trace?.Stage(progress.Stage.ToString(), $"source={DescribeSource(progress.Source)}");
                operation.Report(progress);
                UpsertView(new DownloadTransferView(
                    destinationPath,
                    progress.Stage,
                    progress.Source,
                    progress.DownloadedBytes,
                    progress.TotalBytes,
                    progress.BytesPerSecond));
            }

            DownloadTransferResult result = await DownloadCoreAsync(
                operation.Request,
                destinationPath,
                Report,
                operation.CancellationToken).ConfigureAwait(false);
            if (result.Success) trace?.Complete($"bytes={result.TotalBytes} failed_sources={result.Errors.Count}");
            else trace?.Reject("download.sources_exhausted");
            complete = () => operation.SetResult(result);
        }
        catch (OperationCanceledException failure)
        {
            trace?.Cancel();
            complete = () => operation.SetCanceled(failure.CancellationToken);
        }
        catch (Exception failure)
        {
            trace?.Fail(failure);
            complete = () => operation.SetException(failure);
        }
        finally
        {
            RemoveView(destinationPath);
            if (_active.TryGetValue(destinationPath, out Lazy<DownloadOperation>? lazyOperation)
                && lazyOperation.IsValueCreated
                && ReferenceEquals(lazyOperation.Value, operation))
            {
                _active.TryRemove(new KeyValuePair<string, Lazy<DownloadOperation>>(destinationPath, lazyOperation));
            }

            complete?.Invoke();
            operation.Dispose();
        }
    }
}
