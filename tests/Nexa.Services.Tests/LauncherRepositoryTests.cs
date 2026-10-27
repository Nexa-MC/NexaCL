using System.Net;
using Nexa.Services.Updates;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask PublisherDownloadsUseNewRepositoryAndOnlyPinnedNotFoundFallbacks()
    {
        const string version = "2.0.0.beta.1", name = "Nexa-2.0.0.beta.1-linux-x64.tar.gz";
        string[] repositories = [LauncherRepository.UpdatesUrl, LauncherRepository.Url, LauncherRepository.LegacyUrl, LauncherRepository.HistoricalUrl];
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
            AssertEqual(LauncherRepository.UpdatesUrl + "/releases/download/" + version + "/" + name, requests[0].AbsoluteUri);
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
    private static async ValueTask PublisherAssetsFollowBoundedRedirects()
    {
        const string version = "2.0.0.alpha.14";
        int disposed = 0, requests = 0;
        using var client = new HttpClient(new TrustHttpHandler(request =>
        {
            requests++;
            AssertTrue(request.Headers.Authorization is null);
            var uri = request.RequestUri!;
            if (uri.Host == "github.com" && !uri.AbsolutePath.Contains("/v" + version + "/", StringComparison.Ordinal))
                return new(HttpStatusCode.NotFound);
            if (uri.Host == "github.com")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found)
                { Content = new RedirectDisposalContent(() => disposed++) };
                response.Headers.Location = new("https://release-assets.githubusercontent.com/asset?signature=fixture");
                return response;
            }
            AssertEqual("release-assets.githubusercontent.com", uri.Host);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }));
        var source = new GitHubUpdateReleaseSource(client);
        var release = await source.ReadReleaseAsync(version, default);
        AssertEqual(3, release.Manifest.Length); AssertEqual(3, release.Signature.Length);
        var delta = await ((IUpdateDeltaSource)source).ReadDeltaIndexAsync(version, default);
        AssertEqual(3, delta.Index.Length); AssertEqual(3, delta.Signature.Length);
        using var package = await source.OpenPackageAsync(version, "Nexa-" + version + "-win-x64.portable.zip", default);
        AssertEqual(1, package.ReadByte()); AssertEqual(5, disposed); AssertEqual(15, requests);

        foreach (string? location in new string?[] { null, "http://github.com/asset", "https://example.com/asset",
            "https://github.com.evil.example/asset", "https://user@github.com/asset", "https://github.com:444/asset",
            "https://release-assets.githubusercontent.com/asset#fragment", "/loop" })
        {
            int calls = 0, retired = 0;
            using var denied = new HttpClient(new TrustHttpHandler(_ =>
            {
                calls++;
                var response = new HttpResponseMessage(HttpStatusCode.Found)
                { Content = new RedirectDisposalContent(() => retired++) };
                if (location is not null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                return response;
            }));
            bool rejected = false;
            try { await new GitHubUpdateReleaseSource(denied).ReadReleaseAsync(version, default); }
            catch (HttpRequestException) { rejected = true; }
            AssertTrue(rejected); AssertEqual(location == "/loop" ? 6 : 1, calls); AssertEqual(calls, retired);
        }
        using var stop = new CancellationTokenSource();
        int cancelledCalls = 0, cancelledDisposed = 0;
        using var cancelled = new HttpClient(new TrustHttpHandler(_ =>
        {
            cancelledCalls++; stop.Cancel();
            var response = new HttpResponseMessage(HttpStatusCode.Found)
            { Content = new RedirectDisposalContent(() => cancelledDisposed++) };
            response.Headers.Location = new("https://release-assets.githubusercontent.com/asset");
            return response;
        }));
        bool wasCancelled = false;
        try { await new GitHubUpdateReleaseSource(cancelled).ReadReleaseAsync(version, stop.Token); }
        catch (OperationCanceledException) { wasCancelled = true; }
        AssertTrue(wasCancelled); AssertEqual(1, cancelledCalls); AssertEqual(1, cancelledDisposed);
    }

    private sealed class RedirectDisposalContent(Action disposed) : ByteArrayContent([])
    {
        protected override void Dispose(bool disposing) { if (disposing) disposed(); base.Dispose(disposing); }
    }

}
