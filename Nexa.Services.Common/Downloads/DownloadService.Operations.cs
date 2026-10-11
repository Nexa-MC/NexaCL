using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Nexa.Services.Logging;
using Nexa.Services.Scheduling;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Downloads;

public sealed partial class DownloadService
{

    private void UpsertView(DownloadTransferView view)
    {
        lock (_stateGate)
        {
            for (int attempt = 0; attempt < MaxStateConflicts; attempt++)
            {
                XsrCollectionSnapshot<DownloadTransferView> snapshot =
                    _store.ReadCollection<DownloadTransferView>(_transfersId);
                XsrCollectionApplyResult result = _store.PublishDelta(
                    _transfersId,
                    new XsrCollectionDelta<DownloadTransferView, string>(snapshot.Revision, [view], []));
                if (result.IsApplied)
                {
                    return;
                }
            }
        }
    }

    private void RemoveView(string destinationPath)
    {
        lock (_stateGate)
        {
            for (int attempt = 0; attempt < MaxStateConflicts; attempt++)
            {
                XsrCollectionSnapshot<DownloadTransferView> snapshot =
                    _store.ReadCollection<DownloadTransferView>(_transfersId);
                List<string> removals = snapshot
                    .Items
                    .Where(view => string.Equals(view.DestinationPath, destinationPath, StringComparison.Ordinal))
                    .Select(view => view.DestinationPath)
                    .ToList();
                if (removals.Count == 0)
                {
                    return;
                }

                XsrCollectionApplyResult result = _store.PublishDelta(
                    _transfersId,
                    new XsrCollectionDelta<DownloadTransferView, string>(snapshot.Revision, [], removals));
                if (result.IsApplied)
                {
                    return;
                }
            }
        }
    }

    private static long CalculateSpeed(long bytes, long startedAt)
    {
        double elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        return elapsed < 0.1 ? 0 : checked((long)(bytes / elapsed));
    }

    private static bool ShouldDiscardPartialDownload(Exception exception, long requestedOffset) =>
        requestedOffset > 0
        && exception is System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.RequestedRangeNotSatisfiable };

    private static async ValueTask ResetPartialDownloadAsync(IDownloadWriter? writer, CancellationToken cancellationToken)
    {
        if (writer is null)
        {
            return;
        }

        Stream? resetStream = null;
        try
        {
            resetStream = await writer.CreateStreamAsync(0, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (resetStream is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                resetStream?.Dispose();
            }
        }
    }

    private static async ValueTask StopWriterAsync(IDownloadWriter? writer)
    {
        if (writer is null)
        {
            return;
        }

        try
        {
            await writer.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup must not hide the original transfer outcome.
        }
    }

    private static async ValueTask StopConnectionAsync(IDownloadConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup must not hide the original transfer outcome.
        }
    }

    private static void ValidateRequest(DownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        ArgumentNullException.ThrowIfNull(request.Sources);
        ArgumentNullException.ThrowIfNull(request.ConnectionFactory);
        ArgumentNullException.ThrowIfNull(request.WriterFactory);
        if (request.MaxParallelSegments <= 0)
        {
            throw new ArgumentException("MaxParallelSegments must be positive.", nameof(request));
        }

        if (request.Sources.Count == 0)
        {
            throw new ArgumentException("At least one download source is required.", nameof(request));
        }
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string DescribeSource(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            // Source paths, query strings and userinfo may carry signed credentials. The
            // destination identifies the artifact; the source only identifies the origin.
            return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.IdnHost}" : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
        }

        return "(non-uri source)";
    }

    private sealed class DownloadOperation(
        DownloadRequest request,
        Func<DownloadOperation, Task> start) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource<DownloadTransferResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<long, Action<DownloadProgress>> _subscribers = [];
        private readonly object _sync = new();
        private readonly Func<DownloadOperation, Task> _start = start;
        private long _subscriberId;
        private int _waiterCount;
        private int _started;
        private bool _disposed;

        public DownloadRequest Request { get; } = request;

        public CancellationToken CancellationToken => _cancellation.Token;

        public async Task<DownloadTransferResult> WaitAsync(
            Action<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            long subscriberId = progress is null ? 0 : AddSubscriber(progress);
            Interlocked.Increment(ref _waiterCount);
            try
            {
                if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
                {
                    _ = _start(this);
                }

                return await _completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (subscriberId != 0)
                {
                    RemoveSubscriber(subscriberId);
                }

                if (Interlocked.Decrement(ref _waiterCount) == 0 && !_completion.Task.IsCompleted)
                {
                    await _cancellation.CancelAsync().ConfigureAwait(false);
                }
            }
        }

        public void Report(DownloadProgress progress)
        {
            lock (_sync)
            {
                foreach (Action<DownloadProgress> subscriber in _subscribers.Values)
                {
                    try
                    {
                        subscriber(progress);
                    }
                    catch (Exception)
                    {
                        // Progress handlers must not terminate the transfer.
                    }
                }
            }
        }

        public void SetResult(DownloadTransferResult result) => _completion.TrySetResult(result);

        public void SetCanceled(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);

        public void SetException(Exception exception) => _completion.TrySetException(exception);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation.Dispose();
        }

        private long AddSubscriber(Action<DownloadProgress> progress)
        {
            long id = Interlocked.Increment(ref _subscriberId);
            lock (_sync)
            {
                _subscribers.Add(id, progress);
            }

            return id;
        }

        private void RemoveSubscriber(long subscriberId)
        {
            lock (_sync)
            {
                _subscribers.Remove(subscriberId);
            }
        }
    }
}
