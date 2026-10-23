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
            "feature.used", "feature.invoked", "diagnostic.run", "diagnostic.mods"];
        Guid run = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var sample = new JvmRunSample(run, 1, 30000, 30000, 0, new(), 21, "NeoForge", 40, 4096,
            3, 1280, 1536, 1664, 12.5, 20, false, null);
        var context = new JvmRunContext(run, "NeoForge", "21.1.1", new([], 0, true)) { GameVersion = "1.21.1" };
        return names.Select(name =>
        {
            var p = new Dictionary<string, string> { ["version"] = "2.0.0.alpha.12", ["os"] = "macos", ["arch"] = "arm64", ["result"] = "ok" };
            IReadOnlyDictionary<string, string>? details = name switch
            {
                "diagnostic.error" => new Dictionary<string, string> { ["category"] = "downloads", ["severity"] = "error",
                    ["code"] = "System.IO.IOException", ["stack"] = "Nexa.Services.Downloads.HttpDownloadConnection.ReadAsync" },
                "diagnostic.metric" => new Dictionary<string, string> { ["metric"] = "network.request.ms", ["value"] = "12.5" },
                "feature.used" or "feature.invoked" => new Dictionary<string, string> { ["feature"] = "install" },
                "diagnostic.run" => RunDiagnosticTelemetry.Sample(sample),
                "diagnostic.mods" => RunDiagnosticTelemetry.Inventory(context).Single(),
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
            AssertEqual("https://pcln.top/api/v1/telemetry/events", request.RequestUri!.AbsoluteUri);
            AssertEqual(HttpMethod.Post, request.Method);
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
        AssertEqual(16, sent);
        AssertFalse(await transport.SendAsync([]));
        var invalid = examples[0] with { Level = TelemetryLevel.Diagnostic };
        AssertFalse(await transport.SendAsync([invalid]));
        AssertEqual(16, sent);
    }
}
