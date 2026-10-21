using System.Net;
using Nexa.Services.Updates;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask PublisherDownloadsUseNewRepositoryAndOnlyPinnedNotFoundFallbacks()
    {
        const string version = "2.0.0.beta.1", name = "Nexa-2.0.0.beta.1-linux-x64.tar.gz";
        string[] repositories = [LauncherRepository.Url, LauncherRepository.LegacyUrl, LauncherRepository.HistoricalUrl];
        for (int available = 0; available < repositories.Length; available++)
        {
            List<Uri> requests = [];
            using var handler = new TrustHttpHandler(request =>
            {
                requests.Add(request.RequestUri!);
                AssertTrue(request.Headers.Authorization is null);
                return request.RequestUri!.AbsoluteUri.StartsWith(repositories[available] + "/releases/download/", StringComparison.Ordinal)
                    ? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
                    : new(HttpStatusCode.NotFound);
            });
            using var http = new HttpClient(handler);
            using var package = await new GitHubUpdateReleaseSource(http).OpenPackageAsync(version, name, default);
            AssertEqual(1 + available * 2, requests.Count);
            AssertEqual(LauncherRepository.Url + "/releases/download/" + version + "/" + name, requests[0].AbsoluteUri);
            AssertEqual(repositories[available] + "/releases/download/" + version + "/" + name, requests[^1].AbsoluteUri);
            AssertEqual(1, package.ReadByte());
        }
        int denied = 0;
        using var rejectedHttp = new HttpClient(new TrustHttpHandler(_ => { denied++; return new(HttpStatusCode.Forbidden); }));
        bool rejected = false;
        try { using var package = await new GitHubUpdateReleaseSource(rejectedHttp).OpenPackageAsync(version, name, default); }
        catch (HttpRequestException) { rejected = true; }
        AssertTrue(rejected);
        AssertEqual(1, denied);
    }
}
