using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Notice = Nexa.Sidecar.Host.Notifications.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarNotificationsAbi() => SidecarNotificationsAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarNotificationsAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var feedback = fixture.Feedback;
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        using (session) using (peer)
        using (var api = new SidecarNotificationsApi(_ => session, new NoopDispatchObserver()))
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[2560].Authorize(caller with { PackageName = "foreign" }));
            AssertFalse(routes[2560].Authorize(caller with { SessionId = Guid.NewGuid() }));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version,
                    route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarNotificationsClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Notice.SidecarNotificationsRoutes.Publish, out var publish));
            Task<XsrResult> Send(string title, string body = "Body", Notice.HostNotificationLevel level = Notice.HostNotificationLevel.Info) =>
                client.Commands.Dispatch(publish, new Notice.HostNotification(level, title, body)).Completion;
            AssertFalse((await Send("Before discovery")).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            AssertEqual("xsr.unavailable", (await Send("Unavailable")).Error!.Code.Value);
            var binding = api.Bind((owner, input, token) => DesktopSidecarNotifications.Publish(feedback, owner, input, token));
            try
            {
                string title = "common.cancel", body = "<Button Command='host.erase'/> {state host.secret} & 中文😀";
                AssertTrue((await Send(title, body)).IsSuccess);
                var notification = feedback.Snapshot().Notifications.Single();
                AssertFalse(notification.LocalizeMessage);
                AssertEqual("Sidecar · " + session.PluginName + "\n" + title + "\n" + body, notification.Message);
                fixture.Shell.Renderer.ReducedMotion = true;
                AssertEqual(notification.Message, FindByKey(fixture.Shell, fixture.Shell.Render(new(800, 600)), "NotificationMessage").Text);
                feedback.DismissNotification(notification.Id);
                foreach (var level in Enum.GetValues<Notice.HostNotificationLevel>())
                    AssertTrue((await Send(level.ToString(), level: level)).IsSuccess);
                AssertTrue(feedback.Snapshot().Notifications.Select(value => (int)value.Level).Order().SequenceEqual(Enumerable.Range(1, 3)));
                AssertTrue((await Send("Warning", level: Notice.HostNotificationLevel.Warning)).IsSuccess);
                AssertEqual(3, feedback.Snapshot().Notifications.Count); // Existing feedback deduplication is authoritative.
                foreach (var invalid in new[]
                {
                    Send("Bad", level: (Notice.HostNotificationLevel)0), Send("Bad", level: (Notice.HostNotificationLevel)4),
                    Send(new string('a', 4097)), Send("Bad", new string('b', 8193)), Send("Bad\0Title"), Send("Bad", "body\u0001")
                }) AssertFalse((await invalid).IsSuccess);
                AssertEqual(3, feedback.Snapshot().Notifications.Count);
                binding.Dispose();
                AssertEqual("xsr.unavailable", (await Send("Detached")).Error!.Code.Value);
                binding = api.Bind((_, _, _) => throw new IOException("fixture rejection"));
                AssertEqual("xsr.rejected", (await Send("Rejected")).Error!.Code.Value);
                binding.Dispose();
                binding = api.Bind((_, _, _) => binding.Dispose());
                AssertEqual("xsr.cancelled", (await Send("Reentrant detach")).Error!.Code.Value);
                binding = api.Bind((_, _, token) => { session.Dispose(); AssertTrue(token.IsCancellationRequested); });
                AssertEqual("xsr.cancelled", (await Send("Retired session")).Error!.Code.Value);
                AssertFalse((await client.DiscoverAsync()).IsSuccess);
                AssertEqual(3, feedback.Snapshot().Notifications.Count);
            }
            finally { binding.Dispose(); }
        }
    }
}
