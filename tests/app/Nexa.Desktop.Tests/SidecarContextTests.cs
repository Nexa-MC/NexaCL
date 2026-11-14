using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Context = Nexa.Sidecar.Host.Context.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarContextAbi() => SidecarContextAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarContextAbiAsync()
    {
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        // This value exceeds the string codec's 32 KiB limit, without touching the filesystem.
        string root = Path.GetPathRoot(Path.GetTempPath()) + new string('中', 12000) + "😀";
        using (session) using (peer)
        using (var api = new SidecarContextApi(_ => session, new NoopDispatchObserver(), "2.0.0.alpha.29", root))
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[3328].Authorize(caller with { PackageName = "foreign" }));
            AssertFalse(routes[3328].Authorize(caller with { SessionId = Guid.NewGuid() }));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                if (frame.MessageType == SidecarMessageType.Cancel) return;
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, SidecarMessageType.QueryResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarContextClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Queries.TryResolve(Context.SidecarContextRoutes.Snapshot, out var snapshot));
            async Task<Context.HostContextSnapshot> Read()
            {
                var result = await client.Queries.QueryAsync<Context.HostContextQuery, Context.HostContextSnapshot>(snapshot, new());
                AssertTrue(result.IsSuccess); return result.Value;
            }
            AssertFalse((await client.Queries.QueryAsync<Context.HostContextQuery, Context.HostContextSnapshot>(snapshot, new())).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            var composing = await Read();
            AssertEqual(Context.HostStartupPhase.Composing, composing.Phase);
            AssertFalse(composing.NativeWindowReady);
            AssertEqual(root, new UTF8Encoding(false, true).GetString(composing.Utf8DataRoot));
            AssertEqual("2.0.0.alpha.29", composing.Version);
            composing.Utf8DataRoot[0] = 0;
            AssertEqual(root, Encoding.UTF8.GetString((await Read()).Utf8DataRoot));
            bool native = false, stopDuringRead = false;
            api.MarkReady(() => { if (stopDuringRead) api.MarkStopping(); return native; });
            AssertEqual(Context.HostStartupPhase.Ready, (await Read()).Phase);
            AssertFalse((await Read()).NativeWindowReady);
            native = true;
            AssertTrue((await Read()).NativeWindowReady);
            native = false;
            AssertFalse((await Read()).NativeWindowReady);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                AssertFalse((await client.Queries.QueryAsync<Context.HostContextQuery, Context.HostContextSnapshot>(snapshot, new(), cancellationToken: cancelled.Token)).IsSuccess);
            }
            native = true; stopDuringRead = true;
            var stopped = await Read();
            AssertEqual(Context.HostStartupPhase.Stopping, stopped.Phase);
            AssertFalse(stopped.NativeWindowReady); // Closing during a native fact read must not resurrect Ready.
            api.MarkStopping();
            bool rejected = false;
            try { api.MarkReady(() => true); } catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected);
            api.Dispose(); api.Dispose();
            AssertFalse(routes[3327].Authorize(caller));
            AssertFalse(routes[3328].Authorize(caller));
        }
        var (small, smallPeer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All & ~SidecarFeatures.LargeGeneratedDtos);
        using (small) using (smallPeer)
        using (var api = new SidecarContextApi(_ => small, new NoopDispatchObserver(), "2.0.0", Path.GetTempPath()))
        {
            var caller = new SidecarHostCaller(small.PluginName, small.SessionId);
            AssertTrue(api.Routes.Single(route => route.ContractId == 3327).Authorize(caller));
            AssertFalse(api.Routes.Single(route => route.ContractId == 3328).Authorize(caller));
            await small.DeactivateAsync();
            AssertFalse(api.Routes.Single(route => route.ContractId == 3327).Authorize(caller));
        }
        foreach (var invalid in new[] { "relative", "", Path.GetTempPath() + '\0', Path.GetTempPath() + '\ud800', Path.GetTempPath() + new string('x', 131073) })
        {
            bool rejected = false;
            try { using var api = new SidecarContextApi(_ => null, new NoopDispatchObserver(), "2.0.0", invalid); }
            catch (ArgumentException) { rejected = true; }
            AssertTrue(rejected);
        }
    }
}
