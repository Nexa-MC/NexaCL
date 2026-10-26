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

    private async Task<DownloadTransferResult> DownloadCoreAsync(
        DownloadRequest request,
        string destinationPath,
        Action<DownloadProgress> report,
        CancellationToken cancellationToken)
    {
        List<DownloadAttemptError> errors = [];
        long startedAt = Stopwatch.GetTimestamp();

        foreach (string source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            IDownloadConnection? connection = null;
            IDownloadWriter? writer = null;
            long requestedOffset = 0;
            string stage = "connect";
            try
            {
                _log?.Debug(LogModuleName, $"Source attempt started source={DescribeSource(source)} destination={destinationPath} attempt={errors.Count + 1}");
                report(new DownloadProgress(DownloadStage.Connecting, source, 0, -1, 0));
                if (request.AllowResume && request.MaxParallelSegments > 1)
                {
                    stage = "segmented_transfer";
                    DownloadTransferResult? segmented = await TryDownloadSegmentedAsync(
                        request,
                        source,
                        destinationPath,
                        errors,
                        startedAt,
                        report,
                        cancellationToken).ConfigureAwait(false);
                    if (segmented is not null)
                    {
                        return segmented;
                    }
                }

                stage = "open_writer";
                writer = request.WriterFactory(destinationPath)
                    ?? throw new InvalidOperationException($"No download writer was created for {destinationPath}.");
                requestedOffset = request.AllowResume ? Math.Max(0, writer.ExistingLength) : 0;
                _log?.Debug(LogModuleName, $"Resume check destination={destinationPath} existing_bytes={requestedOffset}");
                stage = "connect";
                connection = CreateConnection(request, source)
                    ?? throw new InvalidOperationException($"No download connection was created for {source}.");
                DownloadConnectionInfo connectionInfo = await connection
                    .StartAsync(requestedOffset, cancellationToken)
                    .ConfigureAwait(false);
                if (!request.AllowResume && connectionInfo.BeginOffset != 0)
                    throw new InvalidDataException("A fresh download requires a response starting at zero.");

                long startOffset = connectionInfo.BeginOffset == requestedOffset ? requestedOffset : 0;
                _log?.Debug(LogModuleName, $"Source connected source={DescribeSource(source)} begin_offset={connectionInfo.BeginOffset} length={connectionInfo.Length}");
                stage = "transfer";
                Stream writeStream = await writer
                    .CreateStreamAsync(startOffset, cancellationToken)
                    .ConfigureAwait(false);
                report(new DownloadProgress(
                    DownloadStage.Reading,
                    source,
                    startOffset,
                    Math.Max(connectionInfo.Length, startOffset),
                    0));

                byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
                try
                {
                    long readStartedAt = Stopwatch.GetTimestamp();
                    long sessionRead = 0;
                    long totalRead = startOffset;
                    long lastProgressAt = 0, lastProgressBytes = startOffset;
                    bool reportedDownload = false;
                    while (true)
                    {
                        int read = await connection
                            .ReadAsync(buffer.AsMemory(0, Math.Min(_bufferSize, request.BandwidthBudget?.MaximumReadBytes ?? int.MaxValue)), cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        if (request.BandwidthBudget is { } bandwidth)
                            await bandwidth.WaitAsync(read, cancellationToken).ConfigureAwait(false);

                        if (connectionInfo.Length >= 0 && read > connectionInfo.Length - totalRead)
                            throw new InvalidDataException("Download response exceeds its declared length.");
                        if (request.ExpectedSize is { } expectedSize && read > expectedSize - totalRead)
                            throw new InvalidDataException("Download response exceeds the artifact size.");

                        await WriteMeasuredAsync(writeStream, buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        totalRead += read;
                        sessionRead += read;
                        long now = ProgressClock.GetTimestamp();
                        if (!reportedDownload || ProgressClock.GetElapsedTime(lastProgressAt, now) >= MinimumSingleStreamProgressInterval)
                        {
                            report(new DownloadProgress(
                                DownloadStage.Downloading,
                                source,
                                totalRead,
                                Math.Max(connectionInfo.Length, totalRead),
                                CalculateSpeed(sessionRead, readStartedAt)));
                            reportedDownload = true;
                            lastProgressAt = now;
                            lastProgressBytes = totalRead;
                        }
                    }

                    if (lastProgressBytes != totalRead)
                    {
                        report(new DownloadProgress(
                            DownloadStage.Downloading,
                            source,
                            totalRead,
                            Math.Max(connectionInfo.Length, totalRead),
                            CalculateSpeed(sessionRead, readStartedAt)));
                    }

                    if (connectionInfo.Length >= 0 && totalRead != connectionInfo.Length)
                        throw new EndOfStreamException("Download response ended before its declared length.");
                    if (request.ExpectedSize is { } expectedLength && totalRead != expectedLength)
                        throw new EndOfStreamException("Download response ended before the artifact was complete.");
                    stage = "flush";
                    await writeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if (request.ExpectedArtifact is { } artifact && writer is FileDownloadWriter fileWriter)
                    {
                        stage = "verify";
                        await writer.StopAsync(cancellationToken).ConfigureAwait(false);
                        if (!await MatchesCacheAsync(fileWriter.TempPath, artifact, cancellationToken).ConfigureAwait(false))
                        {
                            File.Delete(fileWriter.TempPath);
                            throw new InvalidDataException("Artifact digest or size mismatch.");
                        }
                    }
                    stage = "commit";
                    report(new DownloadProgress(DownloadStage.Committing, source, totalRead, totalRead, 0));
                    await writer.FinishAsync(cancellationToken).ConfigureAwait(false);
                    report(new DownloadProgress(DownloadStage.Completed, source, totalRead, totalRead, 0));
                    DownloadTransferResult result = new(
                        true,
                        destinationPath,
                        source,
                        totalRead,
                        Stopwatch.GetElapsedTime(startedAt),
                        errors.ToArray());
                    _log?.Write(LogLevel.Info, LogModuleName,
                        $"Download completed destination={destinationPath} source={DescribeSource(source)} bytes={totalRead} failed_sources={errors.Count}");
                    return result;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                _log?.Info(LogModuleName, $"Source attempt cancelled stage={stage} destination={destinationPath} source={DescribeSource(source)}");
                throw;
            }
            catch (Exception exception)
            {
                _log?.Write(LogLevel.Warn, LogModuleName,
                    $"Source attempt failed; checking failover stage={stage} destination={destinationPath} source={DescribeSource(source)} attempt={errors.Count + 1}", ExceptionDiagnostics.Describe(exception));
                if (ShouldDiscardPartialDownload(exception, requestedOffset))
                {
                    _log?.Warn(LogModuleName, $"Discarding invalid resume data destination={destinationPath} requested_offset={requestedOffset}");
                    await ResetPartialDownloadAsync(writer, cancellationToken).ConfigureAwait(false);
                }

                errors.Add(new DownloadAttemptError(source, exception.Message, exception));
                report(new DownloadProgress(DownloadStage.Retrying, source, 0, -1, 0));
            }
            finally
            {
                await StopWriterAsync(writer).ConfigureAwait(false);
                await StopConnectionAsync(connection).ConfigureAwait(false);
            }
        }

        report(new DownloadProgress(DownloadStage.Failed, string.Empty, 0, -1, 0));
        _log?.Write(LogLevel.Error, LogModuleName,
            $"All download sources failed destination={destinationPath} sources={request.Sources.Count} errors={errors.Count}");
        return new DownloadTransferResult(
            false,
            destinationPath,
            null,
            0,
            Stopwatch.GetElapsedTime(startedAt),
            errors.ToArray());
    }
}
