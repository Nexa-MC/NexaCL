using System.Net;
using System.Security.Cryptography;
using Nexa.Services.Downloads;
using Nexa.Services.Network;
using Nexa.Services.Resources;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask ResourceMirrorTimeoutFallsBackAndCallerCancellationStops()
    {
        List<string> hosts = [];
        using var http = new HttpClient(new BudgetIconHttp(async (request, token) =>
        {
            hosts.Add(request.RequestUri!.Host);
            AssertEqual(request.RequestUri.Host == "api.curseforge.com", request.Headers.Contains("x-api-key"));
            if (request.RequestUri.Host == "mod.mcimirror.top") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":{\"id\":1}}") };
        }));
        var transport = new ResourceProviderHttp(http, "fixture-key") { CountryPolicy = new("CN"), RequestTimeout = TimeSpan.FromMilliseconds(50) };
        using var result = await transport.ReadAsync(ResourceProvider.CurseForge, "mods/1", true, default);
        AssertEqual(1, result.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        AssertEqual("mod.mcimirror.top,api.curseforge.com", string.Join(',', hosts));
        AssertEqual("api.curseforge.com", transport.LastResolution!.SelectedHost);
        AssertEqual(TimeSpan.FromSeconds(30), new ResourceProviderHttp(http).RequestTimeout);

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int canceledCalls = 0;
        using var canceledHttp = new HttpClient(new BudgetIconHttp(async (_, token) =>
        {
            Interlocked.Increment(ref canceledCalls); admitted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Canceled request unexpectedly resumed.");
        }));
        using var stop = new CancellationTokenSource();
        Task<System.Text.Json.JsonDocument> pending = new ResourceProviderHttp(canceledHttp, "fixture-key") { CountryPolicy = new("CN") }
            .ReadAsync(ResourceProvider.CurseForge, "mods/1", true, stop.Token);
        await admitted.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        bool canceled = false;
        try { using var ignored = await pending; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { canceled = true; }
        AssertTrue(canceled); AssertEqual(1, canceledCalls);
    }

    private static async ValueTask ResourceTranslationsUseCurrentContractAndNeverReadOutsideMainland()
    {
        int calls = 0;
        using var http = new HttpClient(new ResourceHttp(request =>
        {
            calls++;
            AssertEqual("/translate/curseforge", request.RequestUri!.AbsolutePath);
            AssertEqual("?modId=394468", request.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"original\":\"Renderer\",\"translated\":\"渲染优化\"}") };
        }));
        using var cache = new ResourceOnlineInformationCache();
        var query = new ResourceTranslationQuery(new(ResourceProvider.CurseForge, "394468"), "Renderer");
        using var mainland = new ResourceTranslationService(new(http) { CountryPolicy = new("CN") }, cache);
        AssertEqual("渲染优化", (await mainland.ReadAsync(query, default)).Description);
        foreach (string country in new[] { "US", "HK", "TW", "MO", "" })
        {
            using var international = new ResourceTranslationService(new(http) { CountryPolicy = new(country) }, cache);
            AssertTrue((await international.ReadAsync(query, default)).Description is null);
        }
        AssertEqual(1, calls);
    }

    private static async ValueTask ResourceIconsFollowSourcePolicyValidateEveryRedirectAndSeparateCountries()
    {
        foreach (string scenario in new[] { "redirect", "failure", "invalid", "oversized", "private", "scheme", "port", "loop" })
        {
            List<string> requests = [];
            using var http = new HttpClient(new ResourceHttp(request =>
            {
                var uri = request.RequestUri!; requests.Add(uri.AbsoluteUri);
                AssertTrue(request.Headers.Authorization is null && !request.Headers.Contains("x-api-key"));
                if (uri.Host == "cdn.modrinth.com") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(BudgetIcon) };
                AssertEqual("mod.mcimirror.top", uri.Host);
                if (scenario == "failure") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") };
                if (scenario is "invalid" or "oversized") return new(HttpStatusCode.OK)
                { Content = new ByteArrayContent(scenario == "invalid" ? [1, 2, 3] : new byte[1_048_577]) };
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new(scenario switch
                {
                    "private" => "https://127.0.0.1/private",
                    "scheme" => "http://cdn.modrinth.com/data/test/icon.png",
                    "port" => "https://cdn.modrinth.com:444/data/test/icon.png",
                    "loop" => "https://mod.mcimirror.top/data/test/icon.png",
                    _ => "https://cdn.modrinth.com/data/test/icon.png"
                });
                return response;
            }));
            using var icons = new ResourceIconService(http) { CountryPolicy = new("CN"), SourcePriority = () => "mirrors-first" };
            AssertTrue((await icons.ReadAsync(new(IconUrl("test")), default)).Image is not null);
            AssertEqual("mod.mcimirror.top", new Uri(requests[0]).Host);
            AssertEqual("cdn.modrinth.com", new Uri(requests[^1]).Host);
            AssertEqual(scenario == "loop" ? 6 : 2, requests.Count);
        }

        List<string> hosts = [];
        using var regionalHttp = new HttpClient(new ResourceHttp(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(BudgetIcon) };
        }));
        using var shared = new Nexa.Services.Caching.SharedStateCache();
        using var mainland = new ResourceIconService(regionalHttp, shared) { CountryPolicy = new("CN"), SourcePriority = () => "mirrors-first" };
        await mainland.ReadAsync(new(IconUrl("regional")), default);
        using var international = new ResourceIconService(regionalHttp, shared) { CountryPolicy = new("US"), SourcePriority = () => "mirrors-first" };
        await international.ReadAsync(new(IconUrl("regional")), default);
        AssertEqual("mod.mcimirror.top,cdn.modrinth.com", string.Join(',', hosts));
    }

    private static async ValueTask ResourceIconsCannotBypassDisabledProvidersThroughMirrorAvatars()
    {
        int actual = 0;
        var preferences = new NetworkPreferences { CurseForgeProviderEnabled = false };
        using var pool = new NetworkHttpClientPool(() => preferences, (_, _) => new ResourceHttp(_ =>
        {
            actual++;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(BudgetIcon) };
        }));
        using var http = pool.CreateClient(allowAutoRedirect: false);
        using var icons = new ResourceIconService(http) { CountryPolicy = new("CN"), SourcePriority = () => "mirrors-first" };
        AssertTrue((await icons.ReadAsync(new("https://media.forgecdn.net/avatars/1/2/icon.png"), default)).Image is null);
        AssertEqual(0, actual);
        preferences = preferences with { CurseForgeProviderEnabled = true, MirrorProviderEnabled = false };
        AssertTrue((await icons.ReadAsync(new("https://media.forgecdn.net/avatars/1/2/icon.png"), default)).Image is not null);
        AssertEqual(1, actual);
    }

    private static async ValueTask ResourceInternationalDownloadsRejectMirrorMetadataAndRedirects()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] bytes = [1, 2, 3];
            foreach (string scenario in new[] { "official", "metadata", "redirect" })
            {
                int calls = 0;
                using var http = new HttpClient(new ResourceHttp(request =>
                {
                    calls++;
                    AssertEqual("cdn.modrinth.com", request.RequestUri!.Host);
                    if (scenario != "redirect") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = new("https://mod.mcimirror.top/data/test/file.jar");
                    return response;
                }));
                var catalog = new CatalogStub((_, _) => throw new NotSupportedException())
                {
                    File = new(scenario + ".jar", (scenario == "metadata" ? "https://mod.mcimirror.top" : "https://cdn.modrinth.com") + "/data/test/file.jar",
                        bytes.Length, null, Convert.ToHexString(SHA512.HashData(bytes)))
                };
                var builder = new XsrStateStoreBuilder(); DownloadService.DeclareState(builder); TaskCenterStateContract.DeclareState(builder);
                var store = builder.Build();
                var downloads = new ResourceDownloadService(catalog, new(store), new TaskCenterService(store), http)
                { CountryPolicy = new("US"), SourcePriority = () => "mirrors-first" };
                bool failed = false;
                try { await downloads.DownloadAsync(new(ResourceProvider.Modrinth, "Project", "Version", directory), default); }
                catch (Exception error) when (error is IOException or InvalidDataException) { failed = true; }
                AssertEqual(scenario != "official", failed);
                AssertEqual(scenario == "metadata" ? 0 : 1, calls);
                AssertEqual(scenario == "official", File.Exists(Path.Combine(directory, scenario + ".jar")));
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
