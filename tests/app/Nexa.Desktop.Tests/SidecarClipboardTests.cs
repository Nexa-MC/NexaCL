using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Clip = Nexa.Sidecar.Host.Clipboard.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarClipboardAbi() => SidecarClipboardAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarClipboardAbiAsync()
    {
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        var (other, otherPeer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        using var clock = new FeedbackClock();
        using (session) using (peer) using (other) using (otherPeer)
        using (var api = new SidecarClipboardApi(c => c.SessionId == session.SessionId ? session : other, new NoopDispatchObserver(), clock))
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[3072].Authorize(caller with { PackageName = "foreign" }));
            AssertFalse(routes[3072].Authorize(caller with { SessionId = Guid.NewGuid() }));
            Dictionary<Guid, CancellationTokenSource> calls = [];
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                if (frame.MessageType == SidecarMessageType.Cancel)
                {
                    CancellationTokenSource? pending;
                    lock (calls) calls.TryGetValue(frame.CorrelationId.Value, out pending);
                    pending?.Cancel(); return;
                }
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (calls) calls.Add(frame.CorrelationId.Value, lifetime);
                try
                {
                    var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                    var route = routes[request.ContractId];
                    var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, lifetime.Token)
                        : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                    wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                        SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                            result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value)));
                }
                finally { lock (calls) calls.Remove(frame.CorrelationId.Value); }
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarClipboardClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Queries.TryResolve(Clip.SidecarClipboardRoutes.Read, out var read));
            AssertTrue(client.Queries.TryResolve(Clip.SidecarClipboardRoutes.ReadChunk, out var chunk));
            AssertTrue(client.Commands.TryResolve(Clip.SidecarClipboardRoutes.BeginWrite, out var begin));
            AssertTrue(client.Commands.TryResolve(Clip.SidecarClipboardRoutes.Append, out var append));
            AssertTrue(client.Commands.TryResolve(Clip.SidecarClipboardRoutes.Commit, out var commit));
            AssertTrue(client.Commands.TryResolve(Clip.SidecarClipboardRoutes.Release, out var releaseRoute));
            Task<XsrResult<Clip.HostClipboardSnapshot>> Read(long id, CancellationToken token = default) =>
                client.Queries.QueryAsync<Clip.HostClipboardTransfer, Clip.HostClipboardSnapshot>(read, new(id), cancellationToken: token).AsTask();
            Task<XsrResult> Begin(long id, byte[]? text = null, int length = -1, byte[]? hash = null) =>
                client.Commands.Dispatch(begin, new Clip.HostClipboardWrite(id, text, length, hash)).Completion;
            Task<XsrResult> Commit(long id, CancellationToken token = default) => client.Commands.Dispatch(commit, new Clip.HostClipboardTransfer(id), cancellationToken: token).Completion;
            Task<XsrResult> Release(long id) => client.Commands.Dispatch(releaseRoute, new Clip.HostClipboardTransfer(id)).Completion;
            Task<XsrResult<Clip.HostClipboardChunk>> Chunk(long id, int offset, int length) =>
                client.Queries.QueryAsync<Clip.HostClipboardReadChunk, Clip.HostClipboardChunk>(chunk, new(id, offset, length)).AsTask();
            Task<XsrResult> Append(long id, int offset, byte[] bytes) => client.Commands.Dispatch(append, new Clip.HostClipboardAppend(id, offset, bytes)).Completion;
            AssertFalse((await Read(1)).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            AssertEqual("xsr.unavailable", (await Read(1)).Error!.Code.Value);
            string? text = new('中', 65536), writtenText = "initial";
            byte[]? image = ClipboardPng(16 * 1024 * 1024), writtenImage = null;
            int writes = 0, reads = 0;
            TaskCompletionSource? entered = null, unblock = null;
            bool fail = false;
            CancellationToken nativeToken = default;
            HostClipboardActions actions = new(async token =>
            {
                reads++; nativeToken = token; entered?.TrySetResult();
                if (unblock is not null) await unblock.Task;
                if (fail) throw new IOException("late native read failure");
                return (text, image);
            }, async (value, bytes, token) =>
            {
                writes++; writtenText = value; writtenImage = bytes?.ToArray(); nativeToken = token; entered?.TrySetResult();
                if (unblock is not null) await unblock.Task;
                if (fail) throw new IOException("late native write failure");
            });
            var binding = api.Bind(actions);
            try
            {
                var snapshot = await Read(2); AssertTrue(snapshot.IsSuccess);
                AssertEqual(text, Encoding.UTF8.GetString(snapshot.Value.TextUtf8!));
                AssertEqual(image.Length, snapshot.Value.ImageLength);
                byte[] gathered = new byte[image.Length];
                for (int offset = 0; offset < gathered.Length; offset += 256 * 1024)
                {
                    var part = await Chunk(2, offset, 256 * 1024); AssertTrue(part.IsSuccess);
                    AssertEqual(offset, part.Value.Offset); part.Value.Bytes.CopyTo(gathered, offset);
                }
                AssertTrue(SHA256.HashData(gathered).SequenceEqual(snapshot.Value.ImageSha256!));
                image[32] ^= 1; // The snapshot owns its bytes, not the native provider's buffer.
                AssertEqual(gathered[32], (await Chunk(2, 32, 1)).Value.Bytes[0]); image[32] ^= 1;
                AssertFalse((await Chunk(2, -1, 1)).IsSuccess); AssertFalse((await Chunk(2, 0, 256 * 1024 + 1)).IsSuccess);
                AssertFalse((await Chunk(2, image.Length, 1)).IsSuccess);
                caller = new(other.PluginName, other.SessionId);
                AssertFalse((await Chunk(2, 0, 1)).IsSuccess); AssertEqual("success", (await Release(2)).Error?.Code.Value ?? "success");
                caller = new(session.PluginName, session.SessionId);
                AssertTrue((await Chunk(2, 0, 1)).IsSuccess); AssertTrue((await Release(2)).IsSuccess);
                AssertFalse((await Chunk(2, 0, 1)).IsSuccess); AssertFalse((await Read(2)).IsSuccess);
                AssertTrue((await Begin(3, Encoding.UTF8.GetBytes(text), image.Length, SHA256.HashData(image))).IsSuccess);
                AssertFalse((await Commit(3)).IsSuccess); AssertEqual(0, writes);
                AssertFalse((await Append(3, 1, [1])).IsSuccess);
                for (int offset = 0; offset < image.Length; offset += 256 * 1024)
                    AssertTrue((await Append(3, offset, image.AsSpan(offset, 256 * 1024).ToArray())).IsSuccess);
                AssertFalse((await Append(3, 0, [1])).IsSuccess);
                AssertTrue((await Commit(3)).IsSuccess); AssertEqual(1, writes);
                AssertEqual(text, writtenText); AssertTrue(image.SequenceEqual(writtenImage!));
                AssertFalse((await Commit(3)).IsSuccess); AssertEqual(1, writes);
                AssertTrue((await Begin(4, [])).IsSuccess); AssertTrue((await Commit(4)).IsSuccess); AssertEqual("", writtenText); AssertTrue(writtenImage is null);
                AssertTrue((await Begin(5)).IsSuccess); AssertTrue((await Commit(5)).IsSuccess); AssertTrue(writtenText is null);
                foreach (var bad in new[] { new byte[] { 0xff }, Encoding.UTF8.GetBytes(new string('x', 65537)) }) AssertFalse((await Begin(6, bad)).IsSuccess);
                AssertFalse((await Begin(6, length: 0, hash: new byte[32])).IsSuccess);
                AssertFalse((await Begin(6, length: -1, hash: new byte[32])).IsSuccess);
                AssertFalse((await Begin(6, length: 16 * 1024 * 1024 + 1, hash: new byte[32])).IsSuccess);
                AssertFalse((await Begin(6, length: 33, hash: new byte[31])).IsSuccess);
                AssertTrue((await Begin(6, length: 33, hash: new byte[32])).IsSuccess);
                AssertTrue((await Append(6, 0, ClipboardPng(33))).IsSuccess);
                AssertFalse((await Commit(6)).IsSuccess); AssertEqual(3, writes);
                var invalidPng = ClipboardPng(33); BinaryPrimitives.WriteUInt32BigEndian(invalidPng.AsSpan(16), 4097);
                AssertTrue((await Begin(7, length: 33, hash: SHA256.HashData(invalidPng))).IsSuccess);
                AssertTrue((await Append(7, 0, invalidPng)).IsSuccess); AssertFalse((await Commit(7)).IsSuccess); AssertEqual(3, writes);
                // Unknown release fences a delayed begin, including across UI rebinding.
                AssertTrue((await Release(10)).IsSuccess); binding.Dispose(); binding = api.Bind(actions);
                AssertFalse((await Begin(10)).IsSuccess);
                AssertTrue((await Begin(11)).IsSuccess); AssertTrue((await Begin(12)).IsSuccess);
                AssertEqual("xsr.backpressure", (await Begin(13)).Error!.Code.Value);
                AssertTrue((await Release(11)).IsSuccess); AssertTrue((await Commit(12)).IsSuccess);
                AssertFalse((await Begin(13)).IsSuccess); // Failed admission still consumes its ID.
                AssertTrue((await Begin(14)).IsSuccess);
                clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
                AssertFalse((await Commit(14)).IsSuccess);
                AssertTrue((await Begin(15)).IsSuccess); AssertTrue((await Release(15)).IsSuccess);
                text = null; image = null;
                var empty = await Read(16); AssertTrue(empty.IsSuccess); AssertEqual(-1, empty.Value.ImageLength);
                AssertTrue(empty.Value.TextUtf8 is null && empty.Value.ImageSha256 is null); AssertTrue((await Release(16)).IsSuccess);
                // Four staged images fill the shared storage budget across two owners.
                AssertTrue((await Begin(17, length: 16 * 1024 * 1024, hash: new byte[32])).IsSuccess);
                AssertTrue((await Begin(18, length: 16 * 1024 * 1024, hash: new byte[32])).IsSuccess);
                caller = new(other.PluginName, other.SessionId);
                AssertTrue((await Begin(3, length: 16 * 1024 * 1024, hash: new byte[32])).IsSuccess);
                AssertTrue((await Begin(4, length: 16 * 1024 * 1024, hash: new byte[32])).IsSuccess);
                caller = new(session.PluginName, session.SessionId);
                AssertTrue((await Release(18)).IsSuccess);
                // One per-owner slot is free, but the read reservation exceeds remaining global bytes.
                AssertEqual("xsr.backpressure", (await Read(19)).Error!.Code.Value);
                AssertTrue((await Release(17)).IsSuccess); AssertTrue((await Read(20)).IsSuccess); AssertTrue((await Release(20)).IsSuccess);
                caller = new(other.PluginName, other.SessionId);
                AssertTrue((await Release(3)).IsSuccess); AssertTrue((await Release(4)).IsSuccess);
                caller = new(session.PluginName, session.SessionId);
                AssertTrue((await Begin(21)).IsSuccess);
                using (var beforeCommit = new CancellationTokenSource())
                {
                    int beforeWrites = writes; beforeCommit.Cancel(); AssertFalse((await Commit(21, beforeCommit.Token)).IsSuccess);
                    AssertEqual(beforeWrites, writes);
                }
                AssertTrue((await Release(21)).IsSuccess);
                entered = new(TaskCreationOptions.RunContinuationsAsynchronously); unblock = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var stop = new CancellationTokenSource();
                var pendingRead = Read(30, stop.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                stop.Cancel(); AssertFalse((await pendingRead.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess); AssertTrue(nativeToken.IsCancellationRequested);
                binding.Dispose(); binding = api.Bind(actions);
                int before = reads; AssertEqual("xsr.backpressure", (await Read(31)).Error!.Code.Value); AssertEqual(before, reads);
                fail = true; unblock.TrySetResult();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                long next = 32; XsrResult<Clip.HostClipboardSnapshot> after;
                do { await Task.Delay(1, deadline.Token); after = await Read(next++); } while (after.Error?.Kind == XsrErrorKind.Backpressure);
                AssertEqual("xsr.unavailable", after.Error!.Code.Value);
                fail = false; unblock = null; entered = null;
                AssertTrue((await Begin(next++)).IsSuccess);
                var commitId = next - 1;
                entered = new(TaskCreationOptions.RunContinuationsAsynchronously); unblock = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var pendingCommit = Commit(commitId); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                int committedWrites = writes; binding.Dispose(); binding = api.Bind(actions);
                AssertFalse((await pendingCommit.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                AssertEqual("xsr.backpressure", (await Read(next++)).Error!.Code.Value);
                AssertFalse((await Commit(commitId)).IsSuccess); AssertEqual(committedWrites, writes);
                unblock.TrySetResult(); unblock = null; entered = null;
                do { await Task.Delay(1, deadline.Token); after = await Read(next++); } while (after.Error?.Kind == XsrErrorKind.Backpressure);
                AssertTrue(after.IsSuccess); AssertTrue((await Release(next - 1)).IsSuccess);
                entered = new(TaskCreationOptions.RunContinuationsAsynchronously); unblock = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var retiring = Read(next++); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); session.Dispose();
                AssertFalse((await retiring.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                AssertTrue(nativeToken.IsCancellationRequested); unblock.TrySetResult(); AssertFalse((await Read(next)).IsSuccess);
            }
            finally { unblock?.TrySetResult(); binding.Dispose(); }
        }
    }
    // A wire fixture; the native backend separately decodes the complete PNG before writing.
    private static byte[] ClipboardPng(int length)
    {
        byte[] bytes = new byte[length]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13); "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 1); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 1);
        return bytes;
    }
}
