using System.Net;
using System.Text.Json;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Telemetry;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static TelemetryEvent[] WebsiteTelemetryExamples()
    {
        string[] names = ["app.started", "app.failure", "update.checked", "rollout.checked", "game.started", "game.exited",
            "task.started", "task.finished", "feature.exposed", "diagnostic.session", "diagnostic.error", "diagnostic.metric",
            "feature.used", "feature.invoked", "diagnostic.run", "diagnostic.mods", "diagnostic.launch", "diagnostic.run-summary"];
        Guid run = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var sample = new JvmRunSample(run, 1, 30000, 30000, 0, new(), 21, "NeoForge", 40, 4096,
            3, 1280, 1536, 1664, 12.5, 20, false, null);
        var context = new JvmRunContext(run, "NeoForge", "21.1.1", new([new("example", "1.0", "fabric.mod.json", true,
            new Dictionary<string,string> { ["minecraft"] = ">=1.21" }, true)
            { ContentSha256 = new string('a', 64), NestedCandidate = true, ProvidedIds = new Dictionary<string,string> { ["alias"] = "1.0" } }], 0, true))
        { GameVersion = "1.21.1" };
        return names.Select(name =>
        {
            var p = new Dictionary<string, string> { ["version"] = "2.0.0.alpha.12", ["os"] = "macos", ["arch"] = "arm64", ["result"] = "ok" };
            IReadOnlyDictionary<string, string>? details = name switch
            {
                "diagnostic.error" => new Dictionary<string, string>
                {
                    ["category"] = "downloads",
                    ["severity"] = "error",
                    ["code"] = "System.IO.IOException",
                    ["stack"] = "Nexa.Services.Downloads.HttpDownloadConnection.ReadAsync"
                },
                "diagnostic.metric" => new Dictionary<string, string> { ["metric"] = "network.request.ms", ["value"] = "12.5" },
                "feature.used" or "feature.invoked" => new Dictionary<string, string> { ["feature"] = "install" },
                "diagnostic.run" => RunDiagnosticTelemetry.Sample(sample),
                "diagnostic.mods" => RunDiagnosticTelemetry.Inventory(context).Single(),
                "diagnostic.launch" => RunLifecycleTelemetry.Launch(run, new(21, 40, 40, 4, 4096, 0, 0, 0, 0, 0, 0, 0, false, false, false)),
                "diagnostic.run-summary" => RunLifecycleTelemetry.Summary(LifecycleObservation(run)),
                _ => null,
            };
            if (details is not null) foreach (var pair in details) p.Add(pair.Key, pair.Value);
            return new TelemetryEvent(name, DateTimeOffset.UtcNow, p) { Level = TelemetryEventCatalog.Level(name)!.Value };
        }).ToArray();
    }

    private static async ValueTask TelemetryUsesWebsiteIngressAndTieredWireContract()
    {
        var examples = WebsiteTelemetryExamples();
        int sent = 0;
        using var http = new HttpClient(new ResourceHttp(request =>
        {
            AssertEqual("https://nexa-telemetry.pcln.workers.dev/v1/events", request.RequestUri!.AbsoluteUri);
            AssertEqual(HttpMethod.Post, request.Method);
            AssertTrue(request.Headers.UserAgent.Any(value => value.Product?.Name == "NexaCL"));
            AssertEqual("application/json", request.Content!.Headers.ContentType!.MediaType);
            AssertFalse(request.Headers.Contains("Authorization") || request.Headers.Contains("Cookie"));
            using var json = JsonDocument.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            foreach (var item in json.RootElement.EnumerateArray())
            {
                AssertTrue(item.GetProperty("timestamp").GetInt64() > 0);
                AssertTrue(item.GetProperty("level").GetString() is "necessary" or "diagnostic");
                sent++;
            }
            return new(HttpStatusCode.NoContent);
        }));
        var transport = new CloudflareTelemetryTransport(http);
        foreach (var batch in examples.Chunk(transport.MaximumBatchSize)) AssertTrue(await transport.SendAsync(batch));
        AssertEqual(18, sent);
        AssertFalse(await transport.SendAsync([]));
        var invalid = examples[0] with { Level = TelemetryLevel.Diagnostic };
        AssertFalse(await transport.SendAsync([invalid]));
        AssertEqual(18, sent);
    }

    private static async ValueTask TelemetryEdgeChallengeReportsSafeDiagnostic()
    {
        List<string> diagnostics = [];
        using var http = new HttpClient(new ResourceHttp(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html>private challenge response must not be logged</html>")
            };
            response.Headers.Add("cf-mitigated", "challenge");
            return response;
        }));
        var transport = new CloudflareTelemetryTransport(http, reportFailure: diagnostics.Add);
        var item = WebsiteTelemetryExamples()[0];
        AssertFalse(await transport.SendAsync([item]));
        AssertFalse(await transport.SendAsync([item]));
        AssertEqual(1, diagnostics.Count);
        AssertTrue(diagnostics[0].Contains("Cloudflare", StringComparison.Ordinal));
        AssertFalse(diagnostics[0].Contains("private", StringComparison.Ordinal));
    }
}
