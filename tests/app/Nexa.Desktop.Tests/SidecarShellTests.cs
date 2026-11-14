using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Shell = Nexa.Sidecar.Host.Shell.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarShellAbi() => SidecarShellAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarShellAbiAsync()
    {
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        using (session) using (peer)
        using (var api = new SidecarShellApi(_ => session, new NoopDispatchObserver()))
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[1792].Authorize(caller with { SessionId = Guid.NewGuid() }));
            AssertFalse(routes[1794].Authorize(caller with { PackageName = "another-package" }));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarShellClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Shell.SidecarShellRoutes.OpenHttps, out var open));
            AssertTrue(client.Commands.TryResolve(Shell.SidecarShellRoutes.RevealDirectory, out var reveal));
            AssertTrue(client.Queries.TryResolve(Shell.SidecarShellRoutes.Displays, out var displays));
            Task<XsrResult> Open(string address, CancellationToken token = default) => client.Commands.Dispatch(open,
                new Shell.HostHttpsRequest(address), cancellationToken: token).Completion;
            Task<XsrResult> Reveal(string path) => client.Commands.Dispatch(reveal, new Shell.HostDirectoryRequest(path)).Completion;
            Task<XsrResult<Shell.HostDisplays>> Displays() => client.Queries.QueryAsync<Shell.HostShellEmpty, Shell.HostDisplays>(displays, new()).AsTask();
            AssertFalse((await Open("https://example.com")).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            AssertEqual("xsr.unavailable", (await Displays()).Error!.Code.Value);
            int opens = 0, reveals = 0; string? lastAddress = null, lastPath = null;
            TaskCompletionSource? entered = null, release = null;
            HostDisplay[] screens = [new("main", 1920, 1080, 1.25)];
            var binding = api.Bind(new(async (address, token) =>
            {
                opens++; lastAddress = address;
                entered?.TrySetResult();
                if (release is not null) await release.Task; // A noncooperative callback cannot hold a detached binding alive.
                token.ThrowIfCancellationRequested();
            }, (path, token) => { token.ThrowIfCancellationRequested(); reveals++; lastPath = path; return Task.CompletedTask; },
                token => { token.ThrowIfCancellationRequested(); return Task.FromResult(screens); }));
            AssertTrue((await Open("https://example.com/a?q=1")).IsSuccess); AssertEqual(1, opens);
            AssertEqual("https://example.com/a?q=1", lastAddress);
            foreach (string invalid in new[] { "http://example.com", "file:///tmp/test", "https://user:secret@example.com", "https://example.com/\n", new string('x', 16385) })
                AssertFalse((await Open(invalid)).IsSuccess);
            AssertEqual(1, opens);
            string directory = Path.GetFullPath(Path.GetTempPath());
            AssertTrue((await Reveal(directory)).IsSuccess); AssertEqual(directory, lastPath);
            foreach (string invalid in new[] { "relative/path", Path.Combine(directory, Guid.NewGuid().ToString("N")), "file:///tmp" })
                AssertFalse((await Reveal(invalid)).IsSuccess);
            AssertEqual(1, reveals);
            var display = await Displays(); AssertTrue(display.IsSuccess); AssertEqual(1.25, display.Value.Items.Single().Scale);
            foreach (var invalid in new HostDisplay[][] { [new("main", 0, 1080, 1)], [new("main", 1, 1, double.NaN)],
                [new("same", 1, 1, 1), new("same", 1, 1, 1)], Enumerable.Range(0, 33).Select(i => new HostDisplay(i.ToString(System.Globalization.CultureInfo.InvariantCulture), 1, 1, 1)).ToArray() })
            { screens = invalid; AssertFalse((await Displays()).IsSuccess); }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            AssertFalse((await Open("https://example.com", cancelled.Token)).IsSuccess); AssertEqual(1, opens);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Open("https://example.com/pending");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); AssertFalse(pending.IsCompleted);
            binding.Dispose();
            AssertEqual("xsr.cancelled", (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Error!.Code.Value);
            AssertEqual("xsr.unavailable", (await Displays()).Error!.Code.Value);
            release.TrySetResult();
            using var replacement = api.Bind(new((_, token) => { token.ThrowIfCancellationRequested(); opens++; return Task.CompletedTask; },
                (_, _) => Task.CompletedTask, _ => Task.FromResult<HostDisplay[]>([])));
            AssertTrue((await Open("https://example.com/new-binding")).IsSuccess); AssertEqual(3, opens);
            session.Dispose(); AssertFalse((await client.DiscoverAsync()).IsSuccess);
            AssertFalse((await Open("https://example.com/retired")).IsSuccess); AssertEqual(3, opens);
        }
    }
}
