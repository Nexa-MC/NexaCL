using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Nexa.Services.Logging;

/// <summary>A bounded disk mirror, with lazy construction or a reserved session file. One worker owns writes and retention.</summary>
public sealed class FileLogSink : ILogSink, ILogRetentionSink, IDisposable, IAsyncDisposable
{
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    private readonly OwnedLogFiles _files;
    private readonly int _maximumFileBytes;
    private readonly Channel<PendingLine> _lines = Channel.CreateBounded<PendingLine>(new BoundedChannelOptions(8192)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _drain;
    private long _dropped;
    private int _disposed, _failed, _retentionDays, _policyVersion;
    private readonly record struct PendingLine(string? Line, SnapshotRequest? Snapshot = null, TaskCompletionSource? Flushed = null);
    private sealed class SnapshotRequest(CancellationToken token)
    {
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource<DiskLogSnapshot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Cancel()
        {
            // If cancellation loses to delivery, the waiting caller still owns disposal.
            if (!Completion.TrySetCanceled(Token) && Completion.Task.IsCompletedSuccessfully)
                Completion.Task.Result.Dispose();
            else if (Completion.Task.IsFaulted) _ = Completion.Task.Exception;
        }
    }

    public FileLogSink(string filePath) : this(filePath, MaximumFileBytes) { }

    internal FileLogSink(string filePath, int maximumFileBytes) : this(filePath, maximumFileBytes, null) { }

    private FileLogSink(string filePath, int maximumFileBytes, FileStream? reserved, FileStream? sessionLease = null)
    {
        if (maximumFileBytes is < 128 or > MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        _files = new(filePath); _maximumFileBytes = maximumFileBytes;
        _drain = Task.Run(() => DrainAsync(reserved, sessionLease));
    }

    public string FilePath => _files.CurrentPath;

    /// <summary>Reserves a unique UTC timestamp file for one launcher session.</summary>
    public static FileLogSink CreateSession(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        new OwnedLogFiles(Path.Combine(directory, "launcher.log")).EnsureDirectory();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        for (int attempt = 0; attempt < 256; attempt++)
        {
            string path = Path.Combine(directory, timestamp.AddTicks(attempt).ToString(OwnedLogFiles.SessionTimestampFormat, CultureInfo.InvariantCulture) + ".log");
            FileStream? reserved = null;
            FileStream? lease = null;
            try
            {
                reserved = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
                lease = new FileStream(path + ".active", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                return new FileLogSink(path, MaximumFileBytes, reserved, lease);
            }
            catch (IOException) when (reserved is null && File.Exists(path)) { }
            catch { reserved?.Dispose(); lease?.Dispose(); throw; }
        }
        throw new IOException("Unable to reserve a unique session log.");
    }

    /// <summary>Flushes all admitted records before a startup failure offers its local file link.</summary>
    public async Task FlushAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) == 0)
        {
            var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await _lines.Writer.WriteAsync(new(null, Flushed: flushed), token).ConfigureAwait(false);
                await flushed.Task.WaitAsync(token).ConfigureAwait(false);
                return;
            }
            catch (ChannelClosedException) { }
        }
        await _drain.WaitAsync(token).ConfigureAwait(false);
        if (Volatile.Read(ref _failed) != 0) throw new IOException("The disk log sink is unavailable.");
    }

    public void Write(LogEntry entry, string formattedLine)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        // This ceiling also bounds the channel even if a caller supplies an enormous exception.
        int limit = Math.Min(16_384, (_maximumFileBytes - 64) / 4);
        string line = formattedLine.Length <= limit ? formattedLine : formattedLine[..limit] + " [disk record truncated]";
        if (!_lines.Writer.TryWrite(new(line))) Interlocked.Increment(ref _dropped);
    }

    public void SetRetentionDays(int days)
    {
        if (days is < 1 or > 90) throw new ArgumentOutOfRangeException(nameof(days));
        if (Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _retentionDays, days);
        Interlocked.Increment(ref _policyVersion);
        _lines.Writer.TryWrite(default); // A full queue already guarantees the worker will wake.
    }

    /// <summary>Explicit, cancellable export of bounded disk facts; free-form disk text is excluded.</summary>
    public async Task ExportAsync(string destination, CancellationToken token = default)
    {
        using var snapshot = await CaptureSnapshotAsync(token).ConfigureAwait(false);
        await DiskLogExport.WriteAsync(destination, snapshot, token).ConfigureAwait(false);
    }

    internal async Task<DiskLogSnapshot> CaptureSnapshotAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
        {
            await _drain.WaitAsync(token).ConfigureAwait(false);
            if (Volatile.Read(ref _failed) != 0) throw new IOException("The disk log sink is unavailable.");
            return await Task.Run(() => _files.CaptureSnapshot(token), token).ConfigureAwait(false);
        }
        var request = new SnapshotRequest(token);
        try
        {
            await _lines.Writer.WriteAsync(new(null, request), token).ConfigureAwait(false);
            return await request.Completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { request.Cancel(); throw; }
        catch (ChannelClosedException)
        {
            await _drain.WaitAsync(token).ConfigureAwait(false);
            if (Volatile.Read(ref _failed) != 0) throw new IOException("The disk log sink is unavailable.");
            return await Task.Run(() => _files.CaptureSnapshot(token), token).ConfigureAwait(false);
        }
    }

    private async Task DrainAsync(FileStream? reserved, FileStream? sessionLease)
    {
        StreamWriter? writer = reserved is null ? null : new StreamWriter(reserved, new UTF8Encoding(false), 65536);
        long writtenBytes = 0, flushedAt = Stopwatch.GetTimestamp();
        int appliedPolicy = 0;
        SnapshotRequest? capturing = null;
        TaskCompletionSource? flushing = null;
        try
        {
            while (await _lines.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                ApplyRetention();
                int drained = 0;
                while (drained++ < 256 && _lines.Reader.TryRead(out var pending))
                {
                    if (pending.Line is { } line) await WriteLineAsync(line).ConfigureAwait(false);
                    if (pending.Flushed is { } barrier)
                    {
                        flushing = barrier;
                        if (writer is not null) await writer.FlushAsync().ConfigureAwait(false);
                        barrier.TrySetResult();
                        flushing = null;
                    }
                    if (pending.Snapshot is { } request)
                    {
                        capturing = request;
                        if (writer is not null) await writer.FlushAsync().ConfigureAwait(false);
                        try
                        {
                            var snapshot = _files.CaptureSnapshot(request.Token);
                            if (!request.Completion.TrySetResult(snapshot)) snapshot.Dispose();
                        }
                        catch (OperationCanceledException) { request.Completion.TrySetCanceled(request.Token); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                        { request.Completion.TrySetException(error); }
                        capturing = null;
                    }
                }
                if (writer is not null && (!_lines.Reader.TryPeek(out _) || Stopwatch.GetElapsedTime(flushedAt) >= TimeSpan.FromMilliseconds(200)))
                { await writer.FlushAsync().ConfigureAwait(false); flushedAt = Stopwatch.GetTimestamp(); }
            }
            ApplyRetention();
            long dropped = Interlocked.Read(ref _dropped);
            if (writer is not null && dropped > 0)
                await WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] [Warn] [Logging] File sink omitted {dropped} lines because its bounded queue was full.").ConfigureAwait(false);

            void ApplyRetention()
            {
                int version = Volatile.Read(ref _policyVersion);
                if (version == appliedPolicy) return;
                _files.Prune(Volatile.Read(ref _retentionDays));
                appliedPolicy = version;
            }

            async Task WriteLineAsync(string line)
            {
                int bytes = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
                if (writer is null)
                {
                    _files.EnsureDirectory(); OwnedLogFiles.EnsureRegularFile(_files.CurrentPath);
                    writtenBytes = File.Exists(_files.CurrentPath) ? new FileInfo(_files.CurrentPath).Length : 0;
                }
                if (writtenBytes + bytes > _maximumFileBytes && writtenBytes > 0)
                {
                    if (writer is not null) { await writer.DisposeAsync().ConfigureAwait(false); writer = null; }
                    OwnedLogFiles.EnsureRegularFile(_files.CurrentPath);
                    File.Move(_files.CurrentPath, _files.NewArchivePath(), overwrite: false);
                    writtenBytes = 0;
                    _files.Prune(appliedPolicy == 0 ? null : Volatile.Read(ref _retentionDays));
                }
                writer ??= new StreamWriter(new FileStream(_files.CurrentPath, FileMode.Append, FileAccess.Write,
                    FileShare.Read, 65536, useAsync: true), new UTF8Encoding(false), 65536);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                writtenBytes += bytes;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Volatile.Write(ref _failed, 1);
            capturing?.Completion.TrySetException(new IOException("The disk log sink is unavailable."));
            flushing?.TrySetException(new IOException("The disk log sink is unavailable."));
            _lines.Writer.TryComplete();
        }
        finally
        {
            while (_lines.Reader.TryRead(out var pending))
            {
                pending.Snapshot?.Completion.TrySetException(new IOException("The disk log sink is unavailable."));
                pending.Flushed?.TrySetException(new IOException("The disk log sink is unavailable."));
            }
            if (writer is not null)
                try { await writer.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Volatile.Write(ref _failed, 1); }
            if (sessionLease is not null)
            {
                string path = sessionLease.Name;
                try { sessionLease.Dispose(); _files.EnsureNoDirectoryLinks(); OwnedLogFiles.EnsureRegularFile(path); File.Delete(path); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Closes admission without blocking; DisposeAsync also drains and flushes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _lines.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync() { Dispose(); await _drain.ConfigureAwait(false); }
}
