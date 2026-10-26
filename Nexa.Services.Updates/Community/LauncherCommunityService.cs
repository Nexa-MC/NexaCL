using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexa.Services.Community;

public sealed record LauncherAnnouncement(string Id, long Revision, string Title, string Body);
public sealed record LauncherCommunitySnapshot(bool SurveyDue, IReadOnlyList<LauncherAnnouncement> Announcements);

/// <summary>Local startup cadence and bounded public announcements, independent of rendering.</summary>
public sealed class LauncherCommunityService(string statePath, HttpClient http) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private JsonObject _state = new();

    public async Task<LauncherCommunitySnapshot> StartAsync(CancellationToken token = default)
    {
        bool due;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_started) return new(false, []);
            _started = true;
            if (File.Exists(statePath))
            {
                try
                {
                    if (new FileInfo(statePath).Length <= 65536)
                        _state = JsonNode.Parse(await File.ReadAllTextAsync(statePath, token).ConfigureAwait(false)) as JsonObject ?? new();
                }
                catch (JsonException) { _state = new(); }
            }
            long previous = _state["starts"] is JsonValue value && value.TryGetValue<long>(out long n) && n >= 0 ? n : 0;
            long count = previous == long.MaxValue ? 1 : previous + 1;
            _state["starts"] = count;
            due = count % 30 == 0;
            await SaveAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        List<LauncherAnnouncement> announcements = [];
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await http.GetAsync("https://nexa-telemetry.pcln.workers.dev/v1/announcements", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            byte[] bytes = new byte[65537]; int length = 0;
            while (length < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > 65536) return new(due, []);
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            foreach (var item in document.RootElement.GetProperty("announcements").EnumerateArray().Take(20))
            {
                string id = item.GetProperty("id").GetString() ?? "", title = item.GetProperty("title").GetString() ?? "", body = item.GetProperty("body").GetString() ?? "";
                long revision = item.GetProperty("revision").GetInt64();
                if (!Guid.TryParse(id, out _) || title.Length is 0 or > 120 || body.Length is 0 or > 8000 || revision <= 0) continue;
                if (_state["seen"] is JsonObject seen && seen[id] is JsonValue stored && stored.TryGetValue<long>(out long old) && old >= revision) continue;
                announcements.Add(new(id, revision, title, body));
            }
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or FormatException) { }
        return new(due, announcements);
    }

    public async Task AcknowledgeAsync(LauncherAnnouncement announcement, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_state["seen"] is not JsonObject seen) _state["seen"] = seen = new();
            seen[announcement.Id] = announcement.Revision;
            while (seen.Count > 100) seen.Remove(seen.First().Key);
            await SaveAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        // Shutdown queues behind acknowledgements already submitted by the UI.
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task SaveAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        string temporary = statePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, _state.ToJsonString(), token).ConfigureAwait(false);
            File.Move(temporary, statePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
