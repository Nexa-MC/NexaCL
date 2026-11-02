using System.Net;
using Nexa.Services.Foundation;
using Nexa.Services.Network;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask FoundationRegionalNetworkPolicyRejectsSavedMirrorsWithoutChangingPreference()
    {
        using var host = FoundationComposer.Compose(new DiagnosticSettingsPort(), LauncherDefaults.CreateSchema(), new ThrowingProfilePort());
        AssertTrue(host.SettingsPolicy.Set(new("network.provider-mirror", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
        foreach (string country in new[] { "CN", "US", "HK", "TW", "MO", "" })
        {
            RegionalPolicy policy = new(country);
            int actual = 0;
            using var pool = new NetworkHttpClientPool(() => host.CaptureNetworkPreferences(policy), (_, _) => new ResourceHttp(_ =>
            {
                actual++;
                return new(HttpStatusCode.OK) { Content = new StringContent("ok") };
            }));
            using var client = pool.CreateClient(allowAutoRedirect: false);
            foreach (string address in new[]
            {
                "https://bmclapi2.bangbang93.com/mc/game.jar",
                "https://bmclapi.bangbang93.com/mc/game.jar",
                "https://download.mcbbs.net/mc/game.jar",
                "https://mod.mcimirror.top/modrinth/v2/search",
                "https://mcim-files.pysio.online/data/project/version/file.jar"
            })
            {
                bool rejected = false;
                try { using var response = await client.GetAsync(address); }
                catch (HttpRequestException) { rejected = true; }
                AssertEqual(!policy.IsMainlandChina, rejected);
            }
            AssertEqual(policy.IsMainlandChina ? 5 : 0, actual);
            using (var response = await client.GetAsync("https://api.modrinth.com/v2/search")) AssertEqual(HttpStatusCode.OK, response.StatusCode);
            AssertEqual(policy.IsMainlandChina ? 6 : 1, actual);
            AssertEqual("true", host.SettingsPolicy.Read(new()).Value!.Values.Single(item => item.Key == "network.provider-mirror").Value.Value);
        }

        AssertTrue(host.SettingsPolicy.Set(new("network.provider-mirror", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "false"))).IsSuccess);
        AssertFalse(host.CaptureNetworkPreferences(new("CN")).MirrorProviderEnabled);
        AssertFalse(host.CaptureNetworkPreferences(new("US")).MirrorProviderEnabled);
        AssertEqual("false", host.SettingsPolicy.Read(new()).Value!.Values.Single(item => item.Key == "network.provider-mirror").Value.Value);
    }
}
