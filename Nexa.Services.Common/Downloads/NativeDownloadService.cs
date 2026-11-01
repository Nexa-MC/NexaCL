#if NEXA_NATIVE_SERVICES
using Nexa.Native.Network.Client;
using Nexa.Native.Network.Services;
using Nexa.Services.Logging;
using Nexa.Services.Network;
using Nexa.Services.Scheduling;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Downloads;

/// <summary>Production descriptor adapter; execution and state ownership are native.</summary>
public sealed class DownloadService : IDisposable
{
    public const string OwnerName = "Nexa.Services.Downloads", LogModuleName = "Download";
    public static readonly XsrSemanticId TransfersKey = DownloadStateContract.TransfersKey;
    public static void DeclareState(XsrStateStoreBuilder builder) => NativeNetworkClient.DeclareDownloadState(builder);
    public XsrStateStore StateStore { get; }
    public IWorkScheduler? WorkScheduler { get; init; }
    public DownloadBandwidthLimiter? BandwidthLimiter { get; internal set; }
    public Func<bool>? BackgroundDownloadsEnabled { get; set; }
    internal TimeProvider ProgressClock { get; init; } = TimeProvider.System;
    private INativeDownloadPort? _client;
    private bool _closed;

    public DownloadService(XsrStateStore store, int bufferSize = 128 * 1024, LogService? log = null,
        long minimumSegmentBytes = 8 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSegmentBytes);
        StateStore = store ?? throw new ArgumentNullException(nameof(store));
    }
    internal void AttachNative(INativeDownloadPort client) => _client = client;
    public Task<DownloadTransferResult> DownloadAsync(DownloadRequest request, Action<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_closed, this); ArgumentNullException.ThrowIfNull(request);
        var client = _client ?? throw new InvalidOperationException("Native download composition has not initialized.");
        WorkPriority priority = WorkScheduler?.CurrentPriority ?? WorkPriority.Interactive;
        long bandwidth = (request.BandwidthBudget ?? BandwidthLimiter?.Capture())?.BytesPerSecond ?? 0;
        bool background = BackgroundDownloadsEnabled?.Invoke() != false;
        return Task.Run(async () =>
        {
            List<NativeDownloadSource> sources = [];
            foreach (string address in request.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var connection = request.ConnectionFactory(address);
                try
                {
                    if (connection is null) { sources.Add(new(address, new NetworkPreferences(), Available: false)); continue; }
                    if (connection is not INativeDownloadConnectionDescriptor described)
                        throw new NotSupportedException("Native production requires a described download connection.");
                    sources.Add(await described.DescribeDownloadAsync(cancellationToken).ConfigureAwait(false));
                }
                finally
                {
                    if (connection is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                    else if (connection is IDisposable disposable) disposable.Dispose();
                }
            }
            string destination = Path.GetFullPath(request.DestinationPath);
            var writer = request.WriterFactory(destination) ?? throw new InvalidOperationException("Missing download writer.");
            string extension;
            try { extension = writer is INativeDownloadWriterDescriptor described ? described.TemporaryExtension
                : throw new NotSupportedException("Native production requires a described file writer."); }
            finally
            {
                if (writer is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                else if (writer is IDisposable disposable) disposable.Dispose();
            }
            return await client.DownloadAsync(new(sources, destination, request.AllowResume, request.MaxParallelSegments,
                extension, request.CacheIdentity?.Digest, request.CacheIdentity?.Size ?? request.ExpectedSize, bandwidth, priority, background), progress, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }
    public void Dispose() => _closed = true;
}
#endif
