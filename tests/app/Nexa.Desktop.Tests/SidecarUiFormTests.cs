using System.Text;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarFormsCaptureOnlyInvokingExposure() => SidecarFormsCaptureOnlyInvokingExposureAsync().GetAwaiter().GetResult();
    private static async Task SidecarFormsCaptureOnlyInvokingExposureAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        var fallback = fixture.Shell.Stage.Navigation.Current;
        List<string> warnings = [];
        using var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
        [
            new(SidecarRegistrationKind.Command, "test.form", 0, SidecarWireCodecs.GeneratedDto),
            PageDeclaration("test.form.nav", "#navigation-toggle", SidecarNxmlOperation.Before,
                """<NavigationItem Key="FormNav" Label="Form" Content="Form" Command="ui.sidecar.page.open" Argument="#FormPage" />"""),
            PageDeclaration("test.form.page", "#FormPage", SidecarNxmlOperation.Page,
                """<Page Key="FormPage"><StackPanel><TextInput Key="OwnDraft" Label="Owned" Height="32" /><Dynamic Key="ForeignSlot" /><Button Key="FormSave" Content="Save" Height="44" Command="test.form" Argument="save" /></StackPanel></Page>"""),
            PageDeclaration("test.form.other", "#OtherFormPage", SidecarNxmlOperation.Page,
                """<Page Key="OtherFormPage"><TextInput Key="OtherPageSecret" Label="Other" Height="32" /></Page>"""),
            PageDeclaration("test.form.mount", "#FormPage/#ForeignSlot", SidecarNxmlOperation.Append,
                """<TextInput Key="ForeignDraft" Label="Foreign" Height="32" />""")
        ], features: SidecarFeatures.All, nxml: adapter);
        using (session) using (peer)
        {
            var loop = session.RunReceiveLoopAsync().AsTask();
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "FormNav").Entity));
            scene = fixture.Shell.Render(new(1000, 650));
            var input = FindByKey(fixture.Shell, scene, "OwnDraft").Entity;
            var other = FindByKey(fixture.Shell, scene, "ForeignDraft").Entity;
            fixture.Shell.Renderer.SetTextInputValue(input, "user draft");
            fixture.Shell.Renderer.SetTextInputValue(other, "not this document");
            var button = FindByKey(fixture.Shell, scene, "FormSave").Entity;
            AssertTrue(fixture.Shell.Renderer.Activate(button));
            var frame = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
            AssertEqual(1u, request.ContractId); AssertEqual(SidecarWireCodecs.GeneratedDto, request.Value.CodecId);
            var form = SidecarUiForms.Decode(request.Value.ToArray());
            AssertEqual("save", form.Argument); AssertEqual(1, form.Fields.Length);
            AssertEqual("OwnDraft", form.Fields[0].Key); AssertEqual("user draft", Encoding.UTF8.GetString(form.Fields[0].Utf8Value));
            await peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null)));
            var page = fixture.Shell.Stage.Navigation.Current;
            fixture.Shell.Stage.Navigation.Replace(fallback);
            fixture.Shell.Render(new(1000, 650));
            fixture.Shell.Renderer.SetTextInputValue(input, "hidden draft");
            fixture.Intents.Emit(XsrSemanticId.Parse("test.form"), button, XsrCorrelationId.Create());
            fixture.Shell.Stage.Navigation.Replace(page);
            fixture.Shell.Render(new(1000, 650));
            fixture.Shell.Renderer.SetTextInputValue(input, "active second draft");
            // Replace retires the old exposure while a second command is in flight.
            AssertTrue(fixture.Shell.Renderer.Activate(button));
            frame = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var second = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span).Value.ToArray());
            AssertEqual("active second draft", Encoding.UTF8.GetString(second.Fields.Single().Utf8Value));
            var exposure = fixture.Store.Read<XsrNxmlModuleSnapshot>(adapter.State).Value!.Modules.Single(m => m.Source.Target == "#FormPage").Exposure;
            await session.ReplacePresentationAsync(0, []);
            var cancel = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(SidecarMessageType.Cancel, cancel.MessageType); AssertEqual(frame.CorrelationId, cancel.CorrelationId);
            AssertFalse((await adapter.Runtime.DispatchFormAsync(exposure, XsrSemanticId.Parse("test.form"), "save", new Dictionary<string, string>())).IsSuccess);
            fixture.Shell.Render(new(1000, 650));
            AssertFalse(fixture.Shell.Tree.IsAlive(button)); AssertEqual(0, warnings.Count);
            session.Dispose(); await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
