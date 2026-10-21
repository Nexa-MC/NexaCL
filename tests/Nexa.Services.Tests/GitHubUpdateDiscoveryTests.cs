using System.Net;
using System.Text.Json.Nodes;
using Nexa.Services.Updates;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static JsonObject DistributionRelease(string version)
    {
        string tag = "v" + version;
        JsonArray assets = [];
        foreach (string rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
        {
            string[] formats = rid.StartsWith("win-", StringComparison.Ordinal) ? ["setup.exe", "portable.zip"]
                : rid.StartsWith("osx-", StringComparison.Ordinal) ? ["dmg", "portable.tar.gz"] : ["AppImage", "portable.tar.gz"];
            foreach (string format in formats)
            {
                string name = "Nexa-" + version + "-" + rid + "." + format;
                assets.Add((JsonNode)new JsonObject
                {
                    ["name"] = name,
                    ["size"] = 123,
                    ["browser_download_url"] = LauncherRepository.UpdatesUrl + "/releases/download/" + tag + "/" + name
                });
            }
        }
        return new()
        {
            ["tag_name"] = tag,
            ["draft"] = false,
            ["prerelease"] = version.Contains(".alpha.", StringComparison.Ordinal)
            || version.Contains(".beta.", StringComparison.Ordinal),
            ["html_url"] = LauncherRepository.UpdatesUrl + "/releases/tag/" + tag,
            ["assets"] = assets
        };
    }

    private static async ValueTask DedicatedUpdateDiscoverySelectsChannelsPlatformsAndNewerVersions()
    {
        using var handler = new DistributionDiscoveryHandler();
        using var http = new HttpClient(handler);
        var service = new NexaUpdateService(http);
        var draft = DistributionRelease("2.0.0.beta.3"); draft["draft"] = true;
        JsonArray releases = new(
            DistributionRelease("2.0.0.beta.1"),
            DistributionRelease("2.0.0.alpha.9"),
            draft,
            DistributionRelease("2.0.0.beta.2"));
        handler.Body = releases.ToJsonString();
        foreach (string rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
            foreach (string channel in new[] { "alpha", "beta" })
            {
                var checkedRelease = await service.CheckAsync(new("2.0.0.alpha.7", rid, channel));
                AssertTrue(checkedRelease.IsSuccess);
                AssertEqual("2.0.0.beta.2", checkedRelease.Value!.Offer!.Version);
                AssertTrue(checkedRelease.Value.Offer.InstallerUrl.StartsWith(LauncherRepository.UpdatesUrl + "/releases/download/v2.0.0.beta.2/", StringComparison.Ordinal));
                AssertTrue(checkedRelease.Value.Offer.PortableUrl.EndsWith(rid.StartsWith("win-", StringComparison.Ordinal) ? ".portable.zip" : ".portable.tar.gz", StringComparison.Ordinal));
            }
        AssertEqual(0, handler.LegacyCalls);
        var stableOnly = await service.CheckAsync(new("2.0.0.alpha.7", "win-x64", "stable"));
        AssertTrue(stableOnly.IsSuccess); AssertTrue(stableOnly.Value!.Offer is null);
        AssertEqual(1, handler.LegacyCalls);
        releases.Add((JsonNode)DistributionRelease("2.0.0")); handler.Body = releases.ToJsonString();
        foreach (string channel in new[] { "alpha", "beta", "stable" })
            AssertEqual("2.0.0", (await service.CheckAsync(new("2.0.0.alpha.7", "win-x64", channel))).Value!.Offer!.Version);
        AssertTrue((await service.CheckAsync(new("2.0.0", "win-x64", "stable"))).Value!.Offer is null);
    }

    private static async ValueTask DedicatedUpdateDiscoveryRejectsUntrustedMetadataWithoutFallback()
    {
        using var handler = new DistributionDiscoveryHandler();
        using var http = new HttpClient(handler);
        var service = new NexaUpdateService(http);
        foreach (Action<JsonObject> corrupt in new Action<JsonObject>[]
        {
            release => release["html_url"] = "https://github.com/another/project/releases/tag/v2.0.0.beta.1",
            release => release["prerelease"] = false,
            release => release["assets"]![0]!["browser_download_url"] = "https://example.com/setup.exe",
            release => release["assets"]![0]!["size"] = 0,
            release => release["assets"]!.AsArray().RemoveAt(1),
            release => release["assets"]!.AsArray().Add(release["assets"]![0]!.DeepClone()),
        })
        {
            var release = DistributionRelease("2.0.0.beta.1"); corrupt(release);
            handler.Body = new JsonArray(release).ToJsonString();
            AssertFalse((await service.CheckAsync(new("2.0.0.alpha.7", "win-x64", "alpha"))).IsSuccess);
        }
        foreach (HttpStatusCode status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
        {
            handler.Status = status;
            AssertFalse((await service.CheckAsync(new("2.0.0.alpha.7", "win-x64", "alpha"))).IsSuccess);
        }
        handler.Status = HttpStatusCode.OK;
        handler.Body = new string(' ', 4 * 1024 * 1024 + 1);
        AssertFalse((await service.CheckAsync(new("2.0.0.alpha.7", "win-x64", "alpha"))).IsSuccess);
        AssertEqual(0, handler.LegacyCalls);
        handler.Status = HttpStatusCode.NotFound;
        AssertTrue((await service.CheckAsync(new("2.0.0.alpha.7", "win-x64", "alpha"))).IsSuccess);
        AssertEqual(1, handler.LegacyCalls);
    }

    private sealed class DistributionDiscoveryHandler : HttpMessageHandler
    {
        internal string Body = "[]";
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal int LegacyCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            AssertTrue(request.Headers.Authorization is null);
            if (request.RequestUri!.Host == "api.github.com")
            {
                AssertEqual("/repos/Nexa-MC/NexaCL-Update/releases", request.RequestUri.AbsolutePath);
                AssertEqual("?per_page=30", request.RequestUri.Query);
                AssertTrue(request.Headers.UserAgent.Count > 0);
                return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
            }
            AssertEqual("https://api.pcln.top/v2/updates/latest", request.RequestUri.GetLeftPart(UriPartial.Path));
            LegacyCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
