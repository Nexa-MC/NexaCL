using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using Dialogs = Nexa.Sidecar.Host.DialogControl.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarDialogControlAbi() => SidecarDialogControlAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarDialogControlAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        var fallback = fixture.Shell.Stage.Navigation.Current;
        var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
        [
            new(SidecarRegistrationKind.State, "caption", 0, 0),
            PageDeclaration("test.ordinary", "#Ordinary", SidecarNxmlOperation.Page, """<Page Key="Ordinary"><Text Content="Page" /></Page>"""),
            PageDeclaration("test.modal", "#ModalOne", SidecarNxmlOperation.Dialog,
                """<Page Key="ModalOne"><StackPanel><TextInput Key="ModalDraft" Label="Draft" Height="40" /><Text Content="{data caption}" Height="30" /></StackPanel></Page>"""),
            PageDeclaration("test.second", "#ModalTwo", SidecarNxmlOperation.Dialog, """<Page Key="ModalTwo"><Text Content="Second" /></Page>""")
        ], features: SidecarFeatures.All, initialState: [(1, Encoding.UTF8.GetBytes("initial"))], nxml: adapter);
        var (other, otherPeer) = await ActivateCaptionPatch(new(), "", declarations:
            [PageDeclaration("test.foreign", "#ForeignModal", SidecarNxmlOperation.Dialog, """<Page Key="ForeignModal"><Text Content="Foreign" /></Page>""")],
            features: SidecarFeatures.All, nxml: adapter);
        var (legacy, legacyPeer) = await ActivateCaptionPatch(new(), "", declarations: [],
            features: SidecarFeatures.All & ~SidecarFeatures.Dialogs, nxml: adapter);
        using (session) using (peer) using (other) using (otherPeer) using (legacy) using (legacyPeer)
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarDialogControlApi(_ => session, new NoopDispatchObserver(), adapter.ReadDialogsAsync, adapter.ChangeDialogAsync);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[3585].Authorize(caller with { SessionId = other.SessionId }));
            AssertFalse(routes[3584].Authorize(caller with { PackageName = "Other" }));
            var legacyApi = new SidecarDialogControlApi(_ => legacy, new NoopDispatchObserver(), adapter.ReadDialogsAsync, adapter.ChangeDialogAsync);
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
            var client = new SidecarDialogControlClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Dialogs.SidecarDialogControlRoutes.Open, out var open));
            AssertTrue(client.Commands.TryResolve(Dialogs.SidecarDialogControlRoutes.Close, out var close));
            AssertTrue(client.Queries.TryResolve(Dialogs.SidecarDialogControlRoutes.State, out var read));
            AssertFalse((await client.Commands.Dispatch(open, new Dialogs.HostDialogChange(0, 1)).Completion).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            fixture.Shell.Renderer.ReducedMotion = true;
            void Frame()
            {
                fixture.Shell.Render(new(1000, 750));
                if (warnings.Count != 0) throw new InvalidOperationException(string.Join("; ", warnings));
            }
            async Task Present()
            {
                var until = DateTime.UtcNow.AddSeconds(3);
                while (adapter.PendingDialogControls == 0 && DateTime.UtcNow < until) await Task.Delay(1);
                AssertTrue(adapter.PendingDialogControls > 0); Frame();
            }
            Frame();
            var state = client.Queries.QueryAsync<Dialogs.HostDialogQuery, Dialogs.HostDialogState>(read, new(0)).AsTask();
            await Present(); var initial = await state;
            AssertTrue(initial.IsSuccess); AssertEqual(2, initial.Value.Dialogs.Length);
            AssertTrue(initial.Value.Dialogs.All(dialog => !dialog.Open));
            AssertFalse((await client.Commands.Dispatch(open, new Dialogs.HostDialogChange(0, 0)).Completion).IsSuccess);
            AssertFalse((await client.Commands.Dispatch(open, new Dialogs.HostDialogChange(1, 1)).Completion).IsSuccess);
            var opening = client.Commands.Dispatch(open, new Dialogs.HostDialogChange(0, 1)).Completion;
            await Present(); AssertTrue((await opening).IsSuccess);
            AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            AssertEqual(1, fixture.Shell.Stage.Overlays.Count(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true));
            var chrome = fixture.Shell.Stage.Overlays.Single(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true);
            var scene = fixture.Shell.Render(new(1000, 750));
            var input = FindByKey(fixture.Shell, scene, "ModalDraft").Entity;
            fixture.Shell.Renderer.SetTextInputValue(input, "preserve this draft");
            var foreignClose = adapter.ChangeDialogAsync(other, 0, 0, false, default).AsTask();
            await Present(); await foreignClose; AssertTrue(fixture.Shell.Stage.Overlays.Contains(chrome));

            var hostModal = fixture.Shell.Tree.Create("HostModalPriority");
            fixture.Shell.Tree.SetComponent(hostModal, new XsrUiElement()); fixture.Shell.Stage.Show(hostModal, modal: true);
            opening = client.Commands.Dispatch(open, new Dialogs.HostDialogChange(0, 2)).Completion;
            await Present(); AssertFalse((await opening).IsSuccess);
            var exposure = session.CapturePresentation(0).Documents.Single(document => document.Index == 1).Exposure;
            var closeNode = fixture.Shell.Tree.LiveEntities.Single(node => fixture.Shell.Tree.Name(node) == "SidecarDialogClose_" + exposure.ToString("N"));
            fixture.Intents.Emit(XsrSemanticId.Parse("ui.sidecar.dialog.close"), closeNode, XsrCorrelationId.Create());
            AssertTrue(fixture.Shell.Stage.Overlays.Contains(chrome));
            fixture.Shell.Stage.Dismiss(hostModal); fixture.Shell.Tree.Destroy(hostModal); Frame();
            var compact = fixture.Shell.Render(new(360, 280));
            var closeRect = compact.Nodes.Single(node => node.Entity == closeNode).Rect;
            AssertTrue(closeRect.X >= 0 && closeRect.Y >= 0 && closeRect.X + closeRect.Width <= 360 && closeRect.Y + closeRect.Height <= 280);
            Frame();


            await session.UpdatePresentationModelsAsync(0, new Dictionary<int, IReadOnlyDictionary<string, string>>
            { [1] = new Dictionary<string, string> { ["caption"] = "updated" } });
            Frame();
            AssertEqual("preserve this draft", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft());
            AssertTrue(fixture.Shell.Render(new(1000, 750)).Nodes.Any(node => node.Text == "updated"));
            AssertTrue(fixture.Shell.Stage.Overlays.Contains(chrome));
            AssertTrue(fixture.Shell.Renderer.Activate(closeNode)); Frame();
            state = client.Queries.QueryAsync<Dialogs.HostDialogQuery, Dialogs.HostDialogState>(read, new(1)).AsTask();
            await Present(); AssertFalse((await state).Value.Dialogs.Single(dialog => dialog.Document == 1).Open);
            opening = client.Commands.Dispatch(open, new Dialogs.HostDialogChange(1, 1)).Completion;
            await Present(); AssertTrue((await opening).IsSuccess);
            var closing = client.Commands.Dispatch(close, new Dialogs.HostDialogChange(1, 1)).Completion;
            await Present(); AssertTrue((await closing).IsSuccess);

            using var cancel = new CancellationTokenSource();
            var queued = Enumerable.Range(0, 8).Select(_ => adapter.ReadDialogsAsync(session, 1, cancel.Token).AsTask()).ToArray();
            AssertEqual(8, adapter.PendingDialogControls);
            bool full = false;
            try { await adapter.ReadDialogsAsync(session, 1, default); } catch (InvalidOperationException) { full = true; }
            AssertTrue(full); cancel.Cancel();
            foreach (var pending in queued) { try { await pending; } catch (OperationCanceledException) { } AssertTrue(pending.IsCanceled); }
            AssertEqual(0, adapter.PendingDialogControls);
            var stale = adapter.ChangeDialogAsync(session, 1, 1, true, default).AsTask();
            await session.UpdatePresentationModelsAsync(1, new Dictionary<int, IReadOnlyDictionary<string, string>> { [1] = new Dictionary<string, string>() });
            Frame(); bool rejected = false;
            try { await stale; } catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected);
            opening = client.Commands.Dispatch(open, new Dialogs.HostDialogChange(2, 1)).Completion;
            await Present(); AssertTrue((await opening).IsSuccess);
            var retired = adapter.ReadDialogsAsync(session, 2, default).AsTask();
            await session.ReplacePresentationAsync(2, []);
            try { await retired; } catch (OperationCanceledException) { }
            AssertTrue(retired.IsCanceled); Frame();
            AssertFalse(fixture.Shell.Tree.IsAlive(chrome));
            AssertFalse(fixture.Shell.Stage.Overlays.Any(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true));

            bool expired = false;
            try { await adapter.ReadDialogsAsync(other, 0, default); } catch (TimeoutException) { expired = true; }
            AssertTrue(expired); AssertEqual(0, adapter.PendingDialogControls);
            var foreignOpen = adapter.ChangeDialogAsync(other, 0, 0, true, default).AsTask();
            await Present(); await foreignOpen;
            var foreignChrome = fixture.Shell.Stage.Overlays.Single(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true);
            var detached = adapter.ReadDialogsAsync(other, 0, default).AsTask();
            view.Dispose();
            try { await detached; } catch (OperationCanceledException) { }
            AssertTrue(detached.IsCanceled); AssertEqual(0, adapter.PendingDialogControls);
            AssertFalse(fixture.Shell.Tree.IsAlive(foreignChrome));
            AssertEqual(0, warnings.Count);
            session.Dispose(); AssertFalse((await client.DiscoverAsync()).IsSuccess);
        }
    }
}
