using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Presentation = Nexa.Sidecar.Host.Presentation.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarPresentationAbi() => SidecarPresentationAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarPresentationAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All, nxml: adapter);
        using (session) using (peer)
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarPresentationApi(_ => session, new NoopDispatchObserver());
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[767].Authorize(caller with { SessionId = Guid.NewGuid() }));
            AssertFalse(routes[769].Authorize(caller with { PackageName = "different.package" }));
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
            var client = new SidecarPresentationClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Presentation.SidecarPresentationRoutes.Replace, out var replace));
            AssertFalse((await client.Commands.Dispatch(replace, new Presentation.HostPresentationReplace(0, [])).Completion).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            AssertTrue(client.Queries.TryResolve(Presentation.SidecarPresentationRoutes.Revision, out var revision));
            var initial = await client.Queries.QueryAsync<Presentation.HostPresentationEmpty, Presentation.HostPresentationRevision>(revision, new());
            AssertTrue(initial.IsSuccess); AssertEqual(0L, initial.Value!.Revision);
            Presentation.HostPresentationDocument Document(string target, Presentation.SidecarNxmlOperation operation, string source) =>
                new(target, operation, "main.nxml", [new("main.nxml", source)], "1");
            var documents = new[]
            {
                Document("#navigation-toggle", Presentation.SidecarNxmlOperation.Before,
                    """<NavigationItem Key="DynamicNav" Label="Dynamic" Content="Dynamic" Command="ui.sidecar.page.open" Argument="#DynamicPage" />"""),
                Document("#DynamicPage", Presentation.SidecarNxmlOperation.Page,
                    """<Page Key="DynamicPage"><Text Content="Dynamic content" /></Page>""")
            };
            AssertTrue((await client.Commands.Dispatch(replace, new Presentation.HostPresentationReplace(0, documents)).Completion).IsSuccess);
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "DynamicNav").Entity));
            scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(scene.Nodes.Any(node => node.Text == "Dynamic content"));
            AssertFalse((await client.Commands.Dispatch(replace, new Presentation.HostPresentationReplace(0, [])).Completion).IsSuccess);
            AssertFalse((await client.Commands.Dispatch(replace, new Presentation.HostPresentationReplace(1,
                [Document("#Broken", Presentation.SidecarNxmlOperation.Page, "<MissingRecipe />")])).Completion).IsSuccess);
            AssertEqual(1L, session.PresentationRevision);
            AssertEqual(2, fixture.Store.Read<XsrNxmlModuleSnapshot>(adapter.State).Value!.Modules.Count);
            AssertTrue((await client.Commands.Dispatch(replace, new Presentation.HostPresentationReplace(1, [])).Completion).IsSuccess);
            scene = fixture.Shell.Render(new(1000, 650));
            AssertFalse(scene.Nodes.Any(node => node.Text == "Dynamic content"));
            session.Dispose();
            AssertFalse((await client.DiscoverAsync()).IsSuccess);
            AssertFalse((await client.Commands.Dispatch(replace, new Presentation.HostPresentationReplace(2, documents)).Completion).IsSuccess);
            AssertEqual(0, warnings.Count);
        }
    }
}
