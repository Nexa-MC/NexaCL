using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginCatalogProviders() => PluginCatalogProvidersAsync().GetAwaiter().GetResult();
    private static async Task PluginCatalogProvidersAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands);
        var confirmation = new PluginAdapterConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "catalog-provider-storage"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new CatalogPluginEntry("example.catalog"); var consumer = new CatalogPluginEntry("example.consumer");
        foreach (var report in await app.Runtime.LoadAsync([entry.Source(), consumer.Source()])) AssertEqual(LoadState.Active, report.State);
        using var api = consumer.Context!.AsAbi100(); using var other = entry.Context!.AsAbi100();
        var options = CatalogPluginEntry.Options;
        var providers = await api.Catalog.ListProviders(new(null, 16), options()); AssertTrue(providers.Success); AssertEqual(3, providers.Value.Items.Length);
        AssertTrue((await api.Catalog.DescribeProvider(new(CatalogPluginEntry.Provider), options())).Success);
        var query = new CatalogSearch([CatalogPluginEntry.Provider], "query", null, null, null, new(null, 1));
        var first = await api.Catalog.Search(query, options()); if (!first.Success) throw new InvalidOperationException("Catalog search: " + first.Error); AssertEqual("first", first.Value.Items.Single().Resource.ProjectId);
        var second = await api.Catalog.Search(query with { Page = new(first.Value.NextCursor, 1) }, options()); AssertTrue(second.Success); AssertEqual("second", second.Value.Items.Single().Resource.ProjectId);
        AssertFalse((await api.Catalog.Search(query with { Query = "other", Page = new(first.Value.NextCursor, 1) }, options())).Success);
        AssertFalse((await other.Catalog.Search(query with { Page = new(first.Value.NextCursor, 1) }, options())).Success);
        AssertFalse((await api.Catalog.Search(query with { Providers = [CatalogPluginEntry.Provider, new("nexa:modrinth")] }, options())).Success);
        var resource = first.Value.Items[0].Resource;
        AssertTrue((await api.Catalog.ReadProject(resource, options())).Success);
        var versions = await api.Catalog.ListVersions(new(resource, null, new(null, 16)), options()); AssertTrue(versions.Success);
        var version = versions.Value.Items.Single().Version;
        AssertTrue((await api.Catalog.ReadVersion(version, options())).Success);
        AssertEqual("changes", (await api.Catalog.ReadChangelog(version, options())).Value.Text);
        AssertEqual(12L, (await api.Catalog.ResolveFiles(version, options())).Value.Items.Single().Size);
        AssertEqual("dependency", (await api.Catalog.ResolveDependencies(version, options())).Value.Items.Single().Version.Resource.ProjectId);
        entry.Forge = true;
        AssertFalse((await api.Catalog.ReadProject(resource, options())).Success);
        AssertFalse((await api.Catalog.ReadVersion(version, options())).Success);
        AssertFalse((await api.Catalog.ResolveFiles(version, options())).Success);
        AssertFalse((await api.Catalog.Search(query, options())).Success);
        await app.Runtime.StopAsync("example.catalog");
        AssertEqual(2, (await api.Catalog.ListProviders(new(null, 16), options())).Value.Items.Length);
        AssertFalse((await api.Catalog.ReadProject(resource, options())).Success);
    }
    private sealed class CatalogPluginEntry(string id) : IPluginBootstrapV1
    {
        internal static readonly QualifiedId Provider = new("example.catalog:catalog");
        internal ABIContext? Context; internal bool Forge; private ulong _callback;
        private static readonly ImmutableArray<string> Permissions = ["nexa.catalog.read", "nexa.catalog.write", "nexa.tasks.write"];
        internal static InvocationOptions Options() => new(Guid.NewGuid(), TimeSpan.FromSeconds(5), default) { IdempotencyKey = Guid.NewGuid().ToString("N") };
        internal PluginLoadSource Source() => new(new(1, id, "1.0.0", 1, new("test", "test"), new([], [], "fixture"), [100], [], [], [], Permissions, []), Permissions, _ => ValueTask.FromResult<IPluginBootstrapV1>(this), () => ValueTask.CompletedTask);
        private CatalogProject Project(ResourceRef resource) => new(Forge ? resource with { Provider = new("nexa:modrinth") } : resource, "Project", "description", new("nexa:resource/mod"), []);
        private CatalogFile File() => new("sample.jar", new(Forge ? "file:///private" : "https://example.test/file.jar"), 12, new string('a', 64));
        private CatalogVersion Version(ResourceVersionRef version) => new(Forge ? version with { VersionId = "forged" } : version, "Version", ["1.21.1"], [new("nexa:loader/fabric")], [File()]);
        public async ValueTask<AbiReply<PreInitResult>> OnPreInitialize(PreInitContext context)
        {
            _callback = context.Callbacks.Register("catalog", async (input, _) =>
            {
                var call = BusinessProviderCall.Read(input); AssertEqual(Provider, call.Provider); AssertEqual("nexa:catalog", call.Kind.Value);
                using var api = Context!.AsAbi100();
                AssertEqual(AbiErrors.STAGE_DENIED, (await api.Tasks.CreateOwned(new(new("example.catalog:effect"), new("effect", []), new("parent"), true), Options())).Error!.Code);
                AbiValue result;
                switch (call.Method)
                {
                    case "Search":
                        var search = CatalogSearch.Read(call.Inputs); var next = search.Page.Cursor is null;
                        if (!next) AssertEqual("provider cursor", search.Page.Cursor);
                        result = Page<CatalogProject>.Write(new([Project(new(Provider, next ? "first" : "second"))], next ? "provider cursor" : null, new("catalog-v1"))); break;
                    case "ReadProject": result = CatalogProject.Write(Project(ResourceRef.Read(call.Inputs))); break;
                    case "ListVersions": result = Page<CatalogVersion>.Write(new([Version(new(CatalogVersionQuery.Read(call.Inputs).Resource, "v1"))], null, new("versions-v1"))); break;
                    case "ReadVersion": result = CatalogVersion.Write(Version(ResourceVersionRef.Read(call.Inputs))); break;
                    case "ReadChangelog": result = TextResult.Write(new("changes")); break;
                    case "ResolveFiles": result = Page<CatalogFile>.Write(new([File()], null, new("files-v1"))); break;
                    case "ResolveDependencies": result = Page<CatalogDependency>.Write(new([new(new(new(Provider, "dependency"), "v1"), true, false)], null, new("dependencies-v1"))); break;
                    default: throw new InvalidOperationException(call.Method);
                }
                AssertTrue(Abi100Schemas.Validate(call.InputSchema, call.Inputs)); AssertTrue(Abi100Schemas.Validate(call.OutputSchema, result));
                return AbiReply<AbiValue>.Ok(result);
            }).Value;
            var handle = await context.Negotiator.GetABIContext(100, Options()); AssertTrue(handle.Success);
            var made = await new Abi100Draft(handle.Value).Require("nexa.catalog/100").Require("nexa.tasks/100").Make(context.Negotiator, Guid.NewGuid(), Permissions, default); AssertTrue(made.Success);
            Context = made.Value; return AbiReply<PreInitResult>.Ok(new(Context, [], [], [], []));
        }
        public async ValueTask<AbiReply<Unit>> OnInitialize(ABIContext context, StageContext stage)
        {
            if (id == "example.catalog")
            {
                using var api = context.ForStage100(stage);
                var registered = await api.Catalog.RegisterProvider(new(Provider, new("nexa:catalog"), 100, Abi100Schemas.BusinessProviderCall.Digest, new("Example", []), ["Search", "ReadProject", "ListVersions", "ReadVersion", "ReadChangelog", "ResolveFiles", "ResolveDependencies"], ["application"], SelectionPolicy.ExplicitRequest, ConcurrencyPolicy.Parallel, 5000, ProviderFailurePolicy.FailOperation, Abi100Schemas.Empty, _callback), Options());
                AssertTrue(registered.Success);
                AssertEqual(2, (await api.Catalog.ListProviders(new(null, 16), Options())).Value.Items.Length);
            }
            return AbiReply<Unit>.Ok(new());
        }
        public ValueTask<AbiReply<Unit>> OnPostInitialize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<CheckupResult>> OnCheckup(ABIContext context, StageContext stage) => ValueTask.FromResult(AbiReply<CheckupResult>.Ok(new(CheckupStatus.Passed, [], [])));
        public ValueTask<AbiReply<Unit>> OnFinalize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<Unit>> OnActivated(ABIContext context, InvocationContext invocation) => Ok();
        public ValueTask<AbiReply<Unit>> OnStop(StopContext context) => Ok();
        public ValueTask<AbiReply<Unit>> OnAbort(AbortContext context) => Ok();
        private static ValueTask<AbiReply<Unit>> Ok() => ValueTask.FromResult(AbiReply<Unit>.Ok(new()));
    }
}
