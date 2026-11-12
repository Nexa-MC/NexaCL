using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;

namespace Nexa.Sidecar.Tests;

internal static partial class Program
{
    private static async ValueTask LargeGeneratedDtosRoundTrip()
    {
        foreach (int size in new[] { 0, 65535, 65536, 131070, SidecarWireCodecs.MaximumGeneratedDtoLength })
        {
            byte[] bytes = new byte[size];
            for (int i = 0; i < size; i++) bytes[i] = (byte)(i % 251);
            var value = new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, bytes);
            var encoded = SidecarDataMessages.EncodeBinaryRequest(300, value);
            var request = SidecarDataMessages.DecodeBinaryRequest(encoded);
            AssertEqual(300u, request.ContractId); AssertTrue(request.Value.Span.SequenceEqual(bytes));
            var result = SidecarDataMessages.DecodeBinaryResult(SidecarDataMessages.EncodeBinaryResult(true, value, null));
            AssertTrue(result.Success); AssertTrue(result.Value.Span.SequenceEqual(bytes));
            if (size > 65535)
                AssertThrows<SidecarProtocolException>(() => SidecarDataMessages.DecodeBinaryRequest(encoded.AsSpan(0, encoded.Length - 1)));
        }
        AssertThrows<SidecarProtocolException>(() => _ = new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, new byte[SidecarWireCodecs.MaximumGeneratedDtoLength + 1]));
        AssertThrows<SidecarProtocolException>(() => _ = new SidecarBinaryValue(SidecarWireCodecs.Bytes, new byte[65536]));
        using var missing = new SidecarPayloadWriter();
        missing.WriteUInt32(1, 300); missing.WriteUInt32(3, SidecarWireCodecs.GeneratedDto);
        missing.WriteBytes(4, new byte[65535]); missing.WriteUInt32(6, 65536);
        AssertThrows<SidecarProtocolException>(() => SidecarDataMessages.DecodeBinaryRequest(missing.ToArray()));
        using var gap = new SidecarPayloadWriter();
        gap.WriteUInt32(1, 300); gap.WriteUInt32(3, SidecarWireCodecs.GeneratedDto);
        gap.WriteBytes(4, new byte[65535]); gap.WriteUInt32(6, 65536); gap.WriteBytes(8, [1]);
        AssertThrows<SidecarProtocolException>(() => SidecarDataMessages.DecodeBinaryRequest(gap.ToArray()));

        var large = new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, new byte[70000]);
        using var legacy = new SidecarHostRequestClient((_, _) => throw new InvalidOperationException("Must reject before sending."));
        legacy.Activate(SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests);
        bool rejected = false;
        try { await legacy.InvokeAsync(300, true, large, large.CodecId, TimeSpan.FromSeconds(5)); }
        catch (SidecarProtocolException) { rejected = true; }
        AssertTrue(rejected);

        SidecarHostRequestClient? peer = null;
        using var client = peer = new((frame, _) =>
        {
            var decoded = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
            AssertTrue(peer!.TryAcceptReply(new(SidecarProtocol.Version, SidecarMessageType.QueryResult,
                SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, decoded.Value, null))));
            return ValueTask.CompletedTask;
        });
        client.Activate(SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests | SidecarFeatures.LargeGeneratedDtos);
        var reply = await client.InvokeAsync(300, true, large, large.CodecId, TimeSpan.FromSeconds(5));
        AssertTrue(reply.Success); AssertEqual(large.Length, reply.Value.Length);
    }
}
