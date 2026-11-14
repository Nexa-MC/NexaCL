using System.Text;
using Nexa.Desktop.Ui;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Confirm = Nexa.Sidecar.Host.Confirmation.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarConfirmationAbi()
    {
        SidecarConfirmationAbiAsync().GetAwaiter().GetResult();
        SidecarConfirmationRetiresWithTransport().GetAwaiter().GetResult();
    }
    private static async Task SidecarConfirmationAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var clock = new FeedbackClock();
        var feedback = fixture.Feedback;
        using var adapter = new DesktopSidecarConfirmation(clock);
        var attachment = adapter.Attach(feedback);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        using (session) using (peer)
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarConfirmationApi(_ => session, new NoopDispatchObserver(), adapter.AskAsync);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[1536].Authorize(caller with { SessionId = Guid.NewGuid() }));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, SidecarMessageType.QueryResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarConfirmationClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Queries.TryResolve(Confirm.SidecarConfirmationRoutes.Ask, out var ask));
            Task<XsrResult<Confirm.HostConfirmationDecision>> Ask(string title, byte[] details) => client.Queries
                .QueryAsync<Confirm.HostConfirmationRequest, Confirm.HostConfirmationDecision>(ask, new(title, details)).AsTask();
            AssertFalse((await Ask("Action", "Details"u8.ToArray())).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            async Task<DesktopDialog> Dialog()
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (feedback.Snapshot().Dialog is null && DateTime.UtcNow < until) await Task.Delay(1);
                return feedback.Snapshot().Dialog ?? throw new InvalidOperationException("Confirmation was not displayed.");
            }
            string literal = "<Button Command='host.erase'/> {state host.secret}";
            var pending = Ask("Requested action", Encoding.UTF8.GetBytes(literal));
            var dialog = await Dialog();
            AssertEqual("Sidecar · " + session.PluginName, dialog.Title);
            AssertEqual("Requested action\n\n" + literal, dialog.Message);
            AssertFalse(dialog.LocalizeMessage); AssertFalse(dialog.LocalizeTitle);
            AssertFalse((await Ask("Competing", "Cannot displace"u8.ToArray())).IsSuccess);
            AssertEqual(dialog.Id, feedback.Snapshot().Dialog!.Id);
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(800, 600));
            AssertEqual(dialog.Message, FindByKey(fixture.Shell, scene, "DialogMessage").Text);
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "DialogAccept").Entity));
            AssertTrue((await pending).Value.Accepted);
            pending = Ask("Decline", "Details"u8.ToArray()); dialog = await Dialog();
            feedback.ResolveDialog(dialog.Id, false); AssertFalse((await pending).Value.Accepted);
            Guid protectedDialog = feedback.ShowDialog("host.confirm", "Host", "Protected", "Yes", "No", _ => { });
            AssertFalse((await Ask("Competing", "Details"u8.ToArray())).IsSuccess);
            AssertEqual(protectedDialog, feedback.Snapshot().Dialog!.Id); feedback.DismissDialog(protectedDialog);
            AssertFalse((await Ask("Invalid", [255])).IsSuccess);
            AssertFalse((await Ask("Invalid\nTitle", "Details"u8.ToArray())).IsSuccess);
            AssertFalse((await Ask("Invalid", Encoding.UTF8.GetBytes("nul\0value"))).IsSuccess);
            AssertFalse((await Ask("Oversize", new byte[256 * 1024 + 1])).IsSuccess);
            AssertTrue(feedback.Snapshot().Dialog is null);
            // Remote UI work must also support details above the generic string limit.
            pending = Ask("Large", Encoding.UTF8.GetBytes(new string('x', 40000))); dialog = await Dialog();
            scene = fixture.Shell.Render(new(800, 600));
            AssertEqual(dialog.Message, FindByKey(fixture.Shell, scene, "DialogMessage").Text);
            feedback.ResolveDialog(dialog.Id, false); AssertTrue((await pending).IsSuccess);
            pending = Ask("Expire", "Details"u8.ToArray()); dialog = await Dialog();
            clock.Advance(TimeSpan.FromMinutes(2));
            AssertEqual("xsr.timed_out", (await pending).Error!.Code.Value);
            AssertTrue(feedback.Snapshot().Dialog is null); AssertFalse(feedback.ResolveDialog(dialog.Id, true));
            using var cancel = new CancellationTokenSource();
            var cancelled = adapter.AskAsync(caller, "Cancel", "Details", cancel.Token).AsTask(); dialog = await Dialog();
            cancel.Cancel(); try { await cancelled; } catch (OperationCanceledException) { }
            AssertTrue(cancelled.IsCanceled); AssertTrue(feedback.Snapshot().Dialog is null);
            pending = Ask("Detach", "Details"u8.ToArray()); dialog = await Dialog(); attachment.Dispose();
            AssertFalse((await pending).IsSuccess); AssertTrue(feedback.Snapshot().Dialog is null);
            AssertFalse(feedback.ResolveDialog(dialog.Id, true));
            AssertFalse((await Ask("Unavailable", "Details"u8.ToArray())).IsSuccess);
            session.Dispose(); AssertFalse((await client.DiscoverAsync()).IsSuccess);
        }
    }
    private static async Task SidecarConfirmationRetiresWithTransport()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var feedback = new DesktopFeedbackService();
        using var adapter = new DesktopSidecarConfirmation();
        using var attachment = adapter.Attach(feedback);
        SidecarHostSession? active = null;
        var api = new SidecarConfirmationApi(_ => active, new NoopDispatchObserver(), adapter.AskAsync);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All,
            hostRequests: new(api.Routes));
        active = session;
        using (session) using (peer)
        using (var requests = new SidecarHostRequestClient((frame, token) => peer.SendAsync(frame, token)))
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var receiving = session.RunReceiveLoopAsync(stop.Token).AsTask();
            requests.Activate(SidecarFeatures.All);
            var replies = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var reply = await peer.ReceiveAsync(stop.Token);
                        AssertTrue(requests.TryAcceptReply(reply));
                    }
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
            });
            var client = new SidecarConfirmationClient(requests, new NoopDispatchObserver());
            AssertTrue((await client.DiscoverAsync(stop.Token)).IsSuccess);
            AssertTrue(client.Queries.TryResolve(Confirm.SidecarConfirmationRoutes.Ask, out var ask));
            var pending = client.Queries.QueryAsync<Confirm.HostConfirmationRequest, Confirm.HostConfirmationDecision>(ask,
                new("Pending", "Retire this decision"u8.ToArray()), cancellationToken: stop.Token).AsTask();
            while (feedback.Snapshot().Dialog is null) await Task.Delay(1, stop.Token);
            var id = feedback.Snapshot().Dialog!.Id;
            session.Dispose();
            while (feedback.Snapshot().Dialog is not null) await Task.Delay(1, stop.Token);
            AssertFalse(feedback.ResolveDialog(id, true));
            requests.Dispose();
            AssertFalse((await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
            stop.Cancel();
            await receiving.WaitAsync(TimeSpan.FromSeconds(5));
            await replies.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

}
