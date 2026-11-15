using System.Net;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Http = Nexa.Sidecar.Host.Http.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarHttpAbi() => SidecarHttpAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarHttpAbiAsync()
    {
        const SidecarFeatures features = SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests;
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: features);
        using (session) using (peer)
        {
            var handler = new HttpProbe();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var pool = new SidecarHttpPool(new HttpClient(handler), () => now);
            var api = new SidecarHttpApi(_ => session, new NoopDispatchObserver(), pool);
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[4607].Authorize(caller with { PackageName = "foreign" }));
            AssertFalse(routes[4610].Authorize(caller with { SessionId = Guid.NewGuid() }));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                if (frame.MessageType == SidecarMessageType.Cancel) return;
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                AssertTrue(request.Value.Length <= SidecarWireCodecs.MaximumValueLength);
                var route = routes[request.ContractId];
                var result = await route.Invoke(caller, request.Value, token);
                AssertTrue(result.Value.Length <= SidecarWireCodecs.MaximumValueLength);
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version,
                    frame.MessageType == SidecarMessageType.QueryRequest ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(features);
            var client = new SidecarHttpClient(requests, new NoopDispatchObserver());
            async Task<XsrResult> Command<T>(XsrSemanticId semantic, T value) where T : notnull
            { AssertTrue(client.Commands.TryResolve(semantic, out var id)); return await client.Commands.Dispatch(id, value).Completion; }
            async Task<XsrResult<TOut>> Query<TIn, TOut>(XsrSemanticId semantic, TIn value) where TIn : notnull
            { AssertTrue(client.Queries.TryResolve(semantic, out var id)); return await client.Queries.QueryAsync<TIn, TOut>(id, value); }
            var begin = new Http.HostHttpBegin(1, "POST", "https://configured.invalid/data", [new("X-Probe", ["original"])], 130001);
            AssertFalse((await Command(Http.SidecarHttpRoutes.Begin, begin)).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            AssertTrue((await Command(Http.SidecarHttpRoutes.Begin, begin)).IsSuccess);
            begin.Headers[0].Values[0] = "mutated";
            byte[] body = Enumerable.Range(0, begin.BodyLength).Select(index => (byte)(index % 251)).ToArray();
            for (int offset = 0; offset < body.Length; offset += SidecarHttpPool.ChunkLimit)
                AssertTrue((await Command(Http.SidecarHttpRoutes.Append, new Http.HostHttpAppend(1, offset,
                    body.AsSpan(offset, Math.Min(SidecarHttpPool.ChunkLimit, body.Length - offset)).ToArray()))).IsSuccess);
            AssertTrue((await Query<Http.HostHttpRef, Http.HostHttpResponse>(Http.SidecarHttpRoutes.Send, new(1))).IsSuccess);
            AssertEqual("original", handler.Header); AssertTrue(handler.Body!.SequenceEqual(body)); AssertEqual(1, handler.Sends);
            long position = 0;
            while (true)
            {
                var chunk = await Query<Http.HostHttpRead, Http.HostHttpChunk>(Http.SidecarHttpRoutes.Read, new(1, position, SidecarHttpPool.ChunkLimit));
                AssertTrue(chunk.IsSuccess); AssertEqual(position, chunk.Value.Offset);
                if (chunk.Value.Bytes.Length == 0) break;
                AssertTrue(chunk.Value.Bytes.All(value => value == 73)); position += chunk.Value.Bytes.Length;
            }
            AssertEqual(250003L, position);
            AssertTrue((await Command(Http.SidecarHttpRoutes.Release, new Http.HostHttpRef(1))).IsSuccess);
            AssertTrue(handler.LastStream!.Disposed);
            AssertTrue((await Command(Http.SidecarHttpRoutes.Release, new Http.HostHttpRef(1))).IsSuccess);

            AssertTrue((await Command(Http.SidecarHttpRoutes.Begin, begin with { Id = 2, BodyLength = 0 })).IsSuccess);
            AssertTrue((await Query<Http.HostHttpRef, Http.HostHttpResponse>(Http.SidecarHttpRoutes.Send, new(2))).IsSuccess);
            AssertFalse((await Query<Http.HostHttpRef, Http.HostHttpResponse>(Http.SidecarHttpRoutes.Send, new(2))).IsSuccess);
            await pool.ReleaseAsync(session.SessionId, new(2), default);
            AssertEqual(2, handler.Sends); // A rejected duplicate never sends a second HTTP effect.

            Guid foreign = Guid.NewGuid();
            pool.Begin(session.SessionId, new(3, "GET", "https://configured.invalid/", [], -1), () => true);
            await HttpReject(() => pool.SendAsync(foreign, new(3), default).AsTask());
            now += TimeSpan.FromMinutes(3); pool.Sweep();
            await pool.ReleaseAsync(session.SessionId, new(3), default);
            await HttpReject(() => pool.SendAsync(session.SessionId, new(3), default).AsTask());
            AssertEqual(2, handler.Sends);
            foreach (var invalid in new[]
            {
                new HostHttpBegin(4, "CONNECT", "https://configured.invalid/", [], -1),
                new HostHttpBegin(4, "GET", "file:///private", [], -1),
                new HostHttpBegin(4, "GET", "https://configured.invalid/", [new("X-Test", ["bad\r\nheader"])], -1),
                new HostHttpBegin(4, "POST", "https://configured.invalid/", [new("Content-Length", ["7"])], 0)
            }) await HttpReject(() => { pool.Begin(session.SessionId, invalid, () => true); return Task.CompletedTask; });
            await HttpPoolRetirement();
            Console.WriteLine("PASS: headless HTTP binary discovery, configured transport, bounded upload/stream chunks, header snapshots, single-send semantics, session isolation, expiry and draining quotas.");
        }
    }
    private static async Task HttpPoolRetirement()
    {
        var handler = new HttpProbe { Block = true };
        await using var pool = new SidecarHttpPool(new HttpClient(handler)); Guid owner = Guid.NewGuid();
        for (long id = 1; id <= 4; id++) pool.Begin(owner, new(id, "POST", "https://configured.invalid/", [], SidecarHttpPool.BodyLimit), () => true);
        byte[] bytes = new byte[SidecarHttpPool.ChunkLimit];
        for (int offset = 0; offset < SidecarHttpPool.BodyLimit; offset += bytes.Length)
            await pool.AppendAsync(owner, new(1, offset, bytes.AsSpan(0, Math.Min(bytes.Length, SidecarHttpPool.BodyLimit - offset)).ToArray()), default);
        var send = pool.SendAsync(owner, new(1), default).AsTask();
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retirement = pool.ReleaseAsync(owner, new(1), default).AsTask();
        try
        {
            await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertFalse(retirement.IsCompleted);
            await HttpReject(() => { pool.Begin(owner, new(5, "POST", "https://configured.invalid/", [], 1), () => true); return Task.CompletedTask; });
        }
        finally { handler.Unblock.TrySetResult(); }
        await HttpReject(() => send); await retirement;
        pool.Begin(owner, new(5, "POST", "https://configured.invalid/", [], 1), () => true);
        await pool.ReleaseAsync(owner, new(5), default);
    }
    private static async Task HttpReject(Func<Task> work)
    {
        try { await work(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Invalid HTTP operation succeeded.");
    }
    private sealed class HttpProbe : HttpMessageHandler
    {
        internal int Sends;
        internal bool Block;
        internal byte[]? Body;
        internal string? Header;
        internal HttpBytes? LastStream;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously), Unblock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Sends++; Header = request.Headers.TryGetValues("X-Probe", out var values) ? values.Single() : null;
            Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(token);
            if (Block)
            {
                using var registration = token.Register(() => Cancelled.TrySetResult()); Entered.TrySetResult();
                await Unblock.Task; token.ThrowIfCancellationRequested();
            }
            LastStream = new(250003);
            return new(HttpStatusCode.OK) { Content = new StreamContent(LastStream) };
        }
    }
    private sealed class HttpBytes(long remaining) : Stream
    {
        internal bool Disposed;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { int read = (int)Math.Min(count, remaining); buffer.AsSpan(offset, read).Fill(73); remaining -= read; return read; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); int read = (int)Math.Min(buffer.Length, remaining); buffer.Span[..read].Fill(73); remaining -= read; return ValueTask.FromResult(read); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
