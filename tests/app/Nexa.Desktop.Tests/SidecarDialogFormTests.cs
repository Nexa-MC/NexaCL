using System.Text;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarDialogFormsRetireWithWindow() => SidecarDialogFormsRetireWithWindowAsync().GetAwaiter().GetResult();
    private static async Task SidecarDialogFormsRetireWithWindowAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
        [
            new(SidecarRegistrationKind.Command, "test.modal.form", 0, SidecarWireCodecs.GeneratedDto),
            PageDeclaration("test.modal.form.body", "#FormModal", SidecarNxmlOperation.Dialog,
                """<Page Key="FormModal"><StackPanel><TextInput Key="ModalOwnDraft" Label="ModalOwnDraft" Height="36" /><Button Key="ModalOwnSave" Content="Save" Height="42" Command="test.modal.form" Argument="own" /><Dynamic Key="ModalChildSlot" /></StackPanel></Page>"""),
            PageDeclaration("test.modal.form.child", "#FormModal/#ModalChildSlot", SidecarNxmlOperation.Append,
                """<StackPanel><TextInput Key="ModalChildDraft" Label="ModalChildDraft" Height="36" /><Button Key="ModalChildSave" Content="Nested" Height="42" Command="test.modal.form" Argument="child" /></StackPanel>"""),
            PageDeclaration("test.modal.form.other", "#OtherModalPage", SidecarNxmlOperation.Page,
                """<Page Key="OtherModalPage"><TextInput Key="UnrelatedSecret" Label="UnrelatedSecret" Height="36" /></Page>""")
        ], features: SidecarFeatures.All, nxml: adapter);
        using (session) using (peer)
        {
            var loop = session.RunReceiveLoopAsync().AsTask();
            fixture.Shell.Renderer.ReducedMotion = true;
            void Frame() => fixture.Shell.Render(new(1000, 750));
            XsrUiEntityId Node(string key) => fixture.Shell.Tree.LiveEntities.Single(node => fixture.Shell.Tree.Name(node) == key);
            async Task Change(bool open)
            {
                var task = adapter.ChangeDialogAsync(session, 0, 0, open, default).AsTask();
                var until = DateTime.UtcNow.AddSeconds(3);
                while (adapter.PendingDialogControls == 0 && DateTime.UtcNow < until) await Task.Delay(1);
                AssertTrue(adapter.PendingDialogControls > 0); Frame(); await task;
            }
            void Emit(string key) => fixture.Intents.Emit(XsrSemanticId.Parse("test.modal.form"), Node(key), XsrCorrelationId.Create());
            Frame();
            Emit("ModalOwnSave"); Emit("ModalChildSave");
            await Change(true); Frame();
            fixture.Shell.Renderer.SetTextInputValue(Node("ModalOwnDraft"), "owner value");
            fixture.Shell.Renderer.SetTextInputValue(Node("ModalChildDraft"), "nested value");
            fixture.Shell.Renderer.SetTextInputValue(Node("UnrelatedSecret"), "must not leak");
            AssertTrue(fixture.Shell.Renderer.Activate(Node("ModalOwnSave")));
            var own = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var ownForm = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(own.Payload.Span).Value.ToArray());
            AssertEqual("own", ownForm.Argument); AssertEqual(1, ownForm.Fields.Length);
            AssertEqual("ModalOwnDraft", ownForm.Fields[0].Key); AssertEqual("owner value", Encoding.UTF8.GetString(ownForm.Fields[0].Utf8Value));
            AssertTrue(fixture.Shell.Renderer.Activate(Node("ModalChildSave")));
            var child = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var childForm = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(child.Payload.Span).Value.ToArray());
            AssertEqual("child", childForm.Argument); AssertEqual(1, childForm.Fields.Length);
            AssertEqual("ModalChildDraft", childForm.Fields[0].Key); AssertEqual("nested value", Encoding.UTF8.GetString(childForm.Fields[0].Utf8Value));

            var hostModal = fixture.Shell.Tree.Create("ProtectedHostModal");
            fixture.Shell.Tree.SetComponent(hostModal, new XsrUiElement()); fixture.Shell.Stage.Show(hostModal, modal: true);
            Emit("ModalOwnSave"); Emit("ModalChildSave");
            fixture.Shell.Stage.Dismiss(hostModal); fixture.Shell.Tree.Destroy(hostModal);
            await Change(false);
            HashSet<SidecarCorrelationId> expected = [own.CorrelationId, child.CorrelationId];
            for (int index = 0; index < 2; index++)
            {
                var cancel = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(SidecarMessageType.Cancel, cancel.MessageType);
                AssertTrue(expected.Remove(cancel.CorrelationId));
            }
            Emit("ModalOwnSave"); Emit("ModalChildSave");
            await Change(true); Frame();
            fixture.Shell.Renderer.SetTextInputValue(Node("ModalChildDraft"), "reopened value");
            AssertTrue(fixture.Shell.Renderer.Activate(Node("ModalChildSave")));
            var reopened = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var reopenedForm = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(reopened.Payload.Span).Value.ToArray());
            AssertEqual("reopened value", Encoding.UTF8.GetString(reopenedForm.Fields.Single().Utf8Value));
            var oldButton = Node("ModalChildSave");
            await session.ReplacePresentationAsync(0, []);
            var retired = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(SidecarMessageType.Cancel, retired.MessageType); AssertEqual(reopened.CorrelationId, retired.CorrelationId);
            Frame(); AssertFalse(fixture.Shell.Tree.IsAlive(oldButton));
            AssertEqual(0, warnings.Count);
            session.Dispose(); await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
