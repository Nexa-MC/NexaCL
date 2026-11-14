using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using Reconcile = Nexa.Sidecar.Host.ViewReconcile.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarViewReconcileAbi() => SidecarViewReconcileAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarViewReconcileAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
        [
            new(SidecarRegistrationKind.State, "caption", 0, 0),
            new(SidecarRegistrationKind.Command, "test.retained.form", 0, SidecarWireCodecs.GeneratedDto),
            PageDeclaration("test.retained.modal", "#RetainedModal", SidecarNxmlOperation.Dialog,
                """<Page Key="RetainedModal"><StackPanel><TextInput Key="RetainedDraft" Label="Draft" Height="36" /><Text Content="{data caption}" Height="30" /><Button Key="RetainedSubmit" Content="Save" Height="42" Command="test.retained.form" /><Dynamic Key="RetainedSlot" /></StackPanel></Page>"""),
            PageDeclaration("test.retained.child", "#RetainedModal/#RetainedSlot", SidecarNxmlOperation.Append,
                """<StackPanel><TextInput Key="RetainedChildDraft" Label="Nested draft" Height="36" /><Button Key="RetainedChildSubmit" Content="Nested" Height="42" Command="test.retained.form" /></StackPanel>""")
        ], features: SidecarFeatures.All, initialState: [(1, Encoding.UTF8.GetBytes("initial"))], nxml: adapter);
        var (legacy, legacyPeer) = await ActivateCaptionPatch(new(), "", declarations: [],
            features: SidecarFeatures.All & ~SidecarFeatures.RetainedDocuments, nxml: adapter);
        using (session) using (peer) using (legacy) using (legacyPeer)
        {
            var loop = session.RunReceiveLoopAsync().AsTask();
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarViewReconcileApi(_ => session, new NoopDispatchObserver());
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[3840].Authorize(caller with { SessionId = legacy.SessionId }));
            AssertFalse(routes[3839].Authorize(caller with { PackageName = "Other" }));
            var legacyApi = new SidecarViewReconcileApi(_ => legacy, new NoopDispatchObserver());
            AssertFalse(legacyApi.Routes.Any(route => route.Authorize(new(legacy.PluginName, legacy.SessionId))));
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
            var client = new SidecarViewReconcileClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Reconcile.SidecarViewReconcileRoutes.Reconcile, out var reconcile));
            Task<XsrResult> Apply(long revision, params Reconcile.HostViewCandidate[] documents)
                => client.Commands.Dispatch(reconcile, new Reconcile.HostViewReconcileRequest(revision, documents)).Completion;
            static Reconcile.HostViewCandidate Keep(int index, string? caption = null)
                => new(index, [], caption is null ? [] : [new("caption", Encoding.UTF8.GetBytes(caption))]);
            AssertFalse((await Apply(0)).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            fixture.Shell.Renderer.ReducedMotion = true;
            void Frame()
            {
                fixture.Shell.Render(new(1000, 750));
                if (warnings.Count != 0) throw new InvalidOperationException(string.Join("; ", warnings));
            }
            XsrUiEntityId Node(string key) => fixture.Shell.Tree.LiveEntities.Single(node => fixture.Shell.Tree.Name(node) == key);
            async Task Change(long revision, int index, bool open)
            {
                var change = adapter.ChangeDialogAsync(session, revision, index, open, default).AsTask();
                Frame(); await change;
            }
            Frame(); await Change(0, 0, true);
            var before = session.CapturePresentation(0);
            var chrome = Node("SidecarDialog_" + before.Documents[0].Exposure.ToString("N"));
            var draft = Node("RetainedDraft"); var childDraft = Node("RetainedChildDraft");
            fixture.Shell.Renderer.SetTextInputValue(draft, "keep body draft");
            fixture.Shell.Renderer.SetTextInputValue(childDraft, "keep nested draft");
            AssertTrue(fixture.Shell.Renderer.Activate(Node("RetainedSubmit")));
            var bodyAction = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertTrue(fixture.Shell.Renderer.Activate(Node("RetainedChildSubmit")));
            var childAction = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var staleControl = adapter.ReadDialogsAsync(session, 0, default).AsTask();
            var added = new Reconcile.HostViewCandidate(-1,
                PageDeclaration("test.retained.second", "#AddedModal", SidecarNxmlOperation.Dialog,
                    """<Page Key="AddedModal"><Text Content="New window" /></Page>""").Payload!, []);
            AssertTrue((await Apply(0, added, Keep(1), Keep(0, "updated caption"))).IsSuccess);
            try { await staleControl; } catch (OperationCanceledException) { }
            AssertTrue(staleControl.IsCanceled); Frame();
            AssertTrue(before.Retirement.IsCancellationRequested);
            AssertEqual(before.Documents[0].Exposure, session.CapturePresentation(1).Documents[2].Exposure);
            AssertEqual(draft, Node("RetainedDraft")); AssertEqual(childDraft, Node("RetainedChildDraft"));
            AssertEqual("keep body draft", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(draft)!.ReadDraft());
            AssertEqual("keep nested draft", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(childDraft)!.ReadDraft());
            AssertTrue(fixture.Shell.Stage.Overlays.Contains(chrome));
            AssertTrue(fixture.Shell.Render(new(1000, 750)).Nodes.Any(node => node.Text == "updated caption"));
            // An ordinary reply must still complete both retained actions; closing them early would emit Cancel.
            foreach (var action in new[] { bodyAction, childAction })
                await peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                    action.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null)));
            await Change(1, 0, true);
            AssertEqual(2, fixture.Shell.Stage.Overlays.Count(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true));
            await Change(1, 0, false);
            AssertTrue(fixture.Shell.Renderer.Activate(Node("RetainedChildSubmit")));
            var retainedAction = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(SidecarMessageType.CommandRequest, retainedAction.MessageType);
            var captured = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(retainedAction.Payload.Span).Value.ToArray());
            AssertEqual("keep nested draft", Encoding.UTF8.GetString(captured.Fields.Single().Utf8Value));
            // Invalid changes are rejected through the generated binary API without losing either document.
            foreach (var candidates in new Reconcile.HostViewCandidate[][]
            {
                [Keep(2), Keep(2)], [Keep(3)], [new(0, added.Source, [])], [new(-1, [], [])],
                [new(2, [], [new("caption", [0xff])])],
                [new(2, [], [new("caption", []), new("caption", [])])]
            }) AssertFalse((await Apply(1, candidates)).IsSuccess);
            AssertFalse((await Apply(0, Keep(2))).IsSuccess);
            AssertEqual(1L, session.PresentationRevision); Frame();
            AssertTrue(fixture.Shell.Stage.Overlays.Contains(chrome));
            // A native close cancels its pending nested action. Retention must not reopen the window.
            AssertTrue(fixture.Shell.Renderer.Activate(Node("SidecarDialogClose_" + before.Documents[0].Exposure.ToString("N"))));
            var cancelled = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(SidecarMessageType.Cancel, cancelled.MessageType); AssertEqual(retainedAction.CorrelationId, cancelled.CorrelationId);
            AssertTrue((await Apply(1, Keep(2), Keep(1), Keep(0))).IsSuccess); Frame();
            AssertFalse(fixture.Shell.Stage.Overlays.Contains(chrome));
            var read = adapter.ReadDialogsAsync(session, 2, default).AsTask(); Frame();
            AssertTrue((await read).Dialogs.All(dialog => !dialog.Open));
            await Change(2, 0, true);
            AssertTrue(fixture.Shell.Renderer.Activate(Node("RetainedSubmit")));
            var retiring = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertTrue((await Apply(2, Keep(2))).IsSuccess);
            cancelled = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(SidecarMessageType.Cancel, cancelled.MessageType); AssertEqual(retiring.CorrelationId, cancelled.CorrelationId);
            Frame(); AssertFalse(fixture.Shell.Tree.IsAlive(chrome)); AssertFalse(fixture.Shell.Tree.IsAlive(childDraft));
            await Change(3, 0, true);
            session.Dispose(); await loop.WaitAsync(TimeSpan.FromSeconds(5)); Frame();
            AssertFalse(fixture.Shell.Stage.Overlays.Any(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true));
            AssertFalse((await client.DiscoverAsync()).IsSuccess); AssertEqual(0, warnings.Count);
        }
    }
}
