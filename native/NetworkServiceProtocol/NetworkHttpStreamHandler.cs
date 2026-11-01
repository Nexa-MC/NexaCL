using System.Net;
using System.Net.Http.Headers;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Network.Services;

internal sealed class NetworkHttpStreamHandler(
    Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> invoke,
    Func<HttpRequestMessage, byte[]> configuration) : HttpMessageHandler, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, RequestState> _active = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _admitting;
    private bool _closed;
    private Exception? _disposalFailure;
    internal Task Retirement => _drained.Task;

    private async Task<byte[]> CallAsync(int operation, Action<ServiceWireWriter> write, CancellationToken token)
    {
        using var writer = NetworkProtocol.Request(operation); write(writer);
        try { return await invoke(writer.Finish(), token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        { throw new HttpRequestException("The native HTTP operation failed.", error); }
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_admitting + _active.Count >= 128) throw new HttpRequestException("Native HTTP request budget exceeded.");
            _admitting++;
        }
        return SendCoreAsync(request, cancellationToken);
    }
    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken token)
    {
        RequestState? state = null;
        bool transferred = false;
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        try
        {
            byte[] result = await CallAsync(NetworkHttpWire.Create, writer =>
            {
                writer.Buffer(configuration(request)); writer.String(request.Method.Method); writer.Uri(request.RequestUri);
                writer.Int32(request.Version.Major); writer.Int32(request.Version.Minor); writer.Int32((int)request.VersionPolicy);
                writer.Boolean(request.Content is not null); writer.Int64(request.Content is null ? 0 : request.Content.Headers.ContentLength ?? -1);
                NetworkHttpWire.WriteHeaders(writer, request.Headers);
                if (request.Content is not null) NetworkHttpWire.WriteHeaders(writer, request.Content.Headers);
            }, admission.Token).ConfigureAwait(false);
            long handle;
            using (var reader = new ServiceWireReader(result))
            { handle = reader.Int64(); reader.End(); if (handle <= 0) throw new InvalidDataException("Invalid native HTTP handle."); }
            state = new(this, handle, token, _stop.Token);
            lock (_gate) _active.Add(handle, state);
            byte[] claim = await CallAsync(NetworkHttpWire.Claim, writer => writer.Int64(handle), admission.Token).ConfigureAwait(false);
            using (var reader = new ServiceWireReader(claim))
            { if (!reader.Boolean()) throw new HttpRequestException("The native HTTP request retired before admission."); reader.End(); }
            state.Upload = request.Content is null ? Task.CompletedTask : UploadAsync(state, request.Content);
            Task<byte[]> headers = CallAsync(NetworkHttpWire.Headers, writer => writer.Int64(handle), state.Token);
            Task first = await Task.WhenAny(headers, state.Upload).ConfigureAwait(false);
            if (first == state.Upload && !state.Upload.IsCompletedSuccessfully) await state.Upload.ConfigureAwait(false);
            byte[] bytes;
            try { bytes = await headers.ConfigureAwait(false); }
            catch
            {
                state.Cancel();
                try { await state.Upload.ConfigureAwait(false); } catch (OperationCanceledException) { }
                throw;
            }
            using var reply = new ServiceWireReader(bytes);
            int status = reply.Int32(); string? reason = reply.String(); int major = reply.Int32(), minor = reply.Int32(); var uri = reply.Uri();
            var method = new HttpMethod(reply.String() ?? throw new InvalidDataException("Missing final HTTP method."));
            if (status is < 100 or > 599 || major is < 1 or > 3 || minor is < 0 or > 1) throw new InvalidDataException("Invalid native HTTP response.");
            var content = new RemoteContent(state);
            var response = new HttpResponseMessage((HttpStatusCode)status)
            { ReasonPhrase = reason, Version = new(major, minor), RequestMessage = new(method, uri), Content = content };
            try
            {
                NetworkHttpWire.ReadHeaders(reply, response.Headers); NetworkHttpWire.ReadHeaders(reply, content.Headers); reply.End();
                content.CaptureLength(); transferred = true; return response;
            }
            catch { response.Dispose(); throw; }
        }
        finally
        {
            state?.CompleteAdmission();
            try { if (!transferred && state is not null) await state.DisposeAsync().ConfigureAwait(false); }
            finally { lock (_gate) { _admitting--; CompleteDrain(); } }
        }
    }
    private async Task UploadAsync(RequestState state, HttpContent content)
    {
        try
        {
            using var target = new UploadStream(state);
            await content.CopyToAsync(target, state.Token).ConfigureAwait(false);
            await CallAsync(NetworkHttpWire.EndUpload, writer => { writer.Int64(state.Handle); writer.Int64(target.Sequence); }, state.Token).ConfigureAwait(false);
        }
        catch { state.Cancel(); throw; }
    }
    private void Removed(long handle)
    { lock (_gate) { _active.Remove(handle); CompleteDrain(); } }
    private void CompleteDrain()
    {
        if (!_closed || _admitting != 0 || _active.Count != 0) return;
        if (_disposalFailure is null) _drained.TrySetResult();
        else { _drained.TrySetException(_disposalFailure); _ = _drained.Task.Exception; }
    }
    private void DisposalFailed(Exception error) { lock (_gate) _disposalFailure ??= error; }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RequestState[] pending;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true; pending = [.. _active.Values]; CompleteDrain();
            }
            _stop.Cancel();
            foreach (var state in pending) state.Close();
        }
        base.Dispose(disposing);
    }
    public async ValueTask DisposeAsync()
    { Dispose(); await _drained.Task.ConfigureAwait(false); _stop.Dispose(); }

    private sealed class RequestState : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly NetworkHttpStreamHandler _owner;
        private readonly CancellationTokenSource _lifetime;
        private readonly TaskCompletionSource _admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _retirement;
        internal long Handle { get; }
        internal CancellationToken Token => _lifetime.Token;
        internal Task Upload { get; set; } = Task.CompletedTask;
        internal RequestState(NetworkHttpStreamHandler owner, long handle, CancellationToken request, CancellationToken ownerStop)
        { _owner = owner; Handle = handle; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(request, ownerStop); }
        internal void Cancel() => _lifetime.Cancel();
        internal void CompleteAdmission() => _admission.TrySetResult();
        internal void Close() => _ = ObserveRetirementAsync();
        private async Task ObserveRetirementAsync() { try { await DisposeAsync().ConfigureAwait(false); } catch { } }
        internal Task<byte[]> CallAsync(int operation, Action<ServiceWireWriter> write, CancellationToken token) => _owner.CallAsync(operation, write, token);
        public ValueTask DisposeAsync() { lock (_gate) return new(_retirement ??= RetireAsync()); }
        private async Task RetireAsync()
        {
            Cancel();
            try
            {
                await _admission.Task.ConfigureAwait(false);
                await _owner.CallAsync(NetworkHttpWire.Release, writer => writer.Int64(Handle), CancellationToken.None).ConfigureAwait(false);
                try { await Upload.ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            }
            catch (Exception error) { _owner.DisposalFailed(error); throw; }
            finally { _lifetime.Dispose(); _owner.Removed(Handle); }
        }
    }
    private sealed class RemoteContent(RequestState state) : HttpContent
    {
        private readonly DownloadStream _stream = new(state);
        private long? _length;
        internal void CaptureLength() => _length = Headers.ContentLength;
        protected override bool TryComputeLength(out long length) { length = _length ?? 0; return _length.HasValue; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(_stream);
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult<Stream>(_stream);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _stream.CopyToAsync(stream, state.Token);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => _stream.CopyToAsync(stream, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }
    private sealed class DownloadStream(RequestState state) : Stream
    {
        private long _sequence;
        private int _reading;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) return 0;
            if (Interlocked.Exchange(ref _reading, 1) != 0) throw new InvalidOperationException("Native HTTP supports one response reader.");
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, state.Token);
                byte[] bytes = await state.CallAsync(NetworkHttpWire.Read,
                    writer => { writer.Int64(state.Handle); writer.Int64(_sequence); writer.Int32(Math.Min(buffer.Length, NetworkHttpWire.Chunk)); }, linked.Token).ConfigureAwait(false);
                using var reader = new ServiceWireReader(bytes); byte[] chunk = reader.Buffer(); reader.End();
                if (chunk.Length > Math.Min(buffer.Length, NetworkHttpWire.Chunk)) throw new InvalidDataException("Native HTTP chunk exceeds read credit.");
                chunk.CopyTo(buffer); _sequence++; return chunk.Length;
            }
            finally { Volatile.Write(ref _reading, 0); }
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing) { if (disposing) state.Close(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync()
        {
            try { await state.DisposeAsync().ConfigureAwait(false); }
            finally { await base.DisposeAsync().ConfigureAwait(false); }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Native HTTP reads are asynchronous.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class UploadStream(RequestState state) : Stream
    {
        internal long Sequence { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, state.Token);
            while (!buffer.IsEmpty)
            {
                var chunk = buffer[..Math.Min(buffer.Length, NetworkHttpWire.Chunk)];
                await state.CallAsync(NetworkHttpWire.Upload,
                    writer => { writer.Int64(state.Handle); writer.Int64(Sequence); writer.Buffer(chunk); }, linked.Token).ConfigureAwait(false);
                Sequence++; buffer = buffer[chunk.Length..];
            }
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Native HTTP writes are asynchronous.");
    }
}
