using System.Net;
using System.Text.Json.Nodes;
using Nexa.Services.Community;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask CommunityStartupCadenceAndAnnouncementAcknowledgement()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexa-community-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "state.json"), id = Guid.NewGuid().ToString();
        using var http = new HttpClient(new ResourceHttp(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject
            {
                ["announcements"] = new JsonArray(new JsonObject
                { ["id"] = id, ["revision"] = 1, ["title"] = "Notice", ["body"] = "Plain text" })
            }.ToJsonString())
        }));
        try
        {
            for (int i = 1; i <= 61; i++)
            {
                await using var service = new LauncherCommunityService(path, http);
                var snapshot = await service.StartAsync();
                AssertEqual(i % 30 == 0, snapshot.SurveyDue);
                AssertEqual(i == 1 ? 1 : 0, snapshot.Announcements.Count);
                if (i == 1) await service.AcknowledgeAsync(snapshot.Announcements[0]);
                AssertFalse((await service.StartAsync()).SurveyDue);
            }
            AssertEqual(61L, JsonNode.Parse(await File.ReadAllTextAsync(path))!["starts"]!.GetValue<long>());
            using var failedHttp = new HttpClient(new ResourceHttp(_ => new(HttpStatusCode.ServiceUnavailable)));
            await File.WriteAllTextAsync(path, "{\"starts\":89}");
            await using var offline = new LauncherCommunityService(path, failedHttp);
            AssertTrue((await offline.StartAsync()).SurveyDue);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
