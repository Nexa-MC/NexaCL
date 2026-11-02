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

    /// <summary>
    /// Attempts one segmented transfer against the source. Returns null when the source cannot
    /// serve parallel segments (non-segmented connection, server rejection, or a file too small
    /// to split), and the caller falls back to the single-stream path.
    /// </summary>
    private async Task<DownloadTransferResult?> TryDownloadSegmentedAsync(
        DownloadRequest request,
        string source,
        string destinationPath,
        List<DownloadAttemptError> errors,
        long startedAt,
        Action<DownloadProgress> report,
        CancellationToken cancellationToken)
    {
        IDownloadConnection? probeConnection = CreateConnection(request, source, probe: true);
        if (probeConnection is not ISegmentedDownloadConnection probe)
        {
            await StopConnectionAsync(probeConnection).ConfigureAwait(false);
            return null;
        }

        long totalLength;
        try
        {
            DownloadConnectionInfo probeInfo = await probe
                .StartSegmentAsync(0, 0, cancellationToken)
                .ConfigureAwait(false);
            if (!probeInfo.IsSupportSegment || probeInfo.BeginOffset != 0 || probeInfo.EndOffset != 0
                || probeInfo.Length <= 0)
            {
                return null;
            }

            totalLength = probeInfo.Length;
        }
        finally
        {
            await StopConnectionAsync(probe).ConfigureAwait(false);
        }

        int segmentCount = Math.Min(
            request.MaxParallelSegments,
            Math.Max(1, (int)Math.Ceiling(totalLength / (double)_minimumSegmentBytes)));
        if (segmentCount <= 1)
        {
            return null;
        }

        string partPrefix = destinationPath + ".NexaSegment." + Guid.NewGuid().ToString("N");
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        long[] segmentBytes = new long[segmentCount];
        long speedStartedAt = Stopwatch.GetTimestamp();
        long lastReportedBytes = 0;
        object progressGate = new();
        try
        {
            Task[] transfers = new Task[segmentCount];
            for (int index = 0; index < segmentCount; index++)
            {
                int segmentIndex = index;
                long begin = totalLength * segmentIndex / segmentCount;
                long end = totalLength * (segmentIndex + 1) / segmentCount - 1;
                string partPath = partPrefix + segmentIndex.ToString(CultureInfo.InvariantCulture);
                _log?.Write(LogLevel.RealTime, LogModuleName,
                    $"Segment started index={segmentIndex} range={begin}-{end} source={DescribeSource(source)}");
                transfers[segmentIndex] = DownloadSegmentAsync(
                    request,
                    source,
                    partPath,
                    segmentIndex,
                    begin,
                    end,
                    totalLength,
                    segmentBytes,
                    speedStartedAt,
                    report,
                    progressGate,
                    () => lastReportedBytes,
                    value => lastReportedBytes = value,
                    cancellationToken);
            }

            await Task.WhenAll(transfers).ConfigureAwait(false);
            _log?.Write(LogLevel.RealTime, LogModuleName,
                $"Segments ready for merge destination={destinationPath} segments={segmentCount}");

            IDownloadWriter writer = request.WriterFactory(destinationPath)
                ?? throw new InvalidOperationException($"No download writer was created for {destinationPath}.");
            try
            {
                Stream output = await writer.CreateStreamAsync(0, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < segmentCount; index++)
                {
                    await using FileStream input = File.OpenRead(partPrefix + index.ToString(CultureInfo.InvariantCulture));
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                report(new DownloadProgress(DownloadStage.Committing, source, totalLength, totalLength, 0));
                await writer.FinishAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await StopWriterAsync(writer).ConfigureAwait(false);
            }

            report(new DownloadProgress(DownloadStage.Completed, source, totalLength, totalLength, 0));
            _log?.Write(LogLevel.Info, LogModuleName,
                $"Segmented download completed destination={destinationPath} source={DescribeSource(source)} bytes={totalLength} segments={segmentCount}");
            return new DownloadTransferResult(
                true,
                destinationPath,
                source,
                totalLength,
                Stopwatch.GetElapsedTime(startedAt),
                errors.ToArray());
        }
        finally
        {
            for (int index = 0; index < segmentCount; index++)
            {
                try
                {
                    File.Delete(partPrefix + index.ToString(CultureInfo.InvariantCulture));
                }
                catch (IOException)
                {
                    // Part cleanup must not mask the transfer outcome.
                }
            }
        }
    }

    private async Task DownloadSegmentAsync(
        DownloadRequest request,
        string source,
        string partPath,
        int segmentIndex,
        long begin,
        long end,
        long totalLength,
        long[] segmentBytes,
        long speedStartedAt,
        Action<DownloadProgress> report,
        object progressGate,
        Func<long> getLastReportedBytes,
        Action<long> setLastReportedBytes,
        CancellationToken cancellationToken)
    {
        IDownloadConnection? connection = CreateConnection(request, source);
        if (connection is not ISegmentedDownloadConnection segmented)
        {
            await StopConnectionAsync(connection).ConfigureAwait(false);
            throw new InvalidOperationException("The download connection does not support segmented requests.");
        }

        try
        {
            DownloadConnectionInfo info = await segmented
                .StartSegmentAsync(begin, end, cancellationToken)
                .ConfigureAwait(false);
            if (info.BeginOffset != begin || info.EndOffset != end)
            {
                throw new IOException($"The server returned a wrong download segment: {info.BeginOffset}-{info.EndOffset}.");
            }

            await using FileStream target = new(
                partPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                _bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
            try
            {
                long expected = end - begin + 1;
                while (segmentBytes[segmentIndex] < expected)
                {
                    int requested = (int)Math.Min(Math.Min(_bufferSize, request.BandwidthBudget?.MaximumReadBytes ?? int.MaxValue), expected - segmentBytes[segmentIndex]);
                    int read = await connection
                        .ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (request.BandwidthBudget is { } bandwidth)
                        await bandwidth.WaitAsync(read, cancellationToken).ConfigureAwait(false);

                    await WriteMeasuredAsync(target, buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref segmentBytes[segmentIndex], read);
                    AggregateSegmentProgress(
                        segmentBytes,
                        totalLength,
                        speedStartedAt,
                        source,
                        report,
                        progressGate,
                        getLastReportedBytes,
                        setLastReportedBytes);
                }

                if (segmentBytes[segmentIndex] != expected)
                {
                    throw new EndOfStreamException($"The download segment is incomplete: {segmentBytes[segmentIndex]}/{expected}.");
                }

                _log?.Write(LogLevel.RealTime, LogModuleName,
                    $"Segment completed index={segmentIndex} bytes={expected} source={DescribeSource(source)}");
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            await StopConnectionAsync(segmented).ConfigureAwait(false);
        }
    }

    private static void AggregateSegmentProgress(
        long[] segmentBytes,
        long totalLength,
        long speedStartedAt,
        string source,
        Action<DownloadProgress> report,
        object progressGate,
        Func<long> getLastReportedBytes,
        Action<long> setLastReportedBytes)
    {
        lock (progressGate)
        {
            long downloaded = 0;
            for (int index = 0; index < segmentBytes.Length; index++)
            {
                downloaded += Interlocked.Read(ref segmentBytes[index]);
            }

            if (downloaded - getLastReportedBytes() < MinBytesBetweenSegmentProgress && downloaded != totalLength)
            {
                return;
            }

            setLastReportedBytes(downloaded);
            double elapsed = Stopwatch.GetElapsedTime(speedStartedAt).TotalSeconds;
            long speed = elapsed < 0.1 ? 0 : checked((long)(downloaded / elapsed));
            report(new DownloadProgress(DownloadStage.Downloading, source, downloaded, totalLength, speed));
        }
    }
}
