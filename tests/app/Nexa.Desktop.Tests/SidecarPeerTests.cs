using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Peer = Nexa.Sidecar.Host.Peer.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarPeerAbi() => SidecarPeerAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarPeerAbiAsync()
    {
        const SidecarFeatures features = SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests;
        const string service = "sidecar.service.test.exchange";
        var (source, sourceWire) = await ActivateCaptionPatch(new(), "", declarations: [], features: features, packageName: "Source");
        var (target, targetWire) = await ActivateCaptionPatch(new(), "", declarations:
        [new(SidecarRegistrationKind.Query, service, 0, SidecarWireCodecs.GeneratedDto) { ResultCodecId = SidecarWireCodecs.GeneratedDto },
         new(SidecarRegistrationKind.Query, "ui.private.query", 0, SidecarWireCodecs.GeneratedDto) { ResultCodecId = SidecarWireCodecs.GeneratedDto },
         new(SidecarRegistrationKind.Query, "sidecar.service.test.scalar", 0, SidecarWireCodecs.Utf8String) { ResultCodecId = SidecarWireCodecs.GeneratedDto }],
            features: features, packageName: "Target");
        using (source) using (sourceWire) using (target) using (targetWire)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var pool = new SidecarPeerPool(() => now);
            SidecarHostSession? currentTarget = target;
            var api = new SidecarPeerApi(_ => source, new NoopDispatchObserver(), pool, name => name == "Target" ? currentTarget : null);
            var caller = new SidecarHostCaller(source.PluginName, source.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[4863].Authorize(caller with { PackageName = "forged" }));
            AssertFalse(routes[4865].Authorize(caller with { SessionId = Guid.NewGuid() }));
            AssertFalse((await routes[4865].Invoke(caller,
                new(SidecarWireCodecs.GeneratedDto, new byte[SidecarWireCodecs.MaximumValueLength + 1]), default)).IsSuccess);
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                if (frame.MessageType == SidecarMessageType.Cancel) return;
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(features);
            var client = new SidecarPeerClient(requests, new NoopDispatchObserver());
            async Task<XsrResult<TOut>> Query<TIn, TOut>(XsrSemanticId semantic, TIn value) where TIn : notnull
            { AssertTrue(client.Queries.TryResolve(semantic, out var id)); return await client.Queries.QueryAsync<TIn, TOut>(id, value); }
            async Task<XsrResult> Release(long id)
            { AssertTrue(client.Commands.TryResolve(Peer.SidecarPeerRoutes.Release, out var route)); return await client.Commands.Dispatch(route, new Peer.HostPeerRef(id)).Completion; }
            Task<XsrResult<Peer.HostPeerBinding>> Bind(long id, string name = service) => Query<Peer.HostPeerBind, Peer.HostPeerBinding>(Peer.SidecarPeerRoutes.Bind, new(id, "Target", name));
            AssertFalse((await Bind(1)).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            foreach (string invalid in new[] { "ui.private.query", "sidecar.service.test.scalar", "sidecar.service.missing" })
                AssertFalse((await Bind(1, invalid)).IsSuccess);
            var binding = await Bind(1); AssertTrue(binding.IsSuccess); AssertEqual(target.SessionId.ToString("N"), binding.Value.TargetSession);
            AssertFalse((await Bind(1)).IsSuccess);
            await PeerReject(() => { pool.Bind(source, source, new(2, source.PluginName, service), () => true); return Task.CompletedTask; });
            await PeerReject(() => pool.InvokeAsync(Guid.NewGuid(), new(1, []), default).AsTask());
            await PeerReject(() => pool.InvokeAsync(source.SessionId, new(1, new byte[SidecarPeerPool.PayloadLimit + 1]), default).AsTask());
            var receive = target.RunReceiveLoopAsync().AsTask();
            async Task<SidecarFrame> Read(SidecarMessageType kind)
            { var frame = await targetWire.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); AssertEqual(kind, frame.MessageType); return frame; }
            ValueTask Reply(SidecarFrame frame, byte[] payload) => targetWire.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.QueryResult,
                SidecarFrameTraits.Final, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(SidecarWireCodecs.GeneratedDto, payload), null)));
            var call = Query<Peer.HostPeerCall, Peer.HostPeerReply>(Peer.SidecarPeerRoutes.Invoke, new(1, [3, 5, 8]));
            var sent = await Read(SidecarMessageType.QueryRequest);
            var decoded = SidecarDataMessages.DecodeBinaryRequest(sent.Payload.Span);
            var envelope = SidecarPeerService.DecodeRequest(decoded.Value);
            AssertEqual(source.PluginName, envelope.CallerPackage); AssertEqual(source.SessionId.ToString("N"), envelope.CallerSession);
            AssertTrue(envelope.Payload.SequenceEqual(new byte[] { 3, 5, 8 }));
            await PeerReject(() => pool.InvokeAsync(source.SessionId, new(1, []), default).AsTask());
            await Reply(sent, [13, 21]);
            var response = await call; AssertTrue(response.IsSuccess); AssertTrue(response.Value.Payload.SequenceEqual(new byte[] { 13, 21 }));

            byte[] original = [34];
            var snapshot = pool.InvokeAsync(source.SessionId, new(1, original), default).AsTask(); original[0] = 99;
            sent = await Read(SidecarMessageType.QueryRequest);
            AssertEqual((byte)34, SidecarPeerService.DecodeRequest(SidecarDataMessages.DecodeBinaryRequest(sent.Payload.Span).Value).Payload[0]);
            await Reply(sent, new byte[SidecarPeerPool.PayloadLimit]); AssertEqual(SidecarPeerPool.PayloadLimit, (await snapshot).Payload.Length);

            // Release cancels the pending request and retires its binding without replaying it.
            var pending = pool.InvokeAsync(source.SessionId, new(1, []), default).AsTask();
            sent = await Read(SidecarMessageType.QueryRequest);
            await pool.ReleaseAsync(source.SessionId, new(1), default);
            await PeerReject(() => pending); _ = await Read(SidecarMessageType.Cancel);
            await PeerReject(() => pool.InvokeAsync(source.SessionId, new(1, []), default).AsTask());
            await Reply(sent, [1]); // A late response cannot revive the binding.
            AssertTrue((await Release(1)).IsSuccess);

            for (long id = 1; id <= 32; id++) AssertTrue((await Bind(id)).IsSuccess);
            AssertFalse((await Bind(33)).IsSuccess);
            now += TimeSpan.FromMinutes(3);
            await PeerReject(() => pool.InvokeAsync(source.SessionId, new(1, []), default).AsTask());
            // The independent timer must release expired quota, even with no new invocation.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!(await Bind(33)).IsSuccess)
            { if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("Peer idle quota was not reclaimed."); await Task.Delay(25); }
            AssertTrue((await Release(33)).IsSuccess);
            AssertTrue((await Bind(34)).IsSuccess);
            var oversized = pool.InvokeAsync(source.SessionId, new(34, []), default).AsTask();
            sent = await Read(SidecarMessageType.QueryRequest); await Reply(sent, new byte[SidecarPeerPool.PayloadLimit + 1]);
            await PeerReject(() => oversized);
            await pool.ReleaseAsync(source.SessionId, new(34), default);

            AssertTrue((await Bind(35)).IsSuccess);
            currentTarget = null; // The supervisor no longer exposes this exact activation.
            await PeerReject(() => pool.InvokeAsync(source.SessionId, new(35, []), default).AsTask());
            AssertFalse((await Bind(36)).IsSuccess);
            currentTarget = target;
            AssertTrue((await Bind(36)).IsSuccess);
            pending = pool.InvokeAsync(source.SessionId, new(36, []), default).AsTask();
            _ = await Read(SidecarMessageType.QueryRequest);
            target.Dispose(); await PeerReject(() => pending); await receive.WaitAsync(TimeSpan.FromSeconds(5));
            AssertFalse((await Bind(37)).IsSuccess);
            source.Dispose(); AssertFalse(routes[4863].Authorize(caller));
            Console.WriteLine("PASS: authenticated peer discovery, explicit service admission, binary envelopes, payload bounds, isolation, snapshots, quotas, timer expiry, cancellation, late replies and target retirement.");
        }
    }
    private static async Task PeerReject(Func<Task> work)
    {
        try { await work().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Invalid peer operation succeeded.");
    }
}
