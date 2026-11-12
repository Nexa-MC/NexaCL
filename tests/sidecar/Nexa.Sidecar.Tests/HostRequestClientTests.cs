using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;

namespace Nexa.Sidecar.Tests;

internal static partial class Program
{
    private static async ValueTask HostRequestClientLifecycle()
    {
        var sent = new List<SidecarFrame>();
        using var client = new SidecarHostRequestClient((frame, _) => { sent.Add(frame); return ValueTask.CompletedTask; });
        var input = new SidecarBinaryValue(SidecarWireCodecs.Bytes, [1, 2]);
        async Task Fails<T>(ValueTask<SidecarHostReply> task) where T : Exception
        {
            try { await task; }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
        await Fails<InvalidOperationException>(client.InvokeAsync(1, true, input, input.CodecId, TimeSpan.FromSeconds(10)));
        AssertThrows<InvalidOperationException>(() => client.Activate(SidecarFeatures.BinaryPayloads));
        client.Activate(SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests);
        var first = client.InvokeAsync(1, true, input, input.CodecId, TimeSpan.FromSeconds(10));
        var second = client.InvokeAsync(2, false, input, input.CodecId, TimeSpan.FromSeconds(10));
        SidecarFrame Reply(SidecarFrame request, bool success = true) => new(SidecarProtocol.Version,
            request.MessageType == SidecarMessageType.QueryRequest ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
            SidecarFrameTraits.None, request.CorrelationId, SidecarDataMessages.EncodeBinaryResult(success, input, success ? null : "xsr.permission_denied"));
        AssertTrue(client.TryAcceptReply(Reply(sent[1], false)));
        AssertEqual("xsr.permission_denied", (await second).ErrorCode);
        AssertTrue(client.TryAcceptReply(Reply(sent[0])));
        AssertTrue((await first).Success);
        AssertFalse(client.TryAcceptReply(Reply(sent[0])));

        using var cancellation = new CancellationTokenSource();
        var cancelled = client.InvokeAsync(3, false, input, input.CodecId, TimeSpan.FromSeconds(10), cancellation.Token);
        var cancelledFrame = sent[^1];
        cancellation.Cancel();
        await Fails<OperationCanceledException>(cancelled);
        AssertEqual(SidecarMessageType.Cancel, sent[^1].MessageType);
        AssertEqual(cancelledFrame.CorrelationId, sent[^1].CorrelationId);
        AssertTrue(client.TryAcceptReply(Reply(cancelledFrame)));

        var outstanding = new List<Task<SidecarHostReply>>();
        for (int i = 0; i < SidecarHostRequestClient.MaximumPendingRequests; i++)
            outstanding.Add(client.InvokeAsync(4, true, input, input.CodecId, TimeSpan.FromSeconds(10)).AsTask());
        await Fails<SidecarTransportBackpressureException>(client.InvokeAsync(4, true, input, input.CodecId, TimeSpan.FromSeconds(10)));
        var invalid = new SidecarFrame(SidecarProtocol.Version, SidecarMessageType.CommandResult,
            SidecarFrameTraits.None, sent[^1].CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, input, null));
        AssertThrows<SidecarProtocolException>(() => client.TryAcceptReply(invalid));
        client.Dispose();
        foreach (var task in outstanding) await Fails<OperationCanceledException>(new(task));
        await Fails<ObjectDisposedException>(client.InvokeAsync(1, true, input, input.CodecId, TimeSpan.FromSeconds(10)));

        var stalledSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Both request and cancellation sends may be blocked by a stalled writer.
        using var stalled = new SidecarHostRequestClient((_, _) => new(stalledSend.Task));
        stalled.Activate(SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests);
        using var stop = new CancellationTokenSource();
        var blocked = stalled.InvokeAsync(1, true, input, input.CodecId, TimeSpan.FromSeconds(10), stop.Token).AsTask();
        stop.Cancel();
        await Fails<OperationCanceledException>(new(blocked.WaitAsync(TimeSpan.FromSeconds(5))));
        stalledSend.TrySetException(new IOException("Transport closed after the caller stopped waiting."));
    }
}
