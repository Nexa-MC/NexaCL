using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Pages = Nexa.Sidecar.Host.PageControl.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarPageControlAbi() => SidecarPageControlAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarPageControlAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        var fallback = fixture.Shell.Stage.Navigation.Current;
        var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
            [PageDeclaration("test.page", "#ControlledPage", SidecarNxmlOperation.Page, """<Page Key="ControlledPage"><Text Content="Controlled" /></Page>""")],
            features: SidecarFeatures.All, nxml: adapter);
        var (other, otherPeer) = await ActivateCaptionPatch(new(), "", declarations:
            [PageDeclaration("test.other", "#OtherControlledPage", SidecarNxmlOperation.Page, """<Page Key="OtherControlledPage"><Text Content="Other" /></Page>""")],
            features: SidecarFeatures.All, nxml: adapter);
        using (session) using (peer) using (other) using (otherPeer)
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarPageControlApi(_ => session, new NoopDispatchObserver(), adapter.ReadPagesAsync, adapter.ChangePageAsync);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[1281].Authorize(caller with { SessionId = other.SessionId }));
            AssertFalse(routes[1280].Authorize(caller with { PackageName = "Other" }));
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
            var client = new SidecarPageControlClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Pages.SidecarPageControlRoutes.Open, out var open));
            AssertTrue(client.Commands.TryResolve(Pages.SidecarPageControlRoutes.Close, out var close));
            AssertTrue(client.Queries.TryResolve(Pages.SidecarPageControlRoutes.State, out var read));
            AssertFalse((await client.Commands.Dispatch(open, new Pages.HostPageChange(0, 0)).Completion).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            fixture.Shell.Renderer.ReducedMotion = true;
            void Frame() => fixture.Shell.Render(new(1000, 650));
            async Task Present()
            {
                var until = DateTime.UtcNow.AddSeconds(3);
                while (adapter.PendingPageControls == 0 && DateTime.UtcNow < until) await Task.Delay(1);
                AssertTrue(adapter.PendingPageControls > 0);
                Frame();
            }
            Frame();
            var state = client.Queries.QueryAsync<Pages.HostPageQuery, Pages.HostPageState>(read, new(0)).AsTask();
            await Present();
            var initial = await state;
            AssertTrue(initial.IsSuccess); AssertEqual(1, initial.Value.Pages.Length);
            AssertEqual("#ControlledPage", initial.Value.Pages[0].Target); AssertFalse(initial.Value.Pages[0].Visible);
            var opening = client.Commands.Dispatch(open, new Pages.HostPageChange(0, 0)).Completion;
            AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            await Present(); AssertTrue((await opening).IsSuccess);
            var page = fixture.Shell.Stage.Navigation.Current; AssertFalse(page == fallback);
            state = client.Queries.QueryAsync<Pages.HostPageQuery, Pages.HostPageState>(read, new(0)).AsTask();
            await Present(); AssertTrue((await state).Value.Pages[0].Visible);
            // Another session cannot close the visible page, even with the same document index.
            var foreignClose = adapter.ChangePageAsync(other, 0, 0, false, default).AsTask();
            await Present(); await foreignClose; AssertEqual(page, fixture.Shell.Stage.Navigation.Current);
            var closing = client.Commands.Dispatch(close, new Pages.HostPageChange(0, 0)).Completion;
            await Present(); AssertTrue((await closing).IsSuccess); AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            AssertFalse((await client.Commands.Dispatch(open, new Pages.HostPageChange(0, 1)).Completion).IsSuccess);
            AssertFalse((await client.Commands.Dispatch(open, new Pages.HostPageChange(1, 0)).Completion).IsSuccess);
            // Admission is bounded, and cancellation frees capacity without rendering a frame.
            using var cancel = new CancellationTokenSource();
            var pending = Enumerable.Range(0, 8).Select(_ => adapter.ReadPagesAsync(session, 0, cancel.Token).AsTask()).ToArray();
            AssertEqual(8, adapter.PendingPageControls);
            bool full = false;
            try { await adapter.ReadPagesAsync(session, 0, default); } catch (InvalidOperationException) { full = true; }
            AssertTrue(full); cancel.Cancel();
            foreach (var task in pending) { try { await task; } catch (OperationCanceledException) { } AssertTrue(task.IsCanceled); }
            AssertEqual(0, adapter.PendingPageControls);
            // Model changes preserve exposure but invalidate a queued request's expected revision.
            var stale = adapter.ChangePageAsync(session, 0, 0, true, default).AsTask();
            await session.UpdatePresentationModelsAsync(0, new Dictionary<int, IReadOnlyDictionary<string, string>> { [0] = new Dictionary<string, string>() });
            Frame(); bool rejected = false;
            try { await stale; } catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected); AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            // Source replacement cancels old work before another UI frame.
            var retired = adapter.ChangePageAsync(session, 1, 0, true, default).AsTask();
            await session.ReplacePresentationAsync(1, []);
            try { await retired; } catch (OperationCanceledException) { }
            AssertTrue(retired.IsCanceled); AssertEqual(0, adapter.PendingPageControls);
            // A paused renderer cannot retain the queue or execute a timed-out request later.
            bool expired = false;
            try { await adapter.ChangePageAsync(other, 0, 0, true, default); } catch (TimeoutException) { expired = true; }
            AssertTrue(expired); AssertEqual(0, adapter.PendingPageControls);
            Frame(); AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            var detached = adapter.ReadPagesAsync(other, 0, default).AsTask();
            view.Dispose();
            try { await detached; } catch (OperationCanceledException) { }
            AssertTrue(detached.IsCanceled); AssertEqual(0, adapter.PendingPageControls);
            bool unavailable = false;
            try { await adapter.ReadPagesAsync(other, 0, default); } catch (InvalidOperationException) { unavailable = true; }
            AssertTrue(unavailable); AssertEqual(0, warnings.Count);
            session.Dispose(); AssertFalse((await client.DiscoverAsync()).IsSuccess);
        }
    }
}
