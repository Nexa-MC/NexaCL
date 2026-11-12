using Nexa.Services.Composition.Sidecars;
using Nexa.Services.Minecraft;
using Nexa.Services.Resources;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;
using ClientData = Nexa.Sidecar.Host.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarBusinessAbi() => SidecarBusinessAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarBusinessAbiAsync()
    {
        bool allowed = true;
        bool allowState = true;
        bool corruptDiscovery = false;
        var caller = new SidecarHostCaller("example.general-sidecar", Guid.NewGuid());
        var api = new SidecarBusinessApi((source, capability) => allowed && source == caller && (capability != "host.state.read" || allowState));
        var states = new XsrStateStoreBuilder();
        MinecraftLibraryContract.DeclareState(states);
        var secretKey = XsrSemanticId.Parse("accounts.test.private-secret");
        states.Cell<string>(secretKey, "test");
        var store = states.Build();
        store.Publish(store.Resolve(secretKey), "credential-must-not-cross-boundary");
        api.BindState(store);
        var observer = new NoopDispatchObserver();
        AssertEqual("AccountLoginStartCommand", new ClientData.AccountLoginStartCommand(
            ClientData.AccountLoginProvider.ThirdParty, Password: "never-print-this-secret").ToString());
        string? selected = null;
        XsrCommandRouterBuilder commands = new();
        commands.Register<MinecraftLibrarySelectCommand>(MinecraftLibraryRoutes.Select, (request, _) =>
        {
            if (request.InstanceId == "conflict") return ValueTask.FromResult(XsrResult.Failure(new(XsrErrorKind.Rejected,
                XsrSemanticId.Parse("minecraft.library.revision_conflict"), "Revision changed.")));
            selected = request.RootDirectory + "/" + request.InstanceId;
            return ValueTask.FromResult(XsrResult.Success());
        });
        api.Bind("library", commands.Build(observer), null);
        XsrQueryRouterBuilder queries = new();
        queries.Register<ResourceFavoritesQuery, ResourceFavoritesSnapshot>(ResourceCatalogContract.Favorites,
            (_, _) => ValueTask.FromResult(XsrResult.Success(new ResourceFavoritesSnapshot([]) { Revision = 42 })));
        api.Bind("catalog", null, queries.Build(observer));

        SidecarHostRequestClient? wire = null;
        using var requests = wire = new(async (frame, token) =>
        {
            var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
            var route = api.Routes.Single(r => r.ContractId == request.ContractId);
            var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                : XsrResult.Failure<SidecarBinaryValue>(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.permission_denied"), "Denied"));
            if (route.ContractId == 255 && result.IsSuccess && corruptDiscovery)
            {
                var bytes = result.Value.ToArray(); bytes[8] = (byte)'?';
                result = XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, bytes));
            }
            AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version,
                route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult, SidecarFrameTraits.None,
                frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                    result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
        });
        requests.Activate(SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests | SidecarFeatures.LargeGeneratedDtos);
        var client = new SidecarBusinessClient(requests, observer);
        AssertFalse((await client.DiscoverAsync()).IsSuccess);
        api.CompleteComposition();
        AssertTrue((await client.DiscoverAsync()).IsSuccess);
        var publicState = await client.ReadStateAsync();
        AssertTrue(publicState.IsSuccess); AssertTrue(publicState.Value!.Library is null);
        AssertEqual(-1L, publicState.Value.AccountsRevision);
        var revision = store.Publish(store.Resolve(MinecraftLibraryContract.StateKey),
            new MinecraftLibrarySnapshot(23, [new("/games")], "/games", [], "", false));
        publicState = await client.ReadStateAsync();
        AssertTrue(publicState.IsSuccess); AssertEqual(revision, publicState.Value!.LibraryRevision);
        AssertEqual(23L, publicState.Value.Library!.Revision); AssertEqual("/games", publicState.Value.Library.RootDirectory);
        store.Publish(store.Resolve(MinecraftLibraryContract.StateKey), new MinecraftLibrarySnapshot(24,
            Enumerable.Range(0, 100).Select(index => new MinecraftLibraryDirectory("/" + new string('a', 1000) + index)).ToArray(),
            "/games", [], "", false));
        publicState = await client.ReadStateAsync();
        AssertTrue(publicState.IsSuccess); AssertEqual(100, publicState.Value!.Library!.Directories.Count);
        allowState = false;
        AssertFalse((await client.ReadStateAsync()).IsSuccess);
        AssertTrue((await client.DiscoverAsync()).IsSuccess);
        AssertEqual("xsr.route_not_found", (await client.ReadStateAsync()).Error!.Code.Value);
        AssertTrue(client.Commands.TryResolve(ClientData.MinecraftLibraryRoutes.Select, out var select));
        var result = await client.Commands.Dispatch(select, new ClientData.MinecraftLibrarySelectCommand("/游戏", "实例一")).Completion;
        AssertTrue(result.IsSuccess); AssertEqual("/游戏/实例一", selected);
        result = await client.Commands.Dispatch(select, new ClientData.MinecraftLibrarySelectCommand("/游戏", "conflict")).Completion;
        AssertFalse(result.IsSuccess); AssertEqual(XsrErrorKind.Rejected, result.Error!.Kind);
        AssertEqual("minecraft.library.revision_conflict", result.Error.Code.Value);
        AssertTrue(client.Queries.TryResolve(ClientData.ResourceCatalogContract.Favorites, out var favorites));
        var snapshot = await client.Queries.QueryAsync<ClientData.ResourceFavoritesQuery, ClientData.ResourceFavoritesSnapshot>(favorites, new());
        AssertTrue(snapshot.IsSuccess); AssertEqual(42L, snapshot.Value!.Revision); AssertEqual(0, snapshot.Value.Projects.Count);
        corruptDiscovery = true;
        AssertEqual(XsrErrorKind.ContractMismatch, (await client.DiscoverAsync()).Error!.Kind);
        AssertEqual("xsr.route_not_found", (await client.Commands.Dispatch(select, new ClientData.MinecraftLibrarySelectCommand("/blocked", "blocked")).Completion).Error!.Code.Value);
        corruptDiscovery = false;
        AssertTrue((await client.DiscoverAsync()).IsSuccess);
        allowed = false;
        result = await client.Commands.Dispatch(select, new ClientData.MinecraftLibrarySelectCommand("/blocked", "blocked")).Completion;
        AssertFalse(result.IsSuccess); AssertEqual("xsr.permission_denied", result.Error!.Code.Value);
        AssertEqual("/游戏/实例一", selected);
        AssertFalse((await client.DiscoverAsync()).IsSuccess);
        result = await client.Commands.Dispatch(select, new ClientData.MinecraftLibrarySelectCommand("/blocked", "blocked")).Completion;
        AssertEqual("xsr.route_not_found", result.Error!.Code.Value);
    }
}
