using Nexa.Services.Network;
using Nexa.Services.Resources;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    // Opt-in network evidence, kept out of the deterministic Services suite.
    private static async Task LiveResourceCatalogSmoke()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var pool = new NetworkHttpClientPool(() => new NetworkPreferences(), mainlandChina: true);
        using var http = pool.CreateClient(allowAutoRedirect: false);
        http.Timeout = TimeSpan.FromSeconds(45);
        var transport = new ResourceProviderHttp(http, "") { CountryPolicy = new("CN"), SourcePriority = () => "mirrors-first" };
        using var icons = new ResourceIconService(http) { CountryPolicy = new("CN"), SourcePriority = () => "mirrors-first" };
        using var translations = new ResourceTranslationService(transport);
        foreach (var provider in new[] { ResourceProvider.Modrinth, ResourceProvider.CurseForge })
        {
            IResourceCatalogSource catalog = provider == ResourceProvider.Modrinth ? new ResourceCatalogService(http, transport) : new CurseForgeResourceCatalog(transport);
            var search = await catalog.SearchAsync(new(ResourceKind.Mod, "sodium", "1.21.1", "fabric"), stop.Token);
            AssertTrue(search.Projects.Count > 0, provider + " mirror search returned no projects.");
            AssertEqual("mod.mcimirror.top", transport.LastResolution!.SelectedHost);
            var project = search.Projects[0];
            var detail = await catalog.DetailAsync(new(project.Id, "1.21.1", "fabric"), stop.Token);
            AssertTrue(detail.Versions.Count > 0, provider + " mirror detail returned no compatible versions.");
            var version = detail.Versions.First(row => row.File is not null);
            var exact = await ((IResourceFileSource)catalog).ReadVersionAsync(new(provider, project.Id, version.Id, ""), stop.Token);
            AssertEqual(version.Id, exact!.Id);
            AssertTrue(exact.File is { Size: > 0 });
            AssertEqual("mod.mcimirror.top", transport.LastResolution!.SelectedHost);
            if (project.IconUrl is { } url) AssertTrue((await icons.ReadAsync(new(url), stop.Token)).Image is not null, provider + " icon was unavailable.");
            var translation = await translations.ReadAsync(new(new(provider, project.Id), project.Description), stop.Token);
            Console.WriteLine($"PASS: live {provider} mirror search={search.Projects.Count} versions={detail.Versions.Count} exact-file=true icon={project.IconUrl is not null} translation={translation.Description is not null}");
        }
    }
}
