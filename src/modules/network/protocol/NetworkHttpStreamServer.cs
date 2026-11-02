using System.Net.Http.Headers;
using System.Threading.Channels;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
#if NEXA_MINECRAFT_HTTP_BRIDGE
using static Nexa.Native.Minecraft.Http.NetworkHttpWire;
#else
using static Nexa.Native.Network.Services.NetworkHttpWire;
#endif

#if NEXA_MINECRAFT_HTTP_BRIDGE
namespace Nexa.Native.Minecraft.Http;
#else
namespace Nexa.Native.Network.Services;
#endif

// Linked into the network owner only. Its peer exchanges handles and bounded chunks.
internal sealed class NetworkHttpStreamServer : IAsyncDisposable
{
    private readonly NativeUiAsyncResources<Session> _sessions = new(128);
    private readonly Func<HttpRequestMessage, ReadOnlyMemory<byte>, CancellationToken, Task<HttpResponseMessage>> _send;

    internal NetworkHttpStreamServer(Func<HttpRequestMessage, ReadOnlyMemory<byte>, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

    internal async Task<byte[]> ExecuteAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        if (operation == Create)
        {
            byte[] configuration = reader.Buffer();
            if (configuration.Length > 64 * 1024) throw new InvalidDataException("Network policy descriptor is too large.");
            string method = reader.String() ?? throw new InvalidDataException("Missing HTTP method.");
            Uri uri = reader.Uri() ?? throw new InvalidDataException("Missing HTTP address.");
            if (uri.Scheme is not ("http" or "https")) throw new InvalidDataException("Invalid HTTP address scheme.");
            int major = reader.Int32(), minor = reader.Int32(), policy = reader.Int32();
            if (major is < 1 or > 3 || minor is < 0 or > 1 || policy is < 0 or > 2) throw new InvalidDataException("Invalid HTTP version policy.");
            bool hasContent = reader.Boolean(); long length = reader.Int64();
            if (length < -1 || !hasContent && length != 0) throw new InvalidDataException("Invalid HTTP content length.");
            HttpRequestMessage request = new(new HttpMethod(method), uri)
            { Version = new(major, minor), VersionPolicy = (HttpVersionPolicy)policy };
            Session? session = null;
            try
            {
                session = new(request, configuration, hasContent, length, _send);
                ReadHeaders(reader, request.Headers);
                if (hasContent) ReadHeaders(reader, request.Content!.Headers);
                if (hasContent && length >= 0)
                {
                    if (request.Content!.Headers.ContentLength is { } declared && declared != length)
                        throw new InvalidDataException("HTTP content-length descriptors disagree.");
                    request.Content.Headers.ContentLength = length;
                }
                reader.End(); token.ThrowIfCancellationRequested();
                long id = _sessions.Register(session); writer.Int64(id);
                session.Start(); return writer.Finish();
            }
            catch
            {
                if (session is not null) await session.DisposeAsync().ConfigureAwait(false); else request.Dispose();
                throw;
            }
        }
        long handle = reader.Int64();
        if (operation == Claim)
        { reader.End(); writer.Boolean(_sessions.Claim(handle)); return writer.Finish(); }
        if (operation == Release)
        {
            reader.End();
            using (var active = _sessions.TryAcquire(handle)) active?.Resource.Cancel();
            await _sessions.ReleaseAsync(handle).ConfigureAwait(false); return [];
        }
        using var borrow = _sessions.Acquire(handle);
        switch (operation)
        {
            case Headers:
                reader.End(); await borrow.Resource.HeadersAsync(writer, token).ConfigureAwait(false); break;
            case Upload:
                long sequence = reader.Int64(); byte[] bytes = reader.Buffer(); reader.End();
                if (bytes.Length is < 1 or > Chunk) throw new InvalidDataException("Invalid HTTP upload chunk.");
                await borrow.Resource.UploadAsync(sequence, bytes, token).ConfigureAwait(false); break;
            case EndUpload:
                long last = reader.Int64(); reader.End(); await borrow.Resource.EndUploadAsync(last, token).ConfigureAwait(false); break;
            case Read:
                long index = reader.Int64(); int count = reader.Int32(); reader.End();
                if (count is < 1 or > Chunk) throw new InvalidDataException("Invalid HTTP read size.");
                writer.Buffer(await borrow.Resource.ReadAsync(index, count, token).ConfigureAwait(false)); break;
            default: throw new InvalidDataException("Unknown network stream operation.");
        }
        return writer.Finish();
    }

    // A rejected provisional creation has no borrower; cancellation still precedes cleanup.
    internal Task RejectAsync(long handle) => _sessions.RejectProvisionalAsync(handle);
    public ValueTask DisposeAsync() => _sessions.DisposeAsync();

    private sealed class Session : IAsyncDisposable
    {
        private readonly HttpRequestMessage _request;
        private readonly ReadOnlyMemory<byte> _configuration;
        private readonly Func<HttpRequestMessage, ReadOnlyMemory<byte>, CancellationToken, Task<HttpResponseMessage>> _send;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly UploadPipe? _upload;
        private readonly SemaphoreSlim _writeGate = new(1, 1), _readGate = new(1, 1);
        private Task<HttpResponseMessage>? _response;
        private Stream? _body;
        private long _uploadSequence, _readSequence;
        private bool _uploadEnded;

        internal Session(HttpRequestMessage request, ReadOnlyMemory<byte> configuration, bool hasContent, long length,
            Func<HttpRequestMessage, ReadOnlyMemory<byte>, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _request = request; _configuration = configuration; _send = send;
            if (hasContent)
            {
                _upload = new(); request.Content = new StreamContent(_upload);
            }
        }
        internal void Start() => _response = Task.Run(() => _send(_request, _configuration, _lifetime.Token));
        internal void Cancel() { _lifetime.Cancel(); _upload?.Cancel(); }
        private Task<HttpResponseMessage> Response => _response ?? throw new InvalidOperationException("HTTP request has not started.");
        internal async Task HeadersAsync(ServiceWireWriter writer, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var response = await Response.WaitAsync(linked.Token).ConfigureAwait(false);
            writer.Int32((int)response.StatusCode); writer.String(response.ReasonPhrase);
            writer.Int32(response.Version.Major); writer.Int32(response.Version.Minor);
            writer.Uri(response.RequestMessage?.RequestUri ?? _request.RequestUri);
            writer.String(response.RequestMessage?.Method.Method ?? _request.Method.Method);
            WriteHeaders(writer, response.Headers); WriteHeaders(writer, response.Content.Headers);
        }
        internal async Task UploadAsync(long sequence, byte[] bytes, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (_upload is null || _uploadEnded || sequence != _uploadSequence) throw new InvalidDataException("HTTP upload sequence mismatch.");
                await _upload.WriteAsync(bytes, linked.Token).ConfigureAwait(false); _uploadSequence++;
            }
            finally { _writeGate.Release(); }
        }
        internal async Task EndUploadAsync(long sequence, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (_upload is null || sequence != _uploadSequence) throw new InvalidDataException("HTTP upload completion mismatch.");
                _uploadEnded = true; _upload.Complete();
            }
            finally { _writeGate.Release(); }
        }
        internal async Task<byte[]> ReadAsync(long sequence, int count, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _readGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (sequence != _readSequence) throw new InvalidDataException("HTTP response sequence mismatch.");
                var response = await Response.WaitAsync(linked.Token).ConfigureAwait(false);
                _body ??= await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                byte[] bytes = new byte[count]; int length = await _body.ReadAsync(bytes, linked.Token).ConfigureAwait(false);
                _readSequence++; return bytes.AsSpan(0, length).ToArray();
            }
            finally { _readGate.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            Cancel();
            HttpResponseMessage? response = null;
            if (_response is not null)
            {
                try { response = await _response.ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            }
            try { response?.Dispose(); }
            finally
            {
                if (_body is not null) await _body.DisposeAsync().ConfigureAwait(false);
                _request.Dispose(); _lifetime.Dispose(); _writeGate.Dispose(); _readGate.Dispose();
            }
        }
    }

    private sealed class UploadPipe : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        private ReadOnlyMemory<byte> _current;
        internal ValueTask WriteAsync(byte[] bytes, CancellationToken token) => _chunks.Writer.WriteAsync(bytes, token);
        internal void Complete() => _chunks.Writer.TryComplete();
        internal void Cancel() => _chunks.Writer.TryComplete(new OperationCanceledException());
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return 0;
            while (_current.IsEmpty)
            {
                if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return 0;
                if (_chunks.Reader.TryRead(out var bytes)) _current = bytes;
            }
            int count = Math.Min(buffer.Length, _current.Length);
            _current[..count].CopyTo(buffer); _current = _current[count..]; return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("HTTP upload reads are asynchronous.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
