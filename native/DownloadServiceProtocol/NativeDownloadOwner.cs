using System.Collections.Concurrent;
using System.Net;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Downloads;
using Nexa.Services.Logging;
using Nexa.Xsr.State;

namespace Nexa.Native.Network.Services;

internal sealed class NativeDownloadOwner : IAsyncDisposable
{
    private readonly XsrStateStore _store;
    private readonly DownloadService _service;
    private readonly RemoteWorkScheduler _work;
    private readonly ServiceStatePublisher _publications;
    private readonly LogService _log;
    private readonly NativeLogPipe _logs;
    private readonly AsyncLocal<NativeDownloadRequest?> _current = new();
    private readonly ConcurrentQueue<int> _slots = new(Enumerable.Range(1, 64));
    private readonly SemaphoreSlim _admission = new(64, 64);
    private readonly Func<HttpRequestMessage, NativeDownloadSource, bool, CancellationToken, Task<HttpResponseMessage>> _send;
    private long _progressRevision;

    internal NativeDownloadOwner(ServiceEndpoint endpoint,
        Func<HttpRequestMessage, NativeDownloadSource, bool, CancellationToken, Task<HttpResponseMessage>> send)
    {
        _send = send; _work = new(endpoint); _publications = new(endpoint);
        _logs = new(async message =>
        {
            using var writer = NetworkProtocol.Request(DownloadProtocol.Log);
            writer.Int32((int)message.Level); writer.String(message.Module); writer.String(message.Message); writer.String(message.Exception);
            writer.Boolean(message.Operation); writer.TimeSpan(message.Duration); writer.Boolean(message.Succeeded);
            byte[] bytes = writer.Finish(); int delay = 1;
            while (true)
            {
                try { endpoint.Publish(bytes); return; }
                catch (Nexa.Native.Ui.NativeUiCallBusyException) { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
            }
        });
        XsrStateStoreBuilder builder = new(); DownloadService.DeclareState(builder); LogService.DeclareState(builder); _store = builder.Build();
        _log = new(_store) { MaximumLevel = LogLevel.RealTime }; _log.AddSink(new Mirror(_logs));
        _service = new(_store, log: _log)
        {
            WorkScheduler = _work,
            BandwidthLimiter = new(() => _current.Value?.BandwidthBytesPerSecond ?? 0),
            BackgroundDownloadsEnabled = () => _current.Value?.BackgroundDownloadsEnabled != false
        };
        _store.Changed += Changed;
    }
    internal async Task<byte[]> ExecuteAsync(ServiceWireReader reader, CancellationToken token)
    {
        long call = reader.Int64(); var request = reader.NativeDownloadRequest() ?? throw new InvalidDataException("Missing download descriptor."); reader.End();
        if (call <= 0 || !Path.IsPathFullyQualified(request.DestinationPath) || request.ExpectedSize < 0
            || request.BandwidthBytesPerSecond is < 0 or > 1024L * 1024 * 1024)
            throw new InvalidDataException("Invalid native download admission.");
        await _admission.WaitAsync(token).ConfigureAwait(false);
        if (!_slots.TryDequeue(out int slot)) { _admission.Release(); throw new InvalidOperationException("Native download slot accounting failed."); }
        try
        {
            _current.Value = request;
            using var priority = _work.UsePriority(request.Priority);
            Dictionary<string, NativeDownloadSource> sources = new(StringComparer.Ordinal);
            foreach (var source in request.Sources)
            {
                if (sources.TryGetValue(source.Address, out var existing) && existing != source)
                    throw new InvalidDataException("A repeated download source has conflicting descriptors.");
                sources[source.Address] = source;
            }
            DownloadArtifactIdentity? artifact = request.Digest is null ? null : new(request.Digest, request.ExpectedSize);
            void Report(DownloadProgress progress)
            {
                long revision = Interlocked.Increment(ref _progressRevision);
                _publications.Post(slot, revision, () =>
                {
                    using var writer = NetworkProtocol.Request(DownloadProtocol.Progress);
                    writer.Int64(call); writer.DownloadProgress(progress); return writer.Finish();
                });
            }
            Task<DownloadTransferResult> transfer = _service.DownloadAsync(new()
            {
                Sources = request.Sources.Select(source => source.Address).ToArray(),
                DestinationPath = request.DestinationPath,
                AllowResume = request.AllowResume,
                MaxParallelSegments = request.MaxParallelSegments,
                CacheIdentity = artifact,
                ExpectedArtifact = artifact,
                ExpectedSize = request.ExpectedSize,
                WriterFactory = destination => new FileDownloadWriter(destination, request.TemporaryExtension),
                ConnectionFactory = address => sources[address].Available ? new NativeDownloadHttpConnection(sources[address], _send) : null
            }, Report, token);
            // A caller joining an existing transfer receives its real current snapshot.
            // It need not wait for another body read before observing its admission.
            var active = _store.ReadCollection<DownloadTransferView>(_store.Resolve(DownloadStateContract.TransfersKey), token);
            foreach (var item in active.Items)
                if (Nexa.Core.PathIdentity.Comparer.Equals(item.DestinationPath, request.DestinationPath))
                    Report(new(item.Stage, item.Source, item.DownloadedBytes, item.TotalBytes, item.BytesPerSecond));
            var result = await transfer.ConfigureAwait(false);
            using var output = new ServiceWireWriter();
            output.NativeDownloadOutcome(NativeDownloadResults.Encode(result));
            return output.Finish();
        }
        finally
        {
            try
            {
                await _work.FlushAsync().ConfigureAwait(false);
                await _publications.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally { _current.Value = null; _slots.Enqueue(slot); _admission.Release(); }
        }
    }
    private void Changed(XsrStateChange change)
    {
        if (change.SemanticId != DownloadStateContract.TransfersKey) return;
        var entry = _store.CaptureSnapshot().Entries.Single(item => item.SemanticId == change.SemanticId);
        _publications.Post(0, entry.Revision, () =>
        {
            using var writer = NetworkProtocol.Request(DownloadProtocol.State);
            writer.Int64(entry.Revision); writer.Int32((int)entry.Availability);
            var items = (IReadOnlyList<DownloadTransferView>)entry.Value!; writer.Count(items.Count);
            foreach (var item in items) writer.DownloadTransferView(item);
            return writer.Finish();
        });
    }
    public async ValueTask DisposeAsync()
    {
        // The enclosing endpoint first cancels and physically drains every download call.
        _store.Changed -= Changed; _service.Dispose();
        await _publications.DisposeAsync().ConfigureAwait(false);
        _log.Dispose(); await _logs.DisposeAsync().ConfigureAwait(false);
        await _work.DisposeAsync().ConfigureAwait(false); _admission.Dispose();
    }
    private sealed class Mirror(NativeLogPipe pipe) : ILogSink, ILogOperationSink
    {
        public void Write(LogEntry entry, string formattedLine) => pipe.Post(new(entry.Level, entry.Module, entry.Message, entry.ExceptionText));
        public void OnOperation(string module, TimeSpan duration, bool succeeded) => pipe.Post(new(LogLevel.Debug, module, "", null, true, duration, succeeded));
    }
}
