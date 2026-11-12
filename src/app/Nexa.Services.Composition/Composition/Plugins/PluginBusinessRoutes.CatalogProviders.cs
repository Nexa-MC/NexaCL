using System.Collections.Immutable;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static readonly QualifiedId CatalogProviderKind = new("nexa:catalog");
    private static readonly BusinessProviderMethod[] CatalogProviderMethods = [
        new("Search", Abi100Schemas.CatalogSearch, Abi100Schemas.PageOfCatalogProject),
        new("ReadProject", Abi100Schemas.ResourceRef, Abi100Schemas.CatalogProject),
        new("ListVersions", Abi100Schemas.CatalogVersionQuery, Abi100Schemas.PageOfCatalogVersion),
        new("ReadVersion", Abi100Schemas.ResourceVersionRef, Abi100Schemas.CatalogVersion),
        new("ReadChangelog", Abi100Schemas.ResourceVersionRef, Abi100Schemas.TextResult),
        new("ResolveFiles", Abi100Schemas.ResourceVersionRef, Abi100Schemas.PageOfCatalogFile),
        new("ResolveDependencies", Abi100Schemas.ResourceVersionRef, Abi100Schemas.PageOfCatalogDependency)
    ];
    private static bool CustomCatalog(QualifiedId provider) => provider.Value is not ("nexa:modrinth" or "nexa:curseforge");
    private ValueTask<AbiValue> QueryCatalogProvider(HostInvocation c, QualifiedId provider, string method, AbiValue input) =>
        Services.QueryHostProvider(c, CatalogProviderKind, provider, CatalogProviderMethods.Single(m => m.Name == method), input);
    private static void CheckCatalogReference(ResourceRef resource, QualifiedId provider)
    {
        if (resource.Provider != provider || resource.ProjectId.Length is < 1 or > 256 || resource.ProjectId.Any(char.IsControl))
            throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
    }
    private static void CheckCatalogProject(CatalogProject project, QualifiedId provider)
    {
        CheckCatalogReference(project.Resource, provider); CatalogKind(project.Kind);
        if (project.Name.Length > 1024 || project.Summary.Length > 65536) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
    }
    private static void CheckCatalogFile(CatalogFile file)
    {
        if (file.Name.Length is < 1 or > 256 || file.Name is "." or ".." || file.Name.IndexOfAny(['/', '\\']) >= 0 || file.Name.Any(char.IsControl)
            || file.Size < 0 || !Uri.TryCreate(file.Source.Value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || file.Sha256.Length != 0 && (file.Sha256.Length != 64 || file.Sha256.Any(c => !char.IsAsciiHexDigit(c))))
            throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
    }
    private static void CheckCatalogVersion(CatalogVersion version, ResourceRef resource)
    {
        if (version.Version.Resource != resource || version.Version.VersionId.Length is < 1 or > 256 || version.Version.VersionId.Any(char.IsControl)
            || version.Files.Length > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        foreach (var file in version.Files) CheckCatalogFile(file);
    }
    private PageRequest UnwrapCatalogCursor(HostInvocation c, PageRequest page, string scope)
    {
        if (page.Limit is < 1 or > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        if (page.Cursor is null) return page;
        var parts = page.Cursor.Split('.');
        if (parts.Length != 2 || parts[1].Length > 8192 || parts[0] != Token(c, "catalog-provider:" + scope + ":" + parts[1]))
            throw new AbiHostException(AbiErrors.CONFLICT);
        try { return page with { Cursor = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[1])) }; }
        catch (Exception error) when (error is FormatException or DecoderFallbackException) { throw new AbiHostException(AbiErrors.CONFLICT); }
    }
    private Page<T> WrapCatalogPage<T>(HostInvocation c, Page<T> page, PageRequest request, string scope) where T : IAbiData<T>
    {
        if (page.Items.Length > request.Limit || page.Revision.Value.Length is < 1 or > 256 || page.NextCursor is { } next && Encoding.UTF8.GetByteCount(next) > 4096)
            throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        string? cursor = page.NextCursor;
        if (cursor is not null)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor));
            cursor = Token(c, "catalog-provider:" + scope + ":" + encoded) + "." + encoded;
        }
        return page with { NextCursor = cursor };
    }
    private async ValueTask<Page<CatalogProject>> SearchCatalogProvider(HostInvocation c, CatalogSearch request)
    {
        if (request.Providers.Length != 1) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string scope = ProjectionRevision(request with { Page = new(null, request.Page.Limit) }).Value;
        var input = request with { Page = UnwrapCatalogCursor(c, request.Page, scope) };
        var result = Nexa.Plugin.Sdk.Abi100.Data.Page<CatalogProject>.Read(await QueryCatalogProvider(c, request.Providers[0], "Search", CatalogSearch.Write(input)).ConfigureAwait(false));
        foreach (var project in result.Items) CheckCatalogProject(project, request.Providers[0]);
        return WrapCatalogPage(c, result, request.Page, scope);
    }
    private async ValueTask<Page<CatalogVersion>> CatalogProviderVersions(HostInvocation c, CatalogVersionQuery request)
    {
        CheckCatalogReference(request.Resource, request.Resource.Provider);
        string scope = "versions:" + ProjectionRevision(request with { Page = new(null, request.Page.Limit) }).Value;
        var input = request with { Page = UnwrapCatalogCursor(c, request.Page, scope) };
        var result = Nexa.Plugin.Sdk.Abi100.Data.Page<CatalogVersion>.Read(await QueryCatalogProvider(c, request.Resource.Provider, "ListVersions", CatalogVersionQuery.Write(input)).ConfigureAwait(false));
        foreach (var version in result.Items) CheckCatalogVersion(version, request.Resource);
        return WrapCatalogPage(c, result, request.Page, scope);
    }
}
