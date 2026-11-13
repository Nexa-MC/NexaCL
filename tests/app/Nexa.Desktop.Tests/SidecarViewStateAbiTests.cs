using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using View = Nexa.Sidecar.Host.ViewState.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarViewStateAbi() => SidecarViewStateAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarViewStateAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All, nxml: adapter);
        using (session) using (peer)
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarViewStateApi(_ => session, new NoopDispatchObserver());
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[1026].Authorize(caller with { SessionId = Guid.NewGuid() }));
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
            var client = new SidecarViewStateClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(View.SidecarViewStateRoutes.Replace, out var replace));
            AssertFalse((await client.Commands.Dispatch(replace, new View.HostViewReplace(0, [], [])).Completion).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            byte[] Document(string target, SidecarNxmlOperation operation, string source) => SidecarNxmlDocument.Encode(new(target, operation,
                "main.nxml", new Dictionary<string, string> { ["main.nxml"] = source }));
            var documents = new[]
            {
                Document("#navigation-toggle", SidecarNxmlOperation.Before,
                    """<NavigationItem Key="ViewNav" Label="View" Content="View" Command="ui.sidecar.page.open" Argument="#ViewPage" />"""),
                Document("#ViewPage", SidecarNxmlOperation.Page, "<!--" + new string('x', 40000) + "-->" +
                    """<Page Key="ViewPage"><StackPanel><Text Key="ViewTitle" Content="{data title}" /><TextInput Key="ViewDraft" Label="Draft" Height="32" /></StackPanel></Page>""")
            };
            View.HostViewValue Value(string key, string value) => new(key, Encoding.UTF8.GetBytes(value));
            AssertTrue((await client.Commands.Dispatch(replace, new View.HostViewReplace(0, documents,
                [new(1, [Value("title", "Initial title"), Value("large", new string('z', 40000))])])).Completion).IsSuccess);
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "ViewNav").Entity));
            scene = fixture.Shell.Render(new(1000, 650));
            var page = fixture.Shell.Stage.Navigation.Current;
            var input = FindByKey(fixture.Shell, scene, "ViewDraft").Entity;
            fixture.Shell.Renderer.SetTextInputValue(input, "unsaved draft");
            var exposure = fixture.Store.Read<XsrNxmlModuleSnapshot>(adapter.State).Value!.Modules[1].Exposure;
            AssertTrue(client.Commands.TryResolve(View.SidecarViewStateRoutes.Models, out var models));
            AssertTrue((await client.Commands.Dispatch(models, new View.HostViewModelsUpdate(1, [new(1, [Value("title", "Updated title")])])).Completion).IsSuccess);
            scene = fixture.Shell.Render(new(1000, 650));
            AssertEqual(page, fixture.Shell.Stage.Navigation.Current);
            AssertEqual(input, FindByKey(fixture.Shell, scene, "ViewDraft").Entity);
            AssertEqual("unsaved draft", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft());
            AssertTrue(scene.Nodes.Any(node => node.Text == "Updated title"));
            AssertEqual(exposure, fixture.Store.Read<XsrNxmlModuleSnapshot>(adapter.State).Value!.Modules[1].Exposure);
            AssertFalse((await client.Commands.Dispatch(models, new View.HostViewModelsUpdate(2, [new(1, [new("title", [255])])])).Completion).IsSuccess);
            AssertFalse((await client.Commands.Dispatch(models, new View.HostViewModelsUpdate(2, [new(1, []), new(1, [])])).Completion).IsSuccess);
            AssertFalse((await client.Commands.Dispatch(models, new View.HostViewModelsUpdate(1, [new(1, [Value("title", "Stale")])])).Completion).IsSuccess);
            AssertEqual(2L, session.PresentationRevision);
            AssertEqual(0, warnings.Count);
            session.Dispose();
            AssertFalse((await client.DiscoverAsync()).IsSuccess);
        }
    }
}
