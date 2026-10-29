using System.Security.Cryptography;
using System.Text.Json;

namespace Nexa.Services.Updates;

/// <summary>Signed update catalogs and immutable per-user packages; never mutates the application bundle.</summary>
public sealed class SidecarUpdateService : ISidecarUpdateControl, IDisposable
{
    private const int CatalogLimit = 256 * 1024;
    private readonly HttpClient _http;
    private readonly IUpdateSignatureVerifier _verify;
    private readonly string _root, _bundled, _rid, _hostVersion;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly Dictionary<string, string> _runningHashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Candidate> _offers = new(StringComparer.Ordinal);
    private SidecarUpdateSnapshot _snapshot = new("initial", []);
    private sealed record Candidate(string Id, string Version, string Hash, long Size, Uri Url, string Release, string MinimumHost, byte[] Catalog, byte[] Signature);
    public SidecarUpdateService(HttpClient http, IUpdateSignatureVerifier verifier, string root, string bundled, string rid, string hostVersion)
    { _http = http; _verify = verifier; _root = Path.GetFullPath(root); _bundled = Path.GetFullPath(bundled); _rid = rid; _hostVersion = hostVersion; }
    public SidecarUpdateSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    private void Publish(string phase, IReadOnlyList<SidecarUpdateItem>? items = null)
    { lock (_gate) _snapshot = new(phase, items ?? _snapshot.Items); }
    private void Item(string id, string phase, bool canInstall, double progress = 0)
    { lock (_gate) _snapshot = _snapshot with { Items = _snapshot.Items.Select(i => i.Id == id ? i with { Phase = phase, CanInstall = canInstall, Progress = progress } : i).ToArray() }; }
    public async Task CheckAsync(string channel, CancellationToken token)
    {
        if (channel is not ("alpha" or "beta" or "stable")) throw new ArgumentException("Invalid update channel.", nameof(channel));
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Publish("checking");
            var installed = await Inventory(token).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            byte[] releases = await Fetch(new Uri("https://api.github.com/repos/" + LauncherRepository.UpdatesSlug + "/releases?per_page=30"), 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(releases);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 30) throw new InvalidDataException("Invalid release list.");
            string? version = null; UpdateVersion best = default;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean()) continue;
                string tag = release.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith('v') || !UpdateVersion.TryParse(tag[1..], out var parsed) || parsed.ToString() != tag[1..]
                    || parsed.Stage == UpdateVersionStage.Ci || channel == "stable" && parsed.Stage != UpdateVersionStage.Stable
                    || channel == "beta" && parsed.Stage == UpdateVersionStage.Alpha) continue;
                if (!release.GetProperty("assets").EnumerateArray().Any(a => a.GetProperty("name").GetString() == "Nexa-Sidecars-" + _rid + ".json")) continue;
                if (version is null || parsed.CompareTo(best) > 0) { version = tag[1..]; best = parsed; }
            }
            if (version is null) { _offers.Clear(); Publish("unpublished", installed.Values.ToArray()); return; }
            var url = ReleaseUri(version, "Nexa-Sidecars-" + _rid + ".json");
            byte[] catalog = await Fetch(url, CatalogLimit, deadline.Token).ConfigureAwait(false);
            byte[] signature = await Fetch(new Uri(url.AbsoluteUri + ".asc"), 16384, deadline.Token).ConfigureAwait(false);
            await Verify(catalog, signature, deadline.Token).ConfigureAwait(false);
            var candidates = Parse(catalog, signature, version);
            _offers.Clear(); List<SidecarUpdateItem> items = [];
            foreach (var pair in installed)
            {
                if (!candidates.TryGetValue(pair.Key, out var offer)) { items.Add(pair.Value); continue; }
                string? currentHash = await InstalledHash(pair.Key, token).ConfigureAwait(false);
                if (currentHash == offer.Hash) { items.Add(pair.Value with { Phase = _runningHashes.GetValueOrDefault(pair.Key) == offer.Hash ? "current" : "restart" }); continue; }
                if (await IsOlderThanActive(offer, token).ConfigureAwait(false)) { items.Add(pair.Value with { Phase = "current" }); continue; }
                bool compatible = UpdateVersion.TryParse(_hostVersion, out var host) && UpdateVersion.TryParse(offer.MinimumHost, out var minimum) && host.CompareTo(minimum) >= 0;
                _offers.Add(pair.Key, offer);
                items.Add(pair.Value with { AvailableVersion = offer.Version, Phase = compatible ? "available" : "host-required", CanInstall = compatible });
            }
            Publish("checked", items);
        }
        catch (OperationCanceledException) { Publish("failed"); throw; }
        catch { Publish("failed"); throw; }
        finally { _operations.Release(); }
    }
    public async Task InstallAsync(string packageId, CancellationToken token)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (!_offers.TryGetValue(packageId, out var offer) || !Snapshot.Items.Any(i => i.Id == packageId && i.CanInstall)) throw new InvalidOperationException("No admitted update offer.");
            if (await IsOlderThanActive(offer, token).ConfigureAwait(false)) throw new InvalidDataException("A newer Sidecar is already installed.");
            string parent = Path.Combine(_root, packageId); Directory.CreateDirectory(parent);
            temporary = Path.Combine(parent, ".download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            string imagePath = Path.Combine(temporary, packageId + ".nsc");
            Item(packageId, "downloading", false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(10));
            using (var request = Request(offer.Url))
            using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != offer.Size) throw new InvalidDataException("Sidecar size mismatch.");
                await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                await using var output = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[65536]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
                {
                    total += read; if (total > offer.Size) throw new InvalidDataException("Sidecar exceeds size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
                    Item(packageId, "downloading", false, (double)total / offer.Size);
                }
                if (total != offer.Size) throw new InvalidDataException("Incomplete Sidecar download.");
                output.Flush(flushToDisk: true);
            }
            Item(packageId, "verifying", false);
            byte[] imageSignature = await Fetch(new Uri(offer.Url.AbsoluteUri + ".asc"), 16384, deadline.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(imagePath + ".asc", imageSignature, deadline.Token).ConfigureAwait(false);
            await ValidateImage(imagePath, offer.Hash, deadline.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(temporary, "catalog.json"), offer.Catalog, deadline.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(temporary, "catalog.json.asc"), offer.Signature, deadline.Token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(imagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            deadline.Token.ThrowIfCancellationRequested();
            string destination = Path.Combine(parent, offer.Hash);
            if (Directory.Exists(destination))
            {
                try { await ValidateImage(Path.Combine(destination, packageId + ".nsc"), offer.Hash, deadline.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or InvalidDataException)
                {
                    string quarantine = destination + ".invalid-" + Guid.NewGuid().ToString("N");
                    Directory.Move(destination, quarantine);
                    try { Directory.Move(temporary!, destination); temporary = null; }
                    catch { Directory.Move(quarantine, destination); throw; }
                    Directory.Delete(quarantine, recursive: true);
                }
                // Repair metadata left by an interrupted publication without replacing executable bytes.
                await File.WriteAllBytesAsync(Path.Combine(destination, "catalog.json"), offer.Catalog, deadline.Token).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(destination, "catalog.json.asc"), offer.Signature, deadline.Token).ConfigureAwait(false);
            }
            else { Directory.Move(temporary, destination); temporary = null; }
            string pointer = Path.Combine(parent, "active"), staged = Path.Combine(parent, "active.tmp");
            await using (var pointerStream = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await pointerStream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(offer.Hash), deadline.Token).ConfigureAwait(false);
                pointerStream.Flush(flushToDisk: true);
            }
            File.Move(staged, pointer, overwrite: true);
            Item(packageId, "restart", false, 1);
        }
        catch { Item(packageId, "failed", true); throw; }
        finally { if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); _operations.Release(); }
    }
    public async Task<IReadOnlyDictionary<string, string>> ActivePackagesAsync(CancellationToken token = default)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string folder in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root).Take(65) : [])
        {
            token.ThrowIfCancellationRequested(); string id = Path.GetFileName(folder);
            if (!ValidId(id) || !File.Exists(Path.Combine(_bundled, id + ".nsc"))) continue;
            try
            {
                var offer = await ActiveOffer(id, token).ConfigureAwait(false);
                if (offer is null || !UpdateVersion.TryParse(_hostVersion, out var host) || host.CompareTo(UpdateHighWaterJournal.ParseVersion(offer.MinimumHost)) < 0
                    || UpdateHighWaterJournal.ParseVersion(offer.Release).CompareTo(host) < 0) continue;
                string image = Path.Combine(folder, offer.Hash, id + ".nsc");
                await ValidateImage(image, offer.Hash, token).ConfigureAwait(false); result.Add(id, image);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { }
        }
        foreach (var item in await Inventory(token).ConfigureAwait(false))
        {
            string image = result.GetValueOrDefault(item.Key) ?? Path.Combine(_bundled, item.Key + ".nsc");
            await using var stream = File.OpenRead(image);
            _runningHashes[item.Key] = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        }
        Publish("initial", (await Inventory(token).ConfigureAwait(false)).Values.ToArray());
        return result;
    }
    private async Task<Dictionary<string, SidecarUpdateItem>> Inventory(CancellationToken token)
    {
        Dictionary<string, SidecarUpdateItem> result = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(_bundled, "*.nsc").Take(65))
        {
            token.ThrowIfCancellationRequested(); string id = Path.GetFileNameWithoutExtension(path); if (!ValidId(id)) continue;
            Candidate? active = null;
            try { active = await ActiveOffer(id, token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException) { }
            string version = active?.Version ?? "";
            string info = Path.Combine(_bundled, id + ".json");
            if (version.Length == 0 && File.Exists(info) && new FileInfo(info).Length <= CatalogLimit)
            {
                try { using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(info, token).ConfigureAwait(false)); version = json.RootElement.GetProperty("runtimeVersion").GetString() ?? ""; }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
            result.Add(id, new(id, version, null, "installed", false));
        }
        return result;
    }
    private async Task<Candidate?> ActiveOffer(string id, CancellationToken token)
    {
        try { return await ReadActiveOffer(id, token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { return null; }
    }
    private async Task<Candidate?> ReadActiveOffer(string id, CancellationToken token)
    {
        string pointer = Path.Combine(_root, id, "active"); if (!File.Exists(pointer)) return null;
        if (new FileInfo(pointer).Length != 64) throw new InvalidDataException("Invalid active Sidecar pointer.");
        string hash = await File.ReadAllTextAsync(pointer, token).ConfigureAwait(false); if (!Digest(hash)) throw new InvalidDataException("Invalid active Sidecar digest.");
        string directory = Path.Combine(_root, id, hash);
        byte[] catalog = await ReadBounded(Path.Combine(directory, "catalog.json"), CatalogLimit, token).ConfigureAwait(false);
        byte[] signature = await ReadBounded(Path.Combine(directory, "catalog.json.asc"), 16384, token).ConfigureAwait(false);
        await Verify(catalog, signature, token).ConfigureAwait(false);
        var entries = Parse(catalog, signature, null);
        if (!entries.TryGetValue(id, out var item) || item.Hash != hash) throw new InvalidDataException("Active catalog does not bind this package.");
        await ValidateImage(Path.Combine(directory, id + ".nsc"), hash, token).ConfigureAwait(false);
        return item;
    }
    private async Task<bool> IsOlderThanActive(Candidate offer, CancellationToken token)
    {
        var release = UpdateHighWaterJournal.ParseVersion(offer.Release);
        if (UpdateVersion.TryParse(_hostVersion, out var bundledRelease) && release.CompareTo(bundledRelease) < 0) return true;
        var active = await ActiveOffer(offer.Id, token).ConfigureAwait(false);
        return active is not null && release.CompareTo(UpdateHighWaterJournal.ParseVersion(active.Release)) < 0;
    }
    private async Task<string?> InstalledHash(string id, CancellationToken token)
    {
        var active = await ActiveOffer(id, token).ConfigureAwait(false); if (active is not null) return active.Hash;
        await using var stream = File.OpenRead(Path.Combine(_bundled, id + ".nsc")); return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
    private Dictionary<string, Candidate> Parse(byte[] bytes, byte[] signature, string? expectedRelease)
    {
        using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
        string release = root.GetProperty("release").GetString() ?? "";
        if (root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("rid").GetString() != _rid || expectedRelease is not null && release != expectedRelease
            || !UpdateVersion.TryParse(release, out var releaseVersion) || releaseVersion.ToString() != release) throw new InvalidDataException("Invalid Sidecar catalog identity.");
        var entries = root.GetProperty("packages"); if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 64) throw new InvalidDataException("Sidecar catalog limit exceeded.");
        Dictionary<string, Candidate> result = new(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString() ?? "", hash = entry.GetProperty("sha256").GetString() ?? "";
            string version = entry.GetProperty("version").GetString() ?? "", minimum = entry.GetProperty("minimumHostVersion").GetString() ?? "";
            long size = entry.GetProperty("size").GetInt64();
            if (!ValidId(id) || !Digest(hash) || version.Length is < 1 or > 64 || version.Any(char.IsControl) || !UpdateVersion.TryParse(minimum, out _) || size is < 64 or > 536870912)
                throw new InvalidDataException("Invalid Sidecar package entry.");
            if (!result.TryAdd(id, new(id, version, hash, size, ReleaseUri(release, id + "-" + _rid + ".nsc"), release, minimum, bytes, signature))) throw new InvalidDataException("Duplicate Sidecar identity.");
        }
        return result;
    }
    private async Task ValidateImage(string path, string hash, CancellationToken token)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked Sidecar package.");
        await using var image = File.OpenRead(path); if (image.Length > 536870912) throw new InvalidDataException("Sidecar too large.");
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(image, token).ConfigureAwait(false)) != hash) throw new InvalidDataException("Sidecar digest mismatch.");
        image.Position = 0; await using var signature = File.OpenRead(path + ".asc"); await _verify.VerifyAsync(image, signature, token).ConfigureAwait(false);
    }
    private Task Verify(byte[] content, byte[] signature, CancellationToken token) => VerifyBuffers(content, signature, token);
    private async Task VerifyBuffers(byte[] content, byte[] signature, CancellationToken token)
    { using var data = new MemoryStream(content); using var proof = new MemoryStream(signature); await _verify.VerifyAsync(data, proof, token).ConfigureAwait(false); }
    private static async Task<byte[]> ReadBounded(string path, int maximum, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Sidecar metadata exceeds budget.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static HttpRequestMessage Request(Uri uri)
    { var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("NexaCL-Sidecar-Update/1.0"); return request; }
    private async Task<byte[]> Fetch(Uri uri, int maximum, CancellationToken token)
    {
        using var request = Request(uri); using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Response exceeds budget.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false); using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { if (output.Length + count > maximum) throw new InvalidDataException("Response exceeds budget."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    private static Uri ReleaseUri(string version, string name) => new(LauncherRepository.UpdatesUrl + "/releases/download/v" + version + "/" + name);
    // No wait handle is created; keep the managed gate valid for an in-flight operation's finally.
    public void Dispose() { }
    private static bool Digest(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidId(string value) => value.Length is > 0 and <= 128 && value[0] != '.' && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
