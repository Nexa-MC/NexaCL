using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using Shortcuts = Nexa.Sidecar.Host.ShortcutControl.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarShortcutAbi() => SidecarShortcutAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarShortcutAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        var fallback = fixture.Shell.Stage.Navigation.Current;
        var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        const string command = "test.shortcuts.invoke";
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
        [
            new(SidecarRegistrationKind.Command, command, 0, SidecarWireCodecs.GeneratedDto),
            new(SidecarRegistrationKind.Command, "test.shortcuts.scalar", 0, SidecarWireCodecs.Utf8String),
            PageDeclaration("test.shortcuts.page", "#ShortcutPage", SidecarNxmlOperation.Page,
                """<Page Key="ShortcutPage"><StackPanel><TextInput Key="ShortcutSecret" Label="Private draft" Height="36" /><Dynamic Key="ShortcutSlot" /></StackPanel></Page>"""),
            PageDeclaration("test.shortcuts.child", "#ShortcutPage/#ShortcutSlot", SidecarNxmlOperation.Append,
                """<StackPanel Key="ShortcutChild" Height="40"><Text Content="Mounted callback" /></StackPanel>"""),
            PageDeclaration("test.shortcuts.modal", "#ShortcutModal", SidecarNxmlOperation.Dialog,
                """<Page Key="ShortcutModal"><Text Content="Modal" /></Page>""")
        ], features: SidecarFeatures.All, nxml: adapter);
        var (legacy, legacyPeer) = await ActivateCaptionPatch(new(), "", declarations: [],
            features: SidecarFeatures.All & ~SidecarFeatures.UiForms, nxml: adapter);
        using (session) using (peer) using (legacy) using (legacyPeer)
        {
            var loop = session.RunReceiveLoopAsync().AsTask();
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarShortcutControlApi(_ => session, new NoopDispatchObserver(), adapter.ReplaceShortcutsAsync);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[4096].Authorize(caller with { SessionId = legacy.SessionId }));
            AssertFalse(routes[4095].Authorize(caller with { PackageName = "Other" }));
            var legacyApi = new SidecarShortcutControlApi(_ => legacy, new NoopDispatchObserver(), adapter.ReplaceShortcutsAsync);
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
            var client = new SidecarShortcutControlClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Shortcuts.SidecarShortcutControlRoutes.Replace, out var replace));
            AssertFalse((await client.Commands.Dispatch(replace, new Shortcuts.HostShortcutSet(0, [])).Completion).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            fixture.Shell.Renderer.ReducedMotion = true;
            void Frame() => fixture.Shell.Render(new(1000, 750));
            XsrUiEntityId Node(string key) => fixture.Shell.Tree.LiveEntities.Single(node => fixture.Shell.Tree.Name(node) == key);
            async Task Set(long revision, params Shortcuts.HostShortcutBinding[] bindings)
            {
                var setting = client.Commands.Dispatch(replace, new Shortcuts.HostShortcutSet(revision, bindings)).Completion;
                await Until(() => adapter.PendingShortcutControls != 0 || setting.IsCompleted);
                Frame(); AssertTrue((await setting).IsSuccess);
            }
            async Task Open(long revision, int index)
            {
                var opening = adapter.ChangePageAsync(session, revision, index, true, default).AsTask();
                Frame(); await opening; Frame();
            }
            async Task<SidecarFrame> Read(SidecarMessageType kind)
            {
                var frame = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(kind, frame.MessageType); return frame;
            }
            async Task Reply(SidecarFrame frame)
            {
                await peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                    frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null)));
                await Until(() => adapter.ActiveShortcutCallbacks == 0);
            }
            Frame();
            var bindings = new Shortcuts.HostShortcutBinding[] { new(0, "Ctrl+K", command, "page"), new(1, "Ctrl+J", command, "child") };
            await Set(0, bindings);
            AssertFalse(adapter.HandleShortcut("Ctrl+K")); // Hidden ordinary pages cannot own keyboard input.
            await Open(0, 0);
            fixture.Shell.Renderer.SetTextInputValue(Node("ShortcutSecret"), "must not cross IPC");
            AssertTrue(adapter.HandleShortcut("ctrl+k"));
            var action = await Read(SidecarMessageType.CommandRequest);
            var form = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(action.Payload.Span).Value.ToArray());
            AssertEqual("page", form.Argument); AssertEqual(0, form.Fields.Length);
            AssertTrue(adapter.HandleShortcut("Ctrl+K")); AssertEqual(1, adapter.ActiveShortcutCallbacks);
            await Set(0, bindings); // Identical replacements retain the in-flight repeat guard.
            AssertTrue(adapter.HandleShortcut("Ctrl+K")); AssertEqual(1, adapter.ActiveShortcutCallbacks);
            await Reply(action);
            AssertTrue(fixture.Shell.Renderer.Focus(Node("ShortcutSecret")));
            AssertFalse(adapter.HandleShortcut("Ctrl+K")); fixture.Shell.Renderer.Focus(default);
            var modal = fixture.Shell.Tree.Create("ShortcutHostModal");
            fixture.Shell.Tree.SetComponent(modal, new XsrUiElement()); fixture.Shell.Stage.Show(modal, modal: true);
            AssertFalse(adapter.HandleShortcut("Ctrl+K")); fixture.Shell.Stage.Dismiss(modal); fixture.Shell.Tree.Destroy(modal);
            var childElement = fixture.Shell.Tree.GetComponent<XsrUiElement>(Node("ShortcutChild"))!;
            childElement.IsVisible = false; AssertFalse(adapter.HandleShortcut("Ctrl+J")); childElement.IsVisible = true;
            AssertTrue(adapter.HandleShortcut("Ctrl+J")); action = await Read(SidecarMessageType.CommandRequest);
            form = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(action.Payload.Span).Value.ToArray());
            AssertEqual("child", form.Argument); AssertEqual(0, form.Fields.Length); await Reply(action);
            foreach (var invalid in new Shortcuts.HostShortcutBinding[][]
            {
                [new(3, "Ctrl+K", command, "foreign index")], [new(2, "Ctrl+K", command, "modal")],
                [new(0, "Ctrl+K", "host.only.command", "host")], [new(0, "Ctrl+K", "test.shortcuts.scalar", "scalar")],
                [new(0, "Ctrl+K", command, "a"), new(0, "ctrl+k", command, "b")],
                [new(0, "\n", command, "control")], [new(0, "Ctrl+K", command, new('x', 32769))],
                Enumerable.Range(0, 65).Select(index => new Shortcuts.HostShortcutBinding(0, "Key" + index, command, "a")).ToArray()
            }) AssertFalse((await client.Commands.Dispatch(replace, new Shortcuts.HostShortcutSet(0, invalid)).Completion).IsSuccess);
            AssertTrue(adapter.HandleShortcut("Ctrl+K")); action = await Read(SidecarMessageType.CommandRequest);
            var original = session.CapturePresentation(0);
            await session.ReconcilePresentationAsync(0,
                [new(1, null, new Dictionary<string, string>()), new(2, null, new Dictionary<string, string>()), new(0, null, new Dictionary<string, string>())]);
            Frame(); AssertEqual(original.Documents[0].Exposure, session.CapturePresentation(1).Documents[2].Exposure);
            AssertTrue(adapter.HandleShortcut("Ctrl+K")); AssertEqual(1, adapter.ActiveShortcutCallbacks);
            await Reply(action);
            AssertTrue(adapter.HandleShortcut("Ctrl+J")); action = await Read(SidecarMessageType.CommandRequest);
            await Set(1, new Shortcuts.HostShortcutBinding(2, "Ctrl+K", command, "page"));
            var cancelled = await Read(SidecarMessageType.Cancel); AssertEqual(action.CorrelationId, cancelled.CorrelationId);
            await Until(() => adapter.ActiveShortcutCallbacks == 0);
            AssertFalse(adapter.HandleShortcut("Ctrl+J"));
            // Caller data is copied before awaiting a UI frame.
            HostShortcutBinding[] mutable = [new(2, "Ctrl+M", command, "original")];
            var copied = adapter.ReplaceShortcutsAsync(session, new(1, mutable), default).AsTask();
            mutable[0] = new(2, "Ctrl+M", command, "changed"); Frame(); await copied;
            AssertTrue(adapter.HandleShortcut("Ctrl+M")); action = await Read(SidecarMessageType.CommandRequest);
            form = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(action.Payload.Span).Value.ToArray());
            AssertEqual("original", form.Argument); await Reply(action);
            // A model revision invalidates queued controls but preserves installed bindings.
            var stale = adapter.ReplaceShortcutsAsync(session, new(1, []), default).AsTask();
            await session.UpdatePresentationModelsAsync(1, new Dictionary<int, IReadOnlyDictionary<string, string>> { [2] = new Dictionary<string, string>() });
            Frame(); bool rejected = false;
            try { await stale; } catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected); AssertTrue(adapter.HandleShortcut("Ctrl+M")); action = await Read(SidecarMessageType.CommandRequest);
            await session.ReplacePresentationAsync(2, []);
            cancelled = await Read(SidecarMessageType.Cancel); AssertEqual(action.CorrelationId, cancelled.CorrelationId);
            Frame(); await Until(() => adapter.ActiveShortcutCallbacks == 0); AssertFalse(adapter.HandleShortcut("Ctrl+M"));
            AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            using var cancel = new CancellationTokenSource();
            var pending = Enumerable.Range(0, 8).Select(_ => adapter.ReplaceShortcutsAsync(session, new(3, []), cancel.Token).AsTask()).ToArray();
            AssertEqual(8, adapter.PendingShortcutControls); bool full = false;
            try { await adapter.ReplaceShortcutsAsync(session, new(3, []), default); } catch (InvalidOperationException) { full = true; }
            AssertTrue(full); cancel.Cancel();
            foreach (var task in pending) { try { await task; } catch (OperationCanceledException) { } AssertTrue(task.IsCanceled); }
            AssertEqual(0, adapter.PendingShortcutControls);
            var detached = adapter.ReplaceShortcutsAsync(session, new(3, []), default).AsTask();
            view.Dispose(); try { await detached; } catch (OperationCanceledException) { }
            AssertTrue(detached.IsCanceled); AssertEqual(0, adapter.PendingShortcutControls);
            AssertFalse(adapter.HandleShortcut("Ctrl+M")); AssertEqual(0, warnings.Count);
            session.Dispose(); await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        static async Task Until(Func<bool> done)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            while (!done() && DateTime.UtcNow < end) await Task.Delay(1);
            AssertTrue(done());
        }
    }
}
