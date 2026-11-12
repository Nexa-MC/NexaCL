using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Resources;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class PluginCatalogSource : IResourceCatalogSource, IResourceChangelogSource
    {
        private static ResourceProject Project(string id) => new(id, "Project " + id, "Literal <summary>", "fixture", 42, "https://modrinth.com/project/" + id)
        { Sources = [new(ResourceProvider.Modrinth, id)], Kind = ResourceKind.Mod };
        public Task<ResourceSearchResult> SearchAsync(ResourceSearchQuery query, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new ResourceSearchResult([Project("first"), Project("second")], 2, query.Page) { HasMore = false }); }
        public Task<ResourceDetail> DetailAsync(ResourceDetailQuery query, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); return Task.FromResult(new ResourceDetail(Project(query.ProjectId), "MIT", [
                new("v1", "Version one", "1", "release", ["1.21.1"], ["fabric"], "2026-11-12", "https://modrinth.com")
                { Provider = ResourceProvider.Modrinth, ProjectId = query.ProjectId, File = new("fixture.jar", "https://cdn.modrinth.com/fixture.jar", 42, null, "provider-sha512"), Dependencies = [new("dependency", null, "required")] }
            ]));
        }
        public Task<ResourceChangelog> ReadChangelogAsync(ResourceChangelogQuery query, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new ResourceChangelog("Literal changelog")); }
    }
    private static async Task ExercisePluginCatalog(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke)
    {
        var providers = await invoke("Catalog", "ListProviders", PageRequest.Write(new(null, 16))); AssertTrue(providers.Success);
        AssertEqual(2, Page<ProviderDescriptor>.Read(providers.Value).Items.Length);
        var query = new CatalogSearch([new("nexa:modrinth")], "fixture", null, null, null, new(null, 1));
        var result = await invoke("Catalog", "Search", CatalogSearch.Write(query)); AssertTrue(result.Success); var first = Page<CatalogProject>.Read(result.Value);
        AssertEqual(1, first.Items.Length); AssertEqual("first", first.Items[0].Resource.ProjectId);
        var secondResult = await invoke("Catalog", "Search", CatalogSearch.Write(query with { Page = new(first.NextCursor, 1) })); AssertTrue(secondResult.Success);
        var second = Page<CatalogProject>.Read(secondResult.Value); AssertEqual("second", second.Items.Single().Resource.ProjectId); AssertEqual<string?>(null, second.NextCursor);
        AssertFalse((await invoke("Catalog", "Search", CatalogSearch.Write(query with { Query = "changed", Page = new(first.NextCursor, 1) }))).Success);
        var resource = first.Items[0].Resource;
        var versions = await invoke("Catalog", "ListVersions", CatalogVersionQuery.Write(new(resource, "1.21.1", new(null, 20)))); AssertTrue(versions.Success);
        var version = Page<CatalogVersion>.Read(versions.Value).Items.Single(); AssertEqual("", version.Files.Single().Sha256);
        AssertTrue((await invoke("Catalog", "ReadVersion", ResourceVersionRef.Write(version.Version))).Success);
        AssertFalse((await invoke("Catalog", "ReadVersion", ResourceVersionRef.Write(new(resource, "missing")))).Success);
        var files = await invoke("Catalog", "ResolveFiles", ResourceVersionRef.Write(version.Version)); AssertTrue(files.Success); AssertEqual(42L, Page<CatalogFile>.Read(files.Value).Items.Single().Size);
        var dependencies = await invoke("Catalog", "ResolveDependencies", ResourceVersionRef.Write(version.Version)); AssertTrue(dependencies.Success);
        AssertTrue(Page<CatalogDependency>.Read(dependencies.Value).Items.Single().Required);
        AssertEqual("Literal changelog", TextResult.Read((await invoke("Catalog", "ReadChangelog", ResourceVersionRef.Write(version.Version))).Value).Text);
        var favorites = Page<CatalogProject>.Read((await invoke("Catalog", "ListFavorites", PageRequest.Write(new(null, 20)))).Value); AssertEqual(0, favorites.Items.Length);
        AssertTrue((await invoke("Catalog", "SetFavorite", FavoriteChange.Write(new(resource, true, favorites.Revision)))).Success);
        AssertFalse((await invoke("Catalog", "SetFavorite", FavoriteChange.Write(new(resource, false, favorites.Revision)))).Success);
        favorites = Page<CatalogProject>.Read((await invoke("Catalog", "ListFavorites", PageRequest.Write(new(null, 20)))).Value); AssertEqual(1, favorites.Items.Length);
        AssertTrue((await invoke("Catalog", "SetFavorite", FavoriteChange.Write(new(resource, false, favorites.Revision)))).Success);
    }
}
