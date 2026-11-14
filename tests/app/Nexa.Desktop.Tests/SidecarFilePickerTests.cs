using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Picker = Nexa.Sidecar.Host.FilePicker.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarFilePickerAbi() => SidecarFilePickerAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarFilePickerAbiAsync()
    {
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        using (session) using (peer)
        using (var api = new SidecarFilePickerApi(_ => session, new NoopDispatchObserver()))
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[2816].Authorize(caller with { PackageName = "foreign" }));
            AssertFalse(routes[2816].Authorize(caller with { SessionId = Guid.NewGuid() }));
            Dictionary<Guid, CancellationTokenSource> calls = [];
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                if (frame.MessageType == SidecarMessageType.Cancel)
                {
                    lock (calls) if (calls.TryGetValue(frame.CorrelationId.Value, out var pending)) pending.Cancel();
                    return;
                }
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (calls) calls.Add(frame.CorrelationId.Value, lifetime);
                try
                {
                    var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                    var route = routes[request.ContractId];
                    var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, lifetime.Token)
                        : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                    AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, SidecarMessageType.QueryResult,
                        SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                            result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
                }
                finally { lock (calls) calls.Remove(frame.CorrelationId.Value); }
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarFilePickerClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Queries.TryResolve(Picker.SidecarFilePickerRoutes.Pick, out var pick));
            Task<XsrResult<Picker.HostFileSelection>> Send(Picker.HostFilePick? value = null, CancellationToken token = default) =>
                client.Queries.QueryAsync<Picker.HostFilePick, Picker.HostFileSelection>(pick,
                    value ?? new(Picker.HostFilePickKind.OpenFile, "File", ["zip"]), cancellationToken: token).AsTask();
            AssertFalse((await Send()).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            AssertEqual("xsr.unavailable", (await Send()).Error!.Code.Value);
            int opened = 0;
            string? selection = Path.Combine(Path.GetTempPath(), "中文😀.zip");
            TaskCompletionSource? entered = null, release = null;
            bool fail = false;
            CancellationToken nativeToken = default;
            Func<SidecarHostCaller, HostFilePick, CancellationToken, Task<string?>> native = async (owner, input, token) =>
            {
                AssertEqual(caller, owner); AssertTrue(Enum.IsDefined(input.Kind));
                opened++; nativeToken = token; entered?.TrySetResult();
                if (release is not null) await release.Task; // Simulates an OS dialog that cannot be dismissed by cancellation.
                if (fail) throw new IOException("native picker failed after returning");
                return selection;
            };
            var binding = api.Bind(native);
            try
            {
                foreach (var kind in Enum.GetValues<Picker.HostFilePickKind>())
                {
                    var result = await Send(new(kind, "<literal> {state}", [".zip", "tar.gz"]));
                    AssertTrue(result.IsSuccess); AssertEqual(selection, Encoding.UTF8.GetString(result.Value.Utf8Path!));
                }
                foreach (var invalid in new Picker.HostFilePick[]
                {
                    new((Picker.HostFilePickKind)3, "File", []), new(Picker.HostFilePickKind.OpenFile, new string('x', 257), []),
                    new(Picker.HostFilePickKind.OpenFile, "Title\nImpersonation", []),
                    new(Picker.HostFilePickKind.OpenFile, "File", ["*"]), new(Picker.HostFilePickKind.OpenFile, "File", ["../"]),
                    new(Picker.HostFilePickKind.OpenFile, "File", ["..."]), new(Picker.HostFilePickKind.OpenFile, "File", [new string('x', 33)]),
                    new(Picker.HostFilePickKind.OpenFile, "File", Enumerable.Repeat("zip", 33).ToArray())
                }) AssertFalse((await Send(invalid)).IsSuccess);
                AssertEqual(3, opened);
                using (var cancelled = new CancellationTokenSource())
                { cancelled.Cancel(); AssertFalse((await Send(token: cancelled.Token)).IsSuccess); AssertEqual(3, opened); }
                selection = null; var dismissed = await Send(); AssertTrue(dismissed.IsSuccess); AssertTrue(dismissed.Value.Utf8Path is null);
                selection = Path.GetPathRoot(Path.GetTempPath()) + new string('中', 12000); // More than the general string codec's 32 KiB limit.
                var unicode = await Send(); AssertTrue(unicode.IsSuccess); AssertEqual(selection, Encoding.UTF8.GetString(unicode.Value.Utf8Path!));
                foreach (string bad in new[] { "relative", "bad\0path", Path.GetPathRoot(Path.GetTempPath()) + new string('x', 32769) })
                { selection = bad; AssertFalse((await Send()).IsSuccess); }
                selection = Path.Combine(Path.GetTempPath(), "selection.zip");
                entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var stop = new CancellationTokenSource();
                var first = Send(token: stop.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AssertFalse(first.IsCompleted); int before = opened;
                AssertEqual("xsr.backpressure", (await Send()).Error!.Code.Value); AssertEqual(before, opened);
                stop.Cancel(); AssertFalse((await first.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                AssertTrue(nativeToken.IsCancellationRequested);
                binding.Dispose(); binding = api.Bind(native);
                AssertEqual("xsr.backpressure", (await Send()).Error!.Code.Value); AssertEqual(before, opened);
                fail = true; release.TrySetResult();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                XsrResult<Picker.HostFileSelection> finished;
                do { await Task.Delay(1, deadline.Token); finished = await Send(); }
                while (finished.Error?.Code.Value == "xsr.backpressure");
                AssertEqual("xsr.unavailable", finished.Error!.Code.Value); // Late native exceptions are observed and release the slot.
                fail = false; release = null; entered = null;
                AssertTrue((await Send()).IsSuccess);
                entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var detached = Send(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); binding.Dispose();
                AssertEqual("xsr.cancelled", (await detached.WaitAsync(TimeSpan.FromSeconds(5))).Error!.Code.Value);
                AssertTrue(nativeToken.IsCancellationRequested); release.TrySetResult();
                binding = api.Bind(native);
                do { await Task.Delay(1, deadline.Token); finished = await Send(); }
                while (finished.Error?.Code.Value == "xsr.backpressure");
                AssertTrue(finished.IsSuccess);
                entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var retired = Send(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); session.Dispose();
                AssertEqual("xsr.cancelled", (await retired.WaitAsync(TimeSpan.FromSeconds(5))).Error!.Code.Value);
                AssertTrue(nativeToken.IsCancellationRequested); before = opened; release.TrySetResult();
                AssertFalse((await Send()).IsSuccess); AssertEqual(before, opened);
            }
            finally { release?.TrySetResult(); binding.Dispose(); }
        }
    }
}
