using System.Collections.Immutable;
using System.Globalization;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Resources;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private XsrQueryRouter? _catalogQueries;
    private XsrCommandRouter? _catalogCommands;
    public void BindCatalog(XsrCommandRouter commands, XsrQueryRouter queries) { _catalogCommands = commands; _catalogQueries = queries; }
    private static ResourceProvider CatalogProvider(QualifiedId id) => id.Value switch
    { "nexa:modrinth" => ResourceProvider.Modrinth, "nexa:curseforge" => ResourceProvider.CurseForge, _ => throw new AbiHostException(AbiErrors.PROVIDER_UNAVAILABLE) };
    private static QualifiedId CatalogProviderId(ResourceProvider provider) => new(provider switch
    { ResourceProvider.Modrinth => "nexa:modrinth", ResourceProvider.CurseForge => "nexa:curseforge", _ => throw new AbiHostException(AbiErrors.PROVIDER_UNAVAILABLE) });
    private static ResourceReference CatalogReference(ResourceRef reference)
    {
        if (reference.ProjectId.Length is < 1 or > 256 || reference.ProjectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return new(CatalogProvider(reference.Provider), reference.ProjectId);
    }
    private static ResourceKind CatalogKind(QualifiedId? kind) => kind?.Value switch
    {
        null or "nexa:resource/mod" => ResourceKind.Mod,
        "nexa:resource/modpack" => ResourceKind.Modpack,
        "nexa:resource/resourcepack" => ResourceKind.ResourcePack,
        "nexa:resource/shader" => ResourceKind.Shader,
        "nexa:resource/datapack" => ResourceKind.DataPack,
        "nexa:resource/world" => ResourceKind.World,
        _ => throw new AbiHostException(AbiErrors.SCHEMA_INVALID)
    };
    private static string CatalogLoader(QualifiedId? id)
    {
        if (id is null) return "";
        const string prefix = "nexa:loader/";
        if (!id.Value.StartsWith(prefix, StringComparison.Ordinal) || id.Value[prefix.Length..].Contains('/')) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return id.Value[prefix.Length..];
    }
    private static CatalogProject ProjectResource(ResourceProject project, ResourceReference? preferred = null)
    {
        var source = preferred ?? (project.Sources.Count == 0 ? null : project.Sources[0]) ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return new(new(CatalogProviderId(source.Provider), source.ProjectId), project.DisplayName, project.DisplayDescription,
            new("nexa:resource/" + project.Kind.ToString().ToLowerInvariant()), []);
    }
    private static CatalogVersion ProjectVersion(ResourceRef source, ResourceVersion version) => new(new(source, version.Id), version.Name,
        version.Games.ToImmutableArray(), version.Loaders.Select(v => new QualifiedId("nexa:loader/" + v.ToLowerInvariant())).ToImmutableArray(),
        version.File is { } file ? [new(file.Name, new(file.Url), file.Size, "")] : []);
    private async ValueTask<ResourceDetail> CatalogDetail(ResourceRef reference, CancellationToken token, string game = "")
    {
        var source = CatalogReference(reference);
        var result = await Query<ResourceDetailQuery, ResourceDetail>(_catalogQueries, ResourceCatalogContract.Detail,
            new(source.ProjectId, game) { Sources = [source], WaitForRefresh = true }, token).ConfigureAwait(false);
        if (result.IsStale) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return result;
    }
    private async ValueTask<ResourceVersion> CatalogVersion(ResourceVersionRef reference, CancellationToken token)
    {
        if (reference.VersionId.Length is < 1 or > 256 || reference.VersionId.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var detail = await CatalogDetail(reference.Resource, token).ConfigureAwait(false);
        return detail.Versions.FirstOrDefault(v => v.Id == reference.VersionId && v.Provider == CatalogProvider(reference.Resource.Provider)
            && (v.ProjectId.Length == 0 || v.ProjectId == reference.Resource.ProjectId)) ?? throw new AbiHostException(AbiErrors.NOT_FOUND);
    }
    private static ProviderDescriptor CatalogProviderDescriptor(ResourceProvider provider) => new(CatalogProviderId(provider), new("nexa:catalog"), 100,
        Abi100Schemas.CatalogSearch.Digest, new(provider.ToString(), []), ["search", "versions", "files", "dependencies", "favorites"], ["application"],
        SelectionPolicy.ExplicitRequest, ConcurrencyPolicy.Parallel, 30000, ProviderFailurePolicy.Unavailable, Abi100Schemas.Empty, 0);
    private IEnumerable<HostCapabilityRoute> CatalogRoutes => [
        HostRoutes.Typed<ProviderDescriptor, RegistrationRef>("Catalog", "RegisterProvider", (c, v) => ValueTask.FromResult(
            Services.RegisterHostProvider(c, CatalogProviderKind, v, CatalogProviderMethods))),
        HostRoutes.Typed<PageRequest, Page<ProviderDescriptor>>("Catalog", "ListProviders", (_, v) => ValueTask.FromResult(ContentPage(
            new[] { CatalogProviderDescriptor(ResourceProvider.Modrinth), CatalogProviderDescriptor(ResourceProvider.CurseForge) }.Concat(Services.HostProviders(CatalogProviderKind)).ToImmutableArray(), v))),
        HostRoutes.Typed<QualifiedRequest, ProviderDescriptor>("Catalog", "DescribeProvider", (_, v) => ValueTask.FromResult(CustomCatalog(v.Id) ? Services.HostProviders(CatalogProviderKind).FirstOrDefault(p => p.Id == v.Id) ?? throw new AbiHostException(AbiErrors.PROVIDER_UNAVAILABLE) : CatalogProviderDescriptor(CatalogProvider(v.Id)))),
        HostRoutes.Typed<CatalogSearch, Page<CatalogProject>>("Catalog", "Search", async (c, v) =>
        {
            if (v.Query.Length > 1024 || v.GameVersion?.Length > 128 || v.Providers.Length > 2 || v.Providers.Distinct().Count() != v.Providers.Length || v.Page.Limit is < 1 or > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if (v.Providers.Any(CustomCatalog)) return await SearchCatalogProvider(c, v).ConfigureAwait(false);
            foreach (var provider in v.Providers) CatalogProvider(provider);
            string scope = Token(c, "catalog-search:" + ProjectionRevision(v with { Page = new(null, v.Page.Limit) }).Value);
            int sourcePage = 0, offset = 0; string? previous = null;
            if (v.Page.Cursor is { } cursor)
            {
                var parts = cursor.Split(':');
                if (parts.Length != 4 || parts[0] != scope || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out sourcePage)
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || sourcePage is < 0 or > 10000 || offset is < 0 or >= 4096) throw new AbiHostException(AbiErrors.CONFLICT);
                previous = parts[3];
            }
            var result = await Query<ResourceSearchQuery, ResourceSearchResult>(_catalogQueries, ResourceCatalogContract.Search,
                new(CatalogKind(v.Kind), v.Query, v.GameVersion ?? "", CatalogLoader(v.Loader), Page: sourcePage)
                { Provider = v.Providers.Length == 1 ? CatalogProvider(v.Providers[0]) : null, WaitForRefresh = true }, c.Options.Cancellation).ConfigureAwait(false);
            if (result.IsStale || result.Page != sourcePage || result.Projects.Count > 4096) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var all = result.Projects.Select(p => ProjectResource(p)).ToImmutableArray();
            var revision = ProjectionRevision(new Page<CatalogProject>(all, null, new(scope + ":" + sourcePage)));
            if (offset > all.Length || offset > 0 && previous != revision.Value) throw new AbiHostException(AbiErrors.CONFLICT);
            var items = all.Skip(offset).Take((int)v.Page.Limit).ToImmutableArray(); int end = offset + items.Length;
            bool more = result.HasMore ?? (sourcePage + 1) * ResourceCatalogContract.PageSize < result.Total;
            string? next = end < all.Length ? scope + ":" + sourcePage + ":" + end + ":" + revision.Value
                : more ? scope + ":" + (sourcePage + 1) + ":0:next" : null;
            return new(items, next, revision);
        }),
        HostRoutes.Typed<ResourceRef, CatalogProject>("Catalog", "ReadProject", async (c, v) =>
        {
            if (!CustomCatalog(v.Provider)) return ProjectResource((await CatalogDetail(v, c.Options.Cancellation).ConfigureAwait(false)).Project, CatalogReference(v));
            CheckCatalogReference(v, v.Provider);
            var project = CatalogProject.Read(await QueryCatalogProvider(c, v.Provider, "ReadProject", ResourceRef.Write(v)).ConfigureAwait(false));
            CheckCatalogProject(project, v.Provider);
            if (project.Resource != v) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return project;
        }),
        HostRoutes.Typed<CatalogVersionQuery, Page<CatalogVersion>>("Catalog", "ListVersions", async (c, v) =>
        {
            if (CustomCatalog(v.Resource.Provider)) return await CatalogProviderVersions(c, v).ConfigureAwait(false);
            var detail = await CatalogDetail(v.Resource, c.Options.Cancellation, v.GameVersion ?? "").ConfigureAwait(false);
            return ContentPage(detail.Versions.Where(version => version.Provider == CatalogProvider(v.Resource.Provider)).Select(version => ProjectVersion(v.Resource, version)).ToImmutableArray(), v.Page);
        }),
        HostRoutes.Typed<ResourceVersionRef, CatalogVersion>("Catalog", "ReadVersion", async (c, v) =>
        {
            if (!CustomCatalog(v.Resource.Provider)) return ProjectVersion(v.Resource, await CatalogVersion(v, c.Options.Cancellation).ConfigureAwait(false));
            var version = Nexa.Plugin.Sdk.Abi100.Data.CatalogVersion.Read(await QueryCatalogProvider(c, v.Resource.Provider, "ReadVersion", ResourceVersionRef.Write(v)).ConfigureAwait(false));
            CheckCatalogVersion(version, v.Resource);
            if (version.Version != v) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return version;
        }),
        HostRoutes.Typed<ResourceVersionRef, TextResult>("Catalog", "ReadChangelog", async (c, v) =>
        {
            if (CustomCatalog(v.Resource.Provider))
            {
                var text = TextResult.Read(await QueryCatalogProvider(c, v.Resource.Provider, "ReadChangelog", ResourceVersionRef.Write(v)).ConfigureAwait(false));
                if (text.Text.Length > 256 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                return text;
            }
            await CatalogVersion(v, c.Options.Cancellation).ConfigureAwait(false);
            var result = await Query<ResourceChangelogQuery, ResourceChangelog>(_catalogQueries, ResourceCatalogContract.Changelog, new(CatalogReference(v.Resource), v.VersionId), c.Options.Cancellation).ConfigureAwait(false);
            if (result.Text.Length > 256 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE); return new(result.Text);
        }),
        HostRoutes.Typed<ResourceVersionRef, Page<CatalogFile>>("Catalog", "ResolveFiles", async (c, v) =>
        {
            if (!CustomCatalog(v.Resource.Provider)) return ContentPage(ProjectVersion(v.Resource, await CatalogVersion(v, c.Options.Cancellation).ConfigureAwait(false)).Files, new(null, 256));
            var result = Nexa.Plugin.Sdk.Abi100.Data.Page<CatalogFile>.Read(await QueryCatalogProvider(c, v.Resource.Provider, "ResolveFiles", ResourceVersionRef.Write(v)).ConfigureAwait(false));
            if (result.NextCursor is not null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            foreach (var file in result.Items) CheckCatalogFile(file);
            return WrapCatalogPage(c, result, new(null, 256), "files");
        }),
        HostRoutes.Typed<ResourceVersionRef, Page<CatalogDependency>>("Catalog", "ResolveDependencies", async (c, v) =>
        {
            if (CustomCatalog(v.Resource.Provider))
            {
                var result = Nexa.Plugin.Sdk.Abi100.Data.Page<CatalogDependency>.Read(await QueryCatalogProvider(c, v.Resource.Provider, "ResolveDependencies", ResourceVersionRef.Write(v)).ConfigureAwait(false));
                if (result.NextCursor is not null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                foreach (var dependency in result.Items)
                {
                    CheckCatalogReference(dependency.Version.Resource, v.Resource.Provider);
                    if (dependency.Required && dependency.Conflict || dependency.Version.VersionId.Length > 256 || dependency.Version.VersionId.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                }
                return WrapCatalogPage(c, result, new(null, 256), "dependencies");
            }
            var version = await CatalogVersion(v, c.Options.Cancellation).ConfigureAwait(false);
            if (version.Dependencies.Count > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            return ContentPage(version.Dependencies.Select(d => new CatalogDependency(new(new(v.Resource.Provider, d.ProjectId), d.VersionId ?? ""), d.Kind == "required", d.Kind == "incompatible")).ToImmutableArray(), new(null, 256));
        }),
        HostRoutes.Typed<PageRequest, Page<CatalogProject>>("Catalog", "ListFavorites", async (c, v) =>
        {
            var favorites = await Query<ResourceFavoritesQuery, ResourceFavoritesSnapshot>(_catalogQueries, ResourceCatalogContract.Favorites, new(), c.Options.Cancellation).ConfigureAwait(false);
            return ContentPage(favorites.Projects.Select(p => ProjectResource(p)).ToImmutableArray(), v) with { Revision = new(Token(c, "catalog-favorites:" + favorites.Revision)) };
        }),
        HostRoutes.Typed<FavoriteChange, Empty>("Catalog", "SetFavorite", async (c, v) =>
        {
            var source = CatalogReference(v.Resource); var detail = await CatalogDetail(v.Resource, c.Options.Cancellation).ConfigureAwait(false);
            await Confirm(c, "catalog/favorite", (v.Favorite ? "Favorite " : "Unfavorite ") + detail.Project.DisplayName, FavoriteChange.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            var before = await Query<ResourceFavoritesQuery, ResourceFavoritesSnapshot>(_catalogQueries, ResourceCatalogContract.Favorites, new(), c.Options.Cancellation).ConfigureAwait(false);
            if (v.ExpectedRevision is not null && v.ExpectedRevision != new Revision(Token(c, "catalog-favorites:" + before.Revision))) throw new AbiHostException(AbiErrors.CONFLICT);
            await Command(_catalogCommands, ResourceCatalogContract.Favorite, new ResourceFavoriteCommand(detail.Project with { Sources = [source] }, v.Favorite) { ExpectedRevision = before.Revision }, c.Options.Cancellation).ConfigureAwait(false);
            return new Empty();
        })
    ];
}
