using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static async ValueTask SidecarHostRequestsEnforceBoundaries()
    {
        int invoked = 0;
        var router = new SidecarHostRequestRouter([
            new(101, true, SidecarWireCodecs.I32, SidecarWireCodecs.Bool, TimeSpan.FromSeconds(2),
                caller => caller.PackageName == "DataApiFixture" && caller.SessionId != Guid.Empty,
                (_, value, _) => { invoked++; return ValueTask.FromResult(XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.Bool, [1]))); }),
            new(102, false, SidecarWireCodecs.I32, SidecarWireCodecs.Bool, TimeSpan.FromSeconds(2),
                _ => false, (_, _, _) => throw new InvalidOperationException("Denied handler executed."))
        ]);
        await using var fixture = await DataApiSession([], hostRequests: router);
        var correlation = SidecarCorrelationId.Create();
        await DataApiFrame(fixture.Peer, SidecarMessageType.QueryRequest, SidecarDataMessages.EncodeBinaryRequest(101, BinaryInt32(9)), correlation);
        var reply = await fixture.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(SidecarMessageType.QueryResult, reply.MessageType); AssertEqual(correlation, reply.CorrelationId);
        AssertTrue(SidecarDataMessages.DecodeBinaryResult(reply.Payload.Span).Success); AssertEqual(1, invoked);
        foreach (var (id, query, codec, error) in new[] {
            (101u, false, SidecarWireCodecs.I32, "xsr.contract_mismatch"),
            (101u, true, SidecarWireCodecs.Bool, "xsr.contract_mismatch"),
            (102u, false, SidecarWireCodecs.I32, "xsr.permission_denied"),
            (999u, true, SidecarWireCodecs.I32, "xsr.route_not_found") })
        {
            await DataApiFrame(fixture.Peer, query ? SidecarMessageType.QueryRequest : SidecarMessageType.CommandRequest,
                SidecarDataMessages.EncodeBinaryRequest(id, codec == SidecarWireCodecs.I32 ? BinaryInt32(1) : new(codec, [1])));
            reply = await fixture.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(error, SidecarDataMessages.DecodeBinaryResult(reply.Payload.Span).ErrorCode);
        }
        AssertEqual(1, invoked);
        await fixture.Session.DeactivateAsync(); await fixture.Peer.ReceiveAsync();
        await DataApiFrame(fixture.Peer, SidecarMessageType.QueryRequest, SidecarDataMessages.EncodeBinaryRequest(101, BinaryInt32(9)));
        reply = await fixture.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual("xsr.unavailable", SidecarDataMessages.DecodeBinaryResult(reply.Payload.Span).ErrorCode); AssertEqual(1, invoked);
    }

    private static async ValueTask SidecarHostRequestsBoundCancelledWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<XsrResult<SidecarBinaryValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new SidecarHostRequestRouter([
            new(1, false, SidecarWireCodecs.I32, SidecarWireCodecs.Bool, TimeSpan.FromSeconds(5), _ => true,
                (_, _, _) => { entered.TrySetResult(); return new(release.Task); })
        ], maximumConcurrentCalls: 1);
        await using var fixture = await DataApiSession([], hostRequests: router, maxPending: 1);
        await using var other = await DataApiSession([], hostRequests: router, maxPending: 1);
        try
        {
            var correlation = SidecarCorrelationId.Create();
            await DataApiFrame(fixture.Peer, SidecarMessageType.CommandRequest, SidecarDataMessages.EncodeBinaryRequest(1, BinaryInt32(1)), correlation);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Health still works while the reverse handler is awaiting application work.
            await DataApiFrame(fixture.Peer, SidecarMessageType.HealthPing, SidecarControlMessages.EncodeHealth(42));
            AssertEqual(SidecarMessageType.HealthPong, (await fixture.Peer.ReceiveAsync()).MessageType);
            await DataApiFrame(fixture.Peer, SidecarMessageType.Cancel, SidecarStateSnapshot.EncodeCancel(correlation.Value, "cancel"), correlation);
            var reply = await fixture.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual("xsr.operation_outcome_unknown", SidecarDataMessages.DecodeBinaryResult(reply.Payload.Span).ErrorCode);
            AssertEqual(1, fixture.Session.PendingHostRequestCount);
            await DataApiFrame(fixture.Peer, SidecarMessageType.CommandRequest, SidecarDataMessages.EncodeBinaryRequest(1, BinaryInt32(1)));
            reply = await fixture.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual("xsr.backpressure", SidecarDataMessages.DecodeBinaryResult(reply.Payload.Span).ErrorCode);
            await DataApiFrame(other.Peer, SidecarMessageType.CommandRequest, SidecarDataMessages.EncodeBinaryRequest(1, BinaryInt32(1)));
            reply = await other.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual("xsr.backpressure", SidecarDataMessages.DecodeBinaryResult(reply.Payload.Span).ErrorCode);
        }
        finally { release.TrySetResult(XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.Bool, [1]))); }
        await WaitUntil(() => fixture.Session.PendingHostRequestCount == 0);
        await DataApiFrame(other.Peer, SidecarMessageType.CommandRequest, SidecarDataMessages.EncodeBinaryRequest(1, BinaryInt32(1)));
        var recovered = await other.Peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(SidecarDataMessages.DecodeBinaryResult(recovered.Payload.Span).Success);
    }

    private static async ValueTask SidecarHostRequestsRequireNegotiation()
    {
        await using var fixture = await DataApiSession([], SidecarFeatures.BinaryPayloads);
        await DataApiFrame(fixture.Peer, SidecarMessageType.QueryRequest, SidecarDataMessages.EncodeBinaryRequest(1, BinaryInt32(1)));
        await fixture.ReceiveLoop.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(SidecarSessionState.Failed, fixture.Session.State);
    }
}
